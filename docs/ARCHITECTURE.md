# CoinFlow mimarisi

## Tek finansal kaynak

CoinFlow'un merkezi çıktısı `SalaryPeriodProjection` modelidir. Dashboard, 12 aylık görünüm ve simülatör kendi formüllerini üretmez; aynı `FinancialProjectionCalculator` sonucunu kullanır.

```text
FinancialPlan
   ├─ SalaryPeriodCalculator
   ├─ ProjectionAnchorDate filter
   ├─ PaymentAssignmentStrategyResolver (effective-dated history)
   ├─ SalaryResolver + IncomeProjectionCalculator
   ├─ LoanScheduleCalculator
   ├─ CreditCardStatementCalculator
   └─ ScheduledPaymentCalculator
            ↓
 SalaryFundingPlanner (coverage frontier)
            ↓
 FinancialProjectionCalculator (maaş bazında aktif düzen)
            ↓
 Dashboard / 12 Aylık / Simulator baseline + scenario
```

## Katmanlar

| Proje | Sorumluluk |
|---|---|
| `CoinFlow.Domain` | Saf modeller, tarih kuralları, projection ve simulation motorları |
| `CoinFlow.Application` | Kullanım senaryoları, CRUD, açık onaylı scenario apply ve store sözleşmesi |
| `CoinFlow.Infrastructure` | SQLite şema v6, legacy upgrade ve deterministik development seed |
| `CoinFlow.App` | .NET MAUI Android görünümü ve servis sonuçlarını sunan MVVM katmanı |
| `CoinFlow.Tests` | Domain regression, kanonik veri ve SQLite entegrasyon testleri |

Bağımlılık yönü `App → Application → Domain`; `Infrastructure → Application + Domain` şeklindedir.

## Tarih ve para kuralları

- Maaş dönemi `[başlangıç, bitiş)` semantiğine sahiptir.
- `ProjectionAnchorDate`, anchor öncesini plan dışı sayar ve ilk projection maaşını anchor'daki veya anchor sonrasındaki ilk maaş olarak belirler.
- `PaymentAssignmentStrategyResolver`, her maaşta effective tarihi o maaştan büyük olmayan en yeni history kaydını seçer.
- `SalaryFundingPlanner`, son kapsanan günü izler; her maaşta yalnız yeni coverage aralığını atar. `Previous → Upcoming` geçişinde gap'i catch-up olarak dahil eder, `Upcoming → Previous` geçişinde daha önce fonlanan günleri tekrar saymaz.
- `PreviousPeriod` penceresi `(önceki maaş, mevcut maaş]` olduğundan maaş günü ödemesi hiçbir zaman bir ay geriye kaymaz.
- Maaş günü kısa ayda ayın son gününe kırpılır; aynı kural kredi ve tekrarlı ödeme tarihlerinde kullanılır.
- Dönem maaşı, dönem başlangıcında yürürlükteki son maaş kaydıdır.
- Diğer gelir ve tüm yükümlülükler exact date ile tek bir döneme girer.
- Para hesapları `decimal` ile yapılır; eşit taksitlerde kalan kuruş yalnız son taksite eklenir.
- Kümülatif birikim her dönemin `OpeningProjectedSavings` değerinden devam eder.

## Kredi kartı motoru

`CreditCardStatementCalculator`, devreden borç, dönem içi harcama ve exact posting kayıtlarını kesim tarihine taşır. Ödeme kesimden sonra son ödeme tarihinde yükümlülük olur.

Kart başına gerçek ödeme stratejisi (`AskEachStatement`, asgari, tam ekstre, sabit) ile yalnız projection için kullanılabilen fallback ayrıdır. Exact due-date override varsa stratejinin önüne geçer. Sabit tutar ekstre borcunu aşamaz; asgarinin altındaysa asgariye yükseltilir. Belirsiz ödeme planı tutar uydurmaz ve açıkça işaretlenir.

## Simülatör

`SimulationCalculator` önce mevcut `FinancialPlan` ile baseline hesaplar, sonra yalnız bellekte scenario planı kurup aynı projection motorunu yeniden çalıştırır. Payment strategy senaryosu history kopyasına future effective kayıt ekler; önizleme veritabanına yazmaz. Bu sayede baseline ve scenario kolonları aynı anchor, coverage, tarih, kart ve birikim kurallarına tabidir.

Senaryoyu kaydetmek ayrı bir işlemdir. `CoinFlowService.ApplySimulationAsync` açık `confirmed=true` olmadan kalıcı değişiklik yapmaz. Aynı tarihli maaş değişimi uygulanırken önceki kayıt kaldırıldığı için tekrar apply çoğaltma üretmez.

## Veri ve migration

Store tüm entity'leri exact-date alanlarıyla round-trip eder. Şema v6, eski global `PaymentAssignmentMode` değerini history boşsa ilk projection maaşında başlayan strategy kaydına taşır. Legacy upgrade sırasında eksik `ProjectionAnchorDate` bir kez oluşturulur; fresh veritabanında ise ilk maaş planlamasına kadar boş kalır. Global sütun yalnız migration bootstrap uyumluluğu için kalır; runtime hesaplaması onu okumaz. Şema upgrade'i eski kart sütunlarını yeni devreden/dönem içi borç modeline taşır ve kaldırılmış özelliklerin tablolarını temizler. Fresh development veritabanı otomatik seed edilmez. Açık development seed aksiyonu sabit GUID'lerle idempotent upsert yapar; ayrı clear aksiyonu şemayı ve teknik metadata'yı koruyarak yalnız finansal veriyi siler.
