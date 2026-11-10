# Başlatma ve kontroller

Depoyu klonladığınız klasörü Unity 6000.6.3f1 ile açın (README adımları; Sel Vadisi haritası ilk açılışta üretilir). URP, Input System ve uGUI paketleri manifestte tanımlıdır. `Assets/_Project/Scenes/OfficeGarage.unity` sahnesini açıp Play'e basın. Sahne ayrıca Build Settings'in ilk sahnesidir. Sahneyi ve veri varlıklarını yeniden üretmek için editör menüsünde `Company > Build Office and Garage` kullanılır.

| Tuş | İşlem |
| --- | --- |
| WASD | Yürü |
| Fare | Bak |
| Sol Shift | Koş |
| E | Bakılan nesneyle etkileşim |
| E basılı tut | Alet çantası eldeyken modül cıvatasını sık/sök; erken bırakmak işlemi iptal eder |
| F | Elde tutulan nesneyi bırak |
| R | Elde tutulan nesneyi döndür |
| Tab | Hazırlık panelini göster/gizle |
| Esc | Dünyada duraklat/devam et; bilgisayarda uygulamadan masaüstüne, sonra oyuna dön |
| F9 | Yalnız editör/geliştirme derlemesinde, iş sahada (`Deployed`) görünürken örnek saha sonucunu işle |

Başlangıçta ofis masasının önündesiniz. Monitöre bakıp E ile bilgisayarı açın. Yedi masaüstü uygulaması vardır: İşler, Araç, Depo, Mağaza, Finans, Şirket ve Posta. İşler → İlanlar bölümünden iş kabul edin; isteğe bağlı sözleşme kutuyla seçilir. Hazırlık ve sevkiyat Aktif iş, ödeme Raporlar bölümündedir.

Araç → Modüller yalnız satın alma ve durum gösterir. Modül garaja fiziksel olarak gelir. Araç boş, lift yerde ve arka kapılar kapalıyken arka dış yüzeydeki modül taşıyıcısına gidin, bağlantıyı ayırın, alet çantasıyla dört cıvatayı E basılı tutarak sökün. Eski modülü elinize alıp F ile bırakın; yenisini taşıyıp dış yuvaya yerleştirin. Dört cıvata ve bağlantı tamamlanana kadar modül etkinleşmez. Taşıyıcı ve modül arka kapıyla birlikte hareket eder. [Ayrıntılı elle montaj rehberi](ManualGarageSystems.md).

Depodaki ekipmana E ile bakıp alın. Arka kapıdan yük alanına E ile sabitleyin. Arka kapılar 180 derece yanlara katlanır; iki basamaktan içeri yürüyebilirsiniz. Tab gerçek modül, yük ve bakım durumunu gösterir. Lift panelinin üst/alt düğmelerine bakıp E ile kaldırın/indirin; hareket sırasında E durdurur. Açık kapıyla lift çalışmaz, gövdenin, arka modülün veya basamakların altında oyuncu/eşya varsa iniş bekler. Göreve çıkmadan kapıları kapatıp lifti tamamen indirin.

Modülsüz araçla da lift çalışır. Üzerinde yarım takılmış modül varsa önce bağlantıları tamamlayın. Kumanda yazısı engel nedenini gösterir. [Lift düzeltmesinin doğrulaması](LiftFix.md).

F9 yalnız ofiste ve iş `Deployed` kayıtlıyken çalışır. Normal akışta göreve çıkınca saha sahnesi açılır ve ofiste F9'a basılacak an olmaz. Saha görevi sırasında oyun kapatılıp yeniden açılırsa bilgisayar "Sahaya git" gösterir; F9 o işi örnek sonuçla kapatır. Sonucu İşler → Raporlar'da onaylayın. Hasarlı ekipmanı tezgâhta onarın; elde eşya yokken aynı tezgâh van bakımını yapar. Mağaza ekipman, Şirket → Geliştirmeler bina yatırımları içindir. Kaydet düğmesi ve otomatik kayıt şirket durumunu, fiziksel modülleri ve yarım montajı saklar.

Uygulama penceresinin sağ üstündeki çizgi masaüstüne döndürür, kare pencereyi büyütür/küçültür, X uygulamayı kapatıp masaüstüne döndürür. Bilgisayardan çıkmak için alt çubuktaki Çıkış düğmesini kullanın. Uzun içerikler fare tekerleğiyle kaydırılır. Arayüz 16:9 çalışma alanını farklı ekran oranlarına sığdırır.

Pencereleri başlık çubuğundan sürükleyebilirsiniz. Başlığa çift tıklama büyütür ve önceki konuma geri getirir. Küçültülen pencere görev çubuğundaki adından geri açılır. Tezgâh çekmecelerine bakıp E ile açıp kapatabilirsiniz; açılacak alan doluysa çekmece bekler. [Atölye ayrıntıları](WorkshopDetails.md).

Oyuncu kapsülü ve görsel karakter 1,72 m boyunda, göz hizası 1,57 m'dir. Kamera görüş açısı 62 derecedir; mobilyaların ve ofisin kaynak ölçeği korunmuştur. Küçük fiziksel ekipmanlar yürüyüşü engellemez.

Hareket kademeli hızlanır ve durur. Taşınan eşya elde ortalanır; yakın duvarda geri çekilir. F ile bırakma sırasında boş hacim yoksa eşya elde kalır. Araçtan çıkarılan ekipman doğrudan ele verilir. Kapılar kullanılan taraftaki oyuncudan uzağa açılır. Açılış veya kapanış sırasında oyuncu/ekipman yolu kapatıyorsa hareket bekler; engel kalkınca devam eder. E ile hareket yönünü değiştirebilirsiniz. Elde tutulan eşyanın adı/durumu alt köşede görünür. Eşya salınımını kapatmak için oyuncudaki `carrySway` alanını kapatabilirsiniz.

ESC menüsünden ses, fare hassasiyeti ve elde eşya salınımı ayarlanır. Ayarlar menü kapanınca saklanır. Aynı menüde Oyunu kaydet bulunur. Duraklatılmış oyunda zaman ve oyuncu hareketi durur; ESC veya Devam et ile geri dönülür. Elde tutulan ekipmanın gerçek ağırlığı, durum çubuğu ve F/R kontrolleri alt köşededir. Hazırlık listesi araca yüklenen gerçek eşyaları izler; yüklü hasarlı ekipman bakım gerektirir. [Göstergeler ve ekipman ayrıntıları](HudAndEquipment.md).

Önceki geçici ses efektleri kaldırılmıştır. FPS ve ortalama kare süresi oyun sırasında sol üstte, bilgisayar açıkken sağ üstte gösterilir. Bu sayaç duraklatmada da çalışır.

Mevcut sahnenin ışık, yansıma ve kamera kurulumunu uygulamak için `Company > Apply Lighting and Presentation` kullanılır. Bu işlem kaynak model yerleşimini değiştirmez. [Sunum notları](Presentation.md) ayarları ve yedekleri açıklar.

Garajın yüzey, LED armatür, yön levhası, kapı fiziği ve gölge kurulumunu ayrıca `Company > Refine Garage and Doors` ile uygulayabilirsiniz. Bu kurulum artık aynı minibüsün tam kaynak modelini ve yeni metal tezgâh/dolap/tavan/servis zemini düzenlemesini de uygular. Lift kumandasının E hedefi kolonun önündeki küçük paneldedir. [Garaj ve kapı notları](GarageRefinement.md), [tam araç ve atölye notları](VanAndWorkshop.md) kaynak denetimini ve testleri açıklar.

Saha görevleri ayrı sahnelerde oynanır: Bölüm 1 kurtarma işi ve [Sel Vadisi saha görevi](SelVadisiSahaGorevi.md). F9 sonucu gerçek saha oynanışı değildir. Çok oyunculu oturum başlatma seçeneği yoktur.

Garajın doğu duvarının kuzey ucundaki geçişten (hava hortumu makarasının yanı) Kurtarma Hangarı'na yürünür. Hangarın doğudaki açık araç kapısı dışarı çıkar; güneydeki sarmal kapı kapalıdır. Ekip köpeği Fındık köşesinde uyur, yaklaşınca uyanır; ona bakıp E ile sevebilirsiniz. Hangarı yeniden kurmak için `Rescue Team > Ofis-Garaj > Garajı Büyüt: Kurtarma Hangarı`, kaldırmak için `Kurtarma Hangarını Kaldır` kullanılır. [Hangar notları](KurtarmaHangari.md).

Son model/atmosfer düzenlemesi `Company > Refine Workshop Models and Atmosphere` ile uygulanır. Tekerlek, tezgâh ve raf yüzeyleri kaynak ofis düzenine dokunmadan yenilenir. [Önce/sonra görselleri ve kapsam](WorkshopCraft.md).

Araç önü, tek parçalı yedek lastik ve modül gösterge/bağlantı ayrıntıları ayrıca `Company > Refine Vehicle Front and Equipment` ile uygulanır. [Görüntüler ve doğrulama](VehicleDetailPass.md).

Kabin koltukları, direksiyon/panel ve ahşap yük alanı `Company > Refine Van Cabin and Cargo Lining` ile uygulanır. Genel araç/atölye ayrıntı kurulumu da bu iç mekânı korur. [Görüntüler ve doğrulama](VanInteriorPass.md).

Tezgâh panosundaki aletler, parça kabı ve servis arabası `Company > Refine Workbench Tools and Service Cart` ile uygulanır; genel atölye ayrıntı kurulumuna da dahildir. TAB / İş fişi ve ESC / Menü ipuçları açık yüzeylerde okunacak koyu arka plan kullanır. [Önce/sonra görüntüleri](WorkbenchDetailPass.md).

Garaj raflarının kapaklı kasaları ve açık parça kutuları `Company > Refine Garage Storage Cases` ile yenilenir. Genel atölye model kurulumu da bunları uygular. [Raf modelleri ve doğrulama](GarageStoragePass.md).

Yalnız taşınabilir modül modellerini yenilemek için `Company > Rebuild Portable Module Models` kullanılır. Bu komut aracı, kapıları veya ofis tasarımını yenilemez. [Çekici ve diğer yeni modüller](ModuleEquipmentPass.md).

Alet çantası, bidon, kriko ve diğer taşınabilir eşyaları yenilemek için `Company > Rebuild Portable Workshop Equipment` kullanılır. Bu komut artık sekiz eşyanın Blender modellerini aktarır; mevcut ağırlıkları ve çarpışma kutularını korur. [Güncel ekipman modelleri](BlenderEquipmentPass.md).

Güncel sekiz taşınabilir eşya Blender kaynaklarından aktarılır: `Company > Import Blender Equipment Models`. Kaynaklar `Art/Blender/Models/RefinedEquipment`, üretici `Art/Blender/Scripts/gen_refined_equipment.py` dosyasıdır. Eski taşınabilir ekipman menüsü de artık bu aktarımı kullanır; yangın modülü bu Blender aktarımında değiştirilmez. [Blender modelleri ve LOD'lar](BlenderEquipmentPass.md). [Ek depo rafı yerleşim düzeltmesi](StorageUpgradeFix.md).
