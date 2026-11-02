# Tezgâh aletleri ve servis arabası

29 Ekim 2026. Bu tur garajdaki yakın çalışma alanına yöneliktir. Kaynak ofis yerleşimi, araç, kapılar, arka modül yuvası, lift ve çekmece hareketleri korunur.

## Görsel değişiklikler

- Alt panodaki tekrarlanan aletler yerine kombine pense, uzun burun pense, dört farklı boy tornavida, cırcır ve küresel başlı çekiç modellendi. Tornavidalar boyunlarından tutan bir rafa, diğer aletler çift destekli askılara oturur. Metal çeneler, menteşe, kauçuk saplar ve değişen kesitler vardır.
- Önceki anahtar düzenlemesinden kalan beş bağımsız metal çıkıntı ile artık kullanılmayan askı/alet parçaları kapatıldı. Tezgâhta üst üste dizilmiş on iki cıvata takımı yerine sığ bir metal parça kabı ve birkaç yatık bağlantı parçası yerleştirildi.
- Servis arabası ince kenarlı iki tabla, kauçuk iç yüzey, iki parça tepsisi, katlı bez, metal çerçeve ve tutma koluyla yenilendi. Dört tekerlek yuvarlak kesitli, düzgün kauçuk yüzeylidir; eski modeldeki yaklaşık beş santimetrelik zemin boşluğu giderildi. Yeni lastiklerin alt sınırı araba yerelinde 0,004 m'dir. Fork, aks ve döner bağlantı tabakaları görünür.
- TAB / İş fişi ve ESC / Menü ipuçlarına küçük, koyu arka plan eklendi. Açık renk tavan armatürü önünde kaybolan yazılar böylece sabit karşıtlıkla okunur. Hazırlık paneli ve kontroller aynı kalır.

174 kaynak alet/tezgâh dekor renderer'ı kapatıldı; kaynak dönüşümleri ve dosyası değiştirilmedi. Yeni pano ve parça kabı tek MeshRenderer içinde birleştirilir. Servis arabasının 41 görsel renderer'ı tek MeshRenderer ile değiştirilir. Bu iki modelin birden fazla alt malzemesi vardır; iki çizim çağrısı anlamına gelmez. Yeni collider, ışık, ses veya etkileşim eklenmedi. Asıl taşınabilir ekipmanlar ve servis arabasının mevcut fiziksel hacmi korunur. Panodaki küçük aletler bu turda dekor olarak kalır; elle modül işlemleri mevcut alet çantasıyla yapılır.

## Görüntüler

[Önce — pano](WorkbenchDetailBefore_Board.png), [sonra — aynı açı](WorkbenchDetailAfter_Board.png).

[Önce — servis arabası](WorkbenchDetailBefore_Cart.png), [sonra — aynı açı](WorkbenchDetailAfter_Cart.png).

[Tezgâh geneli](WorkbenchDetailAfter_Bench.png), [Play Mode oyuncu kamerası](WorkbenchDetailPlayView.png). Görseller gerçek Unity modellerinden alınır; yeni görseller açılıp incelenmiştir.

## Yeniden uygulama

`Company > Refine Workbench Tools and Service Cart` mevcut sahneyi günceller. Genel `Refine Workshop Models and Atmosphere` ve `Finish Workshop Surfaces and Vehicle` kurulumları da bu ayrıntıları uygular. `Temp/CompanyUIValidation.request` dosyasına `workbench-detail` yazmak düzenleme ve tam testi, `workbench-preview` yalnız düzenlemeyi çalıştırır.

Kaynak sahne, önceki atölye kurulum kodu, HUD kaynağı ve rapor `Documentation/Backups/WorkbenchDetailPass` içindedir. Geometri raporu `Logs/workbench_detail_geometry.txt` dosyasıdır.

## Doğrulama ve sınırlar

`Logs/workbench_detail_finish_validation.txt`: son tam koşuda 10 PASS ve Validation complete. İlk model koşusu da `Logs/workbench_detail_full_validation.txt` içinde korunur. Kaynak denetiminde 2.887 dönüşüm ve 989 ofis mesh/malzeme bağlantısı değişmedi. `Logs/workbench_detail_finish_state.json` şirket kaydının SHA-256 özetinin geri geldiğini ve bekleyen test/yedek kalmadığını kaydeder. Sahne kaydedilmiş, Play Mode kapatılmıştır.

Bir ara koşu arka yürüyüşte zaman aşımına uğradı; kaydı `Logs/workbench_hud_intermediate_failure.txt` içindedir. Sonraki normal hedefli, düşük FPS hedefli ve son tam koşularda takılma tekrarlanmadı. Sebep kesinleşmediği için çözüldüğü iddia edilmez. Son tam koşu, arka rotayı 20 FPS isteğiyle gerçek W girdisi kullanarak tamamladı; girişte ayak yüksekliği 0,69 m oldu. Gerçek kare aralıkları `Logs/workbench_detail_finish_walk.txt` içindedir; 20 FPS sabit kaldı iddiası yoktur. Hata halinde sentetik tuşların, geçici FPS/VSync ayarlarının ve test kaynaklarının temizlenmesi güvenceye alındı.

`Logs/workbench_detail_reapply_check.json`: dört yeni mesh/malzeme varlığı tekrar uygulandığında SHA-256 özetleri değişmedi. Pano/parça kabı 18.364 üçgen ve dört alt malzeme; araba 10.060 üçgen ve altı alt malzemedir. Kaynak küçük parçalarının renderer sayısı azalır; bu bir FPS garantisi değildir.

`Logs/workbench_detail_finish_performance.json`: aynı açı statik gruplama ölçümünde 3.145 renderer, CPU render gönderim medyanı 13,56 → 12,19 ms. Bağımsız oyun FPS/GPU ölçümü yapılmadı. Görsellerdeki sayaç anlık test koşulunu gösterir; sürümler arası performans kıyası olarak kullanılmaz.
