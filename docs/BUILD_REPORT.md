# CoinFlow MVP doğrulama raporu

Tarih: 19 Ağustos 2026  
Ortam: Windows, .NET SDK 8.0.424, JDK 17, Android SDK 34

## Sonuçlar

- `CoinFlow.Domain`, `CoinFlow.Application` ve `CoinFlow.Infrastructure`: başarılı
- Android Debug build: başarılı, 0 uyarı, 0 hata
- Android Release development publish: başarılı
- Unit + SQLite entegrasyon testleri: 27/27 başarılı
- APK package ID: `com.coinflow.mobile`
- APK sürümü: `0.0.0-dev`, build code `1`
- APK imzası: Android v1/v2/v3 doğrulandı (development debug key)
- APK zip alignment: doğrulandı
- Stable signing parametreleri: geçici private test keystore ile doğrulandı
- Stable version aktarımı: `1.2.3` / version code `123` doğrulandı
- Geçici signing keystore: test sonunda silindi
- GitHub Actions YAML: iki workflow dosyası parse edildi

## Development APK

Dosya: `artifacts/CoinFlow-dev-local.apk`  
Boyut: 36.851.642 byte (35,14 MB)  
SHA-256: `47F6C832004C35831FF21D15A6CED00C27967D087DE313AE98CA1050A321E32C`

Bu dosya development/debug anahtarıyla imzalanmıştır ve doğrudan test cihazına kurulabilir. Production dağıtımı için stable workflow'daki private release keystore kullanılmalıdır.
