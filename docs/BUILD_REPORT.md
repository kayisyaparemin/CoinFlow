# CoinFlow doğrulama raporu

Tarih: 20 Ağustos 2026

Ortam: Windows, .NET SDK 8.0.424, JDK 17.0.20, Android SDK/Build Tools 34

## Sonuçlar

- Release test paketi: 62/62 başarılı; 0 başarısız, 0 atlanan
- Android Release build (varsayılan AOT): başarılı; 0 uyarı, 0 hata
- Android development Release publish (`RunAOTCompilation=false`): başarılı
- Package ID: `com.coinflow.mobile`
- Version: `0.0.0-dev`; version code: `1`
- Minimum SDK: 21; target/compile SDK: 34
- APK imzası: v1/v2/v3 doğrulandı; development debug certificate
- APK zip alignment: başarılı
- Git diff whitespace kontrolü: başarılı
- GitHub Actions development/stable workflow dosyaları değiştirilmedi

## Development APK

Dosya: `src/CoinFlow.App/bin/Release/net8.0-android/publish/com.coinflow.mobile-Signed.apk`

Boyut: 25.590.675 byte

SHA-256: `7FCDD960B45128594454D371C21F507496BFA4EDC4CD66EA114CC23636101A7A`

Bu APK yalnız cihaz testi için development/debug anahtarıyla imzalanmıştır. Production dağıtımı, `release.yml` içindeki repository secret tabanlı private release keystore ile yapılmalıdır.
