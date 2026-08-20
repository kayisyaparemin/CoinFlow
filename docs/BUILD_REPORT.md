# CoinFlow doğrulama raporu

Tarih: 20 Ağustos 2026

Ortam: Windows, .NET SDK 8.0.424, JDK 17.0.20, Android SDK/Build Tools 34

## Sonuçlar

- Release test paketi: 89/89 başarılı; 0 başarısız, 0 atlanan
- Fresh development/production SQLite başlangıcı: otomatik seed yok, boş plan geçerli
- İlk maaş → anchor → tek initial strategy onboarding regresyonu: başarılı
- Clear data ve açık/idempotent canonical seed entegrasyon testleri: başarılı
- Üç effective-dated strategy event'i ve resolver aralık regresyonu: başarılı
- Carry-over deficit exact-decimal, devam, recovery, positive opening, target amount ve simulator risk regresyonları: başarılı
- Android development Release build (`RunAOTCompilation=false`): başarılı
- Package ID: `com.coinflow.mobile`
- Version: `0.0.0-dev`; version code: `1`
- Minimum SDK: 21; target/compile SDK: 34
- APK imzası: v1/v2/v3 doğrulandı; development debug certificate
- APK zip alignment: başarılı
- Shell Flyout Android XAML/C# derlemesi: başarılı; bottom TabBar kaldırıldı
- Git diff whitespace kontrolü: başarılı
- GitHub Actions development/stable workflow dosyaları değiştirilmedi

## Payment assignment doğrulaması

- `UpcomingPeriod` ve `PreviousPeriod` boundary tabloları doğrulandı.
- Kartlar `PaymentDueDate`, krediler ve planlar kendi exact date değerleri üzerinden atanıyor.
- Maaş günü ödemesi iki modda da aynı maaş bütçesinde kalıyor.
- `PaymentBeforeSalary`, restart persistence, legacy migration, Dashboard/12 Aylık yeniden hesaplama ve simulator override testleri başarılı.
- `Previous → Upcoming → Previous` history çözümlemesi eski event'leri değiştirmeden doğrulandı.
- Negatif `EndingProjectedSavings`, yeni obligation veya kart bakiyesi oluşturmadan sonraki dönemin opening değerine taşınıyor; double-count yok.

Development APK, `main` push sonrasında `dev-build.yml` tarafından debug anahtarıyla imzalanıp `dev-latest` prerelease asset'i olarak yayımlanır. Production dağıtımı, `release.yml` içindeki repository secret tabanlı private release keystore ile yapılmalıdır.
