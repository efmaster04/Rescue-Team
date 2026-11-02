# Ofis ve garaj doğrulaması

1 Kasım 2026, Blender ekipmanları ve ek depo rafı düzeltmesi: `Logs/blender_equipment_full_validation.txt` içinde **10 PASS** ve **Validation complete**. Son kova sapı görselinden sonra `Logs/blender_equipment_finish_validation.txt` içindeki iki modül/taşıma grubu da geçti. [Blender dosyaları ve modeller](BlenderEquipmentPass.md).

Son kompakt raf yerleşimi `Logs/urgent_shelf_final_garage_validation.txt` ile doğrulandı: test gerçek depolama geliştirmesini satın alır, kaydeder/yükler, görünür raf ile collider eşleşmesini sınar ve raf açıkken gerçek W/E ile garaj/arka basamak/lift yolunu çalıştırır. `Logs/urgent_shelf_layout_regression.txt` eski üç bölmeli kopyayı tek doğru bölmeye çevirir. [Raf hatası ve görüntüler](StorageUpgradeFix.md).

`Logs/blender_equipment_finish_state.json`: kullanıcı kaydı aynen geri yüklendi; test yedeği ve bekleyen istek yok. 156 korunan araç dosyası değişmedi. 989 ofis mesh/malzeme referansı ile 2.643 özgün kaynak dönüşümü aynı kaldı. Raf geliştirmesine ait `(Clone)` parçaları özgün ortam sayımından ayrıdır; yerleşimleri yeni regresyonda sınanır. Derleme ve boşluk denetimleri geçti.

Önceki koşu, 31 Ekim 2026 taşınabilir atölye ekipmanları: `Logs/portable_equipment_full_validation.txt` — **10 PASS** ve **Validation complete**. Sekiz yeni ekipman modeli ve ayrıntılandırılmış yangın makarasıyla taşıma, güvenli bırakma, araca yükleme, teslimat, modül montajı, kapılar, lift, bilgisayar ve şirket döngüsü geçti. [Eşyalar ve Unity önizlemeleri](PortableEquipmentPass.md).

`Logs/portable_equipment_finish_state.json`: kullanıcı kaydı SHA-256 özetiyle aynı geri yüklendi; bekleyen istek veya kayıt yedeği yok. `Logs/portable_equipment_vehicle_after.json`: 156 korunan araç dosyasında değişiklik yok. Kaynak ofisin 2.887 dönüşümü ve 989 mesh/malzeme referansı aynı kaldı. Elde yeni çanta gerçek oyuncu kamerasında incelendi (`EquipmentCarryPreview.png`). Sekiz ayrı ekipman önizlemesi de kontrol edildi. İlk model üretiminde çanta mandalı mevcut çarpışma sınırını aştığı için oluşturma durduruldu; mandal içeri alındı ve son geometri denetimi/tam koşu geçti.

Önceki bütünleşik koşu, 31 Ekim 2026 ikinci ekipman tasarımı: `Logs/module_redesign_full_validation.txt` — **10 PASS** ve **Validation complete**. Alçak gövdeli çekici, aynı milde birleşen elektrikli pompa, eğimli panelli servis ünitesi, kalıplı nakliye kasası ve yeni ortak kaideyle gerçek montaj/taşıma tekrar geçti. [Modüller ve kapsam](ModuleEquipmentRedesign.md).

`Logs/module_redesign_vehicle_after.json`: 156 korunan araç dosyasında değişiklik yok. Kaynak ofisin 2.887 dönüşümü ve 989 mesh/malzeme referansı aynı. `Logs/module_redesign_finish_state.json`: kullanıcı kaydı aynen geri yüklendi, bekleyen test isteği ve kayıt yedeği yok. `Logs/module_redesign_rear_walk.txt` düşük FPS hedefindeki bütün arka erişim adımlarını geçti. Yeni bir bağımsız derleme FPS ölçümü yapılmadı.

İlk ekipman tasarımının tam koşusu `Logs/module_equipment_full_validation.txt`, son küçük ayrıntıların iki hedefli grubu `Logs/module_equipment_finish_validation.txt` içinde korunur. [İlk tasarımın kaydı](ModuleEquipmentPass.md).

Araç koruması: `Logs/module_equipment_protected_vehicle_after.json` içinde 156 dosyanın SHA-256 özeti değişmedi. Araç altındaki dönüşüm, mesh ve malzeme referansları da aynı kaldı. Şirket kaydı geri yüklendi. Tam koşuda arka yürüme, lift, 16 çekmece, bilgisayar ve şirket döngüsü tekrar geçti.

Önceki raf modellerinin 10 PASS koşusu `Logs/garage_storage_full_validation.txt` içinde korunur. [Raf modelleri](GarageStoragePass.md).

Önceki tezgâh, servis arabası ve HUD koşusu `Logs/workbench_detail_finish_validation.txt` içinde 10 PASS ile korunur. [Tezgâh modelleri](WorkbenchDetailPass.md).

Bir ara tam koşu arka giriş yürüyüşünde zaman aşımına uğradı (`Logs/workbench_hud_intermediate_failure.txt`). Takılma sonraki normal hedefli koşuda, düşük FPS hedefli koşuda ve son tam koşuda tekrarlanmadı. Nedeni kesinleşmedi; çözülmüş bir üretim hatası olarak sunulmaz. Teste tuş/cihaz, karakter yönü, çarpışma bayrakları ve kare süresi tanısı eklendi. İç içe testlerin hata halinde de temizlenmesi, sentetik W'nin bırakılması ve geçici FPS/VSync değerlerinin geri yüklenmesi düzeltildi.

Önceki kabin turu: `Logs/van_interior_full_validation.txt` — 10 PASS. İki koltuğun yük bölmesi duvarının önünde kaldığı ve birleşik iç ayrıntılarda yeni collider/ışık bulunmadığı kontrol edilir. Bu kontroller son tam koşuda da geçti. [İç mekân](VanInteriorPass.md).

Önceki far/ızgara, yeni yedek lastik ve modül ayrıntısı koşusu `Logs/vehicle_detail_full_validation.txt` içinde 10 PASS ile korunur. Son kayış yerleşimi düzeltmesinden sonraki iki modül/taşıma grubu `Logs/vehicle_detail_finish_validation.txt` içindedir. Yeni yedek lastik gerçek E ile alındı, elde render edildi ve F ile bırakıldı. [Model değişiklikleri](VehicleDetailPass.md).

Lift düzeltmesi bu tam koşuda da doğrulandı: modülsüz kayıt/yükleme, kumandanın yakınındaki gerçek boş konumdan E ile kaldırma/durdurma/devam/indirme, tekerlek dış kenarında oyuncu koruması ve yarım takılmış modülde kilit. Önceki hedefli rapor `Logs/lift_usability_validation.txt` içindedir. [Neden ve değişiklik](LiftFix.md).

29 Ekim 2026, Unity 6000.5.5f1. Önceki tekerlek/tezgâh/yüzey turunun tam koşusu `Logs/workshop_craft_full_validation.txt`, son gölge ayarından sonraki hedefli kontrolü `Logs/workshop_craft_finish_validation.txt` dosyalarında korunur. Son raporda FAIL yoktur.

Değişmeden korunan sonuç: `Logs/rear_workshop_full_validation.txt`. Güncel çalışma raporu: `Logs/office_garage_validation.txt`. Hedefli montaj: `Logs/rear_module_finish_validation.txt`. `Logs/Editor.log` geliştirme sürecindeki önceki başarısız ara koşuları da içerir; bütünü hatasız bir son koşu günlüğü olarak sunulmaz. Önceki sürüm raporu `Backups/RearWorkshopRefresh/TestResults.md` içinde korunur.

Önceki tekerlek/tezgâh turunda beş yeni atölye renderer'ı içinde birleştirilen ayrıntılarda collider bulunmadığı ve gerçek Play Mode oyuncu kamerasında görünüm ayrıca denetlendi. Dört lastik 72 çevresel dilime, jantlar havalandırma aralığı/bijon/valfe sahip oldu. Kaynak van gövdesi korunurken türetilmiş tekerlek geometrisi değişti. [Modeller, önce/sonra görüntüleri ve ölçüm kapsamı](WorkshopCraft.md).

## Geçen kontroller

1. **Özgün van:** İki yan, tavan ve arka kapılarda doğrudan görsel mesh üzerinde 227 dış yüzey ışın örneği; opak gövde/cam, özgün farlar ve tutamaklar. Park konumu korunur.
2. **Hareket ve taşıma:** Gerçek W basma/bırakma, duvara güvenli bırakma, merkezlenmiş taşıma, kameraya giren modelin gizlenmesi, oyuncu çarpışma filtresi, FXAA ve sabit oda yansımaları.
3. **Ofis kapıları:** Gerçek E/W ile iki yönden açıp geçme; kolların bağlı kalması, oyuncudan uzağa açılma, engelde durma/devam ve hızlı komutlardan toparlanma.
4. **Çekmeceler:** On altı tepsiyi açıp kapama; oyuncu/eşyada güvenli bekleme, engel kalkınca devam ve kol etkileşim ışını.
5. **HUD ve duraklatma:** Gerçek modül/yük/bakım ve eşya durumu; Escape ile zaman/hareket durması, ayarlar, kayıt geri okuması ve bilgisayar açıkken menü çakışmaması.
6. **Arka erişim ve lift:** Gerçek E ile dört araç kapısı; arka kanatların 180 dereceye ulaşması ve modülün kapıyla gitmesi. Gerçek W ile sağ yandan arkaya, basamaklardan yük tabanına, dışarıya ve sol yana yürüyüş. Karakterin ayak yüksekliği içeride 0,66 m tabana ulaştı. Lift gövde, arka taşıyıcı ve basamak altında oyuncuda bekleyip güvenle devam etti. Seslerin kaldırılması, FPS/ms sayacı ve statik gruplama doğrulandı.
7. **Bilgisayar:** Yedi uygulama, üst bölüm sekmeleri, gerçek iş/sözleşme seçimi, pencere sürükleme/büyütme/küçültme/görev çubuğu, kapatma/kayıt/çıkış; siyah monitör ve 1,72 m karakter ölçeği.
8. **Modül ayrıntıları:** Beş farklı mekanizma meshi; modül başına üç MeshRenderer; cıvatanın basılı E sırasında dönmesi ve erken bırakınca görsel/durum iptali; gerçek porta giden/ayrılınca park eden kablo; duvar yakınında model/etiketin birlikte gizlenmesi ve sabit tavan sinyali kimliği.
9. **Elle arka modül montajı:** Fiziksel satın alma/teslimat; kapı kapalıyken arka dış yuvada gerçek E/F ile alma/bırakma/yerleştirme; dört cıvata ve bağlantı sırası, sökme/takma ve yarım montaj kaydı. Takılı modülün bütün köşeleri arka gövdenin dışında kaldı. Gerçek v4 → v5 yükseltmesi, eski basamak alanındaki ekipmanın kimlik/durum kaybı olmadan taşınması ve E ile arka yükleme/boşaltma geçti. Test, katı collider içine yerleştirilmiş oyuncuya dayanmaz; eşya alma gerçek görünür hedefe açık bir yönden yapılır.
10. **Şirket döngüsü:** Hazırlık, saha sonucu callback'i, sözleşme başarı/başarısızlığı, raporu gerçek pointer ile onaylama ve tek seferlik ödeme, denetim ödül/cezası, araç/ekipman bakımı, sipariş/teslimat, geliştirme ve kayıt/yükleme. Rapor toplamı/tahsilat düğmesi sabit alana alındıktan sonra ödeme tekrar geçti.

Testler Unity Input System ve EventSystem olaylarını kullanır. Şirket kaydı test başında yedeklenir ve sonunda geri yüklenir. Test tercihlerinin yanında arka planda çalışma seçeneği de geri yüklenir; açık editör kapatılmaz.

## Görsel ve geometri kontrolü

`ComputerJobs720Preview.png`, `ComputerJobsPreview.png`, `ComputerJobs1080Preview.png` ve `ComputerShopPreview.png` üst sekmeleri, genişleyen iş alanını ve büyük yazıları gösterir. İş fiyatı/sözleşme/kabul düğmesi 720p ve 900p görüntülerde incelendi. `ComputerReport720Preview.png` sabit toplam/tahsilat alanını gösterir. [Görsel değerlendirme](../design-qa.md).

`RearDoorWalkwayClear.png`, `ExteriorModuleMounted.png`, `ExteriorModuleDetail.png` ve beş `ModuleVariant_*.png` Unity'den alınıp incelendi. [Arka erişim ve yüzey notları](RearWorkshopRefresh.md), [modül kaynakları](ModuleVisuals.md).

`Logs/vehicle_exterior_fix_checks.txt`: arka panel/kol geometrisinin **3.336 köşesi** kendi kapı yarısında; backface ışınları kapalıyken **120 içeriden görünürlük örneği** gerçek opak mesh yüzeyine çarptı.

Önceki yüzey turunun `Logs/workshop_visual_geometry.txt` raporunda 64 van yüzey meshi ve değişmeyen kaynak köşeleri bulunur. Son model turu bu raporun üstüne dört lastik ve dört jantın türetilmiş geometrisini yeniler; `Logs/workshop_craft_geometry.txt` özgün merkez/çapın korunduğunu kaydeder. `VisualQualityAfter_*.png` ve `WorkshopCraftPlayView.png` yeni Unity çıktılarıdır. Yeni atölye ayrıntılarında collider, yeni ışık ve ses yoktur. Modül başına üç MeshRenderer, üç çizim çağrısı garantisi değildir.

## Kaynak ofisin korunması

- `Art/audit_presentation_source.py`: **2.887 kaynak dönüşümünde değişiklik yok**. Ortam FBX SHA-256: `2DA10567574E9A75FE2C0C9AF79F9AD0FC57F7D262898874BF8E10733980BD4C`. Rapor: `Logs/presentation_source_parity.json`.
- `Art/audit_garage_refinement.py`: **989 korunan ofis renderer'ının mesh/malzeme bağlantıları aynı**. Rapor: `Logs/garage_office_preservation.json`.
- Kaynak ofisin yerleşimi, ölçeği ve modelleri değiştirilmedi. Monitörün siyah sahne malzemesi önceki kullanıcı isteğidir.

## Performans ve sınırlar

`Logs/module_equipment_performance.json`, model ailesinin tam koşusunda aynı açıdan 1600×900 statik gruplama A/B ölçümüdür; **2.960** sabit renderer gruplandı. CPU render gönderim medyanı gruplamasız 7,33 ms, gruplamalı 6,21 ms ölçüldü. Son küçük destek ayrıntılarından sonra bu benchmark tekrarlanmadı. Önceki ölçümler kendi raporlarında korunur. Bunlar GPU sunum süresi veya bağımsız oyuncu FPS ölçümü değildir; farklı koşuların süreleriyle sürümler arası FPS karşılaştırması yapılmaz. Çizim çağrısı sayacı kullanılamadığından -1 raporlanır. Render ölçeği 1,0 kalır. FPS/ms sayacı duraklatmada da gerçek zamanla güncellenir; ekran görüntüsü yakalama yükü performans kıyası sayılmaz.

İnsanla serbest uçtan uca oynanış ve bağımsız derlemede FPS ölçümü yapılmadı. Saha sonucu callback'i sınandı; gerçek saha sahnesi, sürüş, NPC ve çok oyunculu ağ eşitlemesi bu çalışmanın tamamlanmış özellikleri değildir.
