# Oyun göstergeleri, duraklatma ve ekipman yüzeyleri

Bu geçiş, kaynak ofisi ve özgün minibüsü korur. Masaüstündeki siyah arka plan, mevcut uygulamalar ve klasik pencere düzeni aynı kalır. Önceki sahne, UI/oyuncu kodları ve elde taşıma görseli `Backups/HudAndEquipment` içindedir.

## Göstergeler

`GameHud.cs`, dünyadaki etkileşim istemini küçük bir tuş ve eylem satırı olarak gösterir. Pasif bilgi satırına sahte E tuşu eklenmez. Elde taşınan ekipmanda ad, gerçek ağırlık, durum yüzdesi, ince durum çubuğu ve F/R kontrolleri bulunur. Taşıma kontrolleri ikinci bir merkezi bildirimde tekrarlanmaz.

Tab ile açılan hazırlık listesi takılı modülü ve her zorunlu ekipmanı ayrı gösterir. Gerçek `ServiceVehicle.LoadedItems` listesi kullanılır; aynı türden birden fazla gereksinim varsa aynı eşya iki kere sayılamaz. Düşük dayanıklılıktaki yüklü eşya “Bakım gerekli” görünür. Araç bakımı ve diğer yüklü hasarlı eşyalar da listelenir. Özet, `CompanyRuntime.MissingPreparation()` sonucuna bağlıdır; rapor ve saha evrelerinde ilgili durum yazılır. Panel başlangıçta kapalı kalır.

Bildirimler sade koyu bir yüzeyde görünür; kısa giriş ve çıkış geçişleri ölçeklenmemiş zamanla çalışır. Bilgisayar arayüzüne slogan veya ek tanıtım alanı eklenmez.

Dar alanda eldeki ekipman kameranın yakın düzlemine girecek kadar geri çekilirse görünür yüzeyi kapatılır; gölgesi ve gerçek elde tutma durumu korunur. Boş alana çıkınca önceki gölge/görünürlük ayarı geri gelir. Bırakma ve yükleme de görünürlüğü geri açar. Böylece eşyanın kesilmiş iç yüzeyi ekranı doldurmaz. Kontrol, döndürülmüş çarpışma kutusunun kamera eksenindeki gerçek derinliğini kullanır.

## ESC menüsü

Dünyadayken ESC oyunu duraklatır: zaman ölçeği sıfırlanır, oyuncu kontrolü kilitlenir, imleç açılır ve dünya sesi duraklatılır. Devam et veya ESC önceki zaman/ses durumunu geri getirir. UI devre dışı bırakılırsa da kendi duraklatma durumunu bırakır. Bilgisayar açıkken ESC'nin mevcut uygulama → masaüstü → oyun sırası korunur; ikinci bir menü üst üste açılmaz.

Menüde ses, fare hassasiyeti ve elde eşya salınımı ayarlanır. `Company.Player.*` PlayerPrefs anahtarları kullanılır; şirket kayıt formatı değişmez. Menü kapanırken ayarlar diske kaydedilir. Oyunu kaydet düğmesi gerçek şirket kaydını yazar. `CompanyRuntime.Save()` artık sonucu bool olarak bildirir; bilgisayar ve duraklatma menüsünde başarı metni yalnız başarılı yazmadan sonra gösterilir.

## Ekipman

Dokuz taşınabilir ekipman prefab'ının geometrisi ve çarpışma boyutları korunur. Materyallerin yalnız bu prefab/örneklerde kullanılan kopyaları `Art/EquipmentDetail/Materials` altında oluşturulur; paylaşılan kaynak/ofis paletine yazılmaz. Yerel, deterministik yüzey haritaları boyada ince toz boya, metalde fırça izi, plastikte ince kalıp dokusu, lastikte mat mikro doku verir. Etiketler korunur. Metrik UV kopyaları `Art/EquipmentDetail/Meshes` altındadır; kaynak FBX'ler yeniden dışa aktarılmaz.

`EquipmentPresentationBuilder.Configure()` tüm garaj kurulumlarında çağrılır; sipariş ve kayıt yüklemesiyle doğan ekipman da güncel prefab'ı kullanır. Dokular `Art/Blender/Scripts/gen_workshop_detail_surfaces.py` ile üretilir.

Modülün küçük tavan işareti, daha alçak kabin tavanı yerine kapalı yük bölümünün tavanına yerleştirilir; iki kısa montaj ayağı gövdeye oturur. Özgün minibüsün gövdesi, farları, kapıları veya garaj konumu değiştirilmez.

## Denetim

`HudInteractionValidation` gerçek ESC/W girdisiyle durma/devam etmeyi, erişilebilir ses kaydırıcısını, gerçek oyuncu ayarlarını, menüden kayıt dosyası geri okumasını ve bakım/yük listelerini kontrol eder. Test sonunda önceki PlayerPrefs değerleri, oyuncu ayarları, eşya durumu ve şirket verileri geri konur. Ana doğrulayıcı kullanıcının şirket kaydını her zamanki gibi yedekleyip geri yükler.

`GameFeelValidation` çantayı gerçek ofis duvarının önünde geri çektirir, kamerada kesilen yüzeyin görünmemesini ve boş garaj alanına çıkınca görünür hale gelmesini kontrol eder. Güvenli bırakma ve yükleme kontrolleri aynı koşuda çalışır. `ComputerDesktopValidation.Capture` dünya görüntülerinde UI kopyasını kameranın yakın düzleminin hemen sonuna koyar; normal oyundaki Screen Space Overlay gibi eldeki eşyanın HUD'ı örtmesini önler.

Son koşu: `Logs/game_polish_final_editor.log`, normal kapanış, yedi PASS grubu. Derleme hatası, FAIL veya exception yoktur. ESC, hazırlık, bildirim ve taşıma görüntüleri incelendi; kaynak koruma denetimleri tekrar geçti. İnsanla serbest oynanış veya ses dinleme değerlendirmesi bu geçişte yapılmadı.
