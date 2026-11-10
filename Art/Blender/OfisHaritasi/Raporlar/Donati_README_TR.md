# Donati seti: ofis kasabasinin sokak donatisi ve agaclari

Rescue Team ofis haritasi (Unity, URP) icin 25 varlik: trafik isiklari, tabelalar, bariyerler, lambalar,
elektrik diregi, durak, bank, cop kutusu/konteyner, mahalle cesmesi, park oyuncaklari, agaclar ve calilar.
Hepsi tek Blender betigiyle bos sahneden uretilir; elle duzenleme yok. Yazi, logo, doku yok; duz renk,
duz golgeleme, tek segment pah. Agaclar ofis onundeki faset agaclarla ayni dilde (dusuk bolumlu ikosfer
yaprak kumeleri, 8-9 kenarli konik cam katlari) ve ayni yaprak/govde renkleriyle yapildi.

Toplam: 25 FBX, 11 311 gorsel ucgen, 27 malzeme. Verify: **TEMIZ (1392 kontrol)**.

## Dosyalar

| Ne | Yol |
|---|---|
| Uretim betigi | `Kaynak\Uretim\build_donati.py` |
| Onizleme betigi | `Kaynak\Uretim\render_donati.py` |
| Kontrol betigi | `Kaynak\Uretim\verify_donati.py` |
| .blend | `Kaynak\Blender\Donati.blend` |
| FBX (25 adet) | `Oyun\Assets\OfisHaritasi\Modeller\Donati\<id>.fbx` |
| Manifest | `Oyun\Assets\OfisHaritasi\Modeller\Donati\Donati_manifest.json` |
| Malzemeler | `Oyun\Assets\OfisHaritasi\Modeller\Donati\Donati_malzemeler.json` |
| Onizlemeler | `Kaynak\Onizlemeler\Donati\<id>.png`, `Trafik_Isigi_*_yanik.png`, `Donati_toplu.png`, `Donati_sokak_kosesi.png` |
| Verify ciktisi | `Kaynak\Raporlar\Donati_verify_sonuc.txt` |

(`.meta` dosyasi yazilmadi; Unity acilmadi.)

## Calistirma

```
set B="C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
%B% -b --factory-startup -P Kaynak\Uretim\build_donati.py                 (FBX + JSON + .blend, ~10 sn)
%B% -b --factory-startup -P Kaynak\Uretim\build_donati.py -- --no-export  (yalniz olcu/ucgen dokumu)
%B% -b --factory-startup -P Kaynak\Uretim\verify_donati.py
%B% -b Kaynak\Blender\Donati.blend -P Kaynak\Uretim\render_donati.py        (tam kalite ~25 dk)
     secenekler: -- --quick  --only Bank,Cesme  --no-singles  --no-toplu  --no-kose
```

## Eksen, orijin, adlar

- FBX ayari ORTAK_KURALLAR'daki gibi (axis_forward=Z, axis_up=Y, bake_space_transform, FBX_SCALE_ALL,
  use_triangles). Blender X = Unity X, Blender Y = Unity Z, Blender Z = Unity Y. Manifestteki butun
  koordinatlar Unity yerel ekseninde: `[x, y, z]` = Blender `(x, z, y)`.
- On yuz Blender -Y (Unity -Z). Trafik isigi basliklari, mercekler ve tabela yuzleri -Y'ye bakar; +Y
  yonunde (Unity +Z) ilerleyen surucu onlari gorur.
- Kollu direklerin (trafik isigi, sokak lambasi) kolu Blender/Unity **-X**'e uzanir; direk yolun sag
  kenarina konur, kol yolun ustune gelir.
- Orijin: direklerde direk ekseni Z=0; agaclarda govde ekseni; bank, durak, konteyner, cesme ve oyun
  aletlerinde taban izinin merkezi; Beton_Bariyer ortada; segmentlerde (Bariyer_Otoyol, Cali_Cit) segment
  basi, segment +X'e uzanir.
- Butun nesneler donus 0 / olcek 1. Yalniz ayri nesnelerin orijini anlamli noktadadir: mercekler mercek on
  yuzu merkezinde, `Lamba_Isik` isik yuzeyinin merkezinde, `Kapak` ve `Oturak_*` mafsalda, `COL_` kutulari
  kutu merkezinde, `Tel_*` EMPTY'leri izolator tepesinde.
- Direkler, govdeler ve ayaklar zeminin 0,25-0,30 m altina kadar uzanir (egimli arazide bosluk kalmasin).
  Manifestte `zemin_alti` alaninda yazili; `boyut` yuksekligi Z=0'dan tepeye olculur.
- .blend icinde ayni adlar birden cok varlikta oldugu icin nesneler `<id>|<ad>` diye adlandirildi
  (ornek `Trafik_Isigi_Direk|Isik_Kirmizi_1`). FBX'e yazarken gecici olarak sade ada cevriliyor; FBX'te
  adlar sade (`Isik_Kirmizi_1`, `COL_Direk`, `Lamba_Isik`, `Tel_1` ...). Verify yeniden ice aktarimda
  adlarin birebir korundugunu ve `.001` eki olmadigini denetliyor.

## Varliklar

Boyut Unity ekseniyle genislik X x yukseklik Y x derinlik Z (m). Ucgen gorsel mesh toplami (COL_ haric).

| id | tur | boyut | ucgen / butce | FBX nesneleri | carpisma |
|---|---|---|---|---|---|
| `Trafik_Isigi_Kollu` | trafik | 4.27 x 6.55 x 0.79 | 1328 / 2500 | `Trafik_Isigi_Kollu`, `Isik_Kirmizi/Sari/Yesil_1` (kol), `_2` (direk) | `COL_Direk` |
| `Trafik_Isigi_Direk` | trafik | 0.46 x 3.62 x 0.71 | 672 / 2500 | `Trafik_Isigi_Direk`, `Isik_Kirmizi/Sari/Yesil_1` | `COL_Direk` |
| `Tabela_YolVer` | trafik | 0.90 x 2.83 x 0.11 | 153 / 800 | `Tabela_YolVer` | `COL_Direk` |
| `Tabela_Dur` | trafik | 0.75 x 2.83 x 0.11 | 178 / 800 | `Tabela_Dur` | `COL_Direk` |
| `Tabela_YayaGecidi` | trafik | 0.60 x 2.83 x 0.11 | 159 / 800 | `Tabela_YayaGecidi` | `COL_Direk` |
| `Tabela_Park` | trafik | 0.60 x 2.83 x 0.11 | 166 / 800 | `Tabela_Park` | `COL_Direk` |
| `Bariyer_Otoyol` | trafik | 4.00 x 0.75 x 0.32 | 228 / 800 | `Bariyer_Otoyol` | `COL_Bariyer` |
| `Beton_Bariyer` | trafik | 2.00 x 0.81 x 0.61 | 60 / 800 | `Beton_Bariyer` | `COL_Govde` |
| `Baba` | trafik | 0.18 x 0.90 x 0.17 | 166 / 800 | `Baba` | `COL_Govde` |
| `Sokak_Lambasi` | aydinlatma | 2.62 x 8.04 x 0.40 | 312 / 1000 | `Sokak_Lambasi`, `Lamba_Isik` | `COL_Direk` |
| `Park_Lambasi` | aydinlatma | 0.43 x 3.50 x 0.43 | 284 / 1000 | `Park_Lambasi`, `Lamba_Isik` | `COL_Direk` |
| `Elektrik_Diregi` | aydinlatma | 1.90 x 9.24 x 0.22 | 412 / 1000 | `Elektrik_Diregi`, `Tel_1`, `Tel_2`, `Tel_3` (EMPTY) | `COL_Direk` |
| `Otobus_Duragi` | esya | 4.20 x 2.60 x 1.62 | 780 / 2000 | `Otobus_Duragi` | `COL_ArkaPanel`, `COL_YanPanel`, `COL_Direk_On`, `COL_Bank`, `COL_Cati` |
| `Bank` | esya | 1.80 x 0.88 x 0.56 | 684 / 800 | `Bank` | `COL_Bank` |
| `Cop_Kutusu` | esya | 0.44 x 1.07 x 0.51 | 332 / 800 | `Cop_Kutusu` | `COL_Govde` |
| `Cop_Konteyneri` | esya | 1.40 x 1.33 x 1.16 | 624 / 800 | `Cop_Konteyneri`, `Kapak` | `COL_Govde` |
| `Cesme` | esya | 1.60 x 2.00 x 0.80 | 593 / 800 | `Cesme` | `COL_Govde`, `COL_Tekne` |
| `Oyun_Kaydirak` | esya | 0.80 x 1.99 x 3.61 | 604 / 800 | `Oyun_Kaydirak` | `COL_Kule`, `COL_Kaydirak_Ust`, `COL_Kaydirak_Alt` |
| `Oyun_Salincak` | esya | 2.90 x 2.28 x 1.97 | 580 / 800 | `Oyun_Salincak`, `Oturak_1`, `Oturak_2` | `COL_Ayak_Sol`, `COL_Ayak_Sag` |
| `Agac_Cinar` | bitki | 9.05 x 9.35 x 9.38 | 1128 / 1500 | `Agac_Cinar` | `COL_Govde` |
| `Agac_Kavak` | bitki | 2.96 x 13.03 x 2.98 | 720 / 1500 | `Agac_Kavak` | `COL_Govde` |
| `Agac_Cam` | bitki | 4.73 x 9.02 x 4.74 | 268 / 1500 | `Agac_Cam` | `COL_Govde` |
| `Agac_Meyve` | bitki | 3.01 x 4.03 x 3.03 | 466 / 1500 | `Agac_Meyve` | `COL_Govde` |
| `Cali_Yuvarlak` | bitki | 1.57 x 1.20 x 1.55 | 200 / 400 | `Cali_Yuvarlak` | - |
| `Cali_Cit` | bitki | 2.00 x 1.12 x 0.77 | 214 / 400 | `Cali_Cit` | - |

Kisa tarifler:

- **Trafik_Isigi_Kollu**: 6,5 m galvaniz konik direk (12 kenar, 5 cm tepe kapagi ile 6,55 m), taban
  flansi ve sekizgen taban kapagi; -X'e 4,0 m kol (koke dogru hafif yukselir, alt destek cubugu).
  Kol basligi kol ucunda (merkez x = -3,72), arka plaka alt kenari **5,40 m**; kol plakanin arkasindan
  gecer, kelepceyle tutulur. Direk basligi diregin onunde, alt kenari **2,60 m**. Baslik: 0,46 x 1,02 m
  siyah arka plaka, 0,30 x 0,86 x 0,22 m koyu gri govde, uc adet 200 mm mercek (ustte kirmizi, ortada
  sari, altta yesil), ustu uzun yanlari kisalan kavisli siperler.
- **Trafik_Isigi_Direk**: 3,2 m direk, ayni baslik diregin onunde, alt kenari 2,60 m.
- **Tabelalar**: 2,8 m galvaniz boru direk, plaka diregin onunde (-Y), arka yuzu galvaniz gri.
  YolVer 0,90 m ters ucgen (kirmizi kenar, beyaz ic); Dur 0,75 m sekizgen (beyaz kenar, duz kirmizi,
  yazisiz); YayaGecidi 0,60 m mavi kare + beyaz ucgen; Park 0,60 m mavi kare + beyaz dikdortgen cerceve.
  Renkli katmanlar plaka onunde 5 mm arayla tek yuzler.
- **Bariyer_Otoyol**: 4 m W profil galvaniz ray (on yuz -Y, tepeler one bakar), x = 1 ve 3 m'de dikme +
  ara takoz; uc uca 4 m aralikla dizilince dikme araligi 2 m kesintisiz kalir. Ray uc kesitleri ozdes.
- **Beton_Bariyer**: 2 m New Jersey profili (taban 0,61 m, ust 0,15 m, yukseklik 0,81 m), pahli uclar.
- **Baba**: 0,9 m koyu dokum kaldirim babasi, kubbe tepe, beyaz bant.
- **Sokak_Lambasi**: 8 m konik galvaniz direk, -X'e 1,8 m kol + destek, yassi koyu gri armatur;
  `Lamba_Isik` armaturun altindaki 0,40 x 0,24 m isik yuzeyi (asagi bakar).
- **Park_Lambasi**: 3,5 m dokum klasik park lambasi, sekizgen kaide, kare fener; `Lamba_Isik` fenerin
  ic camidir (kesik piramit).
- **Elektrik_Diregi**: 9 m konik dikdortgen beton direk (pahli koseler), 1,9 m galvaniz travers (X
  boyunca) + iki egik destek, uc izolator (travers uclarinda x = -0,85 / +0,85 ve direk tepesinde).
  Teller Y (Unity Z) boyunca, sokaga paralel uzanir.
- **Otobus_Duragi**: 4,2 x 1,6 x 2,6 m; koyu gri kare profil iskelet, one dogru yukselen acik gri cati +
  on alin bandi, arkada iki opak acik mavi-gri panel, +X ucunda yan panel, icinde 2 m uc citali bank.
  Reklam yok.
- **Bank**: 1,8 m; dokum yan ayaklar (on ayak kolcaga kadar uzar), dort oturma ve uc arkalik citasi.
- **Cop_Kutusu**: diregin onune asili (-Y) koyu yesil boyali silindir kutu, ici koyu, agiz bilezigi.
- **Cop_Konteyneri**: 1100 lt tipi; konik govde, ust dudak, yan kaldirma pimleri, dort doner teker.
  `Kapak` ayri nesne, orijini arka ust mentese hattinda.
- **Cesme**: tas kaide, arka govde, iki ayak arasinda sivri kemerli nis (ic yuzu koyu tas), nis ustunde
  duz (yazisiz) ayna tasi, cikmali sacak ve basik cati, pirinc musluk blogu + oluk, onde tas tekne.
- **Oyun_Kaydirak**: 1,2 m platform, 4 dikme ve yan korkuluklar (koyu yesil), egik merdiven (+Y),
  U kesitli kirmizi oluk -Y'ye iner.
- **Oyun_Salincak**: A ayakli koyu yesil cerceve, 2,2 m kiris; iki kaucuk oturak ince cubuklarla asili.
  `Oturak_1`, `Oturak_2` ayri nesne, orijin kiristeki mafsalda.
- **Agaclar**: Cinar 9,3 m, genis basik kubbe tac (tac yaricapi 4,8 m, tac alti 3,8 m), kalin govde ve
  bes ana dal. Kavak 13 m ince sutun tac. Cam 9 m, alti katli dokuz kenarli konik dallar (alt katlar koyu,
  ust katlar zeytin yesili). Meyve 4 m kisa govde, uc dal, yuvarlak kucuk tac. Yaprak kumeleri dusuk
  bolumlu (1-2) ikosfer, rastgele kose oynatmali; tohumlar sabit, betik her calistiginda ayni sekli verir.
- **Cali_Yuvarlak**: 1,2 m, alti kume. **Cali_Cit**: 2 m budanmis cit segmenti, ust yuzler acik zeytin,
  yanlar koyu; uc kesitler duz ve ozdes (uc uca dizilince dikis gorunmez), taban acik (zemine gomulu).

## Manifest alanlari (Unity icin)

Her varlik: `id`, `fbx`, `tur`, `boyut`, `ucgen`, `ucgen_butcesi`, `orijin`, `sinirlar_unity`,
`zemin_alti`, `nesneler` (ad, tur, yerel konum), `carpisma`. Sete ozel:

- Trafik isiklari: `lensler` {"kirmizi": [...], "sari": [...], "yesil": [...]}, `basliklar` (alt kenar,
  merkez x, lens eki), `baslik_yonu_unity` [0,0,-1], `lens_yaricapi` 0,10, kollu direkte `kol`.
  Mercek orijinleri mercegin on yuz merkezinde; nokta/spot isik oraya konabilir.
- Lambalar: `isik_nesnesi` = `Lamba_Isik`, `isik_noktasi` (sokak lambasinda isik yuzeyinin 5 cm alti),
  `isik_turu` (sokak: asagi spot, park: nokta).
- Elektrik diregi: `tel_noktalari` [{"ad": "Tel_1", "konum": [-0.85, 8.99, 0]}, {"Tel_2": [0, 9.24, 0]},
  {"Tel_3": [0.85, 8.99, 0]}] ve `tel_yonu_unity` [0,0,1]. EMPTY'ler FBX'te var (yeniden ice aktarimda
  dogrulandi).
- Segmentler: `segment_uzunlugu` (Bariyer_Otoyol 4,0; Cali_Cit 2,0; Beton_Bariyer 2,0) ve
  `segment_yonu_unity` [1,0,0].
- Agaclar/calilar: `tac_yaricapi`, `yukseklik` (agaclarda ayrica `tac_alt_yuksekligi`).
- Hareketli parcalar: `hareketli_parcalar` (Cop_Konteyneri `Kapak`, Oyun_Salincak `Oturak_1/2`): mafsal
  konumu, eksen Unity yerel X. Kapak yerel X etrafinda +110 derece acilir (on kenar kalkar), oturaklar
  +-35 derece salinir.

## Malzemeler

`Donati_malzemeler.json` (27 malzeme, renk sRGB). Sinyal mercekleri `DN_Sinyal_Kirmizi/Sari/Yesil`
sonuk koyu ton ve emission 0; Unity yaktiginda kullanmasi icin her birinde ek `yanik_emission` alani
var (kirmizi [1,0.10,0.05], sari [1,0.60,0.08], yesil [0.12,1,0.55]). `DN_Lamba` sicak beyaz, emission
[1,0.85,0.62] (orta). Yaprak ve govde tonlari oyundaki ofis agaclarinin malzemeleriyle ayni sayilar
(`DN_Yaprak_1` = Olive foliage, `_2` = Sunlit, `_3` = Deep green, `DN_Govde` = Tree bark). Diger
malzemeler: galvaniz, koyu boya, mat siyah, tabela kirmizi/mavi/beyaz, beton, dokum, ahsap, opak cam,
acik metal, konteyner, lastik, tas/koyu tas, musluk, izolator, koyu yesil boya, oyun kirmizisi.
`randomize` alani kullanilmadi. COL_ kutularinin malzemesi yok.

## Verify

`verify_donati.py` sonucu **TEMIZ, 1392 kontrol, 0 hata** (`Donati_verify_sonuc.txt`). Denetlenenler:

- JSON: 25 varlik brif listesiyle ayni, zorunlu alanlar, malzeme oneki/araliklari, sinyal emission 0,
  lamba emission acik.
- .blend: FONT/CURVE nesnesi yok, butun nesnelerde donus 0 / olcek 1, yumusak golgeleme yok, modifier
  yok, malzemeler mesh duzeyinde ve hepsi JSON'da.
- Her FBX yeniden ice aktarilarak: nesne adlari manifestle birebir (ek yok), turler (MESH/EMPTY), donus
  0 / olcek 1, orijin konumlari, ucgen sayisi = manifest ve butce icinde, sinirlar = manifest, brif
  olculeri (direk boylari 6,5 / 3,2 / 2,8 / 8 / 3,5 / 9 m, kol 4,0 ve 1,8 m, durak 4,2 x 1,6 x 2,6,
  cesme 1,6 x 0,8 x 2,0, agac boylari ...), COL_ kutulari 8 koseli ve eksen hizali, zemin ustunde.
- Yon: mercek on yuzleri -Y, her baslikta kirmizi > sari > yesil sirasi ve ayni dikey; kol basligi alt
  kenari 5,400 m, direk basligi 2,600 m; kol -X'te (uc x = -4,04); `_1` kol ucunda, `_2` direkte; tabela
  renkli yuzlerinin hepsi -Y; sokak lambasi isik yuzeyi kol ucunda ve asagi bakiyor.
- EMPTY'ler: `Tel_1..3` konumlari manifestle ayni. Segmentler 0..L araliginda +X, cit ve ray uc
  kesitleri ozdes. Hareketli parca orijinleri mafsalda. Agac COL_Govde govdeyi sariyor.

## Onizlemeler

`Kaynak\Onizlemeler\Donati\`: her varlik icin 3/4 render (`<id>.png`, 25 adet), iki trafik isiginin
mercekleri yanik hali (`Trafik_Isigi_Kollu_yanik.png`, `Trafik_Isigi_Direk_yanik.png`), hepsi bir arada
`Donati_toplu.png` ve sokak kosesi kompozisyonu `Donati_sokak_kosesi.png` (kollu trafik isigi, sokak
lambasi, durak, yaya gecidi tabelasi, babalar, elektrik diregi, kavak/cinar/cam/meyve agaclari, cit,
bank, cop kutusu). Cycles CPU 48 ornek + denoise, 1400 x 950, gri studyo + iki alan isigi.

Onizlemelere bakilip duzeltilenler: yaya gecidi tabelasindaki iki parcali figur "i"/uyari isaretine
benzedigi icin kaldirildi (brifteki mavi kare + beyaz ucgen kaldi); cop kutusu kelepcesi kutunun icine
tasiyordu, kisaltildi; cinar taci "lolipop" gibi duruyordu, genis basik kubbeye cevrildi ve govde
kalinlasti; yuvarlak cali havada duruyordu ve kumeleri daginikti, zemine indirildi; citteki rastgele koyu
yamalar kaldirildi (ust acik, yanlar koyu); yanik mercekler fazla soluk cikiyordu, isik gucu dusuruldu.

## Yorumlar ve zayif noktalar

- **Unity'de denenmedi.** FBX'ler Blender'da yeniden ice aktarilarak dogrulandi; Unity'deki gorunum,
  BoxCollider donusumu ve isik kurulumu ana agentin isi.
- "Direk ustunde 2,6 m'de ikinci baslik" ve Trafik_Isigi_Direk icin 2,6 m **baslik alt kenari** olarak
  yorumlandi (kol basligindaki "alt kenar 5,4 m" ile ayni olcu). Bu yuzden 3,2 m direkte baslik ust
  kenari 3,62 m'ye cikar, yani direk tepesinin 0,42 m ustunde kalir (baslik diregin onune kelepceli).
  Merkez 2,6 m isteniyorsa `signal_head` cagrisindaki 2.60 degeri 2.09 yapilmali.
- Kollu direkte kol 6,18-6,32 m'de; baslik ust kenari (6,42 m) kolun biraz ustune cikar, kol arka
  plakanin arkasindan gecer. Gercek kurulumlarda goruldugu gibi ama "kolun altina asili" bir baslik
  degil.
- Tabela renk katmanlari plakanin 5-15 mm onunde tek yuzler; cok uzak mesafede (yakin kirpma duzlemi
  buyukse) z-fighting olabilir. Gerekirse katman araligi `layer()` icinde buyutulebilir.
- Yaya gecidi tabelasinda piktogram yok (bilincli); Park tabelasinda P yerine beyaz cerceve.
- Calilarda COL_ yok (brif yalniz agac govdesi istedi). Cit duvar gibi kullanilacaksa Unity'de ince bir
  BoxCollider eklenmeli.
- Kaydirak ve salincak COL_ kutulari kaba (egik oluk iki kutuyla, A ayaklar ince dikey kutularla).
- Agaclarda LOD yok; tek mesh, yaprak kumeleri birbirine gecer (gizli yuzler var, ucgen butcesi icinde).
  Ruzgar/sallanma yok.
- Elektrik direginde teller yok (Unity cizecek); izolatorler iki silindirli basit bloklar.
- Cit tek basina kullanildiginda uclari duz kesik gorunur; bir sira cit icin bastaki ve sondaki uc da
  kesik yuz olarak kalir.
- Durak catisinin egimi hafif (2,53 -> 2,60 m), on alin bandi duz (reklam/isim yok).
