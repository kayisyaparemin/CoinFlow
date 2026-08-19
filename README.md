# CoinFlow

CoinFlow, bir maaş gününden sonraki maaş gününe kadar ödenmesi gereken zorunlu ödemeleri ayırıp kalan parayı esnek bir **Daily Coin havuzuna** dönüştüren, Android öncelikli ve tamamen çevrimdışı bir kişisel finans uygulamasıdır.

Uygulama klasik “günlük limit aşıldı” yaklaşımını kullanmaz. Harcanmayan günlük alan havuzda birikir; kullanıcıya kalan dönem bütçesi ve bugünden itibaren sürdürülebilir yeni günlük bütçe gösterilir.

## MVP özellikleri

- Tarihe bağlı maaş planı ve ileri tarihli zamlar
- Bitiş tarihi veya taksit sayısı olan düzenli krediler
- Tek plan altında farklı tarih ve tutarlara sahip geçici ödemeler
- Exact posting/kesim/son ödeme tarihleriyle kredi kartı ekstre projeksiyonu
- Belirli son ödeme tarihine bağlı manuel kart ödeme planları
- Nakit, kart, yeni taksit ve diğer ödeme tipleriyle hızlı harcama
- Serbest bakiye snapshot'ıyla gerçek Current Actual ve düzeltme geçmişi
- Ayrı Daily Reward, sürdürülebilir Daily Coin ve Coin havuzu
- Önümüzdeki 12 maaş döneminin görünümü
- Mevcut borçları temel alan kredi kartı, nakit borç, banka kredisi ve nakit alışveriş simülasyonu
- Günlük bütçeden ayrı acil durum tamponu
- Açılıp kapatılabilen oyunlaştırma dili
- SQLite ile cihazda kalıcı, hesapsız ve internetsiz kullanım
- Development build bilgisi: sürüm, commit ve build numarası

## Proje yapısı

```text
CoinFlow.sln
├─ src/CoinFlow.Domain          # Saf ve deterministic hesaplama motoru
├─ src/CoinFlow.Application     # Kullanım senaryoları ve veri sözleşmeleri
├─ src/CoinFlow.Infrastructure  # SQLite ve development seed
├─ src/CoinFlow.App             # .NET MAUI Android + MVVM UI
└─ tests/CoinFlow.Tests         # Unit ve SQLite entegrasyon testleri
```

Ayrıntılı kararlar için [mimari belgesine](docs/ARCHITECTURE.md), teslim kapsamı için [TODO listesine](TODO.md) bakın.

## Hesaplama kuralları

- Maaş dönemi başlangıç dahil, sonraki maaş günü hariçtir: `[başlangıç, bitiş)`.
- Maaş günüyle aynı gün vadesi gelen ödeme yeni başlayan döneme dahildir.
- Ayın 29/30/31'i ilgili ayda yoksa ayın son günü kullanılır.
- Maaş, dönem başlangıcında yürürlükte olan en yeni salary schedule kaydından gelir.
- Bütün para hesapları `decimal` kullanır; taksit kuruş farkı son taksite yazılır.
- Ana ekranın kaynağı, aktif dönemdeki son serbest bakiye snapshot'ı ve ondan sonraki nakit/diğer harcamalardır. Snapshot yoksa yalnızca takip maaş döneminin başında başladıysa teorik dönem bütçesi fallback olarak kullanılabilir.
- Daily Reward snapshot tutarının snapshot tarihinden sonraki maaşa kalan günlere bölünmüş sabit referansıdır. Sürdürülebilir Coin, gerçek kalan bakiyenin bugünden sonraki maaşa kalan günlere yeniden bölünmüş halidir.
- Kart harcaması nakit havuzunu anında azaltmaz; kart borcunu ve gelecek ödemeyi artırır.
- Kart işlemleri exact posting tarihinde ilk uygun statement close'a girer; ödeme tarihi close sonrasındaki ilk uygun due day'dir. Kart faizi ve vergiler MVP'de hesaplanmaz.
- Planlanan tampon katkısı hedefte kalan tutarla sınırlanır. Manuel aktarımın rezerve katkıya kadar olan bölümü Current Actual'dan ikinci kez düşülmez.
- Simülasyon hiçbir kayıt oluşturmaz; mevcut dönemde Current Actual, gelecek dönemlerde Projected Free Budget kullanır ve kart senaryosu aynı statement motorunu yeniden çalıştırır.

## Mevcut veritabanı migration'ı

Uygulama veritabanını silip yeniden oluşturmaz. Açılışta yeni snapshot, kart ödeme planı ve tampon transfer tabloları idempotent biçimde eklenir. Eski kart alanları şu varsayımla korunur:

- `LastStatementRemaining` → açılış `CarriedBalance`
- `CurrentCycleSpending` → açılış `UnbilledSpending`
- Eski `card_installments` kayıtları → aynı exact tarihte `CardCharge`
- Eski tek manuel ödeme → migration gününden sonraki ilk uygun due date'e bağlı ödeme planı

Migration günü kart açılış bakiyesinin referans tarihi olur; veri sessizce silinmez.

## Gereksinimler

- .NET SDK `8.0.424` (kök `global.json` tarafından sabitlenir)
- .NET MAUI Android workload
- JDK 17
- Android SDK Platform 34 ve Build Tools 34.0.0

```bash
dotnet workload install maui-android
```

Android SDK kurulumu için Android Studio SDK Manager veya .NET Android araçlarının önerdiği kurulum yolu kullanılabilir.

## Test ve build

Kök klasörde:

```bash
dotnet restore CoinFlow.sln
dotnet test tests/CoinFlow.Tests/CoinFlow.Tests.csproj -c Release
dotnet build src/CoinFlow.App/CoinFlow.App.csproj -f net8.0-android -c Debug
```

Komut satırı Android SDK/JDK'yı otomatik bulamıyorsa yolları açıkça verin:

```powershell
dotnet build src/CoinFlow.App/CoinFlow.App.csproj `
  -f net8.0-android -c Debug `
  -p:AndroidSdkDirectory="C:\Android\Sdk" `
  -p:JavaSdkDirectory="C:\Program Files\Microsoft\jdk-17"
```

Debug APK normalde şu dizinde oluşur:

```text
src/CoinFlow.App/bin/Debug/net8.0-android/*-Signed.apk
```

APK'yı USB debugging açık bir cihaza yüklemek için:

```bash
adb install -r path/to/CoinFlow.apk
```

## Development seed

Debug/development build, yalnızca boş bir veritabanında örnek verileri ekler:

- 115.000 TL maaş, maaş günü 10
- 1 Ocak 2027 itibarıyla 132.250 TL maaş
- Garanti'de 22 × 14.501,23 TL ve Burgan On Dijital'de 9 × 7.374,59 TL kalan kredi taksiti
- Akbank Axess'te 61.283,91 TL dönem içi harcama, 35.201,77 TL son ekstreden kalan ve Eylül–Kasım gelecek taksitleri
- 150.000 TL hedef / 32.000 TL mevcut acil tampon
- 19 Ağustos 2026 için 11.000 TL `Serbest bakiye` snapshot'ı; 22 gün için 500 TL Daily Reward

Stable build development seed eklemez ve ilk açılışta boş finans verisiyle başlar.

## Latest Development Build

`.github/workflows/dev-build.yml`, `main` branch'e her push'ta ve manuel çalıştırmada:

1. .NET 8, JDK 17 ve MAUI Android workload'u hazırlar.
2. NuGet restore ve unit testleri çalıştırır.
3. Commit/build metadata içeren development APK üretir.
4. APK'nın imzasını ve `com.coinflow.mobile` package ID'sini doğrular.
5. `coinflow-dev-apk` adlı Actions artifact'ını 14 gün saklar.
6. Tek bir `dev-latest` prerelease'i `CoinFlow-dev-latest.apk` ile yeniler.

İndirme yolları:

- GitHub → **Actions** → son başarılı **Development build** → `coinflow-dev-apk`
- GitHub → **Releases** → **CoinFlow Development Build** → `CoinFlow-dev-latest.apk`

Bu kanal test sürümüdür. Aynı anda yeni bir `main` push'u gelirse eski development çalışması iptal edilir.

Günlük geliştirme akışı:

```bash
git add .
git commit -m "Add daily coin history"
git push
```

## Stable Release

`.github/workflows/release.yml`, yalnızca tam `vX.Y.Z` tag'i için çalışır. Tag sürümün tek kaynağıdır; örneğin `v1.4.2`, uygulamada `ApplicationDisplayVersion=1.4.2` olur. Android version code için GitHub run number kullanılır.

Repository → **Settings → Secrets and variables → Actions** bölümünde şu secret'lar zorunludur:

- `ANDROID_KEYSTORE_BASE64`
- `ANDROID_KEYSTORE_PASSWORD`
- `ANDROID_KEY_ALIAS`
- `ANDROID_KEY_PASSWORD`

Keystore'u base64'e dönüştürme örnekleri:

```bash
base64 -w 0 coinflow-release.keystore
```

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("coinflow-release.keystore"))
```

Stable workflow secret'lardan biri yoksa anlaşılır hatayla durur. Private keystore bellekte/runner geçici dizininde çözülür, loglanmaz, işlem sonunda `always()` adımında silinir. Yalnız doğrulanmış release-key imzalı APK yayınlanır; unsigned APK için boş release oluşturulmaz.

Release akışı:

```bash
git tag v1.0.0
git push origin v1.0.0
```

Başarılı çalışmada prerelease/draft olmayan, otomatik notlu GitHub Release ve `CoinFlow-v1.0.0.apk` oluşur. Stable release çalışmaları birbirini iptal etmez.

## Önerilen branch protection

Küçük kişisel repository için zorunlu değildir; `main` üzerinde şu ayarlar önerilir:

- Force push kapalı
- `Development build / Test and build development APK` status check zorunlu
- Test/build başarılı olmadan merge kapalı
- İstenirse pull request zorunluluğu

## Gizlilik

Uygulama internet izni istemez, hesap açmaz ve veriyi `FileSystem.AppDataDirectory/coinflow.db3` SQLite dosyasında tutar. Android backup kapalıdır. Keystore veya credential dosyaları `.gitignore` kapsamındadır.
