# CoinFlow

CoinFlow, maaş gününden bir sonraki maaş gününe kadar olan dönemi esas alan, zorunlu ödemelerden sonra kalan yaşam bütçesini ve tahmini birikimi gösteren Android öncelikli, çevrimdışı bir kişisel finans uygulamasıdır.

Uygulama mikro harcama takibi yapmaz. Ana kavramlar maaş dönemi, toplam gelir, zorunlu ödeme, yaşam bütçesi ve birikim kapasitesidir.

## Finans modeli

Her maaş dönemi `[başlangıç, sonraki maaş günü)` aralığıdır. Dönem başlangıcı dahildir, sonraki maaş günü dahil değildir.

Kullanıcı, ödemelerin maaş bütçesine nasıl atanacağını global olarak seçebilir:

- **Gelecek dönemi karşılarım:** Maaş tarihi dahil, sonraki maaş tarihi hariç ödemeler aynı maaşa atanır.
- **Geçmiş dönemi kapatırım:** Önceki maaş tarihinden sonraki ödemeler mevcut maaşa atanır; maaş günündeki ödeme geriye kaymaz.

Kredi, kart ve planların gerçek ödeme tarihleri bu tercihten etkilenmez. `PaymentAssignmentResolver` yalnız bütçe atamasını yapar; maaştan önce vadesi gelen ödemeler ayrıca uyarı olarak gösterilir.

```text
Toplam Gelir = Maaş + Döneme denk gelen diğer gelirler
Zorunlu Ödeme = Krediler + Kart ödemeleri + geçici/taksitli/diğer planlı ödemeler
Zorunlu Ödemeler Sonrası = Toplam Gelir - Zorunlu Ödeme
Tahmini Birikim Kapasitesi = Zorunlu Ödemeler Sonrası - Yaşam Bütçesi - Planlı büyük nakit giderler
Dönem Sonu Tahmini Birikim = Dönem Başı Birikim + Tahmini Birikim Kapasitesi
```

Maaş, tek seferlik gelir, kredi, kart harcaması, kart vadesi, geçici ödeme ve büyük giderlerin tamamı exact date ile ilgili maaş dönemine yerleşir. Ayın 29/30/31'i için takvim sonu kırpma kuralı merkezi olarak uygulanır.

## Ekranlar

Alt navigasyon tam olarak dört ana bölüm içerir:

1. **Ana Sayfa:** Aktif maaş dönemi özeti, yaklaşan ödemeler, 12 dönem özeti ve en sıkışık dönem.
2. **12 Aylık:** Her dönem için gelir, zorunlu ödeme, yaşam bütçesi, birikim kapasitesi ve dönem sonu birikim; satıra dokununca exact breakdown.
3. **Simülatör:** Nakit alışveriş, tek çekim/taksitli kart, finansman, nakit borç, ileri tarihli tek/tekrarlı ödeme, gelecek gelir ve maaş değişimi senaryoları.
4. **Gelir & Ödemeler:** Maaş, diğer gelir, kredi, kredi kartı, geçici/taksitli ödeme ve büyük gider yönetimi.

Ayarlar, ikincil bir rota olarak maaş günü, maaş kullanım şekli, aylık yaşam bütçesi ve başlangıç birikimini düzenler. Development build'de kanonik veriyi yeniden yükleme seçeneği bulunur.

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

Boş development veritabanı deterministik olarak şu kanonik planla açılır:

- Maaş: 01.01.2026'dan itibaren 115.000 TL, 01.01.2027'den itibaren 132.250 TL
- Garanti BBVA: 14.501,23 TL, 22 taksit
- Burgan Bank: 7.374,59 TL, 9 taksit
- Eminevim: 20.09.2026 28.167,40 TL; 20.10.2026 28.167,40 TL; 20.11.2026 55.492,20 TL
- Axess: limit 607.350 TL; devreden 35.201,77 TL; dönem içi 61.283,91 TL; exact future charges
- Yaşam bütçesi: 30.000 TL; başlangıç birikimi: 0 TL

Seed yalnızca development ortamında ve boş veritabanında çalışır; tekrar açılışta kayıt çoğaltmaz. Production boş veritabanı otomatik demo veri almaz.

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

SQLite şema sürümü 5'tir. Mevcut kullanıcı verisi yerinde yükseltilir; eski kart aggregate alanları yeni kart modeline aktarılır ve ödeme atama tercihi olmayan kurulumlar bir kez `UpcomingPeriod` varsayılanını alır. Kaldırılan mikro harcama, snapshot ve acil fon tabloları upgrade sırasında düşürülür. Development verisini kanonik duruma getirmek için ayarlardaki açık onaylı sıfırlama kullanılabilir.

## CI/CD

Mevcut GitHub Actions development ve stable workflow'ları korunmuştur. Development hattı test edip imzalı APK artifact'i üretir. Stable hattı repository secret'larındaki release keystore ile sürümlü APK üretir; release anahtarı repoya yazılmaz.
