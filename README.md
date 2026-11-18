# Vocrec2

Vocrec'in Windows ses kaydına, **trslocalwin ile aynı yerel Balbal/Manas STT motoru** ve isteğe bağlı **Qwen REST özeti** eklenmiştir. Kayıt arayüzü korunur; üstteki **STT / LLM ayarları** sekmesi yapılandırma içindir.

## Kullanım

1. Windows x64 paketini yazılabilir bir klasöre açın. `SesKayit.exe`, DLL, `tools`, `_internal`, `configs` ve `licenses` dosyalarını birlikte tutun.
2. trslocalwin'de kullandığınız model klasörünü EXE yanına `models/balbal-v2-manas/` olarak kopyalayın. Model indirilmez. Gerekli dosyalar: `model.json`, `onnx/model.onnx`, `onnx/model.onnx.data`, `lm/model.binary`, `lm/tokenizer.model`. Modelin diğer dosyalarını da koruyun.
3. **STT / LLM ayarları** bölümünde metin ve özet çıktı klasörlerini seçin, **Ayarları kaydet** düğmesine basın.
4. Kaydı başlatın ve bitirin. `.m4a` tamamlandıktan sonra arka planda STT çalışır; `<kayıt-adı>.txt` seçtiğiniz klasöre yazılır. İşlem sürerken yeni kayıt başlatabilirsiniz. Sıradaki kayıtlar sırayla işlenir.
5. LLM servisi hazır olduğunda URL, model ve prompt girip kaydedin. LLM için **OpenAI uyumlu `POST /v1/chat/completions`** beklenir. Örneğin `http://localhost:8000`, `http://localhost:8000/v1` veya tam `http://localhost:8000/v1/chat/completions` kullanılabilir. Model adı servisteki adla aynı olmalıdır.
6. LLM yanıtı aynı kayıt adıyla `<kayıt-adı>.ozet.txt` olarak özet klasörüne kaydedilir. URL boşsa ağ isteği yapılmaz; yalnız STT çalışır.

LLM prompt sistem mesajı, metin dosyası kullanıcı mesajı olarak gönderilir. Akış kapalıdır; 15 dakika zaman aşımı vardır. Kimlik doğrulaması gerekiyorsa **Bearer token (isteğe bağlı)** alanına tokenı girip **Ayarları kaydet** düğmesine basın. Token maskelenir ve Windows DPAPI ile yalnız mevcut Windows hesabının açabileceği şekilde şifreli kaydedilir; bekleyen işlem dosyalarında da yalnız şifreli değer bulunur. İsteklere `Authorization: Bearer <token>` eklenir. `Bearer ` önekiyle yapıştırmak da desteklenir. Tokenı silip kaydetmek kimlik doğrulamasını kaldırır. Başka bilgisayara/Windows hesabına taşındığında tokenı yeniden girin. Bekleyen işleri yeni tokenla çalıştırmak için **Bekleyen işlemleri yeniden dene** düğmesini kullanın. Token boşsa Authorization başlığı gönderilmez; Ollama'nın yerel `/api/generate` uç noktası yerine OpenAI uyumlu uç noktasını kullanın. Uzun metinlerin servis bağlam sınırına sığması gerekir. LLM cevabı alınmadan metin diske kaydedilir.

## Kalıcı ayarlar ve hata sonrası devam

Ayarlar `%LOCALAPPDATA%/Vocrec2/settings.json` içinde atomik olarak saklanır. Vocrec'in mevcut ayarlarından ayrı tutulur. Ayar sekmesindeki değişiklikler **Ayarları kaydet** ile uygulanır; sıraya alınmış işler o andaki kaydedilmiş ayarları kullanır.

Kayıt dosyası tamamlanmadan STT başlatılmaz. İşlem kuyruğu `%LOCALAPPDATA%/Vocrec2/pending/` içinde kalıcıdır. Tamamlanamayan işler sonraki açılışta tekrar denenir. Servis adresini değiştirdikten sonra **Bekleyen işlemleri yeniden dene** düğmesi bekleyen işleri güncel kaydedilmiş ayarlarla çalıştırır. Metin zaten varsa STT tekrarlanmaz. Özet hatasında ses ve metin korunur. Hatalar aynı ayar klasöründeki `processing-errors.log` dosyasına yazılır. Uygulama normal kapanışta devam eden işleri bekler.

Daha önce yalnız STT ile tamamlanmış kayıtlar otomatik olarak yeniden özetlenmez; bekleyen işler yeniden deneme kapsamındadır. Çıktı dosyalarını taşımak yerine klasör ayarını değiştirirseniz bekleyen iş yeni metin klasöründe STT'yi yeniden çalıştırabilir.

## Mikrofon sesi

Varsayılan açık **Mikrofon konuşma iyileştirme**, yalnız mikrofon kanalına uygulanır:

- 90 Hz altındaki uğultuyu ve 10 kHz üstündeki tiz gürültüyü süzer.
- Sessiz bölümlerde yumuşak bir expander ile düşük seviyeli arka planı azaltır.
- Konuşmayı en fazla 3 kat yükselten kontrollü seviyeleme uygular; gürültü düzeyindeki sesi yükseltmez.
- Stereo kanalları birlikte kontrol eder, ani tepe seviyelerini sınırlar.

Bu, yankı giderme veya kaynak ayırma modeli değildir. Konuşma sırasında yüksek çevre gürültüsünü bütünüyle kaldıramaz. Çok kısık konuşmada expander etkisini gerçek mikrofonunuzla karşılaştırın. Ayardan kapatıp kaydedebilirsiniz; değişiklik sonraki kayıtta uygulanır. Mikrofon konumu, cihazın Windows giriş seviyesi ve birden çok mikrofon seçimi sonucu etkiler. Sinyal testleri uğultu/gürültü azalmasını ve düşük seviyeli konuşmanın güçlenmesini doğrular; gerçek donanımda dinleme testi ayrıca yapılmalıdır.

## Derleme ve test

Windows x64 üzerinde **.NET 9 SDK** gerekir. .NET 8 kurulumu gerekmez. Ekran görüntünüzdeki **9.0.317** SDK ile derleme desteklenir. `global.json`, kurulu kararlı 9.0.x SDK'yı seçer; 10/11'e geçmez. Tüm projeler `net9.0` / `net9.0-windows` hedefler; kaynak C# 12 kullanmaya devam eder.

Visual Studio kullanıyorsanız SDK 9.0.3xx için **Visual Studio 2022 17.14+** ve “.NET masaüstü geliştirme” iş yükü gerekir. Visual Studio güncellenemiyorsa mevcut .NET 9 SDK ile PowerShell'den derleyebilirsiniz; komut satırı derlemesi Visual Studio gerektirmez.

Komutları güncel `Vocrec.sln` ve `global.json` dosyalarının bulunduğu depo kökünde çalıştırın:

```powershell
dotnet --list-sdks
dotnet --version
```

`dotnet --version` çıktısı `9.0.x` olmalıdır. Eski ZIP'teki `global.json` dosyasını kullanmayın. Yalnız SDK ayarı değil, proje hedefleri de güncellendiği için güncel kaynak kodu bütün olarak indirin. `NETSDK1141`, `global.json` ile eşleşen SDK bulunamadığını belirtir.

Derleme ve test:

```powershell
dotnet build Vocrec.sln -c Release -warnaserror
dotnet test Tests/SesKayit.Tests.csproj -c Release
dotnet test WindowsTests/SesKayit.WindowsTests.csproj -c Release
dotnet publish App/SesKayit.csproj -c Release -r win-x64 --self-contained true -o dist/win-x64
```

Model dosyalarını çıktıdaki `models/balbal-v2-manas` klasörüne ekleyin. GitHub Actions Windows iş akışı test edip tüm bağımlılıkları içeren `Vocrec2-Windows-x64` paketini üretir; model ağırlıkları dahil edilmez.

Bu geliştirme ortamında Linux üzerinden tüm çözüm Windows hedefiyle derlenebilir ve platform bağımsız testler çalıştırılır. Windows AAC, WinForms ve gerçek STT/model testi Windows ortamı ve model dosyaları gerektirir. Qwen isteği sahte HTTP servisiyle doğrulanır; gerçek servis henüz sağlanmamıştır.

## Kaynak ve lisanslar

Temel kayıt uygulaması: https://github.com/hakancuraci/vocrec (MIT).
STT kaynakları ve taşınan çalıştırma dosyaları: https://github.com/hakancuraci/trslocalwin (`vendor/trslocalwin/UPSTREAM.md`). Balbal Apache-2.0; model ağırlıkları kendi Community License koşullarındadır. FFmpeg GPL ve diğer üçüncü taraf bildirimleri `vendor/trslocalwin/portable-assets/licenses` ve `vendor/trslocalwin/native/licenses` altında korunmuştur. Dağıtımda bu bildirimleri koruyun.
