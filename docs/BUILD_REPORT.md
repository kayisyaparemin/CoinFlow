# CoinFlow doğrulama raporu

Tarih: 21 Ağustos 2026

Ortam: Windows, .NET SDK 8.0.424, JDK 17.0.20, Android SDK/Build Tools 34

## Sonuçlar

- Test paketi: 119/119 başarılı; 0 başarısız, 0 atlanan
- Fresh development/production SQLite başlangıcı: otomatik seed yok, boş plan geçerli
- İlk maaş → anchor → tek initial strategy onboarding regresyonu: başarılı
- Clear data ve açık/idempotent canonical seed entegrasyon testleri: başarılı
- Üç effective-dated strategy event'i ve resolver aralık regresyonu: başarılı
- Carry-over deficit exact-decimal, devam, recovery, positive opening, target amount ve simulator risk regresyonları: başarılı
- Kart carry faizi: asgari ödeme, tam ödeme, zero-rate, aylık compound ve exact iki hane yuvarlama regresyonları başarılı
- Finansman açığı faizi: negatif principal, compound, recovery ve kart faiziyle ayrı toplam regresyonları başarılı
- 12 dönem faiz özeti ve simulator baseline/scenario faiz artışı/tasarrufu regresyonları başarılı
- Ortak Dönem Detayı presenter mapping, zero-row filtreleme, dört bağımsız ödeme satırı, deficit/interest görünürlüğü, transition filtreleme ve simulator delta regresyonları başarılı
- Simulator apply canonical mapping, idempotency, transaction rollback, yeni baseline ve gerçek SQLite restart persistence regresyonları: başarılı
- Android development Release build: başarılı; 0 warning, 0 error
- Package ID: `com.coinflow.mobile`
- Version: `0.0.0-dev`; version code: `1`
- Minimum SDK: 21; target/compile SDK: 34
- APK imzası: v1/v2/v3 doğrulandı; development debug certificate
- APK zip alignment: başarılı
- Shell Flyout Android XAML/C# derlemesi: başarılı; bottom TabBar kaldırıldı
- 12 Aylık / Simulator compact kart → ortak full-screen Dönem Detayı route ve Android XAML binding derlemesi: başarılı
- Git diff whitespace kontrolü: başarılı
- GitHub Actions development/stable workflow dosyaları değiştirilmedi
- İlk current snapshot, frozen plan ve history'siz first-install davranışı doğrulandı.
- Review due, üç adımlı actual girişi, optional revision ve atomik finalize/restart persistence doğrulandı.
- Future settings'in frozen history'yi değiştirmediği ve actual living'in future living varsayımını otomatik değiştirmediği doğrulandı.
- Actual kart ödemesinin exact statement'a bir kez uygulanması, unpaid kredinin outstanding kalması ve 31 → Şubat review tarihi doğrulandı.
- “Eylülde kur + ekimde güncelle” ile “ilk kez ekimde aynı current state ile kur” projection eşdeğerliği doğrulandı; yalnız ilk senaryoda history oluştu.
- Aylık review tek toplam yaşam gideriyle, sıfır günlük transaction girilerek tamamlanabiliyor.

## Payment assignment doğrulaması

- `UpcomingPeriod` ve `PreviousPeriod` boundary tabloları doğrulandı.
- Kartlar `PaymentDueDate`, krediler ve planlar kendi exact date değerleri üzerinden atanıyor.
- Maaş günü ödemesi iki modda da aynı maaş bütçesinde kalıyor.
- `PaymentBeforeSalary`, restart persistence, legacy migration, Dashboard/12 Aylık yeniden hesaplama ve simulator override testleri başarılı.
- `Previous → Upcoming → Previous` history çözümlemesi eski event'leri değiştirmeden doğrulandı.
- Negatif `EndingProjectedSavings`, yeni obligation veya kart bakiyesi oluşturmadan sonraki dönemin opening değerine taşınıyor; double-count yok.

Development APK, `main` push sonrasında `dev-build.yml` tarafından debug anahtarıyla imzalanıp `dev-latest` prerelease asset'i olarak yayımlanır. Production dağıtımı, `release.yml` içindeki repository secret tabanlı private release keystore ile yapılmalıdır.
