# CoinFlow mimarisi

## Hedef

CoinFlow, maaştan bir sonraki maaşa kadar olan gerçek takvim aralığını esas alan, tamamen çevrimdışı çalışan Android öncelikli bir .NET MAUI uygulamasıdır. Hesaplama kodu arayüz ve SQLite'tan bağımsızdır; aynı girdiler her zaman aynı sonucu üretir.

## Solution yapısı

| Proje | Sorumluluk |
|---|---|
| `CoinFlow.Domain` | Finans modelleri, takvim kuralları, maaş dönemi, Daily Coin, kart projeksiyonu ve simülasyon hesapları |
| `CoinFlow.Application` | Kullanım senaryoları, dashboard/future-month orkestrasyonu ve veri deposu sözleşmesi |
| `CoinFlow.Infrastructure` | SQLite tabloları, model eşleme, kalıcı veri ve development seed |
| `CoinFlow.App` | Türkçe .NET MAUI görünümü, MVVM view-model'leri, Android uygulama yaşam döngüsü |
| `CoinFlow.Tests` | Deterministic business logic ve SQLite entegrasyon testleri |

Bağımlılık yönü: `App -> Application -> Domain`; `Infrastructure -> Application + Domain`. Domain hiçbir platform paketine bağlı değildir.

## Tarih politikası

- Maaş dönemi başlangıç dahil, sonraki maaş günü hariç olacak şekilde `[başlangıç, bitiş)` aralığıdır.
- Maaş günüyle aynı gün vadesi gelen ödeme yeni başlayan döneme aittir.
- Tercih edilen gün ilgili ayda yoksa o ayın son geçerli günü kullanılır. Örneğin maaş günü 31 ise Şubat maaşı 28/29 Şubat'tır.
- Tüm zorunlu ödemeler gerçek vade tarihine göre döneme alınır. Takvim sıkışması nedeniyle bir maaş döneminde iki aylık ödeme tarihi oluşursa ikisi de bütçeden düşülür.
- Maaş tutarı, dönemin başlangıç tarihinde yürürlükte olan en yeni salary schedule kaydıdır.
- Tarihler veritabanında kültürden bağımsız `yyyy-MM-dd` olarak saklanır.

## Para politikası

- Bütün para değerleri `decimal` kullanır.
- Kullanıcıya gösterilen günlük tutarlar iki ondalığa, `MidpointRounding.AwayFromZero` ile yuvarlanır.
- Taksit bölmesindeki kuruş farkı son taksite eklenir; toplam tutar değişmez.
- Negatif dönem bütçesi gizlenmez. Böyle bir durumda Daily Coin negatif olabilir ve UI suçlayıcı olmayan, nötr dil kullanır.

## Daily Coin

- Temel Daily Coin = dönem harcanabilir bütçesi / dönemin gerçek gün sayısı.
- Günün coin'i dönem başlangıç gününde kullanılabilir hale gelir.
- Coin havuzu = bugüne kadar açılan coin - nakit/diğer harcamalar.
- Kart harcaması nakit havuzunu anında düşürmez; kartın dönem içi harcamasına ve sonraki ödeme projeksiyonuna eklenir.
- Sürdürülebilir günlük bütçe = kalan dönem bütçesi / bugünden sonraki maaşa kalan harcama günü.

## Kredi kartı varsayımı

MVP faiz hesaplamaz. İlk projeksiyon ayında kullanıcı seçimine göre manuel veya asgari ödeme uygulanır; sonraki aylarda asgari ödeme devam eder. Dönem içi harcama ve gelecek taksitler devreden bakiyeye eklenir. Hesaplama motoru ileride faiz stratejisi eklenebilecek ayrı bir sınıftadır.

## SQLite veri modeli

- `salary_schedule`
- `loans`
- `payment_plans` / `payment_installments`
- `credit_cards` / `card_installments`
- `expenses`
- `emergency_fund`
- `settings`

Aggregate alt kayıtları (plan ve kart taksitleri) ayrı tablolarda tutulur. Uygulama ilk açılışta tabloları idempotent biçimde oluşturur. Development derlemesi boş veritabanını örnek verilerle doldurur; stable derleme boş bir kullanıcı verisiyle başlar.

## Build kanalları

- Development: `main` push veya manuel çalıştırma; test, debug-signed APK, Actions artifact ve tek mutable `dev-latest` prerelease.
- Stable: yalnız `vX.Y.Z` etiketi; tag sürüm kaynağıdır, private keystore zorunludur, imzasız çıktı yayınlanmaz.
- Business logic her iki kanalda aynıdır. Yalnız build etiketi, commit ve build numarası farklıdır.
