# Atölye ayrıntıları ve masaüstü kullanımı

Bu geçiş, mevcut sade garajın ve klasik bilgisayar masaüstünün tarzını korur. Kaynak ofis tasarımı ve tam minibüsün görseli/konumu değiştirilmez. Önceki sahne ve kodlar `Backups/WorkshopTactile` içindedir.

## Çekmeceler

Tezgâhtaki on altı çekmece E ile açılır/kapanır. Her çekmece ayrı bir metal tepsi, yan/arka duvar, keçe taban, ön kapak ve kulptan oluşur. Üst çekmecelerde teslim edilen modeldeki anahtarların küçük dekoratif kopyaları vardır; mevcut fiziksel ekipman stoğu aynı kalır. Eski boş etiket yuvaları kaldırılmıştır.

Hareket `WorkshopDrawer` tarafından sabit fizik adımında kinematik Rigidbody ile yapılır. Oyuncunun kapsülü ve taşınabilir ekipman hareket yolundaysa tepsi bekler; alan boşalınca devam eder. Yeni kutu çarpışması açık tepsiyi de takip eder. Sabit tezgâhın ana çarpışması korunur. Çekmece açılış durumu oturum içinde tutulur; yeni oturum kapalı başlar. Kısa ray sesi yerel `Art/Blender/Scripts/gen_drawer_foley.py` tarafından üretilmiştir.

## Bilgisayar

Pencereler başlığından sürüklenir ve ekranın dışına taşamaz. Görev çubuğunun üzeri hareket sırasında açık kalır. Başlığa çift tıklamak pencereyi büyütür; yeniden çift tıklamak önceki boyut ve sürüklenen konumu geri getirir. Küçültülen uygulama görev çubuğundaki adından geri açılır. Aynı düğme açık pencereyi küçültür. X uygulama düğmesini görev çubuğundan da kaldırır. Siyah masaüstü, mevcut simgeler ve uygulama verileri korunur.

`DesktopWindowDrag` yalnız pencere hareketini ele alır. `ComputerUI` görünür uygulamayı ve görev çubuğundaki düğmesini yönetir. Pencere konumu oturum içinde tutulur; şirket kayıt formatı değişmez.

## Kurulum ve denetim

`Company > Polish Workshop Details` mevcut sahnedeki atölye ayrıntılarını kurup sesi ve sabit yansımaları günceller. Tam sahne kurulumları da aynı çekmece kurulumunu kullanır. `WorkshopInteractionValidation` bütün tepsilerin hareketini, oyuncu ve ekipmanda durma/devam etme davranışını, etkileşim ışınını ve hızlı komutları kontrol eder. `ComputerDesktopValidation` gerçek EventSystem sürükleme/çift tıklama olaylarıyla pencere sınırlarını ve görev çubuğundan geri açmayı kontrol eder.

Bu değişiklik kaynak sahnede ek bir tabela veya slogan oluşturmaz. Küçük DEPO/OFİS plakaları korunur.

Daha sonraki garaj yüzey/dolap/makara/servis arabası geçişi ve son test günlüğü [WorkshopSurfacePass.md](WorkshopSurfacePass.md) içindedir.

Sonuç: `Logs/workshop_details_validation_editor.log` normal kapanışla tamamlandı; altı test grubu PASS verdi. Çekmece ve pencere denetimleriyle birlikte mevcut araç, kapı, taşıma ve şirket oyun döngüsü testleri geçti. `WorkshopDrawerOpen.png` ve `ComputerDraggedWindow.png` Play Mode'da üretildi ve görsel olarak incelendi. Kaynak koruma denetimleri de tekrar geçti.
