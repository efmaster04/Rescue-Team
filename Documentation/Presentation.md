# Ofis ve garaj sunumu

Ofisin kaynak modelleri ve mobilya yerleşimi korunur. Bu geçişte sahnenin ışıkları, kamera ayarları, yansımalar, etkileşim bildirimi ve fiziksel ekipman kullanımı düzenlenir. Önceki sahne ve kodlar `Documentation/Backups/GameFeelRefresh` içindedir.

## Işık ve kamera

`OfficePresentationBuilder.Apply` mevcut `OfficeGarage` sahnesine uygulanır. `ProjectBuilder` da aynı kurulum yöntemlerini kullanır. Ortam ışığı tavan/ufuk/zemin renkleriyle hesaplanır. Üç yönlendirilmiş iç ışık yumuşak gölgeler üretir; ofise düşük güçlü gün ışığı dolgusu eklenir. Üç boyutlu sahne Neutral ton eşlemesi, hafif renk/kontrast ayarı ve SMAA kenar yumuşatma kullanır. Bilgisayar arayüzü ekranda ayrıca çizilir ve renk işlemesinden etkilenmez.

Ofis ve garajın 128 piksel yüzlü küp yansımaları editörde bir kez hazırlanır. Oyun sırasında bu küpler kullanılır; her karede tekrar ortam çekimi yapılmaz. Geometriye yalnız yansıma çekimine katılma bayrağı eklenir. Hareketli ekipman ve oyuncu gölgesi bu sabit çekime dahil edilmez.

## Etkileşim ve taşıma

Oyuncu hızlanması ve durması kademelidir. Adım sesleri gerçekten kat edilen yer mesafesine bağlıdır; duvara karşı yürümek adım sesi üretmez. Bakılan etkileşimli nesnede küçük nişangâh belirginleşir. Elde tutulan eşyanın adı ve durumu ekranın alt köşesinde gösterilir.

Taşınan eşyanın çarpışma hacmi el noktasına ortalanır. Hafif salınım `PlayerInteractor.carrySway` ile kapatılabilir. Yakındaki duvarda eşya geri ve aşağı çekilir. Bırakma sırasında önce taşıma yolunda engel, sonra eşyanın tamamının sığacağı hacim kontrol edilir. Yer yoksa eşya elde kalır. Araçtan çıkarılan ekipman oyuncunun eline verilir.

Kapılar yumuşayarak açılıp kapanır. Sonraki [garaj ve kapı geçişi](GarageRefinement.md) hareketi sabit fizik adımına taşır; iki yöne açılma ve hem açılış hem kapanış için oyuncu/ekipman güvenlik payı ekler. Açma ve mandal sesleri bu hareketlere bağlıdır. Ekipmanın çarpma sesi bırakma tuşundan değil, fizik temasındaki hızdan üretilir. Yeni kısa sesler yerel `Art/Blender/Scripts/gen_room_foley.py` ile üretilmiştir.

## Doğrulama

`OfficePresentationBuilder.ApplyAndValidate` ışık/yansıma kurulumundan sonra mevcut oyun döngüsü testini çalıştırır. `GameFeelValidation` monitöre nişan alma, elde ortalama, duvara bırakmanın reddedilmesi, boş alana bırakma, oyuncu/ekipman çarpışma filtresi ve kapının oyuncuya yer bırakmasını kontrol eder. Sonuç `Logs/office_garage_validation.txt` içindedir. Görsel çıktılar `OfficeLightingPreview.png`, `EquipmentCarryPreview.png` ve garaj kamera önizlemeleridir.

25 Ekim 2026 doğrulamasında sunum, bilgisayar uygulamaları ve bütün ofis/garaj oyun döngüsü PASS verdi. Gerçek oyuncu denetleyicisi sanal W girdisiyle yürütüldü ve tuş bırakıldığında durduğu kontrol edildi. Kaynak dönüşümü denetiminde 2.887 parça aynı kaldı; ortam FBX hash'i değişmedi. Ayrıntılar `TestResults.md` içindedir.
