# Blender ekipman modelleri

1 Kasım 2026. Kullanıcının daha yüksek geometri ayrıntısı ve Blender kaynak dosyaları isteği üzerine sekiz taşınabilir eşya Blender 5.2.1 LTS içinde yeniden modellendi. Araç ve özgün ofis tasarımı korunur. Raf geliştirmesinin ofise taşma hatası da [ayrı düzeltmeyle](StorageUpgradeFix.md) giderildi.

## Model ve kaynaklar

| Eşya | Yakın / orta / uzak üçgen sayısı | Düzenlenebilir Blender dosyası | Unity görüntüsü |
| --- | ---: | --- | --- |
| Alet çantası | 45.836 / 16.454 / 4.518 | [toolbox.blend](../Art/Blender/Models/RefinedEquipment/toolbox.blend) | [Görüntü](PortableEquipmentAfter_toolbox.png) |
| Ekipman çantası | 54.544 / 19.578 / 5.374 | [case.blend](../Art/Blender/Models/RefinedEquipment/case.blend) | [Görüntü](PortableEquipmentAfter_case.png) |
| Parça kasası | 97.988 / 35.274 / 9.798 | [parts.blend](../Art/Blender/Models/RefinedEquipment/parts.blend) | [Görüntü](PortableEquipmentAfter_parts.png) |
| Depo kutusu | 27.480 / 9.846 / 2.682 | [bin.blend](../Art/Blender/Models/RefinedEquipment/bin.blend) | [Görüntü](PortableEquipmentAfter_bin.png) |
| Yakıt bidonu | 41.044 / 14.366 / 3.528 | [fuel.blend](../Art/Blender/Models/RefinedEquipment/fuel.blend) | [Görüntü](PortableEquipmentAfter_fuel.png) |
| Gres kutusu | 28.076 / 10.136 / 2.814 | [grease.blend](../Art/Blender/Models/RefinedEquipment/grease.blend) | [Görüntü](PortableEquipmentAfter_grease.png) |
| Emici malzeme kovası | 37.160 / 13.376 / 3.715 | [absorbent.blend](../Art/Blender/Models/RefinedEquipment/absorbent.blend) | [Görüntü](PortableEquipmentAfter_absorbent.png) |
| Hidrolik kriko | 143.532 / 51.670 / 14.352 | [jack.blend](../Art/Blender/Models/RefinedEquipment/jack.blend) | [Görüntü](PortableEquipmentAfter_jack.png) |

Önceki kriko 14.376, alet çantası 4.744 üçgendi. Artan geometri eğrisel kapaklara, kenar yuvarlatmalarına, döküm kollara, tekerlek profillerine ve bağlantı parçalarına ayrılır. Düz yüzeyler yalnız gereksiz üçgenlerle bölünmez. Çantalarda kavisli/yuvarlak kapaklar ve gerçek yüzey oyukları; bidonda kavisli kaynaklı saplar; krikoda yuvarlatılmış şasi/kollar, tutturulmuş yaylar, piston, taşıma bağlantıları ve oluklu kauçuk tabla bulunur.

Blender kaynakları adlandırılmış ayrı parçaları, yuvarlatma/yüzey normali düzenleyicilerini, malzemeleri, hazır üç LOD meshini ve bir önizleme stüdyosunu içerir. Stüdyo zemini/ışıkları Unity'ye aktarılmaz. [Blender kriko render'ı](BlenderEquipmentStudio_jack.png), [Blender bidon render'ı](BlenderEquipmentStudio_fuel.png). Bu iki stüdyo görüntüsünün aydınlatması oyun görüntülerinden ayrıdır.

## Unity aktarımı ve korunan davranış

FBX'ler `Assets/_Project/Art/BlenderEquipment/Models`, aktarılmış meshler aynı klasörün `Meshes` altındadır. `BlenderEquipmentBuilder` Blender'ın özel yüzey normallerini içe alır, malzeme yuvalarını mevcut ekipman malzemelerine bağlar ve Unity'nin +Z ön yönüne dönüştürür. Mesh başına tek köşe tamponu kullanılır; her malzeme için tüm köşeler yeniden kopyalanmaz.

Her eşyada üç saklanan LOD renderer'ı ve bir LODGroup vardır. Yakında ayrıntılı model, uzakta daha hafif model kullanılır; kısa çapraz geçişte iki sürüm birlikte çizilebilir. Eski eşya kimlikleri, prefab yolları, ağırlıkları ve çarpışma kutuları korunur. Yeni detaylara collider eklenmez. Satın alma/teslimat, E/F ile taşıma, yakın duvar gizlenmesi ve araç yükleme aynı sistemi kullanır. Kriko statik görsel modelidir; bu turda yeni saha krikosu animasyonu eklenmez.

Blender ve Unity'deki gerçek yüzey üçgenleri aynı sayıda olmalıdır. Sıfır alanlı kapak artıkları Blender yüzey sayımına dahil edilmez; FBX içe alımı da bunları atar. `Logs/blender_equipment_triangle_audit.json` ara denetimi, `Logs/blender_equipment_geometry.txt` güncel aktarım sayılarını kaydeder. Tüm LOD'ların özgün fiziksel hacme sığması ve köşe/normallerinin geçerli olması ayrıca denetlenir.

Bağımsız derlemede GPU/FPS kıyaslaması yapılmadı. Daha ayrıntılı yakın modellerden FPS artışı iddia edilmez; uzak geometri maliyetini sınırlamak için LOD'lar kullanılır.

## Yeniden üretme

Blender komutu:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --python Art/Blender/Scripts/gen_refined_equipment.py
```

Var olan `.blend` LOD meshlerini yalnız yeniden dışa aktarmak için `Art/Blender/Scripts/export_refined_equipment.py` kullanılır. Unity menüsü `Company > Import Blender Equipment Models`; eski taşınabilir ekipman menüsü de aynı Blender aktarımını çağırır. `Temp/CompanyUIValidation.request` içine `blender-equipment-preview` modelleri uygular; `blender-equipment` ardından tam Play Mode testini çalıştırır.

Önceki kod, prefablar ve görüntüler `Documentation/Backups/BlenderEquipmentPass` içinde korunur. Güncel doğrulama [TestResults.md](TestResults.md) içindedir.

## Son doğrulama

`Logs/blender_equipment_full_validation.txt`: 10 PASS ve Validation complete. Son kova sapı yüzey düzeltmesinin ardından `Logs/blender_equipment_finish_validation.txt` içindeki iki hedefli taşıma/montaj grubu geçti. Son raf boyutu/konumuyla `Logs/urgent_shelf_final_garage_validation.txt` gerçek satın alma, kayıt/yükleme, görünür/katı hacim eşleşmesi ve W/E ile garaj geçişini doğrular. Oyuncu kamerasındaki yeni çanta da kontrol edildi (`EquipmentCarryPreview.png`).

Sekiz Unity model önizlemesi ayrı ayrı incelendi. Önizleme sırasında görülen sap/tabla birleşimleri Blender kaynaklarında düzeltildi. `Logs/blender_equipment_finish_state.json` şirket kaydının geri geldiğini ve geçici test isteği/yedeği kalmadığını kaydeder. Araç, ofis kaynakları ve dosya koruması son kez denetlendi. Bu turda bağımsız derleme FPS ölçümü yapılmadı.
