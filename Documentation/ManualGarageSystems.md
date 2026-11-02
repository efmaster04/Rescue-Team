# Lift, kapılar ve elle modül montajı

## Kullanım

Araçtaki iki ön ve iki arka kapı **E** ile açılır/kapanır. Arka kapılar 180 derece açılarak yanlara katlanır. Camlar, kollar, aynalar ve kapıya ait boya/çıta parçaları kapıyla birlikte hareket eder. Kapılar, üzerindeki dış modülle birlikte oyuncu ve eşyalara karşı taranır; engelde durur, alan açılınca devam eder. Arka iki basamaktan yük bölmesine yürüyerek girilebilir.

Lift panelinin üst düğmesi yükseltir, alt düğmesi indirir. **E** ile hareket başlatılır; hareket sırasında **E** durdurur. Kollar önce temas mesafesini kapatır, ardından özgün minibüsü 0,85 m yükseltir. Tavan mesafesi korunur. Araçta hiç modül takılı olmasa da lift kullanılabilir. Üzerinde yarım takılmış modül, açık kapı veya devam eden modül işi varsa hareket başlamaz; sebebi kumanda ipucunda görünür. Araç, tekerlek, arka taşıyıcı veya basamakların hareket alanında oyuncu/taşınabilir eşya varsa lift bekler; alan boşalınca devam eder. Kumandanın yanındaki güvenli duruş alanı bu hacimlerin dışında kalır. Yükleme ve modül işi, lift tamamen yerdeyken yapılır. [Lift düzeltmesi ve test kapsamı](LiftFix.md).

## Modüller

1. Bilgisayarda **Araç → Modüller** içinden satın alın. Modül 12 saniyelik teslimat sonrasında garaja fiziksel bir servis kaseti olarak gelir. Satın alma araca kendiliğinden modül takmaz.
2. Araçtaki ekipmanı indirin, lifti yere indirin ve arka kapıları kapatın. Aracın arka dış yüzeyindeki servis taşıyıcısına gidin. Taşıyıcı arka kapıya bağlıdır; kapı açıkken montaj yapılmaz.
3. Dış taşıyıcıdaki takılı modülün bağlantısına bakıp **E** ile bağlantıyı ayırın.
4. Alet çantasını alın. Dört cıvataya sırayla bakıp **E'yi basılı tutarak** sökün. Erken bırakırsanız o işlem iptal olur.
5. Çantayı **F** ile bırakın. Ortadaki modüle bakıp **E** ile çıkarıp alın. Eski modülü garajda bir yere bırakın.
6. Yeni modülü taşıyıp aracın arkasındaki dış bağlantı yuvasına **E** ile yerleştirin.
7. Alet çantasıyla dört cıvatayı sıkın, sonra bağlantıyı takın. Modülün yeteneği ancak bütün bağlantılar tamamlanınca etkinleşir.

Yarım montaj, cıvata bit maskesi, bağlantı durumu, fiziksel kaset konumları ve yoldaki modül teslimatları kayıt sürümü 5'te saklanır. Sürüm 1–4 kayıtları yükseltilir; önceden sahip olunan yedek modüller garajda fiziksel olarak oluşturulur. Ekipman depo kapasitesiyle modül kasetleri birbirine karıştırılmaz.

Beş modülün kendi görseli vardır: nakliye çantası, vinç, yangın makarası, tahliye pompası ve teknik servis dolabı. Mekanizmalar projedeki mevcut modellerden türetilir; ortak kaide, destekler ve servis portuyla dış taşıyıcıya oturur. [Kaynak parçalar ve görüntüler](ModuleVisuals.md).

Cıvatalar E basılıyken dönüp dışarı/içeri ilerler. Erken bırakmak hem durumu hem görsel konumu geri alır. Servis kablosu bağlıyken modülün gerçek portuna ulaşır; bağlantı ayrılınca ucunda fişle aşağıda durur. Taşınan modül kameraya fazla yaklaştığında gövde ve isim etiketi birlikte gizlenir, boşluk açılınca birlikte geri gelir.

Kaset, cıvatalar ve bağlantı arka dış taşıyıcıdadır; yük bölmesi modül montajından bağımsızdır. Taşıyıcı, modül ve kablo kapıyla birlikte açılır. Mevcut kayıttaki takılı modül ve yarım montaj doğrudan yeni arka yuvaya yüklenir; ilerleme silinmez. Eski kayıtta yeni basamakların yerinde duran gevşek ekipman, yalnız ilk yüklemede boş bir garaj konumuna alınır; kimliği, durumu ve şirket bakiyesi korunur. Bu yerleşim uyarlaması sürüm 5 içindeki isteğe bağlı `rearAccessLayoutReady` alanıyla bir kez yapılır.

Araç gövdesi ve kaynak ofis dosyaları yeniden üretilmedi. Kabin, cam ve kapıların iç yüzeyleri ile ayak boşluğu kapatıldı. Arka kapıların kol geometrisi hangi yarıda bulunduğuna göre kendi kanadına ayrıldı; orta fitil de kanatla hareket eder. [29 Ekim arka erişim, model ve yüzey düzenlemesi](RearWorkshopRefresh.md).

## Ses ve FPS

Mevcut tıklama, bildirim, ayak sesi, kapı, çekmece, düşme ve motor sesleri devreden çıkarıldı; yeni ses kaynağı oluşturulmaz. FPS ve ortalama kare süresi, oyunda üst köşedeki ayrı Canvas katmanında 0,5 saniyede bir güncellenir. Duraklatmada da gerçek zamanla ölçüm sürer. Batch testlerinde görüntü sunulmadan ilerleyen simülasyon oranı oyun FPS'si gibi gösterilmez.

## Performans

- Sabit geometri çalışma başında statik gruplara alınır. Kapılar, taşınan eşyalar, modüller ve liftin hareketli 130 parçası gruplama dışında kalır.
- Gölge mesafesi 50 m'den 22 m'ye, kademesi 4'ten 2'ye indirildi. Garajda dört ışık korunur; ikisi gölge, diğerleri dolgu ışığıdır.
- Ortam gölgelendirme yarım çözünürlüktedir. Kullanılmayan opak ekran kopyası kapalıdır. Render ölçeği 1,0 kalır; kamera FXAA kullanır.
- Çok küçük garaj ayrıntıları görünmeye devam eder, gölge üretmez. Masaüstü saati ve odak imleci gereksiz yere her karede güncellenmez.

Aynı açıdan 1600×900 açık render isteğiyle ölçülen statik gruplama karşılaştırması `Logs/garage_performance_comparison.json` içindedir. Süreler CPU'nun render gönderim maliyetidir; ekran kartının gerçek sunum süresi veya bağımsız oyuncu FPS garantisi değildir. Kullanılamayan çizim çağrısı sayacı -1 olarak belirtilir.

## Doğrulama

- `Company/Validate Lift, Vehicle Doors and FPS`
- `Company/Validate Manual Module Installation`
- `Company/Validate Play Loop`

Kontroller gerçek Input System E/F basışlarını, basılı tutma/iptali, satın alma ve fiziksel teslimatı, dört cıvata + bağlantı sırasını ve yarım montaj kayıt/yüklemesini içerir. Kullanıcının şirket kaydı test boyunca yedeklenir ve geri yüklenir. Son bütünleşik sonuç `Logs/office_garage_validation.txt` içindedir.
