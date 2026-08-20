# CoinFlow mimarisi

## Tek finansal kaynak

CoinFlow'un merkezi çıktısı `SalaryPeriodProjection` modelidir. Dashboard, 12 aylık görünüm ve simülatör kendi formüllerini üretmez; aynı `FinancialProjectionCalculator` sonucunu kullanır.

```text
FinancialPlan
   ├─ SalaryPeriodCalculator
   ├─ PaymentAssignmentResolver
   ├─ SalaryResolver + IncomeProjectionCalculator
   ├─ LoanScheduleCalculator
   ├─ CreditCardStatementCalculator
   └─ ScheduledPaymentCalculator
            ↓
 FinancialProjectionCalculator (seçili maaş kullanım şekli)
            ↓
 Dashboard / 12 Aylık / Simulator baseline + scenario
```

## Katmanlar

| Proje | Sorumluluk |
|---|---|
| `CoinFlow.Domain` | Saf modeller, tarih kuralları, projection ve simulation motorları |
| `CoinFlow.Application` | Kullanım senaryoları, CRUD, açık onaylı scenario apply ve store sözleşmesi |
| `CoinFlow.Infrastructure` | SQLite şema v4, legacy upgrade ve deterministik development seed |
| `CoinFlow.App` | .NET MAUI Android görünümü ve servis sonuçlarını sunan MVVM katmanı |
| `CoinFlow.Tests` | Domain regression, kanonik veri ve SQLite entegrasyon testleri |

Bağımlılık yönü `App → Application → Domain`; `Infrastructure → Application + Domain` şeklindedir.

## Tarih ve para kuralları

- Maaş dönemi `[başlangıç, bitiş)` semantiğine sahiptir.
- `PaymentAssignmentResolver`, gerçek ödeme tarihini değiştirmeden ödemeyi `UpcomingPeriod` veya `PreviousPeriod` maaş bütçesine atar.
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

`SimulationCalculator` önce mevcut `FinancialPlan` ile baseline hesaplar, sonra yalnız bellekte scenario planı kurup aynı projection motorunu yeniden çalıştırır. Bu sayede baseline ve scenario kolonları aynı tarih, kart ve birikim kurallarına tabidir.

Senaryoyu kaydetmek ayrı bir işlemdir. `CoinFlowService.ApplySimulationAsync` açık `confirmed=true` olmadan kalıcı değişiklik yapmaz. Aynı tarihli maaş değişimi uygulanırken önceki kayıt kaldırıldığı için tekrar apply çoğaltma üretmez.

## Veri ve migration

Store tüm entity'leri exact-date alanlarıyla round-trip eder. Şema v5, global `PaymentAssignmentMode` ayarını kalıcılaştırır; eski kurulumların diğer ayarlarını değiştirmeden eksik alanı `UpcomingPeriod` olarak ekler. Şema upgrade'i eski kart sütunlarını yeni devreden/dönem içi borç modeline taşır ve kaldırılmış özelliklerin tablolarını temizler. Seed sabit GUID'ler kullanır, yalnız boş development veritabanına uygulanır ve transaction içinde tamamlanır.
