# CoinFlow doğrulama raporu

Tarih: 20 Ağustos 2026

Ortam: Windows, .NET SDK 8.0.424, JDK 17.0.20, Android SDK/Build Tools 34

## Sonuçlar

- Release test paketi: 88/88 başarılı; 0 başarısız, 0 atlanan
- Android Release build (varsayılan AOT): başarılı; 0 uyarı, 0 hata
- PaymentAssignmentMode Android Release build: başarılı; 0 uyarı, 0 hata
- Android development Release build (`RunAOTCompilation=false`): başarılı
- Package ID: `com.coinflow.mobile`
- Version: `0.0.0-dev`; version code: `1`
- Minimum SDK: 21; target/compile SDK: 34
- APK imzası: v1/v2/v3 doğrulandı; development debug certificate
- APK zip alignment: başarılı
- Git diff whitespace kontrolü: başarılı
- GitHub Actions development/stable workflow dosyaları değiştirilmedi

## Payment assignment doğrulaması

- `UpcomingPeriod` ve `PreviousPeriod` boundary tabloları doğrulandı.
- Kartlar `PaymentDueDate`, krediler ve planlar kendi exact date değerleri üzerinden atanıyor.
- Maaş günü ödemesi iki modda da aynı maaş bütçesinde kalıyor.
- `PaymentBeforeSalary`, restart persistence, legacy migration, Dashboard/12 Aylık yeniden hesaplama ve simulator override testleri başarılı.

Development APK, `main` push sonrasında `dev-build.yml` tarafından debug anahtarıyla imzalanıp `dev-latest` prerelease asset'i olarak yayımlanır. Production dağıtımı, `release.yml` içindeki repository secret tabanlı private release keystore ile yapılmalıdır.
