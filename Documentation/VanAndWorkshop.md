# Tam minibüs ve atölye iç mekânı

25 Ekim 2026 geçişi. Sahnenin önceki sürümü ve aracın önceki FBX/prefabı `Backups/VanWorkshopRebuild` içindedir. Ofisin kaynak modeli, parça dönüşümleri ve malzeme bağlantıları korunur. Minibüsün sahnedeki kökü `(4.65, 0, -0.18)` olarak kalır.

## Araç kaynağı ve geometri

Araç artık sahne içindeki kesit kopyasından türetilmez. Kullanıcının model tesliminde bulunan aynı V01–V22 minibüsün tam kaynağı okunur:

`C:/Users/uur/Desktop/modeller/90_ARSIV/Orijinal_Kaynak_Dosyalar/Servis_Minibusu/Blender/Lake_Workshop_Van.blend`

`Art/Blender/Scripts/export_complete_van.py` değerlendirilen 38 özgün parçayı yeni bir Unity FBX'ine aktarır. Hiçbir kaynak `.blend` kaydedilmez. Kapalı yüzey adalarında yüz yönleri işaretli hacimle, açık dış panellerde dış yönle düzeltilir. Eski aynalanmış gövde/şerit yamaları ve sonradan eklenmiş kaba far/mandal kutuları kullanılmaz; tam kaynak modelin far çizgileri, jantları, arka lambaları ve kolları görünür kalır. Tavan-omuz birleşimindeki dar dönüşler gerçek kapanış yüzeyleriyle, arka iki kanadın orta aralığı içerideki ince contayla kapatılır.

Yeni çıktı `Assets/_Project/Art/Models/Vehicles/Vehicle_ServiceVan_Complete.fbx` dosyasıdır. `VanRestorationBuilder` sahne ve mevcut araç prefabının yalnız görsel alt nesnelerini günceller. Araç etkileşimi, katalog/şirket bağlantıları, modül soketi ve sekiz yük soketi korunur. Boya krem ve mavi kimliğini korur; dış yüzeyler opak, derinlik yazan, dışa bakan yüzlerle çizilir. Camlar opak koyu mavi-gri yansımalı malzemedir. Çift taraflı çizimle kayıp yüz gizlenmez.

## Atölye

`WorkshopOverhaulBuilder` yalnız garajın içine yeni bir düzenleme uygular:

- Mavi servis bölümü, gri dolaşım şeridi ve açık renk kesikli sınırlar.
- Kaynak taşıyıcılar arasında metal tavan kasetleri.
- Dört dolap gövdesi, on altı çekmece, metal kulplar, plint ve metal tezgâh yüzeyi.
- Koyu alet panosu, tam boy görev ışığı ve arka çalışma ışığı.
- Lift kolonuna bağlanan küçük kumanda ve aynı yerdeki gerçek E etkileşim hedefi.

Kaynak el aletleri ve raflar kullanılır. Eski büyük kumanda standının görselleri gizlenir; yeni kumanda aynı modül istasyonunun Filo uygulamasını açar. Dolaplar mevcut tezgâh çarpışma hacminin içinde kalır. Zemin boyası ve tavan kaplamaları yürüyüşe yeni takılma engelleri eklemez. Kenarlı parçaların meshleri ölçüye göre bir kez üretilip asset olarak saklanır; oyun sırasında üretilmez.

## Kontroller ve görseller

`VehicleVisualValidation` aracın iki yanı, tavanı, omuz dönüşleri ve arka kapılarında 227 örnek ışını doğrudan görsel meshlerle sınar. Aracın kaba BoxCollider'ı bu denetime katılmaz. Dış yüzlerin yönünü, opak malzemeleri, yama bulunmamasını, özgün far/arka kapı parçalarını ve yeni kumanda hedefini kontrol eder. Ardından kapı E/W, taşıma, UI ve şirket oyun döngüsü testleri çalışır.

Görseller `VehicleCompleteFront/Left/Right/Rear/Roof.png`, `WorkshopOverhaulWide.png` ve `WorkshopCabinets.png` dosyalarıdır. Kaynak koruma denetimleri `Logs/presentation_source_parity.json` ve `Logs/garage_office_preservation.json` içindedir. Görsel ve yüzey testleri tüm oyun hatalarının giderildiği anlamına gelmez; saha oynanışı/ağ sistemi önceki kapsam sınırlarıyla aynıdır.

Son doğrulama `Logs/van_workshop_delivery_editor.log` günlüğünde normal kapanışla tamamlandı. Beş test grubu PASS verdi. Son görsellerde büyük panelin kolları/durum ışığı dahil bütün parçaları gizlidir; yeni kolon kumandasının E hedefi ve fiziksel gövdesi denetlendi.

Sonraki tabela sadeleştirmesi yalnız dekoratif levhaları ve zemin yazısını değiştirdi: numaralar/açıklamalar kaldırıldı, küçük DEPO/OFİS plakaları bırakıldı. `SimpleDepotSign.png`, `SimpleOfficeSign.png`, `WorkshopSimpleSigns.png` bu son görünümü gösterir. Derleme, sahne kaydı ve görsel kontrol `Logs/workshop_simple_signs_editor.log` ile tamamlandı; bu küçük görsel değişiklik için tam oyun döngüsü testi yeniden çalıştırılmadı.
