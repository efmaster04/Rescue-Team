# Minivan — görsel inceleme ve teslim notları

Referanslar doğrudan açılıp incelendi: `01_turnaround_light_blue.jpg`,
`02_color_options_leftmost_chosen.jpg`, `03_scene_style.jpg`.
Her karşılaştırmada solda ana konseptin üç çeyrek görünümü, sağda model bulunur.
Konsept ölçü kaynağı değildir: 5.0 × 1.9 × 2.0 m sözleşmesi çizimdeki çok kısa
karikatür araca göre daha uzun bir yan siluet gerektirir. Burun −Y, aracın solu +X,
sağı −X; sürücü solda ve sürgü yalnız sağdadır. Yazı/marka/plaka eklenmedi.

## Tur 01 — ilk siluet ve mekanik yerleşim

- `renders/round_01/`: üç çeyrek ön-sol, bütün kapılar açık arka-sağ, tavanı gizli üst görünüm, `compare_concept.png`.
- 6.480 üçgen. Dört ayrı tekerlek, dört ayrı kapı, altı oturma yeri ve iki bavul mevcut.
- Gözlem: ışık/albedo birleşimi maviyi fazla beyazlatıyor; arka çeyrekteki ek sabit camlar konseptteki kapalı sac görünümünü değiştiriyor.
- Gözlem: üst kamera eğik/çapraz ve son koltuk sırasını kesiyor. Ön tampon üstünde gereksiz açıklık var.
- Karar: gövdenin nötr orta tonundan yeniden örnek al; ışığı azalt; arka çeyreği kapat; kaputu tampon hizasına uzat; üst kamerayı tam dik ve yatay kadraja al.

## Tur 02 — renk, kapalı arka gövde ve altı koltuk

- `renders/round_02/`: aynı üç kontrol açısı ve karşılaştırma.
- 6.384 üçgen. Boya ana konseptte (210,655) çevresindeki 5×5 piksel medyanından `#9AC4D2`.
- Gözlem: pastel mavi ve kapalı arka çeyrek konseptle daha yakın. Altı koltuk, soldaki direksiyon ve taban üst görünümde tamamıyla okunuyor.
- Gözlem: cam çerçeveleri konseptten açık; tekerleklerin iç yüzü kabinden görünüyor; kapı/kolon birleşimleri fazla ayrık.
- BVH kontrolü: son açık limitler yeterliydi; ara açılma pozları A sütunu ve tavan kenarında 2 cm üzeri temas buldu.
- Karar: çerçevelerde mevcut lastik tonunu kullan; iç çamurlukları kapat; A sütununun menteşe süpürme alanını ve tavan eşiğini düzelt.

## Tur 03 — yedi yön, hareket testi, fon denemesi

- `renders/round_03/`: front, rear, left, right, threequarter_front_left, threequarter_rear_right_open, interior_top; 1920×1080.
- 6.336 üçgen. Tavan/kolon düzenlemesi, koyu cam contaları ve iç çamurluklar işlendi.
- Gözlem: tam yan görünüm küçük panel boşluklarını belirginleştirdi; özellikle sabit yan cam önünde ve sol ezik panelinin sonunda fon görünüyordu.
- Gözlem: saydam film + shadow catcher denemesi camların arkasında grenli alfa üretti; teslim için uygun bulunmadı.
- Karar: arka menteşe yanındaki kalınlığı azalt; kapı aralıklarına ince koyu contalar koy; sabit panel bağlantılarını tamamla; gerçek açık gri stüdyo zeminine geri dön.

## Tur 04 — nihai kontrol

- `renders/round_04/`: yedi teslim açısı ve konsept karşılaştırması. `renders/` kökündeki yedi PNG bunların kopyalarıdır.
- Yedi açı ve karşılaştırma açılıp incelendi: cam grenleri giderildi; altı minder ve sol direksiyon üstten görünür; sol sac kapalı, sağ sürgü ayrı; açık bagaj ve orta çeki gözü arka-sağ görünümde okunur.
- Son geometri **6.552 üçgen**. Gövde 2.060; iç mekân 1.520; sabit cam 24; ön kapılar 156'şar; sürgü 120; bagaj kapağı 132; tekerlekler 520'şer; bavullar 152'şer.
- FBX ölçüleri: uzunluk 4.999 m, kollar dahil genişlik 1.9485 m, yerden tavan 2.000 m. Üçü de ±0.05 m toleransında. Yan kaporta genişliği 1.90 m'dir.
- Son BVH denetiminde 11 poz içindeki en büyük giriş: ön kapılarda 2.675 mm, sağ sürgüde 12.762 mm, bagajda 4.513 mm; hepsi 20 mm sınırının altında.
- Kapalı pozda gövde bağlantıları; açık pozda 70° ön kapılar, 85° bagaj kapağı ve önce 0.12 m dışarı/sonra +Y yönünde 1.39 m kayan sağ sürgü kontrol edilir.
- Üst görünümde yalnız gövdenin tavan/raf yüzleri ve bavullar render için gizlenir; FBX ve kaydedilen Blend tamamen kapalı ve eksiksizdir.
- Bütün hareketli parçalar kökün doğrudan çocuğudur. Kapı camları kapı mesh'indedir; sabit camlar `SM_Minivan_Glass` içindedir.
- Hafif hasar: eğik iki parçalı ön tampon, sol ön farda kırık cam, sol sabit sacda 4.2 cm göçük ve üç küçük çizik.

## Teknik denetimin kapsamı

- Betikler hata durumunda `sys.exit(1)`, başarıda `BUILD OK` / `VERIFY OK` yazar. Komutlarda `--python-exit-code 1` kullanılır.
- `verify_minivan.py` fabrikadan boş sahneye yalnız FBX'i yeniden yükler. Adlar/hiyerarşi, üçgenler, geometriden ölçüler ve pivotlar, koltuk altındaki minder yüzeyi, TowPoint, ham FBX DiffuseColor ve yeniden yüklenmiş materyaller denetlenir.
- Kapıların her biri 11 hareket pozunda BVH yüzey kesişimi, verteks/üçgen iç nokta örnekleri, en yakın yüzey mesafesi ve üç ışınlı iç/dış oylamasıyla denetlenir. Hem kapı→gövde hem gövde→kapı yönü ölçülür. Bu, örneklemeli statik geometri denetimidir; fizik motoru testi değildir.
- Ham geometri SHA256, iki ardışık başarılı build'in kaynak SHA256'sı ve geometri SHA256'sı ile karşılaştırılır. Ayrıca FBX dünya uzayı üçgenleri 0.1 mm çözünürlükte sıralanarak kaynak geometriyle karşılaştırılır.
- Döngüler ve topoloji deterministiktir; Cycles seed 0, 64 sample, denoiser, AgX, OptiX / RTX 5080. Süreler `build_history.json` ve `stats.json` içindedir.
- Boyut kontrolü aynaları, tavan üstü raf/bavulları ve arka çeki halkasını dar konumsal bölgelerle dışarıda bırakır; kapı kolları ölçüye dahildir. Tekerlek tabanı ve kök Z=0'dır.
- Sadece export sırasında Principled Base Color geçici sRGB yapılır; ardından Blender'ın lineer rengine geri alınır. Böylece Unity sRGB renk kayması önlenir.

## Renkler ve alan sözleşmesi

Tüm düz renklerin hex değerleri ve örnek piksel koordinatları `minivan_meta.json` içindeki
`materials` ve `palette_samples` alanlarındadır. Kullanılan tek ek koyu materyal `M_Minivan_Damage`.
`field_descriptions` tüm pivot/eksen/hareket/işaret alanlarının anlamını açıklar.
Cam alpha değeri 0.34'tür; FBX bu değeri taşır. Unity'nin kullanılan render pipeline'ında
saydam shader seçimi sonraki entegrasyon işidir; bu görevde Unity veya mekân değiştirilmez.

| Materyal son eki | sRGB hex |
|---|---|
| Paint | #9AC4D2 |
| Trim | #93909B |
| Tire | #1E2129 |
| Rim | #4D494D |
| Glass | #D7D7DB |
| Interior | #686270 |
| Headlight | #CAC8CC |
| Taillight | #CB4136 |
| Suitcase_A | #A87958 |
| Suitcase_B | #566A82 |
| Strap | #86A1B8 |
| Damage | #171716 |

Kabul kanıtları: `acceptance_build_1.log`, `acceptance_build_2.log`,
`acceptance_verify.log`, `acceptance_exit_codes.json`, `stats.json`.
İki ardışık varsayılan build: **exit 0 / BUILD OK**; bağımsız FBX doğrulama:
**exit 0 / VERIFY OK**, hata listesi boş. Her iki build aynı kaynak SHA256'sını
ve aşağıdaki aynı geometri SHA256'sını üretti.
Son yedi render **35.868 saniye**, son toplam build **36.909 saniye**;
görsel düzeltmeler ve kabul tekrarları dahil tüm render sürelerinin toplamı **273.525 saniye**.
Raf/bavullar dahil gerçek en yüksek nokta **2.4195 m**.
Geometri SHA256: `4d9d9cf66e373d452c38c80156590f21edc2e275e9ca70e3fcbe96bfaf92ed9a`.
Görsel turlardaki değişiklik geçmişi ve her başarılı çalışmanın süresi `build_history.json` içindedir.

## Konseptten bilinçli sapmalar / açık sorular

- Sürgü konseptteki soldan kullanıcının istediği sağ tarafa taşındı; hasar solda kaldı.
- Mutlak uzunluk nedeniyle model konseptteki kısa karikatür siluetinden uzundur; tekerlek çapı 0.75 m ve gövde tavanı 2.0 m korunur.
- İki ayrı bavul ve basit 2+2+2 koltuk düzeni kullanıldı. Son sıranın yanları konseptteki gibi kapalıdır; arka camdan görünür.
- AÇIK SORULAR: yok. Belirsiz ayrıntılarda markasız, yazısız, nötr geometri seçildi.
