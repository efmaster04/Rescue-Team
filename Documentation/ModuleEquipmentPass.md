# Taşınabilir modül modelleri

Bu sayfa ilk model yenilemesinin kaydıdır. Sonraki tasarımda ana biçimler tekrar değişti; [güncel ekipmanlar ve doğrulama kapsamı](ModuleEquipmentRedesign.md) ayrı sayfadadır. Aşağıdaki aynı adlı görüntüler artık güncel prefabları gösterir. İlk turun görüntüleri `Backups/ModuleEquipmentRedesign` içinde korunur.

31 Ekim 2026. Kullanıcının araç modeline dokunmadan ekipmanları yenileme isteği üzerine beş modülün mekanizma geometrisi yeniden modellendi. Önceki büyük arka sac ve geniş isim şeridi yerine açık bir kaide ve küçük kimlik etiketi kullanılır.

| Modül | Yeni model |
| --- | --- |
| Çekici | Ayrı motor ve redüksiyon gövdesi, yuvarlatılmış tambur yanakları, 30 sarımlı ince çelik halat, dört makaralı halat kılavuzu, serbest bırakma kolu ve kanca. |
| Yangın | İki açık kollu kırmızı makara yanağı, derinlik boyunca sarılı hortum, gösterge, vana ve yuvasında duran nozul. |
| Su tahliye | Dairesel pompa gövdesi, içi açık emiş/çıkış bağlantıları, soğutma kanatlı motor, yakıt deposu, marş kapağı, taşıma çerçevesi ve takozlar. |
| Teknik servis | İnce sac kabin, iki servis çekmecesi, siyah ekran yüzü, soketler, ayar düğmesi, taşıma kolu ve yan tarafta sarılı ölçüm kablosu. |
| Nakliye | Ayrı kapak ve conta, köşe korumaları, ön mandallar, taşıma kolu ve kapağın üzerinden arkaya devam eden sabitleme kayışları. |

## Uyum ve sınırlar

Yeni modeller `Assets/_Project/Art/ModuleEquipment` altında üretilir. `ModulePackagePresentationBuilder` mevcut kurulumların kullandığı giriş noktası olarak kalır. Modül prefablarının yolları, eşya kimlikleri, 24 kg fiziksel ağırlık, 0,66 × 0,44 × 0,46 m çarpışma hacmi ve servis portunun konumu korunur. Her modül mekanizma, kaide ve port için üç MeshRenderer kullanır; alt malzemeler nedeniyle bu sayı çizim çağrısı değildir.

Satın alma, fiziksel teslimat, taşıma, dört cıvatalı elle montaj, bağlantı kablosu ve kayıt sistemi aynı davranır. Küçük etiket yalnız gerçek katalog adını taşır. Motor, kanca, ölçü aletleri ve pompalar bu turda görsel varlıklardır; saha vinci/pompa çalıştırma mekaniği eklenmez. Büyük kaynak araç modellerinden küçültülmüş bir parça kullanmak yerine ekipman için ayrı geometri üretilir.

Araç kaportası, iç mekânı, tekerlekleri, malzemeleri, park konumu, kapılar, taşıyıcı ve ofis tasarımı değiştirilmez. Yalnız ekipman prefabları ve ilgili masaüstü önizlemeleri güncellenir. Bağımsız model önizlemeleri geçici bir Unity PreviewScene içinde aydınlatılır; sahne ışıkları değiştirilmez.

## Görüntüler

[Çekici](ModuleEquipmentAfter_towing.png), [yangın](ModuleEquipmentAfter_fire.png), [su tahliye](ModuleEquipmentAfter_water.png), [teknik servis](ModuleEquipmentAfter_tech.png), [nakliye](ModuleEquipmentAfter_cargo.png).

Bunlar gerçek prefabların Unity model önizlemeleridir. `ModuleVariant_*.png` ve `ExteriorModuleDetail.png`, Play Mode montaj denemesinde alınan görüntülerdir. Önceki modül görselleri, prefablar ve kurulum kodu `Backups/ModuleEquipmentPass` altında korunur.

## Uygulama

`Company > Rebuild Portable Module Models` yalnız modül modellerini ve modül önizlemelerini günceller. `Temp/CompanyUIValidation.request` içine `module-equipment-preview` yazmak düzenlemeyi; `module-equipment` yazmak düzenleme ile tam testi başlatır. `module-equipment-manual` düzenleme ardından hedefli montaj kontrolünü çalıştırır. Araç görünümünü yenileyen menü komutunu bu modül turunu uygulamak için kullanmak gerekmez.

Model özeti ve üçgen sayıları `Logs/module_equipment_geometry.txt` içindedir. Araç üzerindeki dönüşüm/mesh/malzeme referans denetimi `Logs/module_equipment_vehicle_parity.txt`, korunan araç varlıklarının SHA-256 karşılaştırması `Logs/module_equipment_protected_vehicle_after.json` dosyasındadır.

## Doğrulama

`Logs/module_equipment_full_validation.txt`: yeni beş modelle 10 PASS ve Validation complete. Son makara desteği, boru bağlantısı ve pompa destek düzeltmelerinin ardından `Logs/module_equipment_finish_validation.txt` içinde iki hedefli modül/montaj grubu da geçti. Gerçek E/F ile alma/bırakma, teslimat, cıvata sıkma/iptal, servis bağlantısı, sökme/takma ve kayıt/yükleme korundu. Son modeller ayrı önizlemelerde ve gerçek Play Mode montaj görüntülerinde incelendi.

156 korunan araç varlığı dosyasının SHA-256 özeti değişmedi. Araç altındaki dönüşümler ve mesh/malzeme bağlantıları ayrıca aynı kaldı. Ofis denetiminde 2.887 kaynak dönüşümü ve 989 mesh/malzeme bağlantısı korundu. Kaynak ortam FBX'i değişmedi. Şirket kaydı test sonunda geri yüklenir.

Her model mevcut fiziksel hacmin içine sığacak şekilde kontrol edilir. Son mekanizma üçgen sayıları: çekici 26.328, yangın 19.444, pompa 12.780, teknik servis 8.044, nakliye 4.948. Kaide ve bağlantı portu bu sayılara eklenir. Halat/hortum kesitleri, paralel taşınan yüzey yönleriyle üretilir; eski modeldeki köşeli sarım görünümü azaltılır. Yeni çalışma anı animasyonu, ışığı veya sesi yoktur.

Tekrar uygulamada 25 model/malzeme dosyasının SHA-256 özeti aynı kaldı (`Logs/module_equipment_reapply_check.json`). Son kayıt ve test temizliği `Logs/module_equipment_finish_state.json` içindedir; kullanıcı kaydı geri gelmiş, bekleyen test isteği veya yedek kalmamıştır.

`Logs/module_equipment_performance.json`, model ailesinin tam koşusundaki aynı açı statik gruplama A/B ölçümüdür: 2.960 renderer, CPU render gönderim medyanı 7,33 → 6,21 ms. Son küçük destek ayrıntıları için bu benchmark tekrarlanmadı. Bağımsız derlemede FPS/GPU ölçümü yapılmadı; model yenilemesinden FPS artışı iddia edilmez.
