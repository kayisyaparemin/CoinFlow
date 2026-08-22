# Mizan mimarisi

## Tek finansal kaynak

Mizan'ın merkezi çıktısı `SalaryPeriodProjection` modelidir. Dashboard, 12 aylık görünüm ve simülatör kendi formüllerini üretmez; aynı `FinancialProjectionCalculator` sonucunu kullanır.

```text
FinancialPlan
   ├─ SalaryPeriodCalculator
   ├─ ProjectionAnchorDate filter
   ├─ ProjectionBoundaryResolver (history'den ilk unrealized maaş)
   ├─ PaymentAssignmentStrategyResolver (effective-dated history)
   ├─ SalaryResolver + IncomeProjectionCalculator
   ├─ LoanScheduleCalculator
   ├─ CreditCardStatementCalculator (principal → carry interest → next carry)
   └─ ScheduledPaymentCalculator
            ↓
 SalaryFundingPlanner (coverage frontier)
            ↓
 FinancialProjectionCalculator (maaş bazında aktif düzen)
            ↓
 Dashboard / 12 Aylık / Simulator baseline + scenario
```

Current/future query'leri, latest current snapshot ve history üzerinden runtime projection boundary türetir. İlk kurulumda `ProjectionAnchorDate` anchor'daki veya sonraki ilk maaşı seçebilir; finalized `PeriodActual` sonucu oluşan snapshot'ta ise `PeriodActual.ResultFinancialSnapshotId` provenance'ı kullanılır ve ilk unrealized maaş kapatılan `PeriodEnd` checkpoint'ından strictly sonra çözülür. `NextProjectionSalaryDate` gibi ikinci bir kalıcı cursor tutulmaz; projection veya simulator için ikinci bir hesap motoru yoktur.

`SalaryPeriodDetailPresenter`, hesaplanmış `SalaryPeriodProjection` sonucunu presentation-only summary, flow, kategori, faiz, transition ve bağımsız ödeme satırlarına ayırır. Formül çalıştırmaz. 12 Aylık ve Simulator aynı `SalaryPeriodDetailPage` / `SalaryPeriodDetailViewModel` ikilisini kullanır; Simulator yalnız aynı modele baseline karşılaştırmasını ekler. Shell navigation hesaplanmış result nesnesini taşır, detail page finans motorunu yeniden kurmaz.

## Katmanlar

| Proje | Sorumluluk |
|---|---|
| `CoinFlow.Domain` | Saf modeller, tarih kuralları, projection ve simulation motorları |
| `CoinFlow.Application` | Kullanım senaryoları, CRUD, açık onaylı scenario apply ve store sözleşmesi |
| `CoinFlow.Infrastructure` | SQLite şema v8, legacy upgrade ve deterministik development seed |
| `CoinFlow.App` | .NET MAUI Android görünümü ve servis sonuçlarını sunan MVVM katmanı |
| `CoinFlow.Tests` | Domain regression, kanonik veri ve SQLite entegrasyon testleri |

Bağımlılık yönü `App → Application → Domain`; `Infrastructure → Application + Domain` şeklindedir.

## Tarih ve para kuralları

- Maaş dönemi `[başlangıç, bitiş)` semantiğine sahiptir.
- Snapshot review tarihi `SalaryPeriodCalculator.GetNextReviewDate` ile çözülür ve snapshot'tan strictly sonra gelen ilk maaş tarihidir. Review hareket penceresi `(SnapshotDate, ReviewDate]` semantiğine sahiptir; ödeme atama modu bu checkpoint'ı değiştirmez.
- Maaş dönemi ortasındaki ilk snapshot'ın yaşam bütçesi `MonthlyLivingBudget × review gün sayısı / tam maaş dönemi gün sayısı` ile iki hane `AwayFromZero` oranlanır. Maaş günündeki snapshot tam aylık bütçeyi kullanır.
- `ProjectionAnchorDate`, anchor öncesini plan dışı sayar ve ilk projection maaşını anchor'daki veya anchor sonrasındaki ilk maaş olarak belirler.
- Actual finalization sonrası current/future projection, obligation anchor'ını kapatılan checkpoint'te tutar fakat ilk projection maaşını strictly sonrasına taşır. Böylece taşınan ödenmemiş yükümlülükler kaybolmaz, kapatılmış checkpoint maaşı ise tekrar income olmaz.
- `PaymentAssignmentStrategyResolver`, her maaşta effective tarihi o maaştan büyük olmayan en yeni history kaydını seçer.
- `SalaryFundingPlanner`, son kapsanan günü izler; her maaşta yalnız yeni coverage aralığını atar. `Previous → Upcoming` geçişinde gap'i catch-up olarak dahil eder, `Upcoming → Previous` geçişinde daha önce fonlanan günleri tekrar saymaz.
- `PreviousPeriod` penceresi `(önceki maaş, mevcut maaş]` olduğundan maaş günü ödemesi hiçbir zaman bir ay geriye kaymaz.
- Maaş günü kısa ayda ayın son gününe kırpılır; aynı kural kredi ve tekrarlı ödeme tarihlerinde kullanılır.
- Dönem maaşı, dönem başlangıcında yürürlükteki son maaş kaydıdır.
- Diğer gelir ve tüm yükümlülükler exact date ile tek bir döneme girer.
- Para hesapları `decimal` ile yapılır; eşit taksitlerde kalan kuruş yalnız son taksite eklenir.
- Planlama faizleri iki haneye `MidpointRounding.AwayFromZero` ile yuvarlanır.
- Kümülatif finansal durum her dönemin `OpeningProjectedSavings` değerinden devam eder.
- Negatif opening değerinin mutlak tutarı `CarryOverDeficit`, zorunlu ödemeler sonrası alandan görünüm amaçlı düşülmüş hali `AvailableAfterCarryOverDeficit` olarak türetilir. Bunlar obligation değildir ve `EndingProjectedSavingsBeforeDeficitInterest = OpeningProjectedSavings + CurrentPeriodNetContribution` hesabında yeniden düşülmez.
- Dönem sonucu negatif kaldığında `DeficitFinancingInterest` bu negatif principal üzerinde hesaplanır; final ending'e bir kez uygulanır ve sonraki opening'e taşınır. Sonuç sıfır veya pozitifse açık faizi üretilmez.

## Kredi kartı motoru

`CreditCardStatementCalculator`, devreden borç, dönem içi harcama ve exact posting kayıtlarını kesim tarihine taşır. Ödeme kesimden sonra son ödeme tarihinde yükümlülük olur.

Kart başına gerçek ödeme stratejisi (`AskEachStatement`, asgari, tam ekstre, sabit) ile yalnız projection için kullanılabilen fallback ayrıdır. Exact due-date override varsa stratejinin önüne geçer. Sabit tutar ekstre borcunu aşamaz; asgarinin altındaysa asgariye yükseltilir. Belirsiz ödeme planı tutar uydurmaz ve açıkça işaretlenir.

Ödeme sonrası kalan principal sıfıra kırpılır, yapılandırılabilir aylık `CreditCardCarryInterestRate` ile faizlenir ve `NextCarriedBalance` olarak sonraki statement'a taşınır. Tam ödemede faiz sıfırdır. Bu faiz aynı dönemin mandatory outflow'una eklenmez; nakit etkisi sonraki ekstre ödeme gününde normal kart yükümlülüğü olarak ortaya çıkar. `CarryInterest` ve `DeficitFinancingInterest` ayrı state'lerdir.

## Simülatör

`SimulationCalculator` önce mevcut `FinancialPlan` ile baseline hesaplar, sonra yalnız bellekte scenario planı kurup aynı projection motorunu yeniden çalıştırır. Payment strategy senaryosu history kopyasına future effective kayıt ekler; önizleme veritabanına yazmaz. Bu sayede baseline ve scenario kolonları aynı anchor, coverage, tarih, kart, carry-over deficit, faiz ve finansal durum kurallarına tabidir. Risk özeti ilk deficit dönemini, maksimum devreden açığı ve recovery dönemini aynı sonuçlardan türetir. Karşılaştırma baseline/scenario kart faizini, açık faizini, ek faiz yükünü veya faiz tasarrufunu ayrı ayrı üretir; “kart ekstresini tamamen kapat” senaryosu exact due-date tam ödeme override'ı kullanır.

Senaryoyu kaydetmek ayrı bir işlemdir. `CoinFlowService.ApplySimulationAsync` açık `confirmed=true` olmadan kalıcı değişiklik yapmaz. Her hesaplanan scenario kalıcı bir application kimliği taşır; entity ve child charge/taksit kimlikleri bundan deterministik üretilir. Böylece hızlı çift tıklama veya retry aynı canonical kaydı ikinci kez oluşturmaz. Apply switch'i nakit gideri `PlannedLargeExpense`, finansmanı `TemporaryPaymentPlan`, kart alışverişini seçili `CreditCard` aggregate'ının charge'ları, gelecek geliri `OneTimeIncome`, maaş ve ödeme düzeni değişikliklerini yeni effective-dated history kayıtları olarak persist eder. Maaş/strategy geçmişi apply sırasında overwrite edilmez.

Kart ve ödeme planı aggregate upsert'leri SQLite transaction içinde ana kayıt ve tüm child satırları birlikte yazar. Apply sonucu hedef bölüm ve entity kimliğini UI'a döndürür; Gelir & Ödemeler sayfası `OnAppearing` sırasında canonical store'u yeniden okur ve istenen gelir/ödeme veya kart detayını açar. Projection katmanında cache bulunmadığından Dashboard, 12 Aylık, Target Amount ve sonraki simulator baseline her çağrıda güncel canonical planı kullanır.

12 Aylık ve Simulator, Dönem Detayı'ndan geri dönüşte collection'ı yeniden üretmez; mevcut page instance ve scroll/scenario state korunur. Başka bir kök ekrandan geri gelindiğinde normal canonical reload davranışı devam eder.

## Aylık snapshot ve history

```text
Current FinancialSnapshot
        ↓ aynı FinancialProjectionCalculator
Frozen PeriodPlanSnapshot
        ↓ review tarihi
PeriodActual + optional PeriodPlanRevision
        ↓ FinancialInstrumentReconciliationService
Canonical kart/kredi/plan state + yeni UserSettings baseline
        ↓ tek transaction
New Current FinancialSnapshot + New Frozen Plan
```

- `PeriodPlanSnapshotService`, snapshot→ilk sonraki maaş checkpoint aralığını doğrudan dondurur. Ödeme adaylarını mevcut projection/kart motorundan alır, yalnız `(SnapshotDate, ReviewDate]` satırlarını tutar ve ilk kısmi yaşam bütçesini oranlar. Bu historical pencere 12 Aylık projection dönemlerini değiştirmez.
- `PeriodReviewService`, due kontrolü, actual doğrulaması ve idempotent finalization orkestrasyonunu yapar.
- `ProjectionBoundaryResolver`, latest current snapshot'ın bir `PeriodActual.ResultFinancialSnapshotId` sonucu olup olmadığını history'den anlık çözer. Actual-generated snapshot için closed checkpoint `PeriodActual.PeriodEnd`, first unrealized salary ise salary calendar'da strictly sonraki maaştır.
- `FinancialStateReconciliationService`, başlangıç durumu semantiğini değiştirmeden dönem sonu önerisini hesaplar.
- `CreditCardActualPaymentReconciler`, actual kart ödemesini exact due-date statement ile eşler; kalan principal ve carry faizini canonical karta bir kez uygular.
- `FinancialInstrumentReconciliationService`, ödenen kredi/taksitleri ilerletir; ödenmeyen veya kaçırılmış yükümlülükleri yeni anchor'a taşıyarak gelecek plandan kaybolmalarını engeller.
- `PlanActualComparisonCalculator` ve `HistoryQueryService` yalnız frozen tarihsel veriyi okur. Gelecek ayar değişiklikleri eski planı yeniden hesaplamaz.

SQLite finalization transaction'ı source snapshot'ın hâlâ current olduğunu ve plan için actual bulunmadığını kontrol eder. Unique `PeriodPlanSnapshotId` indeksi hızlı çift dokunma/retry durumunda ikinci actual ve snapshot oluşmasını engeller.

Normal veya gecikmiş finalization yeni snapshot'ı cihazın açıldığı güne değil planın `ReviewAvailableFrom` checkpoint'ına yazar. Böylece 20 Ağustos → 10 Eylül → 10 Ekim zinciri korunur. Tarih ileri alınırsa pending review due olur fakat actual otomatik üretilmez. Henüz actual'ı olmayan eski hatalı current plan, `ReplacePendingFinancialSnapshotPlanAsync` transaction'ıyla yeniden dondurulabilir; completed historical planlar değiştirilemez.

## Veri ve migration

Store tüm entity'leri exact-date alanlarıyla round-trip eder. Şema v8; `financial_snapshots`, frozen plan/satırları, revision, actual, actual payment/flow ve optional living breakdown tablolarını additive olarak ekler. Existing kullanıcı ilk normal plan okumasında mevcut canonical durumundan initial current snapshot alır; geçmiş actual üretilmez. Şema v7'de eklenen iki global planlama faiz oranı ve eski strategy/card migration davranışları korunur. SQLite-net additive migration finansman planlarına ana tutar ve toplam geri ödeme alanlarını eski kayıtları bozmadan ekler. Legacy upgrade sırasında eksik `ProjectionAnchorDate` bir kez oluşturulur; fresh veritabanında ise ilk maaş planlamasına kadar boş kalır. Fresh development veritabanı otomatik seed edilmez. Clear aksiyonu yeni history tablolarını da temizler.
