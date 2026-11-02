# Araç önü, yedek lastik ve modül ayrıntıları

29 Ekim 2026. Bu tur, oyuncunun yakından gördüğü araç ve ekipman parçalarını yeniler. Ofis tasarımı, aracın park konumu, kapı menteşeleri, arka taşıyıcı ve lift kumandası değiştirilmedi.

Devamında [kabin ve yük bölmesi](VanInteriorPass.md) de yenilendi. Genel araç ayrıntı komutu artık bu iç mekânı da uygular; en güncel bütünleşik rapor `Logs/van_interior_full_validation.txt` içindedir.

## Modeller

- Farlar: iki mevcut renderer korunarak contalı dış çerçeve, ince cam olukları, kapak ayrımları ve sabitleme başları modellendi. Malzemeler opak ve derinlik yazan yüzeylerdir; lambanın arkasından araç içi görünmez. Yeni ışık veya far açma/kapatma özelliği eklenmedi.
- Izgara: derin arka hazne, beş ince yatay çıta, taşıyıcılar, çerçeve ve sade merkez parçası vardır. Boyutlar ve merkezler özgün FBX'ten okunur; tekrar uygulamada konum kayması birikmez. Kaynak FBX dosyası değiştirilmez.
- Yedek lastik: eski 19 parçalı görsel, 64 çevresel dilimli tek görsel mesh ile değiştirildi. Fiziksel collider, ağırlık, eşya kimliği ve kayıt davranışı korunur. Araç lastiklerindeki kauçuk malzemesini paylaşır. Mağaza lastik ve araç önizlemeleri yeni gerçek modellerden render edildi.
- Yangın modülü: göstergeye kadran, ölçek çizgileri, ibre ve merkez bağlantısı eklendi. Bunlar görsel ayrıntıdır; saha basıncı ölçümü uygulanmış özellik değildir.
- Su tahliye modülü: hortum bağlantı ağzı, iç yuva, metal halka ve motor destek ayakları eklendi.
- Nakliye kasası: kayışların üst konumu taşıma kolundan değil, geniş kapak yüzeyinden hesaplanır; kayışlar kapağa oturur ve ön yüzde devam eder.

Modül ayrıntılarının farklı malzemeleri doğru submesh üzerinden birleştirilir. Modül başına mekanizma, ortak şase/ayrıntı ve servis portu olmak üzere üç MeshRenderer korunur; çizim çağrısı sayısı üç değildir. Bu tur yürüme yoluna collider veya yeni fiziksel eşya eklemez.

## Görüntüler

[Önce — araç önü](VehicleDetailBefore_Front.png), [sonra — aynı açı](VehicleDetailAfter_Front.png), [garaj](VehicleDetailAfter_Garage.png), [yedek lastik](SpareTyreRefined.png), [elde lastik](SpareTyreCarry.png).

[Yangın modülü](ModuleVariant_fire.png), [su tahliye modülü](ModuleVariant_water.png), [nakliye kasası](ModuleVariant_cargo.png). Görseller gerçek Unity modellerinden alındı ve incelendi.

## Kurulum ve doğrulama

`Company/Refine Vehicle Front and Equipment` bu turu mevcut sahneye uygular. Genel atölye model kurulumu da aynı ayrıntıları çağırır. `Temp/CompanyUIValidation.request` içine `vehicle-detail` yazmak uygulama ve tam testi başlatır; `detail-preview` yalnız düzenlemeyi uygular. Önceki sahne, kod ve yedek lastik prefabı `Backups/VehicleDetailPass` içindedir.

`Logs/vehicle_detail_full_validation.txt` tam koşunun 10 PASS grubunu içerir. Son kayış yerleşimi düzeltmesinden sonraki modül/taşıma kontrolü `Logs/vehicle_detail_finish_validation.txt` içindedir. Yeni lastik gerçek E ile alındı, elde render edildi ve gerçek F ile bırakıldı. Arka kapılar, modül teslimatı ve dört cıvatalı montaj, kayıt/yükleme, modülsüz lift, güvenlik kilitleri, bilgisayar ve şirket döngüsü de tam koşuda geçti.

Kaynak denetimi: 2.887 kaynak dönüşümü ve 989 ofis mesh/malzeme bağlantısı korunur. Raporlar `Logs/presentation_source_parity.json` ve `Logs/garage_office_preservation.json` dosyalarıdır. Şirket kaydı test sonunda geri yüklenir.

`Logs/vehicle_detail_performance.json`, aynı açıdaki 1600×900 statik gruplama A/B ölçümüdür: 3.360 renderer, CPU render gönderim medyanı 6,47 → 5,76 ms. Bu süreler bağımsız oyun FPS artışı veya GPU sunum ölçümü değildir. Bu tur ana kaportayı ve karakter modelini tamamen yenilemez.
