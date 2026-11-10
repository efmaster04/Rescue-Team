# Ofis ve Garaj

Unity ile geliştirilen şirket ve garaj simülasyonu. Ofisteki bilgisayardan iş,
ekipman, araç ve şirket işlemleri yönetilir. Garajda ekipmanlar elle taşınır;
modüller dört cıvata ve servis bağlantısıyla araca takılır. Garajın doğu
duvarındaki geçiş, yaklaşık 200 m²'lik Kurtarma Hangarı'na açılır. Hangarda
kurtarma konvoyu (drone aracı, römork ve bot), drone'lar, ekipman duvarı, erzak,
alet tezgâhı ve ekip köpeği Fındık bulunur.

Bu depo oyunun Unity proje dosyalarını içerir. Hazır bir çalıştırılabilir oyun
paketi değildir. Mevcut geliştirme sürümünde ofis, garaj ve iki saha görevi
oynanabilir: Bölüm 1 (Uçurumda Mahsur Minibüs) ve Sel Vadisi (Mahsur Kalanlar).
Çok oyunculu oturum henüz bulunmaz.

Projeyi açmak için:

1. Depoyu klonlayın: `git clone https://github.com/efmaster04/Rescue-Team.git`
2. Unity Hub üzerinden klasörü **Unity 6000.6.3f1** ile açın. 6000.5 bu projenin
   fizik ayarını yanlış okur ve karakter zeminden düşer.
3. Paketlerin ve varlıkların ilk aktarımının tamamlanmasını bekleyin.
4. Unity Sel Vadisi haritasını üretmeyi sorar; kabul edin (yaklaşık 1 dakika).
5. `Assets/_Project/Scenes/OfficeGarage.unity` sahnesini açıp **Play** düğmesine basın.

Ofis sahnesi depoda hazırdır. Sel Vadisi haritası (sahne ve yaklaşık 220 MB mesh)
Git'e girmez; her makinede üretilir. Elle üretmek için
`Rescue Team > Sel Vadisi > Haritayı Yeniden Üret ve Görevi Kur` kullanılır.
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
[Kurtarma Hangarı](Documentation/KurtarmaHangari.md) ·
[Sel Vadisi saha görevi](Documentation/SelVadisiSahaGorevi.md) ·
[Elle modül montajı](Documentation/ManualGarageSystems.md) ·
[Doğrulama kayıtları](Documentation/TestResults.md)

Blender kaynakları ve üretim betikleri `Art/Blender` altında bulunur.
`Assets`, `Packages` ve `ProjectSettings` Unity projesinin parçasıdır.
`Library`, `Temp`, `Logs` ve kişisel editör ayarları Git'e dahil edilmez;
Unity gerekli yerel önbellekleri yeniden oluşturur.
