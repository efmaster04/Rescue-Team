# Varlık envanteri

## Kaynak varlıklar

Ana kaynak `C:/Users/uur/Desktop/modeller/01_GARAJ_OFIS_TESLIMI/01_Blender/Sahneler/Garaj_Tam_Sahne.blend`. İncelemede 4.845 sahne nesnesi görüldü: 4.200 mesh, 639 empty, 5 ışık ve 1 kamera. Kaynak dosya değiştirilmedi. Ofis, garaj, dış çevre, depo rafları, mobilyalar, tezgâh ve özgün araç liftleri `Environment_OfficeGarage.fbx` olarak aktarıldı. Kaynaktaki `REUSED | V01-V22` vanı ayrı `Vehicle_ServiceVan_Original.fbx` olarak çıkarıldı ve özgün park yerine kondu.

Kaynak sahnedeki çanta, alet kutusu, parça kasası, depo kutusu, yakıt bidonu, gres kutusu, emici malzeme kovası, yedek lastik ve kriko bağımsız FBX/prefab olarak aktarıldı. Bunlar sahnede fiziksel olarak taşınabilir ve vana yüklenebilir. Kaynak mesh ve malzeme tarama çıktısı `Art/Blender/source_inventory.txt` içindedir.

## Bu projede üretilenler

Blender 5.2 Python scriptleriyle kargo/çekici/yangın/teknik servis/su tahliye modülleri ve servis çalışanı üretildi. Daha önce üretilen kamyon ve portal dosyaları kaynaklarıyla birlikte tutuluyor, ancak sahnede kullanılmıyor. Düzenlenebilir `.blend` kaynakları `Art/Blender/Models`, Unity FBX'leri `Assets/_Project/Art/Models` altındadır. Üretim scriptleri `Art/Blender/Scripts` altındadır. UI tıklama, bildirim, motor ve metal bırakma sesleri `gen_audio.py` ile sentezlendi.

Malzemeler URP Lit olarak düzenlendi; Blender kaynak paleti `Assets/_Project/Art/material_palette.tsv` içinde izlenebilir. Çevre meshleri kaynak dünya koordinatlarıyla dışa aktarıldı. Unity'de FBX köküne uygulanan 180° dönüş sahne kurulurken sıfırlanır; van ve çevre aynı eksenlerde buluşur.

Kaynak van modelinin ofise bakan yük bölmesi yanında 0,5 m², karşı yanında 7,9 m² dış panel yüzeyi vardı. Eksik yan panel ve mavi şerit, karşı yandaki yüzeyler aynalanarak Unity FBX'ine eklendi. Kaynak Blender dosyası değiştirilmedi. Opak van malzemeleri Unity'de iki taraftan çizilir; camlar tek yüzlü bırakılarak koyu görünümü korunur.

`02_KAYNAK_MODELLER/Servis_Minibusu` altındaki bağımsız van da incelendi; onda da yük bölmesinin bir yanında dış yüzey eksik. Mevcut kaynak aracın kimliği ve park konumu korundu. Ön far lensleri ve arka kapı tutamaklarının görünür ayrıntıları Unity prefabında tamamlandı. Ofis tasarımının kaynak `.blend` dosyası ve `Environment_OfficeGarage.fbx` bu iyileştirmede değiştirilmedi.

## Henüz var olmayan veya tamamlanmamış sanat varlıkları

Bağımsız müşteri/denetçi karakterleri, çalışan animasyonları, tamir mini oyun parçaları ve açılır garaj kapısı mekanizması oyun prefabı olarak hazırlanmadı. Kaynakta bulunan diğer tekil modellerin tamamı bu sahnede etkileşimli prefab haline getirilmedi.
