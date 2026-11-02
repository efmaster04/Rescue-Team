# Arka erişim, servis modülleri ve okunurluk

29 Ekim 2026. Kullanıcının son isteğine göre modüller yan yüzeyden aracın arka dışına taşındı. Özgün ofis tasarımı ve minibüsün park konumu korundu.

## Aracın arkası

Taşıyıcı bir arka kapıya bağlıdır; kaset, dört cıvata ve servis kablosu kapıyla birlikte hareket eder. Kapılar 180 derece açılarak yanlara katlanır. Kapı taraması hem kanadı hem dış taşıyıcıyı kapsar; oyuncu veya eşya varsa güvenle bekler. Montaj, taşıyıcının kapısı kapalı ve lift yerdeyken yapılır.

İki kaymaz basamak yük tabanına çıkışı sağlar. Basamakların gerçek yürüyüş collider'ları vardır; üzerlerindeki küçük kavrama şeritleri oyuncuyu takmaz. Gövde, arka taşıyıcı ve basamaklar liftin güvenlik kontrolüne dahildir. Eski kayıtta yeni basamak alanında duran gevşek ekipman bir defa boş garaj konumuna alınır. Eşya kimliği, dayanıklılık, para ve yarım modül montajı korunur.

Gerçek E girdisiyle kapılar açıldı. Ardından W girdisiyle sağ yandan arkaya, yük bölmesinin içine, dışarıya ve sol yana yüründü. İçeri çıkışta karakterin gerçek ayak yüksekliği 0,66 m yük tabanına ulaştı. Test, yalnız kamera veya collider konumu denetimi değildir.

[Kapılar açıkken arka geçiş](RearDoorWalkwayClear.png), [takılı arka modül](ExteriorModuleMounted.png), [montaj ayrıntısı](ExteriorModuleDetail.png).

## Model ve yüzeyler

Beş modülde sarılı halat/hortum, yataklar, soğutma kanatları, boru tutamaklar, çekmeceler ve sabitleme kayışları kullanıldı. Kaide ve taşıyıcı kenarları pahlıdır. Ayrıntılar birleştirildiğinden modül başına üç MeshRenderer korunur; bu sayı çizim çağrısı sayısı anlamına gelmez. [Modül kaynakları ve görüntüleri](ModuleVisuals.md).

Garaj ve van malzemelerine 512 px albedo, normal ve metal/pürüzlülük haritaları eklendi. Haritalar `Art/generate_workshop_finish.py` ile belirlenimci olarak üretilir; kaynak model dokuları üzerine yazılmaz. Zemin, mat boya, metal, kauçuk ve cam farklı ışık tepkisi gösterir. Lastik normalleri yumuşatıldı; minibüsün kaynak köşe konumları değişmedi. Garajdaki mevcut dört armatür ayarlandı ve yalnız garaj yansıması yeniden pişirildi. Bir adet collider'sız zemin kullanım izi renderer'ı eklendi; yeni ışık eklenmedi.

[Garajın güncel görünümü](WorkshopFinishAfter.png). Yüzey kurulumu: `Company/Finish Workshop Surfaces and Vehicle`. Önceki sahne ve malzemeler `Backups/WorkshopVisualFinish` içindedir.

## Arayüz

Uygulama içindeki sol bölüm sütunu kaldırıldı; 21 px yazılı yatay sekmeler başlığın altındadır. İçerik alanı genişledi. Tablo metinleri 20 px oldu; iş fişindeki müşteri, konum, mesafe ve süre iki sütunlu düz bilgi alanında toplandı. Kontrast yükseltildi, başlık ölçüsü dengelendi. Fiyat, sözleşme ve kabul/sipariş düğmeleri görünür alanda kalır. Raporun toplam tutarı ve tahsilat düğmesi sabit alt alanda, ayrıntıları ayrı kaydırılan bölümdedir. Masaüstünde yedi uygulama ve alt çubuk vardır; slogan veya ek karşılama metni yoktur.

[İşler](ComputerJobsPreview.png), [1280×720 görünüm](ComputerJobs720Preview.png), [Mağaza](ComputerShopPreview.png). Güncel görsel değerlendirme [design-qa.md](../design-qa.md) içindedir.

## Kanıt ve sınırlar

Güncel bütünleşik sonuç: `Logs/rear_workshop_full_validation.txt`. Hedefli montaj: `Logs/rear_module_finish_validation.txt`. Kaynak koruması: `Logs/presentation_source_parity.json` ve `Logs/garage_office_preservation.json`. Ayrıntılı test kapsamı [TestResults.md](TestResults.md) içindedir.

Testler Unity Play Mode içinde Input System/EventSystem olaylarını kullanır. Kullanıcının şirket kaydı test bitince geri yüklenir. Bağımsız derlemede FPS ölçümü ve insanla serbest oynanış yapılmadı; saha görevleri veya ağ eşitlemesi bu değişikliğe eklenmedi.
