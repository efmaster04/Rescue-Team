# Atölye model ve atmosfer düzenlemesi

29 Ekim 2026. Güncel Unity sahnesi geniş açı, tezgâh, hortum makarası ve servis arabası yakın görüntüleriyle incelendi. En belirgin sorunlar köşeli tekerlekler, on benzer anahtarın ve küçük tezgâh makinelerinin tekrarı, birbirine benzeyen düz yüzeylerdi. Kaynak ofis tasarımı korunarak bu noktalara müdahale edildi.

Sonraki turda [alt alet panosu ve servis arabası](WorkbenchDetailPass.md) da yenilendi. Bu sayfadaki model kurulum komutu yeni çalışma alanı ayrıntılarını da uygular.

31 Ekim turunda [kapalı kasalar ve açık parça kutuları](GarageStoragePass.md) yenilendi. Aynı genel kurulum bu modelleri de korur.

## Uygulananlar

- Dört lastik 72 çevresel dilim, yuvarlatılmış yanak ve gerçek çevresel kanallarla yeniden modellendi. Jantlarda açık havalandırma aralıkları, altı bijon, göbek ve valf vardır. Özgün merkezler ve 0,83 m lastik çapı korundu. Değişen parçalar türetilmiş Unity meshleridir; kaynak van FBX'i veya kaporta yeniden yazılmadı.
- Tekrarlanan on üst sıra anahtarının yerine beş farklı boyda kombine anahtar ve bunların deliklerinden geçen askılar geldi. Küçük tezgâh makinelerinin tekrarları azaltıldı; döküm gövdeli, çelik çeneli, milli ve kollu bir mengene modellendi. Mengene bu düzenlemede görsel parçadır; yeni bir kullanım sistemi eklenmedi.
- Tezgâha lokma takımı ve katlı bez eklendi. Metal yüzeyin ince çizikleri mevcut çalışma alanındadır. Yirmi görünür garaj kolisinde farklı kraft tonları, kâğıt lifi ve seçili kutularda bant/etiket ayrıntıları kullanıldı. Gizlenmiş eski modellerin üzerinde bağımsız bant parçaları üretilmez.
- Arka garaj ışıkları biraz daha düşük, giriş gün ışığı biraz daha güçlü, tezgâh ışığı daha sıcak ayarlandı. Garaj LED'lerinin gölge normal bias değeri 0,12'den 0,035'e çekildi. Yeni ışık, yeni ses, küresel renk filtresi veya ofis aydınlatma değişikliği eklenmedi. Yalnız garaj yansıması yenilendi.

Yeni tezgâh/raf ayrıntıları **beş MeshRenderer** içinde birleştirildi; yeni collider yoktur. Yürüme yolları, arka kapı rotası, elle modül montajı ve liftin hareketli parçaları korunur. Sekiz tekerlek renderer'ı var olan tekerlek renderer'larının yerine geçer. Dört lastik ve dört jant toplam 25.216 üçgendir. Bu sayılar çizim çağrısı veya FPS garantisi değildir.

## Unity görüntüleri

- [Önce — geniş açı](VisualQualityBefore_Wide.png), [sonra — aynı açı](VisualQualityAfter_Wide.png)
- [Önce — tezgâh](VisualQualityBefore_Bench.png), [sonra — tezgâh](VisualQualityAfter_Bench.png)
- [Tekerlek yakını](VisualQualityAfter_Wheel.png), [mengene yakını](VisualQualityAfter_Vise.png)
- [Girişte 1,57 m göz hizası](VisualQualityAfter_Entry.png), [gerçek Play Mode oyuncu kamerası](WorkshopCraftPlayView.png)

Geniş ve yakın görseller Unity'nin gerçek geometri ve malzemelerinden render edildi. Son oyuncu görüntüsü Play Mode'da gerçek kamera/HUD ile alındı; örnek veya üretilmiş hedef görüntüsü değildir.

## Yeniden uygulama ve doğrulama

Kurulum: `Company/Refine Workshop Models and Atmosphere`. Genel `Finish Workshop Surfaces and Vehicle` kurulumu da bu ayrıntı turunu uygular. İşlem mevcut ofis/garaj sahnesine yöneliktir; kaydedilmemiş bir sahnenin üstüne köprüden çalıştırılmaz. Açık editörde `Temp/CompanyUIValidation.request` içine `workshop-craft` yazmak düzenlemeyi uygular ve tam testi başlatır.

`Art/generate_workshop_craft.py`, 512 px lastik, bez ve kraft malzeme haritaları ile 1024×256 tezgâh kullanım izini belirlenimci üretir. Kullanılan kaynaklar `Assets/_Project/Art/WorkshopCraft` altındadır. Dönel geometri oluşturulurken profil yönü, yüz normali ve üçgen yönü denetlenir; kesiklerin kenarları kapatılır. Tekrar uygulamada göbek/valf sınırlarından konum kayması birikmez.

Tam koşu: `Logs/workshop_craft_full_validation.txt` — 10 PASS ve Validation complete. Son gölge ayarından sonra hedefli kapı/yürüyüş/lift testi: `Logs/workshop_craft_finish_validation.txt`. Kaynak koruması: `Logs/presentation_source_parity.json` ve `Logs/garage_office_preservation.json`; 2.887 kaynak dönüşümü ve 989 ofis mesh/malzeme bağlantısı korundu. Geometri özeti `Logs/workshop_craft_geometry.txt` içindedir.

Tam koşunun aynı açı statik gruplama ölçümü `Logs/workshop_craft_performance.json`: 3.360 renderer gruplandı; CPU render gönderim medyanı 6,30 → 5,11 ms. Bu koşu içi karşılaştırmadır; eski sürümle bağımsız oyun FPS karşılaştırması değildir. Gerçek GPU sunum ve bağımsız derleme FPS ölçümü yapılmadı.

Önceki sahne ve görseller `Backups/WorkshopCraft` içindedir. Ana van kaportası ve çevrenin kalan kaynak modelleri hâlâ mevcut sade model ailesindedir; bu çalışma tüm oyunun sanat varlıklarını değiştiren bir paket değildir.
