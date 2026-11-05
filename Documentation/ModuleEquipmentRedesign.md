# Ekipmanların ikinci tasarımı

> 5 Kasım 2026: beş modül ve ortak kaide Blender'da yeniden modellendi; bu sayfa önceki tasarımın kaydıdır. [Güncel modeller](ServiceModuleArt.md).

31 Ekim 2026. Önceki ekipman görünümünün yetersiz bulunması üzerine çekici, pompa, teknik servis ünitesi ve nakliye kasasının ana biçimleri değiştirildi. Yangın makarası ile ortak taşıyıcı da yenilendi. Bunlar Unity prefablarının gerçek meshleridir; önizlemeler ayrı bir PreviewScene içinde aynı varlıklardan alınır.

| Ekipman | Değişiklik | Mekanizma üçgeni |
| --- | --- | ---: |
| Çekici | Alçak motor ve kademeli planet gövdesi; eğimli döküm ayaklar; bağlantı çubukları; üst röle kutusu ve terminaller; açıklığı gerçekten delik olan halat kılavuzu; turuncu dövme kanca ve çekme halkası | 38.912 |
| Pompa | Motor, kaplin ve pompa gövdesi aynı eksende; kanatlı elektrik motoru, emiş dirseği, iki içi açık bağlantı ağzı, kısa taşıma çerçevesi ve döküm ayaklar. Eski ayrı duran benzin motoru/depo düzeni kaldırıldı | 22.536 |
| Teknik servis | Eğik kumanda paneli, kapalı ekran, bağlantı soketleri, anahtarlar, yan ızgaralar, tek takım çekmecesi ve taşıma sapı | 12.844 |
| Nakliye | Kaburgalı kalıplı kasa, ayrı conta/kapak, iki ön mandal, arka menteşeler, yan tutamaklar, basınç valfi ve köşe korumaları. Önceki kalın kayışlar kaldırıldı | 10.732 |
| Yangın | Dört kollu sac makara yanakları, göbek kapağı/cıvataları ve üçgen destek. Sonraki taşınabilir eşya turunda sarma kolu, daha kalın hortum, birleşim somunları ve kavrama yüzeyli nozul eklendi | 31.884 |

Ortak kaidenin kalın düz levhası ve blok ayakları yerine ince sac taban, bükülmüş kenar dönüşleri, gerçek el açıklıkları ve küçük kauçuk takozlar kullanılır. Servis portunun merkezi değişmeden çapraz destekli yan bağlantıya oturur. İsim etiketi küçültülüp koyulaştırıldı; yeni slogan veya süs yazısı eklenmedi.

Döküm alaşım, işlenmiş metal, boyalı sac, kauçuk ve halat ayrı malzemelerdir. `Art/generate_module_finish.py` yalnız ekipman dökümleri için üç deterministik 512 px PBR haritası üretir. Araç/ofis malzemeleri bu haritaları kullanmaz. Önizleme sahnesindeki zemin ve ışıklar yalnız görüntü alırken oluşturulur; oyun sahnesine eklenmez.

## Korunan davranış

Prefab yolları, katalog kimlikleri, 24 kg fiziksel ağırlık, 0,66 × 0,44 × 0,46 m çarpışma kabuğu, üç MeshRenderer ve servis portunun `(-0,29; 0,27; 0,20)` konumu korunur. Satın alma/teslimat, E/F ile taşıma, dört cıvata, kablo bağlantısı ve kayıt biçimi değiştirilmedi. Yeni ses, çalışma anı ışığı veya animasyon eklenmedi. Saha vinci/pompa çalıştırma mekaniği eklenmez; değişiklik ekipman modelleridir.

Araç, kapılar, araç taşıyıcısı, park konumu ve kaynak ofis tasarımı bu turda değiştirilmedi. 156 korunan araç varlığının özeti `Logs/module_redesign_vehicle_after.json` içinde karşılaştırılır. Ofis denetimleri 2.887 kaynak dönüşümünü ve 989 mesh/malzeme referansını kontrol eder. Önceki modellerin kodu, prefabları ve görüntüleri `Backups/ModuleEquipmentRedesign` içindedir.

## Görüntüler

[Çekici](ModuleEquipmentAfter_towing.png), [pompa](ModuleEquipmentAfter_water.png), [teknik servis](ModuleEquipmentAfter_tech.png), [nakliye](ModuleEquipmentAfter_cargo.png), [yangın](ModuleEquipmentAfter_fire.png).

Model önizlemeleri oyundaki prefabları gösterir. Araç üzerine takılmış Play Mode görüntüleri `ModuleVariant_*.png` ve `ExteriorModuleDetail.png` dosyalarıdır. Son test sonucu [TestResults.md](TestResults.md) içinde tutulur. Üçgen sayıları mekanizma içindir; ortak kaide ve port dahil değildir. Bağımsız oyun derlemesinde FPS/GPU ölçümü yapılmadığından performans artışı iddia edilmez.

## Doğrulama

`Logs/module_redesign_full_validation.txt`: son modellerle 10 PASS ve Validation complete. Gerçek E/F ile teslimat/alma/bırakma, dört cıvata ve kablo, yarım montaj kaydı, arka kapılar ve basamaklardan geçiş, lift, bilgisayar ve şirket döngüsü geçti. `Logs/module_redesign_rear_walk.txt` düşük FPS hedefindeki yedi yürüyüş adımını kaydeder. Beş ayrı model önizlemesi ve gerçek Play Mode görüntüleri görsel olarak incelendi.

`Logs/module_redesign_vehicle_after.json`: 156 araç dosyası kontrol edildi, değişen dosya yok. Kaynak ofisin 2.887 dönüşümü ve 989 mesh/malzeme bağlantısı aynı kaldı. `Logs/module_redesign_finish_state.json`: şirket kaydı test öncesi SHA-256 özetiyle aynı; bekleyen istek veya geçici kayıt yedeği yok. C# derlemesi ve `git diff --check` geçti.

## Tekrar üretme

`python Art/generate_module_finish.py`, ardından Unity içinde `Company > Rebuild Portable Module Models`. Play Mode kapalıyken ve sahnede kaydedilmemiş değişiklik yokken aynı işlem `Temp/CompanyUIValidation.request` içine `module-equipment-preview` yazılarak da istenir. `module-equipment` model üretiminden sonra bütünleşik Play Mode testini çalıştırır.
