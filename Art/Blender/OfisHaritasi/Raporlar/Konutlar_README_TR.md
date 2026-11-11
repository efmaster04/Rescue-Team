# Konutlar seti (Ofis Haritasi, konut tarafi)

Ofisin bulundugu kasabanin konut mahallesi icin 3 apartman ve 4 bahceli mustakil ev. Hepsi tek
Blender betigiyle sifirdan uretilir: sivali betonarme, kiremit kirma ve besik catilar, duz catida
gunes enerjisi + beyaz su deposu, dolu parapetli balkonlar, rulo veya kanatli panjurlar, kagir
ayakli alcak bahce duvari ustunde kare kesitli demir parmaklik, ondeki kapidan eve beton yol.
Yazi, logo, numara, doku yok. Duz golgeleme, duz renk malzeme, pencere basina tek cerceve + 0,15 m
iceride opak koyu cam.

## Dosyalar

| Ne | Yol |
|---|---|
| Uretim betigi | `Kaynak\Uretim\build_konutlar.py` |
| Onizleme betigi | `Kaynak\Uretim\render_konutlar.py` |
| Kontrol betigi | `Kaynak\Uretim\verify_konutlar.py` |
| Kaynak sahne | `Kaynak\Blender\Konutlar.blend` (her varlik kendi koleksiyonunda, orijinde) |
| FBX (7 adet) | `Oyun\Assets\OfisHaritasi\Modeller\Konutlar\{Apartman_A,Apartman_B,Apartman_C,Ev_A,Ev_B,Ev_C,Ev_D}.fbx` |
| Manifest | `Oyun\Assets\OfisHaritasi\Modeller\Konutlar\Konutlar_manifest.json` |
| Malzemeler | `Oyun\Assets\OfisHaritasi\Modeller\Konutlar\Konutlar_malzemeler.json` |
| Onizlemeler | `Kaynak\Onizlemeler\Konutlar\<id>_on.png`, `<id>_arka.png`, `Konutlar_toplu.png` |
| Kontrol ciktisi | `Kaynak\Raporlar\Konutlar_verify_sonuc.txt` |

Calistirma (sirayla; build ~4 sn, verify ~10 sn, tam render ~8-10 dk CPU):

```
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b --factory-startup -P build_konutlar.py   [-- --no-export]
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b --factory-startup -P verify_konutlar.py
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b --factory-startup -P render_konutlar.py  [-- --quick] [--only Ev_A,Ev_B] [--no-toplu]
```

## Eksen ve orijin

- FBX `ORTAK_KURALLAR.md` 4. bolumdeki cagriyla aktarilir: Blender X = Unity X, Blender Y = Unity Z,
  Blender Z = Unity Y.
- Orijin parselin sokak tarafi on kenarinin ortasi, Z=0. Sokak Unity -Z, parsel +Z yonune uzanir:
  X in [-G/2, G/2], Z in [0, D].
- Butun nesneler orijinde, donus 0, olcek 1. Bina ve duvar tabanlari -0,6 m'ye iner. Parsel ici yol ve
  doseme plakalari ust +0,04, alt -0,6. Cim zemini Unity verir (modelde cim yok).
- Manifestteki `boyut` -0,6 m'lik gomulu kismi da icerir; zemin ustu yukseklik `yukseklik_zemin_ustu`.

## Varliklar

| id | Parsel G x D | Bina | Kat / kotlar | Cati | Ayirt edici | Ucgen (gorunur / COL) |
|---|---|---|---|---|---|---|
| Apartman_A | 22 x 22 | 12 x 10, on cephe 5 m geride | zemin + 2 kat, 3,0 m; zemin +0,45, sacak 9,45 | kirma kiremit, 2 baca | ondeki 6 dolu parapetli balkon, ortada merdiven kovasi (sahanlik pencereleri), kapi ustunde beton sacak, 3 basamak, yalniz on bahce duvari, arac kapisi + 2 park yeri | 2866 / 120 |
| Apartman_B | 22 x 22 | 14 x 11, 4 m geride | 3,6 m dukkan kati + 2 x 3,0 m; cati 9,75, parapet 10,75 | duz, parapetli | 2 dukkan (vitrin + yari inik kepenk + bos tabela bandi), ust katlarda 5,6 m'lik orta cikma ve iki yanda balkon, catida 3 gunes enerjisi (cift kolektor + yatay beyaz depo), dik su deposu, arkada merdiven kulesi; dukkan onu doseli, bahce duvari yok | 2300 / 24 |
| Apartman_C | 22 x 24 | 13 x 10, 5 m geride | zemin (+0,9) + 1 kat + cati kati (6,9); sacak 7,6 | besik kiremit, alinlik sokaga bakar, 2 baca | yan cephede (dogu) balkon, giris kapisi yaninda cephe boyunca inen sahanlikli 5 basamakli merdiven + dolu korkuluk + sacak, alinlikta cati kati pencereleri, on + yan bahce duvarlari | 4330 / 156 |
| Ev_A | 18 x 22 | 9 x 8 (+ 9 x 2 veranda), 6 m geride (veranda 4 m) | tek kat, zemin +0,6, sacak 3,5 | kirma kiremit (veranda dahil) | onde 4 kolonlu veranda, 3 basamak, kanatli panjurlar | 3980 / 180 |
| Ev_B | 18 x 22 | 8 x 8, 5 m geride | 2 kat, zemin +0,3, sacak 6,1 | piramit kirma kiremit + gunes enerjisi | ust katta dolu parapetli balkon, giris sacagi + 2 basamak | 4535 / 108 |
| Ev_C | 18 x 22 | L plan 11 x 9 (ana kanat 11 x 5 + sokaga bakan kanat 5 x 4), 5 m geride | tek kat, zemin +0,3, sacak 3,2 | iki mahyali besik kiremit (dere hatti kesik) | ic kosede giris sacagi, sagda ahsap cardak / araba sundurmasi (5 direk, kiris, mertek, sac ortu); park yeri sundurmanin altinda | 4203 / 192 |
| Ev_D | 18 x 22 | 9 x 9, 6 m geride (merdiven 4,75) | 2 kat, zemin +0,25, ust kat 3,1, sacak 5,9 | besik kiremit, genis ahsap sacak (0,8 m) | on cephede sahanlikli 17 basamakli tas dis merdiven + demir korkuluk ile ust kat girisi, zeminde cift kanat ahsap depo kapisi, onde doseli avlu | 4416 / 312 |

Butceler: apartman <= 8000, ev <= 6000 ucgen (COL dahil de altinda). Ucgenlerin yarisi kadari evlerde
bahce duvarlarindadir (dort kenar, ~270 demir cubuk x 8 ucgen).

## FBX icerigi

Her FBX'te yalniz mesh'ler vardir (bos nesne yok):

- `<id>_Bina`: bina, cati, balkonlar, merdivenler, bacalar, cardak, gunes enerjisi.
- `<id>_Bahce`: parsel ici yol, arac yolu, doseme, avlu plakalari.
- `<id>_Duvar_On`, `_Duvar_Sol`, `_Duvar_Sag`, `_Duvar_Arka`: bahce duvari hatlari (ayri nesne).
  Komsu parsellerle ortak sinirlarda cift duvar olusmasin diye `<id>_Duvar_Sol/Sag` ve
  `COL_<id>_Duvar_Sol_*`/`Sag_*` birlikte kapatilabilir. On kosedeki ayaklar `On`'a, arka kosedekiler
  `Arka`'ya aittir (Apartman_C'de arka duvar yok, arka ayaklar yan duvarlarda).
- `COL_<id>_*`: carpisma kutulari (asagida).

Bahce duvari: 0,44 m kagir taban + 0,06 m harpusta (ust 0,50), 0,50-1,20 m arasi 0,02 m kare demir
cubuk (aralik 0,20-0,24 m, alt ve ust lama), en fazla 3,2 m'de bir 0,34 m kagir ayak (1,35 m + baslik
1,43 m). Onde 1,2 m yaya ve 3,0 m arac kapisi boslugu acik (kanat yok). Duvarin dis yuzu parsel
sinirinda, butun parcalar parsel icinde.

## Malzemeler (`Konutlar_malzemeler.json`)

`color` sRGB 0-1, `smoothness` = 1 - roughness, emisyon yok, alpha 1.

| Malzeme | Kullanim | Randomize |
|---|---|---|
| KN_Siva | cephe sivasi, parapet, baca | `konut_siva` |
| KN_Bant | kat bantlari, pencere cevreleri ve kesitleri, sacak alti/alin, harpusta, balkon dosemesi | |
| KN_Kiremit | kiremit cati ve mahya | |
| KN_Cam | opak koyu cam (0,15 m iceride ic yuzey) | |
| KN_Panjur | rulo panjur (apartmanlar), kanatli panjur (evler) | `konut_panjur` |
| KN_Duvar | subasman, bahce duvari tabani ve ayaklari, tas merdiven | |
| KN_Demir | parmaklik, korkuluk, kolektor ayaklari | |
| KN_Kapi | ahsap kapilar | |
| KN_Beton | beton yol, basamak, harpusta, ayak basligi, esik | |
| KN_Doseme | dukkan onu, veranda, avlu, duz cati yuzeyi | |
| KN_Ahsap | Ev_C cardagi, Ev_D sacagi ve kat bandi | |
| KN_Kolektor | gunes enerjisi kolektor camlari | |
| KN_Depo | beyaz su depolari | |
| KN_Kepenk | dukkan kepengi, vitrin cercevesi, cardak sac ortusu, kolektor cercevesi | |
| KN_Tabela | bos tabela bandi | |

`palettes.konut_siva` (7): krem, acik_hardal, acik_somon, kirik_beyaz, acik_gri_yesil,
acik_mavi_gri, acik_bej. `palettes.konut_panjur` (4): koyu_yesil, kahve, gri_mavi, bej. Her giris
`{"ad", "color"}`. Manifestte her varlik icin onizlemede kullanilan secim `onizleme_renkleri` alaninda.

## Manifest (`Konutlar_manifest.json`)

Koordinatlar Unity yerel ekseninde. Her varlik:

- `id`, `fbx`, `boyut` [X, Y, Z], `yukseklik_zemin_ustu`, `sinir_kutusu`, `ucgen`, `ucgen_carpisma`,
  `ucgen_butce`, `parsel` [G, D]
- `bina_izleri`: `{ad, tur, min [x,z], max [x,z], yukseklik}`; `tur` bina / veranda / balkon /
  cardak / merdiven / ust_kat_cikmasi / cati_ustu
- `giris_kesimleri`: on kenarda bordurun alcaltilacagi x araliklari = arac kapisi boslugu
  (Apartman_B'de arac girisi yok, liste bos)
- `park_yerleri`: `{konum [x, 0.04, z], yon_derece 0 (arac onu +Z), boyut [2.5, 5]}`, arac kapisinin
  hemen arkasinda (Apartman_A'da arka arkaya 2, Apartman_B'de yok)
- `agac_noktalari`: [x, 0, z]; her evde 4, apartmanlarda 2-4
- `nesneler`, `carpisma`, `malzemeler`, `bahce_duvari` (kenar basina kapilar ve cubuk sayisi),
  `ozellikler` (kat, kotlar, cati tipi)

## Carpisma

Hepsi 8 koseli, eksenlere hizali, donussuz `COL_` kutular (malzemesiz; Unity BoxCollider'a cevirip
gizler):

- `COL_<id>_Bina` (duvar izi, -0,6'dan sacak/parapet kotuna), Ev_C'de `_Kanat` ayrica,
  `COL_<id>_Cati` (duvar izi icinde, sacaktan cati yuksekliginin %60'ina), Apartman_B'de
  `_Merdiven_Kulesi`
- zemin kat balkonlari (Apartman_A `_Balkon_Sol/_Sag`), Ev_A `_Veranda` ve 4 `_Kolon`, Ev_C 5
  `_Cardak_Direk`
- basamaklar tek tek kutu (`_Basamak_n`, riht 0,15 m; Ev_D dis merdiveni 0,172 m), sahanliklar ve
  merdiven korkuluklari
- bahce duvari hatlari: kapilar arasinda kesintisiz ince kutular (`_Duvar_<kenar>_<n>`, 0,34 m kalin,
  1,43 m), kapi bosluklari acik

## Kontrol sonucu (verify_konutlar.py)

**TEMIZ, 0 hata.** Kontrol edilenler (tam liste `Konutlar_verify_sonuc.txt`):

- parsel disina tasma yok (+-0,05 m; gercekte butun parcalar parsel icinde)
- bina izleri 12x10, 14x11, 13x10, 9x8, 8x8, 11x9 (L), 9x9; on cephe 4-6 m geride; sacak/parapet ve
  kat kotlari (3,0 m kat)
- bahce duvari 0,50 taban + 0,70 parmaklik, cubuk araligi 0,20-0,24 m, on duvarda olculen bosluklar
  1,2 ve 3,0 m, `giris_kesimleri` arac kapisiyla ayni hizada
- ucgen butcesi, butun gorunur mesh'ler ucgenlenmis, duz golgeleme
- COL_ kutulari 8 kose / 6 yuz / eksen hizali / donussuz
- FONT, egri, yazi verisi yok
- butun malzemeler JSON'da, randomize ve paletler (7 + 4)
- park yerleri: 2,5 x 5 x 1,8 m hacim bina/duvar/direklere degmiyor, arac kapisinin arkasinda
- agac noktalari: 1,2 x 1,2 x 3,5 m hacim bina, yol ve duvara degmiyor, binalara >= 2,0 m
- FBX'ler yeniden ice aktarildiginda nesne adlari, malzeme adlari ve olculer (sapma 0,00 mm) korunuyor,
  COL kutulari hala eksen hizali

## Zayif noktalar ve Unity icin notlar

- Unity'de denenmedi; yalniz Blender'da yeniden ice aktarma ile dogrulandi.
- Ic mekan yok; cam opak. Kapi ve kapi bosluklarinda kanat yok (yaya ve arac kapilari acik bosluk).
- Bitisik parsellerde yan duvarlar cift olur; yukaridaki gibi `_Duvar_Sol/Sag` kapatilmali.
- Cati COL kutusu kaba: kirma/besik catinin yalniz alt kismini kapsar, sacak tasmasi COL disinda.
  Ust kat balkonlari, Apartman_B cikmasi ve sacaklar icin COL yok (yerden yuksek).
- Merdivenler kutu basamak; karakterin step offset'i Ev_D icin en az ~0,18 m olmali. Egimli
  korkuluklarin COL'u merdivenin dis kenarinda tek kutu (alcak ucta da korkuluk yuksekliginde).
- Butun cephe tek `KN_Siva`; Unity randomize ettiginde bant/pencere cevreleri sabit beyaz kalir
  (istenen sey bu). Panjurlar da varlik basina tek renk.
- Kiremit, siva, tas dokusuz duz renk; yakindan bakildiginda cati buyuk tek renk yuzey gibi durur
  (mahya ve kirma basliklari hafif golge veriyor).
- Onizlemeler AgX ile alindi; Unity URP'de renkler biraz daha doygun ve koyu gorunebilir.
- Agac noktalarinin cevresi ~1,5-2 m bos; buyuk tacli agaclar binaya yakin noktalarda
  (Ev_D 7,0/13,0; Ev_A 5,8/2,4) sacaga degebilir, oralara cali/kucuk meyve agaci uygun.
