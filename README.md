# Ofis ve Garaj

Unity ile geliştirilen şirket ve garaj simülasyonu. Ofisteki bilgisayardan iş,
ekipman, araç ve şirket işlemleri yönetilir. Garajda ekipmanlar elle taşınır;
modüller dört cıvata ve servis bağlantısıyla araca takılır.

Bu depo oyunun Unity proje dosyalarını içerir. Hazır bir çalıştırılabilir oyun
paketi değildir. Mevcut geliştirme sürümünde ofis ve garaj oynanabilir;
çok oyunculu oturum ve gerçek saha görevleri henüz bulunmaz.

Projeyi açmak için:

1. Depoyu klonlayın: `git clone https://github.com/ugurlpcck/oyun.git`
2. Unity Hub üzerinden klasörü **Unity 6000.5.5f1** ile açın.
3. Paketlerin ve varlıkların ilk aktarımının tamamlanmasını bekleyin.
4. `Assets/_Project/Scenes/OfficeGarage.unity` sahnesini açıp **Play** düğmesine basın.

Sahne depoda hazırdır; açılışta yeniden üretim menülerini çalıştırmak gerekmez.
URP, Input System ve diğer paketler `Packages/manifest.json` içinde tanımlıdır.

| Kontrol | İşlem |
| --- | --- |
| WASD / fare | Yürü / bak |
| Sol Shift | Koş |
| E | Etkileşim / eşya al |
| E basılı tut | Alet çantasıyla modül cıvatasını sık veya sök |
| F / R | Eşyayı bırak / döndür |
| Tab | Hazırlık paneli |
| Esc | Duraklat veya bilgisayardan geri dön |

[Kurulum ve ayrıntılı kontroller](Documentation/SetupAndControls.md) ·
[Elle modül montajı](Documentation/ManualGarageSystems.md) ·
[Doğrulama kayıtları](Documentation/TestResults.md)

Blender kaynakları ve üretim betikleri `Art/Blender` altında bulunur.
`Assets`, `Packages` ve `ProjectSettings` Unity projesinin parçasıdır.
`Library`, `Temp`, `Logs` ve kişisel editör ayarları Git'e dahil edilmez;
Unity gerekli yerel önbellekleri yeniden oluşturur.
