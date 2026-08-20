# CoinFlow

CoinFlow, maaş gününden bir sonraki maaş gününe kadar olan dönemi esas alan, zorunlu ödemelerden sonra kalan yaşam bütçesini ve tahmini birikimi gösteren Android öncelikli, çevrimdışı bir kişisel finans uygulamasıdır.

Uygulama mikro harcama takibi yapmaz. Ana kavramlar maaş dönemi, toplam gelir, zorunlu ödeme, yaşam bütçesi ve birikim kapasitesidir.

## Finans modeli

Her maaş dönemi `[başlangıç, sonraki maaş günü)` aralığıdır. Dönem başlangıcı dahildir, sonraki maaş günü dahil değildir.

Kullanıcının ödemeleri maaş bütçesine atama düzeni effective-dated bir geçmiş olarak tutulur:

- **Gelecek dönemi karşılarım:** Maaş tarihi dahil, sonraki maaş tarihi hariç ödemeler aynı maaşa atanır.
- **Geçmiş dönemi kapatırım:** Önceki maaş tarihinden sonraki ödemeler mevcut maaşa atanır; maaş günündeki ödeme geriye kaymaz.

Kredi, kart ve planların gerçek ödeme tarihleri bu tercihten etkilenmez. `PaymentAssignmentStrategyResolver` her maaş için o tarihte yürürlükteki kaydı seçer; `SalaryFundingPlanner` coverage frontier ile geçiş boşluğu veya mükerrer atama üretmeden yalnız bütçe atamasını yapar. Maaştan önce vadesi gelen ödemeler ayrıca uyarı olarak gösterilir.

Kalıcı `ProjectionAnchorDate`, günlük hayatın projection dışında kabul edildiği snapshot sınırıdır; banka bakiyesi değildir. Projection bu sınırdaki veya sonrasındaki ilk maaştan başlar. İlk düzen `UpcomingPeriod` ise anchor ile ilk maaş arasındaki exact yükümlülükler dashboard'da “Sonraki Maaştan Önce” bölümünde ayrı gösterilir.

```text
Toplam Gelir = Maaş + Döneme denk gelen diğer gelirler
Zorunlu Ödeme = Krediler + Kart ödemeleri + geçici/taksitli/diğer planlı ödemeler
Zorunlu Ödemeler Sonrası = Toplam Gelir - Zorunlu Ödeme
Tahmini Birikim Kapasitesi = Zorunlu Ödemeler Sonrası - Yaşam Bütçesi - Planlı büyük nakit giderler
Faiz Öncesi Dönem Sonu = Dönem Başı Birikim + Tahmini Birikim Kapasitesi
Finansman Açığı Faizi = max(0, -Faiz Öncesi Dönem Sonu) × Açık Faiz Oranı
Dönem Sonu Tahmini Birikim = Faiz Öncesi Dönem Sonu - Finansman Açığı Faizi
```

Negatif dönem sonu tahmini birikim, hesaplanan finansman açığı faiziyle birlikte sonraki maaş dönemine aynen `OpeningProjectedSavings` olarak taşınır. UI bunu **devreden finansman açığı** olarak gösterir. Bu değer yeni kredi, kart borcu veya zorunlu ödeme değildir; yalnız kümülatif planlama başlangıç durumudur ve dönem sonu hesabında ikinci kez çıkarılmaz.

Kart ekstresinde ödenmeyen principal için aylık planlama faizi hesaplanır ve yalnız bir sonraki ekstre opening carry bakiyesine eklenir. Kart faizi mevcut maaş döneminin zorunlu ödemesine tekrar yazılmaz. Kart carry faizi ile genel finansman açığı faizi iki ayrı state ve summary olarak tutulur; ikisi de varsayılan `%5,00`, `decimal` ve iki hane `AwayFromZero` yuvarlama kullanır.

Maaş, tek seferlik gelir, kredi, kart harcaması, kart vadesi, geçici ödeme ve büyük giderlerin tamamı exact date ile ilgili maaş dönemine yerleşir. Ayın 29/30/31'i için takvim sonu kırpma kuralı merkezi olarak uygulanır.

## Ekranlar

Sol üstteki native Shell hamburger menüsü beş kök bölüm içerir; bottom TabBar yoktur:

1. **Ana Sayfa:** Aktif maaş dönemi özeti, yaklaşan ödemeler, 12 dönem özeti ve en sıkışık dönem.
2. **12 Aylık:** Her dönem için gelir, zorunlu ödeme, yaşam bütçesi, faiz maliyeti, birikim kapasitesi ve dönem sonu birikim; satıra dokununca exact breakdown.
3. **Simülatör:** Nakit alışveriş, tek çekim/taksitli kart, kart ekstresini tam kapatma, finansman, nakit borç, ileri tarihli tek/tekrarlı ödeme, gelecek gelir, maaş ve maaş kullanım düzeni değişimi senaryoları; baseline ve scenario faiz yükünü karşılaştırır.
4. **Gelir & Ödemeler:** Maaş, diğer gelir, kredi, kredi kartı, geçici/taksitli ödeme ve büyük gider yönetimi.
5. **Ayarlar:** Maaş günü, bütçe, kart carry/açık faiz varsayımları, read-only düzen geçmişi ve development araçları.

Simülatörde **Simüle Et** yalnız bellekte hypothetical bir plan üretir. **Planı Uygula** açık onaydan sonra scenario türünü canonical finans kaydına dönüştürür; aynı application kimliği ikinci kez yükümlülük oluşturmaz. Uygulanan kayıt Gelir & Ödemeler içindeki doğru bölümde veya seçili kart detayında hemen açılabilir ve sonraki simulator baseline hesabına normal gerçek veri olarak girer.

Ayarlar, düzen geçmişini yalnız bilgi amaçlı gösterir. Kullanıcı bir sonraki değişikliğin başlayacağı maaşı seçer; uygulama eski kayıtları değiştirmeden yeni effective-dated event ekler. Yalnız henüz başlamamış planlanan değişiklik düzenlenebilir veya iptal edilebilir.

## Mimari

```text
CoinFlow.sln
├─ src/CoinFlow.Domain          # Saf, deterministic finans motoru
├─ src/CoinFlow.Application     # Kullanım senaryoları ve store sözleşmesi
├─ src/CoinFlow.Infrastructure  # SQLite, migration ve development seed
├─ src/CoinFlow.App             # .NET MAUI Android + MVVM UI
└─ tests/CoinFlow.Tests         # Unit ve SQLite entegrasyon testleri
```

Projection ve simulator aynı `FinancialProjectionCalculator` çekirdeğini kullanır. Ayrıntılar için [mimari belgeye](docs/ARCHITECTURE.md) bakın.

## Development seed

Fresh development ve production veritabanları finansal olarak boş açılır; otomatik seed çalışmaz. Development build'de Ayarlar altındaki bağımsız **Seed Data Yükle** aksiyonu şu kanonik planı yükler:

- Maaş: 01.01.2026'dan itibaren 115.000 TL, 01.01.2027'den itibaren 132.250 TL
- Garanti BBVA: 14.501,23 TL, 22 taksit
- Burgan Bank: 7.374,59 TL, 9 taksit
- Eminevim: 20.09.2026 28.167,40 TL; 20.10.2026 28.167,40 TL; 20.11.2026 55.492,20 TL
- Axess: limit 607.350 TL; devreden 35.201,77 TL; dönem içi 61.283,91 TL; exact future charges
- Yaşam bütçesi: 30.000 TL; başlangıç birikimi: 0 TL
- Kart carry ve finansman açığı aylık planlama faizi: `%5,00`
- Projection anchor: 20.08.2026; ilk projection maaşı: 10.09.2026
- İlk maaş kullanım düzeni: `UpcomingPeriod`

Seed yalnızca development build'de kullanıcı isteğiyle çalışır. Sabit kimliklerle upsert edildiği için boş veya mevcut veritabanına tekrar yüklenmesi kayıt çoğaltmaz. Ayrı **Verileri Sil** aksiyonu tüm finans kayıtlarını, strategy history'yi ve projection anchor/bütçelerini temizler; şemayı korur ve seed yüklemez. Kullanıcı boş durumda ilk maaşını kaydedince anchor bir kez oluşturulur ve maaş kullanım düzenini seçen onboarding açılır.

## Yerel doğrulama

Gereksinimler: .NET SDK 8, MAUI Android workload, JDK 17 ve Android SDK 34.

```powershell
dotnet restore CoinFlow.sln
dotnet test tests/CoinFlow.Tests/CoinFlow.Tests.csproj -c Release
dotnet build src/CoinFlow.App/CoinFlow.App.csproj -c Release
```

Development APK üretimi:

```powershell
dotnet publish src/CoinFlow.App/CoinFlow.App.csproj -f net8.0-android -c Release `
  -p:AndroidPackageFormat=apk -p:RunAOTCompilation=false `
  -p:CoinFlowDevBuild=true -p:CoinFlowVersion=0.0.0-dev `
  -p:CoinFlowBuildNumber=1 -p:CoinFlowCommit=local
```

## Migration

SQLite şema sürümü 7'dir. Mevcut kullanıcı verisi yerinde yükseltilir; v7 migration iki planlama faiz varsayımını `%5,00` ile başlatır. Eski global ödeme atama değeri bir kez ilk strategy history kaydına dönüştürülür ve runtime source of truth olmaktan çıkar. Eksik projection anchor upgrade tarihinde bir kez oluşturulur ve sonraki açılışlarda ilerletilmez. Eski kart aggregate alanları yeni kart modeline aktarılır. Kaldırılan mikro harcama, balance snapshot ve acil fon tabloları upgrade sırasında düşürülür.

## CI/CD

Mevcut GitHub Actions development ve stable workflow'ları korunmuştur. Development hattı test edip imzalı APK artifact'i üretir. Stable hattı repository secret'larındaki release keystore ile sürümlü APK üretir; release anahtarı repoya yazılmaz.
