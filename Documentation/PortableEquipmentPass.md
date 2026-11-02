# Taşınabilir atölye ekipmanları

Bu sayfa önceki C# model turunun kaydıdır. Kullanıcının daha ayrıntılı Blender modelleri isteğiyle sekiz eşya daha sonra Blender kaynaklarına ve üç LOD'a geçirildi. [Güncel modeller, Blender dosyaları ve kontroller](BlenderEquipmentPass.md). Aşağıdaki aynı adlı görüntüler güncel modelleri gösterir; önceki görüntüler `Backups/BlenderEquipmentPass` içindedir.

31 Ekim 2026. Sekiz ekipmanın eski kaynak görselleri yerine ayrı tamamlanmış modeller üretildi. Araç, kaynak ofis ve son çekici modeli değiştirilmedi. Atölyedeki eşya konumları korunur; satın alınan ve kayıttan yüklenen eşyalar aynı yeni prefabları kullanır.

| Eşya | Yeni model | Unity önizlemesi |
| --- | --- | --- |
| Alet çantası | Eğimli sac kapak, conta, çift mandal, arka menteşeler, kauçuk sap ve yan tutma çerçeveleri | [Görüntü](PortableEquipmentAfter_toolbox.png) |
| Ekipman çantası | Kaburgalı sert gövde, kapak birleşimi, ön mandallar, köşe korumaları, basık taşıma sapı ve valf | [Görüntü](PortableEquipmentAfter_case.png) |
| Parça kasası | Açık kafes gövde, gerçek el açıklıkları, köşe bağlantıları ve içinde ayrı metal parçalar/paket | [Görüntü](PortableEquipmentAfter_parts.png) |
| Depo kutusu | Köşeleri yuvarlatılmış konik gövde, kalıplı kaburgalar, ayrı istif kapağı ve yan tutma çerçeveleri | [Görüntü](PortableEquipmentAfter_bin.png) |
| Yakıt bidonu | Eğimli omuzlar, üç bağlantılı tutamak, çapraz baskı izleri, metal kapak kilidi ve emniyet teli | [Görüntü](PortableEquipmentAfter_fuel.png) |
| Gres kutusu | Yuvarlak teneke, kıvrılmış kenarlar, ayrı metal kapak ve açma halkası | [Görüntü](PortableEquipmentAfter_grease.png) |
| Emici malzeme kovası | Konik gövde, üst bilezikler, ayrı kapak, metal tel sap ve kauçuk tutma parçası | [Görüntü](PortableEquipmentAfter_absorbent.png) |
| Hidrolik kriko | İnce yan şasi, kaldırma kolları, hidrolik gövde/piston, iki geri dönüş yayı, kauçuk tabla, ön aks ve arka döner tekerlekler | [Görüntü](PortableEquipmentAfter_jack.png) |

Yangın modülünde ayrıca sarma kolu, daha kalın hortum, altıgen boru birleşimleri, eğimli boru destekleri, korumalı manometre ve kavrama yüzeyli nozul modellendi. [Yangın makarası](ModuleEquipmentAfter_fire.png). Diğer dört modülün modeli bu turda yeniden üretilmez.

## Yerleşim ve fizik

`PortableEquipmentBuilder` yalnız ekipman prefablarının görsel bileşenlerini değiştirir. Kaynak FBX'ler ve kaynak transform düğümleri korunur. Önceki MeshFilter/MeshRenderer bileşenleri prefab override'larıyla kaldırılır; `CraftedEquipment` altında bir birleşik MeshRenderer oluşturulur. Sekiz eşyada da özgün BoxCollider, ağırlık, Rigidbody ayarları ve eşya kimliği aynı kalır. Yeni geometri mevcut çarpışma kutusunun içine göre sınanır; kutular büyütülmez.

Yeni collider, çalışma anı ışığı, ses veya Update davranışı eklenmedi. E ile alma, F ile bırakma, elde merkezleme/gizlenme, teslimat ve araç yükleme aynı sistemi kullanır. Krikonun çalışma kolu şasi üzerinde saklanmış halde modellenir. Kriko pistonu ve yangın makarasının kolu bu turda statik görsel ayrıntılardır; yeni saha ekipmanı animasyonu eklenmez.

Oyun sahnesindeki yedi yenilenen başlangıç eşyasının her birinde bir MeshRenderer ve bir collider bulunduğu editörde kontrol edildi; sekizinci model olan gres kutusu katalog prefabı üzerinden üretilir. Yedek lastik önceki modelini kullanır. Bilgisayardaki sekiz ürün önizlemesi de gerçek yeni prefablarla yeniden render edildi.

Boyanmış sac, işlenmiş metal, kauçuk ve kalıplı plastik için ayrı ekipman malzemeleri kullanılır. Ortak yüzey haritaları salt okunur kullanılır; ofis veya araç malzemeleri değiştirilmez. Model başına tek MeshRenderer, tek çizim çağrısı anlamına gelmez; alt malzemeler ayrı çizilir. Yeni eşyalar yaklaşık 3–15 bin üçgendir; yangın modülünün mekanizması 31.884 üçgendir. Bağımsız derleme FPS ölçümü yapılmadığından FPS artışı iddia edilmez.

## Yeniden üretme

Unity menüsü: `Company > Rebuild Portable Workshop Equipment`. `Temp/CompanyUIValidation.request` içine `portable-equipment-preview` yazmak modelleri üretip önizlemeleri alır; `portable-equipment` ardından bütünleşik Play Mode testini çalıştırır. Köprü, derleme sırasında veya kaydedilmemiş açık sahnede işlemi bekletir.

`EquipmentPresentationBuilder.Configure()` mevcut genel kurulumların kullandığı giriş noktası olarak yeni model oluşturucusunu çağırır. Meshler ve yalnız bu eşyalara ait malzemeler `Assets/_Project/Art/PortableEquipment` içindedir. Önceki prefablar, kod ve bazı görüntüler `Backups/PortableEquipmentPass` altında korunur. `PortableEquipmentBefore_*.png` ilk modellerden alınmıştır; eski krikonun gevşek renderer sınırları eski önizlemenin kadrajını etkiler.

## Kontroller

Geometri ve fizik özeti `Logs/portable_equipment_geometry.txt` içindedir. Araç dönüşüm/mesh/malzeme referansları `Logs/portable_equipment_vehicle_parity.txt` ile korunur. Son Play Mode sonucu ve kullanıcı kaydının geri yüklenmesi [TestResults.md](TestResults.md) içinde tutulur.

`Logs/portable_equipment_full_validation.txt`: son eşyalar ve yangın modülüyle 10 PASS ve Validation complete. Gerçek E/F ile taşıma/bırakma, yükleme, teslimat, dört cıvatalı montaj, kablo, kapılar, lift ve şirket döngüsü geçti. `Logs/portable_equipment_rear_walk.txt` düşük FPS hedefindeki yedi arka erişim adımını kaydeder. Yeni alet çantası oyuncu kamerasında da kontrol edildi: [oyun görüntüsü](EquipmentCarryPreview.png).

`Logs/portable_equipment_vehicle_after.json`: 156 korunan araç varlığında değişiklik yok. Ofisin 2.887 kaynak dönüşümü ve 989 mesh/malzeme bağlantısı aynı kaldı. `Logs/portable_equipment_finish_state.json`: şirket kaydı test öncesi özetiyle aynı, geçici istek/yedek kalmadı. Derleme ve boşluk denetimleri geçti. İlk üretimde sınırı aşan çanta mandalı ile yakın planda görülen bidon sapı/teneke kapağı birleşimleri son testten önce düzeltildi.
