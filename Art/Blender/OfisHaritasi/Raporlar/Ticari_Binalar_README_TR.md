# Ticari_Binalar seti (ofis kasabası sanayi şeridi)

Ofis-garajın bulunduğu kasabanın sanayi şeridi için beş ticari parsel: üç atölye, bir benzinlik, bir market. Hepsi tek Blender betiğiyle sıfırdan üretilir. Görünüm ofis-garaj ve servis minibüsüyle aynı: sade mid-poly, düz gölgeleme, tek segment pah, donuk düz renkler. Yazı, logo, marka, numara ve doku yok. Tabelalar boş düz panel, fiyat pilonu panelleri boş.

## Dosyalar

| Ne | Yol |
|---|---|
| Üretim | `Kaynak\Uretim\build_ticari_binalar.py` |
| Önizleme | `Kaynak\Uretim\render_ticari_binalar.py` |
| Doğrulama | `Kaynak\Uretim\verify_ticari_binalar.py` (sonuç: `Kaynak\Raporlar\Ticari_Binalar_verify_sonuc.txt`) |
| .blend | `Kaynak\Blender\Ticari_Binalar.blend` (her varlık kendi koleksiyonunda, hepsi orijinde üst üste) |
| FBX | `Oyun\Assets\OfisHaritasi\Modeller\Ticari_Binalar\{Atolye_A,Atolye_B,Atolye_C,Benzinlik,Market}.fbx` |
| Manifest | `...\Ticari_Binalar\Ticari_Binalar_manifest.json` |
| Malzemeler | `...\Ticari_Binalar\Ticari_Binalar_malzemeler.json` (34 malzeme + `palettes`) |
| Önizlemeler | `Kaynak\Onizlemeler\Ticari_Binalar\<id>_on_34.png`, `<id>_arka_34.png`, `Ticari_Binalar_toplu.png` |

Çalıştırma (sırayla):

```
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b -P build_ticari_binalar.py            (-- --no-export: yalnız .blend)
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b -P verify_ticari_binalar.py
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b -P render_ticari_binalar.py           (-- --quick, --only <id>, --no-toplu)
```

Üretim yaklaşık 5 sn, doğrulama 10 sn, tam önizleme seti (Cycles CPU, 56 örnek + OIDN) i3-10100F'de yaklaşık 12 dakika sürer.

## Eksen, orijin, kotlar

- FBX `ORTAK_KURALLAR.md` ayarıyla yazılır: Blender X = Unity X, Blender Y = Unity Z, Blender Z = Unity Y.
- Orijin parselin sokak tarafındaki ön kenarının ortası, Z=0. Sokak Unity −Z yönünde, parsel X ∈ [−G/2, +G/2], Z ∈ [0, D].
- Döşemelerin üst yüzü +0,04, altı −0,6. Bina, duvar ve kolon tabanları −0,6'ya iner.
- Bütün nesnelerin dönüşü 0, ölçeği 1. Sabit nesnelerin orijini parsel orijininde. Kepenk perdelerinin orijini açıklığın üst kenarında (sarılma ekseni). COL kutularının orijini kutu merkezinde.
- Manifestteki bütün koordinatlar Unity yerelinde `[x, y, z]` biçimindedir.

## Varlıklar

| id | parsel G×D | bina kütlesi | yükseklik | görsel üçgen / bütçe | COL |
|---|---|---|---|---|---|
| Atolye_A | 24 × 32 | 14 × 18 çelik kaplamalı atölye, x −10..4, z 11..29 | saçak 5,50, mahya 7,02 | **5848** / 6000 | 16 |
| Atolye_B | 20 × 28 | 10 × 12 sıvalı tuğla atölye, x −9,6..0,4, z 14..26 | parapet 5,07 | **3250** / 6000 | 13 |
| Atolye_C | 30 × 28 | 22 × 14 iki dükkânlı blok, x −11..11, z 12..26 | parapet 6,67 | **5348** / 6000 | 19 |
| Benzinlik | 36 × 30 | kanopi 18 × 10 (x −9..9, z 7..17) + market 12 × 8 (x −6..6, z 21,5..29,5) | kanopi altı 5,20, üstü 6,20; market 4,20 | **4432** / 10000 | 28 |
| Market | 30 × 32 | 20 × 14 tek katlı market, x −10..10, z 17,7..31,7 | 4,50 (giriş parapeti 5,43) | **3230** / 8000 | 14 |

COL kutuları 12 üçgen sayılır ve görsel bütçeye dahil değildir.

### Atolye_A: çelik kaplamalı oto tamir atölyesi
- Trapez sac kaplama (0,75 m aralıklı dikey nervürler, `TB_Kaplama`), 0,9 m beton subasman, antrasit köşe ve pencere profilleri.
- 10° beşik çatı: trapez nervürler, iki ışıklık şeridi, mahya, alın profilleri, oluklar ve dört yağmur borusu.
- Ön cephe: 4,0 × 4,2 m sarmal kepenk (yatay lamel profil, kepenk kutusu, raylar), mavi-gri servis kapısı, ofis penceresi, üstte bant pencere. Yan cephelerde ikişer uzun bant pencere, sağ yanda klima dış ünitesi, arkada kapı, basamak ve iki pencere.
- Avlu: ön ve sağ yan beton döşeme. Ön kenarda ve avlu boyunca yanlarda 0,4 m beton duvar ve üstünde dikey çubuklu çit (2,5 cm kare çubuk, 0,25 m aralık, toplam 1,8 m). Ön kenarda 6,0 m araç kapısı boşluğu var, iki yanında beton baba duruyor. Bina hizasından sonra yan ve arka sınırlarda 2,0 m dolu bahçe duvarı.
- Eşya: üç palet istifi (biri ön avluda), üç yağ varili.

### Atolye_B: sıvalı tuğla atölye
- Krem sıva duvarlar, açık gri boyalı beton karkas (köşe ve ara kolonlar, hatıl), düz çatı, parapet ve harpuşta.
- Önde iki adet 3,2 × 3,6 m sarmal kepenk, küçük ofis kapısı, kapının üstünde saçak ve pencere. Yanlarda pencereler, çatıda su deposu, iki yağmur borusu.
- Ön avlu ve sağ yan avlu beton. Yan ve arka sınırda 1,9 m dolu duvar, ön kenar tümüyle açık.
- Sağ yan avluda binaya yaslanan tek eğimli çelik araç sundurması (üç direk). Lastik yığınları, palet ve variller.

### Atolye_C: iki dükkânlı sanayi bloğu
- 22 × 14 blok, iki adet 11 m'lik birim. Her birimde 4,0 × 4,4 m kepenk, camlı kapı ve vitrinli küçük ofis cephesi, üstünde asma kat penceresi.
- Bütün cephe boyunca 1,5 m beton saçak (5,0–5,22 m), saçağın üstünde birim başına boş tabela paneli. İki tabela ayrı malzeme olduğu için Unity'de farklı renk alabilir.
- Düz çatı, parapet, iki su deposu, iki klima, arkada pencereler ve kapılar.
- Önde beton avlu. Ön kenarda iki adet 6,0 m araç kapısı boşluğu (her kepenk hizasında), beton duvar ve çubuklu çit, beton babalar. Yanlarda 3,8 m genişliğinde beton servis yolları.

### Benzinlik
- Bütün parsel döşeli: kanopi altı beton, kalan alan asfalt. Hiçbir döşeme market binasının altından geçmez.
- Kanopi 18 × 10, alt yüzü 5,20, alın bandı 1,0 m. Kırık beyaz alın bandının ortasından tek koyu petrol yeşili şerit geçer, köşeler pahlıdır. Pompa adalarının üstünde 4 kolon var, kanopinin altında 6 tavan lambası (hafif ışıklı).
- 2 pompa adası, her birinde 2 pompa. Pompalar sade kutu formundadır: antrasit taban, kırık beyaz gövde, petrol yeşili baş. Her yüzde bir tabanca yuvası ve tek kalın hortum kıvrımı var. Ekran ve yazı yok.
- Arkada 12 × 8 camlı market: yarı saydam vitrin, camın 0,3 m arkasında koyu iç yüzey (raf bantlı arka yüzey, aydınlık tavan), otomatik kapı boşluğu, kanopiyle aynı şeritli alın bandı, önünde kaldırım.
- Önde iki giriş (x −15,5..−7,5 ve 7,5..15,5), aralarında bordürlü çimlik ada ve üstünde boş panelli fiyat pilonu. Ön köşelerde iki küçük çim tarh var. Sol arka köşede hava-su direği, yan ve arka sınırda bordür, market yanlarında çizgili park yerleri.

### Market
- 20 × 14 × 4,5 m tek katlı market. Ön cephede iki vitrin ve otomatik kapı boşluğu bulunur (arkalarında 0,3 m içeride koyu iç yüzey, raf bantları, aydınlık tavan). 1,3 m antrasit saçak, boş tabela bandı ve giriş üstünde yükseltilmiş parapet var.
- Sağ yanda servis kapısı ve teslimat kepengi (3,0 × 3,2), solda kapı ve pencereler. Çatıda iki klima ve bir çatı kapağı.
- Önde asfalt otopark: 10 park yeri (6 tanesi binaya bakıyor, 4 tanesi sokağa bakıyor), 1 cm kabarık beyaz boya şeritleri, kaldırım, ön tarh ve şerit tarh.
- Alışveriş arabası sundurmasında iki sıra iç içe araba duruyor. Sağ arka köşede üç duvarlı çöp alanı ve iki konteyner var. Sağ yan servis yolu asfalt, sol yan çim (Unity zemini).

## Nesne adları

Her FBX'te görsel mesh'ler `<id>_<parça>` adını taşır, çarpışma kutuları `COL_<id>_<parça>` adını.

| id | görsel nesneler |
|---|---|
| Atolye_A | `Atolye_A_Bina`, `_Cati`, `_Cit`, `_Doseme`, `_Esya`, `_Kepenk_1` |
| Atolye_B | `Atolye_B_Bina`, `_Cati`, `_Cit`, `_Doseme`, `_Esya`, `_Sundurma`, `_Kepenk_1`, `_Kepenk_2` |
| Atolye_C | `Atolye_C_Bina`, `_Cati`, `_Cit`, `_Doseme`, `_Esya`, `_Kepenk_1`, `_Kepenk_2` |
| Benzinlik | `Benzinlik_Kanopi`, `_Kanopi_Lamba`, `_Pompa_Adalari`, `_Market`, `_Market_Cati`, `_Doseme`, `_Pilon`, `_Hava_Su` |
| Market | `Market_Bina`, `_Cati`, `_Doseme`, `_Araba_Sundurmasi`, `_Konteyner_Alani`, `_Kepenk_1` |

COL kutuları (hepsi eksen hizalı, 8 köşeli, malzemesiz):
- **Bina kütleleri:** `COL_<id>_Bina` (Atölye A'da mahyaya kadar), `COL_Benzinlik_Market`, `COL_Benzinlik_Market_Alin`.
- **Yüksekteki parçalar:** `COL_Atolye_C_Sacak` (5,0 m), `COL_Market_Sacak` (3,15 m), `COL_Market_Giris_Parapeti`.
- **Kanopi:** `COL_Benzinlik_Kanopi` 5,2–6,2 m arasında, altından araç geçer.
- **Kolon ve adalar:** `COL_Benzinlik_Kolon_1..4`, `COL_Benzinlik_Ada_Pompa_1..2` (ada ve pompalar, 2,31 m'ye kadar).
- **Duvar ve çit hatları:** `_Cit_On_*`, `_Cit_Yan_*`, `_Duvar_Yan_*`, `_Duvar_Arka`, `_Kapi_Babasi_*`.
- **Bordür, ada ve kaldırım:** `_Bordur_*`, `_Ada_*`, `_Kaldirim`.
- **Eşyalar:** `_Palet_*`, `_Variller`, `_Lastik_*`, `_Sundurma_Direk_*`, `_Araba_Sundurmasi`, `_Konteyner*`, `_Pilon`, `_Hava_Su`.
- **Sürülebilir döşeme:** `COL_<id>_Doseme_*`, üst yüzü 0,04, alt yüzü −0,6. Araçlar terrain yerine bunun üstünde gitsin diye eklendi. İstenmezse adından süzülüp atlanabilir.

## Manifest alanları

`boyut` = [X genişlik, zemin üstü yükseklik, Z derinlik]. `ucgen` görsel üçgen sayısıdır. Bunların yanında `col_ucgen`, `ucgen_butcesi`, `parsel`, `parsel_sinirlari`, `bina_izleri` (`min`/`max`/`boyut`, Unity yerel), `giris_kesimleri`, `park_yerleri`, `agac_noktalari`, `hareketli` (kepenkler: ad, genişlik, yükseklik), `nesneler`, `col_kutulari` ve `hedef_olculer` alanları da yazılır.

Park yerlerinde `konum` = [x, 0,04, z]. Y değeri döşeme üst kotudur; brifteki 0 yerine araç tekerleği döşemeye otursun diye 0,04 yazıldı. `yon_derece` Unity Y dönüşüdür (0 = araç önü +Z), `boyut` [2,5, 5].

| id | giriş kesimleri (x) | park yerleri [x, z, yön] | ağaç noktaları [x, z] |
|---|---|---|---|
| Atolye_A | [−8,5, −2,5] | ön avlu (0,9, 7,0, 0), (3,6, 7,0, 0); yan avlu (7,0, 18,5, 0), (9,8, 18,5, 0) | (−6,0, 30,6), (0,5, 30,6) |
| Atolye_B | [−9,5, 9,5] | ön (−7,5, 9,0, 0), (−3,7, 9,0, 0), (6,0, 8,0, 0); sundurma altı (2,9, 20,0, 0), (5,9, 20,0, 0) | (−5,0, 27,0) |
| Atolye_C | [−11,1, −5,1], [−0,1, 5,9] | ofis önleri (−3,05, 7,5, 0), (7,95, 7,5, 0); servis yolları (12,9, 17,0, 0), (−12,9, 17,0, 0) | (−6,0, 27,0), (6,0, 27,0) |
| Benzinlik | [−15,5, −7,5], [7,5, 15,5] | pompa başları (−5,85 / −1,35 / 1,35 / 5,85, 12,0, 0); market yanı (8,25 / 10,75 / 13,25 / −8,25 / −10,75, 24,5, 0) | (−16,7, 1,65), (16,7, 1,65), (−2,0, 1,65) |
| Market | [6,0, 14,0] | binaya bakan (−8,25 / −5,75 / −3,25 / −0,75 / 1,75 / 4,25, 14,0, 0); sokağa bakan (−13,25 / −10,75 / −8,25 / −5,75, 3,3, 180) | (−1,0, 3,0), (3,5, 3,0), (−12,6, 20,5), (−12,6, 27,5) |

## Malzemeler

`Ticari_Binalar_malzemeler.json` içinde 34 adet `TB_` malzemesi var. Renkler sRGB'dir.

- **Rastgele renk alanlar:**
  - `TB_Kaplama` (`randomize: atolye_kaplama`): `palettes` bölümünde 5 donuk renk (açık gri, kırık beyaz, mavi-gri, bej-gri, adaçayı gri).
  - `TB_Tabela` ve `TB_Tabela_2` (`randomize: ticari_tabela`): 5 donuk tabela rengi (kırık beyaz, lacivert, bordo, hardal, petrol).
- **Saydam:** `TB_Cam_Vitrin` alpha 0,5. Unity'de Transparent yüzey ister; market ve benzinlik vitrininde kullanılır.
- **Opak koyu cam:** `TB_Cam`, atölye pencerelerinde. İçi görünmez.
- **Işıklı:** `TB_Lamba` (kanopi tavanı) ve `TB_Ic_Acik` (vitrin arkasındaki tavan).
- Prosedürel düğüm yok, UV yok. Bütün renkler düz.

## Doğrulama sonucu

`verify_ticari_binalar.py` çıktısı: **SONUÇ: TEMİZ** (0 hata). Kontrol edilenler:

- **Parsel sınırı:** bütün geometri (COL dahil) parsel sınırı içinde (±0,05 m), en alt kot −0,6.
- **Ölçüler:**
  - Gövdeler 14×18, 10×12, 22×14, 20×14 ve 12×8.
  - Atölye A'nın saçağı 5,50, kepengi 4,0×4,2, kapı boşluğu 6,00.
  - Kanopi 18×10, alt yüzü 5,200, COL altı 5,2.
  - Kolon sayısı 4, ada sayısı 2. Kepenk açıklıkları manifestle aynı. Market yüksekliği 4,50, park yeri 10.
- **Bütçe:** üçgenler bütçe içinde ve manifestle aynı.
- **COL kutuları:** hepsi eksen hizalı 8 köşeli kutu, malzemesiz. Dönüş 0, ölçek 1.
- **Yazı nesnesi:** FONT nesnesi ya da yazı eğrisi yok.
- **Malzemeler:** kullanılan bütün malzemeler JSON'da, randomize paletleri tanımlı.
- **Park yerleri ve girişler:** park yerleri döşeme üstünde; duvar, bina, ada, eşya ya da birbiriyle çakışmıyor. Giriş kesimleri açık ve önlerinde döşeme var.
- **FBX geri okuma:** FBX yeniden içe aktarılınca nesne adları, dünya ölçüleri (±3 mm), üçgen sayıları ve malzeme adları korunuyor, COL kutuları eksen hizalı kalıyor.

## Sınırlar ve bilinen zayıf noktalar

- Unity'de denenmedi (brif gereği Unity açılmadı). FBX geri okuması Blender'da yapıldı.
- İç mekân yok. Kepenkler kapalı modellendi: perdenin 0,22 m arkasında koyu bir yüzey var. Kepenk perdeleri ayrı nesnedir ve orijinleri üst kenardadır; açılma animasyonu (Z ölçeği ya da gizleme) Unity tarafında yapılmalı.
- Çubuklu çitler Atölye A ve C'de üçgenlerin yaklaşık üçte birini tutuyor (2,0k ve 2,2k). LOD yok.
- UV yok. Unity'de ışık haritası pişirilecekse "Generate Lightmap UVs" açılmalı.
- `TB_Cam_Vitrin` saydamlığı Unity içe aktarıcısının alpha < 1 için Transparent kurmasına bağlı. Kurmazsa vitrin koyu opak görünür; arkadaki iç yüzey yine çalışır.
- Yan ve arka bahçe duvarları parselin kendi içindedir (dış yüz sınırdan 2 cm içeride). Parseller bitişik dizilirse iki duvar yan yana gelir. Atölye B'nin yan duvarları sokak kenarına kadar uzanır.
- Ağaç noktaları yalnızca öneridir. Market sol yanı ve atölyelerin arkası Unity çimine açık bırakıldı.
