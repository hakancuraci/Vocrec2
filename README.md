# Ses Kayıt

Windows 10/11 x64 için, bilgisayar çıkışlarını ve mikrofonları aynı anda tek bir stereo M4A dosyasına kaydeden masaüstü uygulaması. Ses cihazlarından WASAPI ile alınır; sanal ses kablosu ve Stereo Mix gerektirmez. Kayıtlar bilgisayarınızda kalır; uygulamada sunucuya yükleme veya telemetri yoktur.

## Çalıştırma

1. `SesKayit-Windows-x64.zip` arşivini bir klasöre çıkartın.
2. `SesKayit.exe` dosyasını açın. .NET kurulumu veya yönetici yetkisi gerekmez.
3. Varsayılan **Otomatik kaynak seçimi** açıkken tüm ses çıkışları, Windows iletişim mikrofonu ve diğer uygulamaların kullandığı mikrofonlar seçilir. Elle seçim yapmak için otomatik modu kapatın. **Bilgisayar sesi** bölümünde YouTube, Teams vb. uygulamaların ses verdiği çıkışları işaretleyin. Teams kulaklığa, YouTube hoparlöre ses veriyorsa ikisini de seçin.
4. **Mikrofonlar** bölümünde kaydedilecek mikrofonları seçin. Birden fazla çıkış ve mikrofon aynı anda seçilebilir.
5. Kayıt klasörünü ve kaliteyi seçip **Kaydı başlat** düğmesine basın.
6. **Durdur ve kaydet** düğmesine bastığınızda son ses tamponları işlenir ve dosya tamamlanır. **Klasörü aç** ile ulaşabilirsiniz. Pencereyi kapatmak da sürmekte olan kaydı tamamlar.

Ses seviyeleri kayıt sırasında değiştirilebilir. Varsayılan %70 değerleri karışımda taşmayı azaltır. Tepe sınırı uyarısı çıkarsa seviyeleri düşürün. Göstergeler yalnızca kayıt sırasında çalışır. Klasör, kalite, cihaz seçimi ve seviyeler sonraki açılış için saklanır.

## Canlı kanallar ve otomatik algılama

- Kanal listelerinin altında Windows ses oturumlarının uygulama adları görünür. Örneğin Teams `ms-teams`, YouTube ise `chrome`, `msedge` veya kullandığınız tarayıcının adıyla görünebilir; sekme/site adı her zaman belirlenemez.
- `ses var` o anda ölçülen ses seviyesini; `oturum açık · ses yok` uygulamanın açık ses oturumunu; `mikrofonu kullanıyor · ses yok` başka uygulamanın sessiz mikrofon oturumunu belirtir. Bu bilgiler yaklaşık her saniye yenilenir; çok kısa sesler göstergede kaçabilir fakat seçili kaynakta kayıt devam eder.
- Uygulama kendi kayıt oturumunu başka uygulamanın mikrofon kullanımı olarak saymaz. Kayıt başlamadan yapılan oturum taraması mikrofon sesini açıp dinlemez.
- **Teams’in yazılımsal sessiz düğmesi evrensel Windows API’leriyle güvenilir biçimde belirlenemez. Teams’te sessize almak bu uygulamanın mikrofon kaydını durdurmaz.** Yerel kaydı susturmak için mikrofon seviyesini %0 yapın veya kayıttan önce elle modda mikrofonu kaldırın.
- Otomatik modda varsayılan iletişim mikrofonu konuşma başlamadan kayda hazırdır. Diğer uygulamalar mikrofonlarını açık tuttuğunda, konuşmasalar bile `kullanılıyor` olarak görünebilir. Seçim ses eşiğine bağlanmaz; sessiz konuşmalar ve ilk hece korunur.
- Kaynaklar otomatik seçilir, ancak kayıt yine **Kaydı başlat** düğmesiyle başlar. Uygulama açılışında kendi kendine kayıt başlatmaz. Uygulama listesi bilgilendirme içindir; uygulamaya özel ayrı ses yakalama yapılmaz.
- Bazı sürücüler/korumalı oturumlar uygulama bilgisi vermeyebilir. Oturum bilgisi okunamıyorsa uyarı gösterilir; elle cihaz seçimi kullanılabilir.

## Kalite ve boyut

Çıktı 48 kHz, stereo, AAC-LC ses içeren `.m4a` dosyasıdır. Diskte ara WAV dosyası oluşturulmaz. Bellek kullanımı kayıt süresiyle büyümez; her cihaz için en fazla üç saniyelik ses tamponu ayrılır.

| Kalite | Yaklaşık 1 saatlik boyut | Kullanım |
| --- | --- | --- |
| 128 kb/sn | 58 MB | Konuşma ve toplantı |
| 160 kb/sn | 72 MB | Konuşma ve müzik için dengeli |
| 192 kb/sn (varsayılan) | 86 MB | Müzik için daha yüksek kalite |

Değerler ondalık MB olarak hesaplanmıştır; kapsayıcı ek yükü nedeniyle küçük farklılıklar olabilir. OBS'te benzer AAC ayarlarıyla kullanılan kodlama sınıfındadır; kaynak cihazı, mikrofon ve ortam kalitesi sonucu etkiler. Gürültü giderme, yankı engelleme veya OBS eklentileri içermez.

## Kapsam ve cihazlar

- Her işaretli çıkışın **tüm sesi** alınır. Uygulama bazlı ayırma yapılmaz. Aynı çıkıştaki YouTube ve Teams birlikte kaydedilir. Windows ses mikserinden uygulamaların çıkışını kontrol edin.
- Kaynaklar tek stereo dosyada birleştirilir; ayrı düzenlenebilir çok kanallı iz dosyaları üretilmez. Çok kanallı hoparlör çıkışları stereo olarak birleştirilir.
- Farklı örnekleme hızları 48 kHz'e dönüştürülür. Bağımsız cihaz saatleri donanımsal olarak senkron değildir; uzun kayıtlardaki kayma ve cihaz gecikmeleri Windows üzerinde ayrıca doğrulanmalıdır.
- **Otomatik mod:** Kaynaklar yaklaşık her saniye taranır; kayıt sırasında bağlanan çıkışlar, değişen varsayılan iletişim mikrofonu ve başka uygulamaların açtığı mikrofonlar kayda eklenir. Çıkarılan cihazlar kayıttan kaldırılır; kalan kaynaklar aynı dosyaya devam eder. Tüm kaynaklar kaybolursa dosyaya sessizlik yazılır ve yeniden bağlananlar eklenir. Yeni bir cihazdaki ilk yaklaşık bir saniyelik ses kaçabilir. **Elle mod:** Seçim kayıt süresince sabittir; cihaz kaybında kayıt hata ile durur.
- Aynı fiziksel sesi hem sanal bir çıkıştan hem gerçek çıkıştan yakalamak çift sese neden olabilir. Hoparlör sesinin mikrofona da girmesini azaltmak için kulaklık kullanın.
- Windows Ayarları → Gizlilik ve güvenlik → Mikrofon bölümünde masaüstü uygulamalarına mikrofon erişimini açın. Windows N sürümlerinde AAC için Media Feature Pack gerekebilir. Korumalı içerikler veya özel sürücüler loopback yakalamayı engelleyebilir.
- Kaydederken uygulamayı zorla kapatmayın; uykuya geçmeyin veya ses cihazını değiştirmeyin. Tamamlanmamış dosyalar `.partial.m4a` adıyla kalır; çökme veya disk hatası sonrasında oynatılabilecekleri garanti edilmez. Başarılı kapanışta dosya normal `.m4a` adına çevrilir.
- Kayıtlar otomatik bölünmez; klasörde yeterli boş alan bulunmalıdır.

## Kaynaktan derleme

.NET 8 SDK gerekir. PowerShell'de proje klasöründen:

```powershell
dotnet test Tests/SesKayit.Tests.csproj -c Release
dotnet publish App/SesKayit.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o dist/win-x64
```

`Core` platformdan bağımsız karıştırıcıyı, `App` Windows arayüzü/WASAPI/AAC motorunu, `Tests` ses işleme testlerini, `WindowsTests` Windows AAC entegrasyon testlerini içerir. Derleme sırasında NAudio 2.2.1 NuGet'ten alınır. Ayrı FFmpeg kurulumu gerekmez.

## GitHub Actions ile test ve EXE paketi

Bu klasörün **içeriğini** (`App`, `Core`, `Tests`, `WindowsTests` ve gizli `.github` klasörü dahil) GitHub deposunun köküne koyun. İş akışı `.github/workflows/windows.yml` dosyasıdır; dosyaları depoda fazladan bir `SesKayit/` alt klasörüne yerleştirmeyin.

Her push ve pull request Windows Server 2022 üzerinde şu adımları çalıştırır:

1. Karıştırıcı, dinamik kaynak değiştirme ve otomatik seçim testleri çalışır.
2. Windows Media Foundation ile 128, 160 ve 192 kb/sn için üç AAC testi çalışır. İki yapay ses kaynağı karıştırılarak ikişer saniyelik M4A dosyaları oluşturulur. Dosya yeniden açılır; stereo/48 kHz biçimi, süre, yaklaşık boyut ve iki kaynak frekansının da her iki kanalda bulunması kontrol edilir. Türkçe karakter ve boşluk içeren dosya yolu da denenir. AAC yoksa test başarısız olur; atlanmaz.
3. Testler başarılıysa Windows x64 için .NET kurulumu gerektirmeyen tek EXE hazırlanır. Yayın derlemesindeki uyarılar hata kabul edilir.
4. EXE, kullanım kılavuzu, lisanslar, EXE'nin SHA256 özeti ve derlenen commit bilgisi **SesKayit-Windows-x64** artifact'ına eklenir. Test raporları ve yapay ses örnekleri, önceki adım başarısız olsa da **Windows-test-results** artifact'ına yüklenmeye çalışılır. Dosyalar 14 gün saklanır.

GitHub'da **Actions → Windows build and audio tests → ilgili çalışma → Artifacts → SesKayit-Windows-x64** yolundan paketi indirin. ZIP'i çıkartıp `SesKayit.exe` dosyasını açın. Elle başlatmak için aynı iş akışının **Run workflow** düğmesini kullanın. Bu işlem GitHub Release yayınlamaz; yalnızca çalışma çıktısı üretir. Standart GitHub Actions kullanım kotası geçerlidir.

Windows AAC testlerini kendi bilgisayarınızda çalıştırmak için:

```powershell
dotnet test WindowsTests/SesKayit.WindowsTests.csproj -c Release
```

Bu iş akışı gerçek mikrofon/loopback cihazlarını, masaüstü arayüzünü, Teams/YouTube yakalamayı veya uzun kayıtta cihaz senkronunu test etmez. Bunlar aşağıdaki yerel testleri gerektirir. İş akışının eklenmesi, GitHub üzerinde başarıyla çalıştığı anlamına gelmez; ilk sonucu Actions sayfasından kontrol edin.

## Doğrulama

Linux üzerinde Windows x64 paketi çapraz derlendi ve karıştırıcı testleri çalıştırıldı. Windows arayüzü, WASAPI cihazları ve Media Foundation AAC kodlayıcısı bu ortamda çalıştırılamadı. İlk gerçek kullanım öncesi Windows'ta:

1. YouTube oynatırken mikrofonla konuşup 30 saniyelik kayıt alın. Dosyayı açıp iki sesi de dinleyin; son sözcüğün kesilmediğini kontrol edin.
2. Teams ayrı çıkış kullanıyorsa iki çıkışı ve mikrofonu seçip her birinin kayda girdiğini kontrol edin.
3. Klasör, üç kalite seçeneği, uzun kayıt senkronu ve pencereyi kapatarak kaydetmeyi kontrol edin.
4. Bir test kaydında cihazı çıkartın; hata ve kaydedilen bölümün davranışını kontrol edin.

Paket kod imzalı değildir. Windows'ta yayıncı kimliği doğrulanmış bir uygulama olarak görünmez.
