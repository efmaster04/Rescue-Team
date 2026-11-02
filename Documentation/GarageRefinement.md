# Garaj ve kapılar — 25 Ekim 2026

`GarageRefinementBuilder.Apply` mevcut `OfficeGarage` sahnesinde çalışır. Ofisin özgün modeli, mobilyaları, malzemeleri ve ölçeği korunur. Vanın özgün konumu ve modeli aynı kalır. Önceki sahne/kod/görsel `Backups/GarageDoorRefinement` içindedir.

Sonraki [tam araç ve atölye geçişi](VanAndWorkshop.md), kesit vanın yamalarını aynı minibüsün tam kaynak modeliyle değiştirir. Mevcut garaj kaplamalarının üzerine dolap/tezgâh, renkli servis bölümü, metal tavan ve küçük kolon kumandası ekler. Her iki kurulum da bu menüden tekrarlanır.

## Kapılar

Kapı kanadı, kola kadar genişleyen bir kutu yerine gerçek kanat kalınlığında bir çarpışma kutusu kullanır. Hareket sabit fizik adımında, yerçekimsiz kinematik Rigidbody üzerinden yapılır. Kapalı kapı hangi taraftan kullanılıyorsa o kişiden uzak yöne açılır. Hareket sırasında ters komut verilmesi mevcut açıyı koruyarak yönü değiştirir.

Açılış ve kapanış, küçük açısal alt adımlarla kontrol edilir. Oyuncunun dik kapsülü ile kapının yönlendirilmiş hacmi arasında önceden bırakılan pay, kanadın oyuncuya girip onu itmesini engeller. Geçişteki fiziksel ekipman da açılışı/kapanışı durdurur. Engel kalkınca hareket devam eder; etkileşim metni kapı önünün dolu olduğunu belirtir. Kapı kolları, vida/backplate parçaları ve ön kapı duyurusu aynı kanadın çocuklarıdır.

## Garaj

Atölye zemini özgün geometrinin üzerinde gri epoksi kaplamadır. Yeni, tekrarlanabilir albedo, normal ve pürüzlülük dokuları `Art/Blender/Scripts/gen_workshop_surfaces.py` tarafından sabit tohumla üretilir; dışarıdan görsel indirilmez. Kaynak FBX'e yazılmaz. Gereken garaj meshlerinin kopyalarında yalnız UV ve teğet bilgisi değiştirilir; köşe konumları/üçgenler korunur.

Garaj duvarları açık gri sıva ve koyu petrol koruma bandı kullanır. Ofisle ortak bölmenin ofis yüzünü korumak için kaplama yalnız garaja bakan yüzde eklenir. Tavan ve liftin boyalı metal yüzeyleri düzenlenir. Üç yeni LED armatür ve kaynak armatürün yenilenen lensi dört çalışma ışığına karşılık gelir. Her garaj ışığı 512 piksel gölge payı kullanır; ofis ışığıyla birlikte mevcut 2048 piksel ek ışık atlasına sığar. Bakım tezgâhının ayrıca düşük güçlü görev ışığı vardır.

Son tabela düzenlemesinde büyük numaralı başlıklar, alt açıklamalar, tezgâhın BAKIM levhası ve servis zemini yazısı kaldırıldı. Yalnız küçük, açık renk plakalar üzerinde normal ağırlıkta DEPO ve OFİS yazıları bulunur. Servis sınırları zemin boyası ve çizgilerle belirtilir. `Company > Simplify Workshop Signs` yalnız bu düzenlemeyi mevcut sahneye uygular; tam sahne kurulumları da aynı `ConfigureSignage` yöntemini kullanır. Bu yüzey kaplamaları ve levhalar yürüyüşe yeni çarpışma engelleri eklemez. Işıklar ve sabit yansıma küpleri editörde kurulur; çalışırken her karede küp çekimi yapılmaz.

Arka sol duvarın çarpışma kutusu, kaynak A42 dolgusunun son 1,2 metresini de örtecek şekilde tamamlanmıştır. Yeni hacim yalnız görünen duvarı takip eder.

## Doğrulama

`DoorRegressionValidation`, iki kapıda iki yönlü gerçek E/W girdisi, kanat kalınlığı, oyuncudan uzak açılma, kol bağlantıları, dolu eşikte kapanma, fiziksel ekipmanda durma/devam etme ve hızlı komutları sınar. Aynı koşuda `GameFeelValidation`, bilgisayar arayüzü ve mevcut şirket oyun döngüsü de çalışır. Sonuçlar `Logs/office_garage_validation.txt` içindedir.

`Art/audit_presentation_source.py` 2.887 kaynak dönüşümünü ve ortam FBX hash'ini karşılaştırır. `Art/audit_garage_refinement.py` ayrıca 989 korunan ofis parçasının mesh/malzeme bağlantılarını karşılaştırır. İnsanla elle uçtan uca oynanış ve algısal ses değerlendirmesi bu otomatik testin parçası değildir.

Son koşu: `Logs/garage_doors_regression_editor.log`. Dört test grubu PASS verdi, Unity normal kapandı. Kaynak dönüşümlerinde ve korunan ofis referanslarında değişiklik bulunmadı. `GarageRefinedPreview.png`, `WorkbenchRefinedPreview.png`, `DoorPassageRefinedPreview.png` o koşuda üretildi.
