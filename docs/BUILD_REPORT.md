# CoinFlow MVP doğrulama raporu

Tarih: 19 Ağustos 2026  
Ortam: Windows, .NET SDK 8.0.424, JDK 17, Android SDK 34

## Sonuçlar

- `CoinFlow.Domain`, `CoinFlow.Application` ve `CoinFlow.Infrastructure`: başarılı
- Android Debug build: başarılı, 0 uyarı, 0 hata
- Android Release development publish: başarılı
- Unit + SQLite migration/entegrasyon testleri: 53/53 başarılı
- APK package ID: `com.coinflow.mobile`
- APK sürümü: `0.0.0-dev`, build code `1`
- APK imzası: Android v1/v2/v3 doğrulandı (development debug key)
- APK zip alignment: doğrulandı
- Stable signing parametreleri: geçici private test keystore ile doğrulandı
- Stable version aktarımı: `1.2.3` / version code `123` doğrulandı
- Geçici signing keystore: test sonunda silindi
- GitHub Actions YAML: iki workflow dosyası parse edildi

## Development APK

Dosya: `src/CoinFlow.App/bin/Release/net8.0-android/com.coinflow.mobile-Signed.apk`
Boyut: 25.549.299 byte (24,37 MB)
SHA-256: `18957C7A5C41D84C791986A359D676C1C808B288997BB7D8DA1151E1660669C9`

Bu dosya development/debug anahtarıyla imzalanmıştır ve doğrudan test cihazına kurulabilir. Production dağıtımı için stable workflow'daki private release keystore kullanılmalıdır.
