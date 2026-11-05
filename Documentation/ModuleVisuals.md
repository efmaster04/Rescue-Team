# Dış modül görselleri ve montaj ayrıntıları

> 5 Kasım 2026: beş modül ve ortak kaide Blender'da yeniden modellendi; bu sayfa önceki tasarımın kaydıdır. [Güncel modeller](ServiceModuleArt.md).

31 Ekim ikinci tasarım turunda ekipmanların ana biçimleri, ortak kaide ve yüzeyleri yeniden düzenlendi. Araç modeli değiştirilmedi. [Güncel modeller ve kapsam](ModuleEquipmentRedesign.md).

Beş taşınabilir servis birimi ekipman için ayrı üretilen geometri kullanır. Modüller aracın arka dış yüzeyinde, arka kapıyla birlikte hareket eden taşıyıcıdadır. Kaynak ofis, araç geometrisi/malzemeleri ve park konumu korundu. Önceki modül prefabları ve kod `Backups/ModuleEquipmentPass` içindedir.

| Modül | Yeni model | Unity görüntüsü |
| --- | --- | --- |
| Nakliye | Kaburgalı kalıplı kasa, conta, mandallar, menteşeler ve yan tutamaklar | [Görsel](ModuleEquipmentAfter_cargo.png) |
| Çekici | Alçak motor, kademeli dişli kutusu, tambur, delik sac kılavuz, turuncu kanca ve bağlı elektrik kabloları | [Görsel](ModuleEquipmentAfter_towing.png) |
| Yangın | Dört kollu sac makara, üçgen destek, hortum, vana, gösterge ve nozul | [Görsel](ModuleEquipmentAfter_fire.png) |
| Su tahliye | Aynı eksende elektrik motoru/kaplin/pompa, emiş dirseği, açık bağlantı ağızları ve koruma çerçevesi | [Görsel](ModuleEquipmentAfter_water.png) |
| Teknik servis | Eğimli kumanda paneli, soketler, anahtarlar, yan ızgaralar, tek takım çekmecesi ve ölçüm kablosu | [Görsel](ModuleEquipmentAfter_tech.png) |

Güncel mekanizma ve kaide meshleri `Assets/_Project/Art/ModuleEquipment`, prefablar `Assets/_Project/Prefabs/Modules/ServiceModule_*.prefab` içindedir. Birimler alçak bir kaide ve servis bağlantı yuvası kullanır. Metal, kauçuk, boya ve dokuma ayrı malzemelerdir. Küçük isim etiketleri yalnız gerçek modül adlarını gösterir. Eski `VehicleMechanics/Portable*` varlıkları yeni modüllerin kaynağı değildir.

## Montajın görünür davranışı

- Altıgen cıvata başı E basılıyken döner ve ekseninde ilerler. İşlem bitmeden kayıtlı cıvata durumu değişmez. Tuş bırakılırsa baş önceki konumuna döner.
- Bağlantı takılıyken kablo modülün gerçek servis portunda sonlanır. Ayrılınca fiş aşağıda park eder. Kablo taşıyıcıya bağlıdır; arka kapı ve lift hareketlerinde araçla birlikte gider.
- Kameraya giren taşınabilir modülün isim etiketi gövdesiyle birlikte gizlenir; açık alanda ikisi de geri gelir.
- Aynı modül durumu tekrar yazıldığında tavan sinyali yeniden oluşturulmaz. Durum göstergeleri malzeme kopyalamak yerine MaterialPropertyBlock kullanır. Her servis birimindeki sabit kaide/destekler birleştirilir: mekanizma, şase ve port için toplam üç MeshRenderer bulunur.

Kayıt sürümü 5 ve elle montaj sırası korunur. Arka yük alanı dış modülden bağımsızdır. Gerçek E girdisiyle kapıları açma, krikoyu yükleme, aynı krikoyu geri alma ve kapatma çalıştırıldı. [Montaj adımları](ManualGarageSystems.md).

## Doğrulama

İkinci model tasarımının tam koşusu `Logs/module_redesign_full_validation.txt` içinde 10 PASS ve Validation complete ile tamamlandı. Araç varlık koruması `Logs/module_redesign_vehicle_after.json`, kullanıcı kaydının geri yüklenmesi `Logs/module_redesign_finish_state.json` içindedir. [Doğrulama sonuçları](TestResults.md). Önceki koşular kendi raporlarında korunur.

Kontroller beş farklı gerçek mekanizma meshini, üç MeshRenderer sınırını, kablo uçlarını, cıvatanın işlem sırasında dönmesini/iptalini, yakın duvarda etiketin gizlenip geri gelmesini, fiziksel teslimat ve montajı, eski kayıt yükseltmesini, yarım montaj kaydını ve gerçek E ile arka yükleme/boşaltmayı kapsar. Montaj testinin oyuncu konumu arka raftan ve gerçek katı collider'lardan uzaktadır; modülün geniş fiziksel kabuğu cıvata etkileşim ışınını kapatmaz. Görüntüler Unity'den ayrı karelerde alınır. Saha pompa/vinç oynanışı bu görsel düzenlemenin parçası değildir.

[Araç üzerindeki görünüm](ExteriorModuleDetail.png), [cıvata üzerinde çalışma](ModuleFastenerWorking.png).
