# CoinFlow mimarisi

## Temel ayrım

CoinFlow iki farklı finansal görünümü birbirine karıştırmaz:

- **Current Actual:** Aktif maaş döneminde şu anda gerçekten harcanabilecek serbest bakiye.
- **Future Projection:** Maaş ve bilinen zorunlu ödemelerle oluşturulan gelecek dönem tahmini.

Ana ekrandaki büyük tutar Current Actual'dır. Sonraki maaş kartı ve gelecek 12 dönem Future Projection'dır.

## Katmanlar

| Proje | Sorumluluk |
|---|---|
| `CoinFlow.Domain` | Saf modeller ve deterministic tarih/para hesaplayıcıları |
| `CoinFlow.Application` | Kullanım senaryoları, ortak projection orkestrasyonu ve repository sözleşmesi |
| `CoinFlow.Infrastructure` | SQLite tabloları, idempotent schema upgrade, legacy kart migration'ı ve development seed |
| `CoinFlow.App` | .NET MAUI Android görünümü ve yalnız servis sonuçlarını formatlayan MVVM katmanı |
| `CoinFlow.Tests` | Saf unit, SQLite migration ve uçtan uca finansal regression testleri |

Bağımlılık yönü `App → Application → Domain`; `Infrastructure → Application + Domain` şeklindedir.

## Hesap motorları

- `SalaryPeriodCalculator`: `[başlangıç, sonraki maaş)` aralığı ve dönem başlangıcındaki maaş seçimi.
- `LoanScheduleCalculator`: Kredi için exact aylık ödeme tarihleri.
- `InstallmentScheduleCalculator`: Decimal eşit bölme ve son taksitte kuruş farkı.
- `CreditCardProjectionCalculator`: Exact posting → statement close → payment due zinciri, carried balance ve manuel due-date ödemesi.
- `MandatoryPaymentCalculator`: Exact due date'i dönem aralığına düşen kredi, kart, plan ve tampon rezervlerini toplar.
- `SpendableBalanceCalculator`: Son snapshot veya dönem başlangıcı fallback'ından sonraki uygun harcamalarla Current Actual üretir.
- `DailyCoinCalculator`: Daily Reward, sürdürülebilir Coin ve Coin Pool hesaplar.
- `EmergencyFundCalculator`: Hedef sınırı, dönem rezervi ve çift düşmeyen manuel transfer dağılımı.
- `PurchaseSimulationCalculator`: Ortak baseline ve kart statement motorunu yeniden kullanarak base/scenario farkını üretir.
- `FinancialProjectionService`: Aynı hesap motorlarıyla dashboard ve 12 maaş dönemi sonuçlarını orkestre eder.

## Current Actual ve snapshot

`SpendableBalanceSnapshot`, banka hesabı toplamını değil, belirtilen anda zorunlu ödemeler ayrıldıktan sonra gerçekten harcanabilir tutarı saklar.

Aktif dönemin son snapshot'ı varsa:

`CurrentAvailable = Snapshot.Amount - Snapshot sonrasındaki Cash/Other harcamalar`

Snapshot ve harcama `CreatedAtUtc` taşıdığı için aynı gündeki düzeltmeden önceki harcamalar ikinci kez düşülmez. Snapshot yoksa yalnız `TrackingStartedDate <= Period.Start` olduğunda teorik başlangıç bütçesinden takip edilen harcamalar düşülebilir; aksi halde UI kullanıcıdan serbest bakiye ister.

## Daily Coin

- `DailyReward = OriginAmount / (NextSalary - OriginDate)`
- `CurrentAvailable = OriginAmount - EligibleExpensesAfterOrigin`
- `SustainableDaily = CurrentAvailable / (NextSalary - Today)`
- `CoinPool = DailyReward × ElapsedDaysIncludingToday - EligibleExpenses`

Negatif sonuçlar korunur. Oyunlaştırma yalnız metni değiştirir.

## Kredi kartı statement döngüsü

Kart açılış durumu:

- `CarriedBalance`: Önceki ekstreden devreden bakiye.
- `UnbilledSpending`: Referans tarihinde henüz ekstreleşmemiş toplam.
- `BalanceAsOfDate`: Bu iki açılış değerinin referans tarihi.
- `Charges`: Exact posting tarihli gelecek veya uygulamada eklenmiş işlemler.
- `PaymentPlans`: Exact due date'e bağlı manuel ödeme tutarları.

Posting tarihi close gününde veya öncesindeyse o close'a, sonrasındaysa sonraki close'a girer. Statement:

`StatementBalance = OpeningCarried + AssignedCharges`

`Payment = ManualForExactDueDate ?? Round(StatementBalance × MinimumRate, 2)`

`CarriedAfterPayment = max(0, StatementBalance - Payment)`

Due date, statement close tarihinden sonraki ilk uygun `PaymentDueDay` tarihidir. Ödeme maaş dönemine close tarihiyle değil exact due date ile atanır. Faiz ve vergiler MVP kapsamı dışındadır.

## Zorunlu ödeme ve gelecek dönem

`Mandatory = Loans + CardPaymentsByDueDate + Temporary + PlannedInstallments + CappedEmergencyContribution`

`ProjectedFreeBudget = Salary - Mandatory`

Gelecek ekranı takvim ayı yerine açıkça `10 Eyl → 10 Eki` gibi maaş dönemi gösterir. İlk satırda hem teorik başlangıç bütçesi hem mevcutsa Current Actual bulunur.

## Acil durum tamponu

Katkı hedefte kalan tutarla sınırlanır. Transfer kaydı, planlanan katkının ne kadarını yerine getirdiğini saklar. Rezerve tutara kadar transfer yalnız tampon bakiyesini artırır; plan üstü kısım Cash expense olarak Current Actual'ı azaltır.

## SQLite migration

Yeni tablolar:

- `spendable_balance_snapshots`
- `credit_card_payment_plans`
- `emergency_fund_transfers`

Eski kart kolonları migration uyumluluğu için korunur. `StatementModelVersion < 2` kayıtları açılışta carried/unbilled modele taşınır; `card_installments` tablosundaki exact tarihler charge posting tarihi olarak korunur. Drop/recreate yapılmaz.

## CI/CD

- `main`: test + development APK + doğrulama + mutable `dev-latest` prerelease.
- `vX.Y.Z`: test + private keystore ile imzalı stable APK.

Refactor bu workflow sözleşmelerini ve APK bulma yollarını değiştirmez.
