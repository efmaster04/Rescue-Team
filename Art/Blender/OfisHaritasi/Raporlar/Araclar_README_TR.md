# Araclar seti (ofis haritasi trafik araclari)

Ofisin bulundugu kasabada dolasan, WheelCollider ile surulecek fizikli trafik araclari. 9 FBX: 8 arac tipi + sedanin taksi turevi. Stil garajdaki servis minibusuyle ayni: duz golgeleme, tek segment pah, okunakli bloklar, donuk renkler. Yazi, plaka, logo, doku yok.

## Dosyalar

| Ne | Yol |
|---|---|
| Uretim betigi | `Kaynak\Uretim\build_araclar.py` |
| Onizleme betigi | `Kaynak\Uretim\render_araclar.py` |
| Kontrol betigi | `Kaynak\Uretim\verify_araclar.py` |
| Blender dosyasi | `Kaynak\Blender\Araclar.blend` (her arac kendi `Arac_<Ad>` koleksiyonunda, hepsi orijinde ust uste) |
| FBX (9) | `Oyun\Assets\OfisHaritasi\Modeller\Araclar\Arac_{Hatchback,Sedan,Taksi,SUV,Pickup,HafifTicari,Minibus,Kamyon,Traktor}.fbx` |
| Manifest | `Oyun\Assets\OfisHaritasi\Modeller\Araclar\Araclar_manifest.json` |
| Malzemeler | `Oyun\Assets\OfisHaritasi\Modeller\Araclar\Araclar_malzemeler.json` |
| Onizlemeler | `Kaynak\Onizlemeler\Araclar\<Arac>_on_yan.png`, `<Arac>_arka_yan.png`, `Araclar_toplu.png`, `Araclar_siluet.png` |

Uretim hatti (Blender 5.2):

```
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b -P Kaynak\Uretim\build_araclar.py            (tam: .blend + FBX + JSON)
"...blender.exe" -b -P Kaynak\Uretim\build_araclar.py -- --no-export --only=Sedan,SUV                      (hizli deneme)
"...blender.exe" -b Kaynak\Blender\Araclar.blend -P Kaynak\Uretim\verify_araclar.py
"...blender.exe" -b Kaynak\Blender\Araclar.blend -P Kaynak\Uretim\render_araclar.py   (AR_QUICK=1, AR_ONLY=..., AR_SHOTS=on,arka,toplu,yan)
```

Build betigi fabrika ayarli bos sahneden her seyi sifirdan uretir (yaklasik 15 sn). Elle duzenleme yok.

## Araclar

| Arac | Tip | Hedef U x G x Y | Olculen U x G x Y | Aks | Teker r (on/arka) | Ucgen | Kutle | Azami hiz | Ceker |
|---|---|---|---|---|---|---|---|---|---|
| Arac_Hatchback | binek | 3.95 x 1.74 x 1.48 | 3.966 x 1.745 x 1.482 | 2.50 | 0.30 / 0.30 | 4076 | 1100 kg | 70 km/h | on |
| Arac_Sedan | binek | 4.50 x 1.78 x 1.45 | 4.516 x 1.785 x 1.457 | 2.64 | 0.31 / 0.31 | 4432 | 1300 kg | 70 km/h | on |
| Arac_Taksi | binek | 4.50 x 1.78 x 1.61 | 4.516 x 1.785 x 1.603 | 2.64 | 0.31 / 0.31 | 4520 | 1350 kg | 70 km/h | on |
| Arac_SUV | binek | 4.40 x 1.82 x 1.66 | 4.426 x 1.848 x 1.659 | 2.62 | 0.34 / 0.34 | 4984 | 1450 kg | 70 km/h | on |
| Arac_Pickup | ticari | 5.30 x 1.86 x 1.80 | 5.300 x 1.868 x 1.800 | 3.10 | 0.37 / 0.37 | 4520 | 2000 kg | 70 km/h | arka |
| Arac_HafifTicari | ticari | 4.40 x 1.82 x 1.86 | 4.415 x 1.839 x 1.860 | 2.75 | 0.31 / 0.31 | 3978 | 1500 kg | 70 km/h | on |
| Arac_Minibus | ticari | 6.00 x 2.02 x 2.60 | 6.030 x 2.025 x 2.600 | 3.66 | 0.36 / 0.36 | 4672 | 3200 kg | 70 km/h | arka |
| Arac_Kamyon | agir | 7.20 x 2.30 x 3.30 | 7.208 x 2.300 x 3.300 | 4.00 | 0.48 / 0.48 | 5756 | 7500 kg | 60 km/h | arka |
| Arac_Traktor | traktor | 3.90 x 2.00 x 2.70 | 3.910 x 2.010 x 2.700 | 2.25 | 0.40 / 0.75 | 5736 | 3200 kg | 25 km/h | arka |

Olculer aynalar haric (arac katalog olcusu gibi). Aynalar dahil en dis kutu manifestte `cerceve` alaninda. Ucgen sayisi Govde + 4 teker (COL kutulari haric). Butce: binek/ticari 2,5-5k, minibus/kamyon en fazla 7k, traktor en fazla 6k.

Taksinin yuksekligi briften turetildi: sedan govdesi (1,45 m) + tavan lambasi (yaklasik 0,15 m) = 1,61 m hedef.

Aracin kisa tarifi:

- **Hatchback**: kisa arka cikinti, dik bagaj kapagi camli, 5 kapi, egik on farlar, tampon kosesinde sinyal.
- **Sedan**: uc hacim (kaput, kabin, bagaj), bagaj kapagi cizgileri, izgaranin ustunde ince krom cubuk.
- **Taksi**: sedanin ayni govdesi, `AR_Taksi_Sari` sabit boya, tavanda koyu taban uzerinde bos (yazisiz) `AR_Taksi_Lamba` kutusu. Ayri bir build fonksiyonu yok; `build_sedan(taxi=True)`.
- **SUV**: yuksek kaput ve bel cizgisi, siyah kemer ve marspiyel kaplamasi, gumus alt koruma, iki tavan rayi, dik arka.
- **Pickup**: cift kabin, acik kasa (ic yuzeyler `AR_Kasa_Ici`), koyu gri tampon, kabin arka cami, genis kemer kaplamalari, yan basamak, gorunur arka aks.
- **HafifTicari**: yuksek tavanli kucuk panelvan, iki yanda surgulu kapi cizgisi + surgu rayi cizgisi, arka cift kapi (camli), yan koruma citasi.
- **Minibus**: dolmus tipi, uzun yuksek tavan, yolcu cam sirasi (govde renkli direkler), sagda surgulu kapi ve basamak, arka cift kapi.
- **Kamyon**: kabin onde (COE), gunesluk, kapali kutu kasa (`AR_Kasa`, randomize `kamyon_kasa`), arka cerceve + kilit cubuklari, sasi, yakit deposu, yan ve arka koruma, arkada cift lastik gorunumlu tek teker nesnesi.
- **Traktor**: dar kaput, yan menfezler, on agirlik, kabin (cam ev + direkler), arka cift tirnak sirali buyuk lastikler, camurluklar, dikey egzoz, basamak, 3 nokta aski. Boya `AR_Traktor_Boya` (randomize `traktor_boya`).

## Nesne yapisi (her FBX'te ayni adlar)

| Nesne | Icerik |
|---|---|
| `Govde` | Butun govde tek mesh: boya, cam, trim, farlar, stoplar, sinyaller, ayna, sasi. Dongu 0, olcek 1, orijin taban izinin ortasi (lastik alti y=0). |
| `Teker_OnSol`, `Teker_OnSag`, `Teker_ArkaSol`, `Teker_ArkaSag` | Lastik + jant tek mesh. Orijin teker merkezinde, mesh kendi X ekseni (aks) etrafinda donel simetrik, donus 0. Sol = -X, on = +Z (Unity). Statik halde lastik alti y=0. |
| `COL_Govde_Alt`, `COL_Govde_Ust`, `COL_Kabin` | Eksenlere hizali 8 koseli kutular (malzemesiz). Kamyonda `COL_Govde_Alt`, `COL_Kabin`, `COL_Kasa`; traktorde `COL_Govde_Alt`, `COL_Kabin`. |

Eksen: FBX ortak kuraldaki cagriyla yazildi (`axis_forward='Z', axis_up='Y'`, bake_space_transform). Blender X = Unity X, Blender Y = Unity Z (ileri), Blender Z = Unity Y (yukari). Manifestteki butun koordinatlar Unity yerel ekseninde `[x, y, z]`.

### WheelCollider icin

- Teker konumu: manifest `tekerler[].konum` = teker nesnesinin orijini = teker merkezi (y = yaricap). WheelCollider bu noktaya konup `radius = yaricap` verilirse statik halde lastik zemine tam oturur.
- `genislik`, `direksiyon` (yalniz on tekerler), `ceker` (her aracin `ceker` alanina gore) manifestte.
- Gorsel teker meshi Unity'de yalniz kendi X ekseninde dondurulur (yuvarlanma) ve on tekerlerde dikey eksende +-35 derece cevrilir. Teker yuvalari bu acida temiz (verify 5 derece adimla sinadi).
- Kamyonun arka tekerleri cift lastik gorunumlu tek nesne (genislik 0,60 m). Tek WheelCollider yeterli.
- Traktorun on ve arka yaricaplari farkli (0,40 / 0,75). Agirlik merkezi alcak tutuldu; Unity'de `Rigidbody.centerOfMass` olarak kullanilabilir.

### Teker konumlari ve carpisma kutulari (Unity yerel)

| Arac | On sag teker | Arka sag teker | Genislik on/arka | Agirlik merkezi | COL kutulari (min -> max) |
|---|---|---|---|---|---|
| Arac_Hatchback | [0.75, 0.3, 1.175] | [0.74, 0.3, -1.325] | 0.185 / 0.185 | [0, 0.42, 0.15] | COL_Govde_Alt: [-0.6175, 0.26, -1.955] -> [0.6175, 0.98, 1.955]<br>COL_Govde_Ust: [-0.86, 0.63, -1.915] -> [0.86, 0.98, 1.915]<br>COL_Kabin: [-0.62, 0.98, -1.745] -> [0.62, 1.46, 0.775] |
| Arac_Sedan | [0.765, 0.31, 1.35] | [0.76, 0.31, -1.29] | 0.195 / 0.195 | [0, 0.43, 0.12] | COL_Govde_Alt: [-0.6325, 0.26, -2.23] -> [0.6325, 0.985, 2.23]<br>COL_Govde_Ust: [-0.88, 0.65, -2.19] -> [0.88, 0.985, 2.19]<br>COL_Kabin: [-0.64, 0.985, -1.65] -> [0.64, 1.455, 0.7] |
| Arac_Taksi | [0.765, 0.31, 1.35] | [0.76, 0.31, -1.29] | 0.195 / 0.195 | [0, 0.44, 0.12] | COL_Govde_Alt: [-0.6325, 0.26, -2.23] -> [0.6325, 0.985, 2.23]<br>COL_Govde_Ust: [-0.88, 0.65, -2.19] -> [0.88, 0.985, 2.19]<br>COL_Kabin: [-0.64, 0.985, -1.65] -> [0.64, 1.6028, 0.7] |
| Arac_SUV | [0.785, 0.34, 1.32] | [0.785, 0.34, -1.3] | 0.215 / 0.215 | [0, 0.52, 0.12] | COL_Govde_Alt: [-0.6475, 0.36, -2.18] -> [0.6475, 1.12, 2.18]<br>COL_Govde_Ust: [-0.9, 0.71, -2.14] -> [0.9, 1.12, 2.14]<br>COL_Kabin: [-0.66, 1.12, -2.05] -> [0.66, 1.62, 0.78] |
| Arac_Pickup | [0.79, 0.37, 1.75] | [0.79, 0.37, -1.35] | 0.245 / 0.245 | [0, 0.6, 0.25] | COL_Govde_Alt: [-0.6375, 0.42, -2.63] -> [0.6375, 1.25, 2.63]<br>COL_Govde_Ust: [-0.908, 0.77, -2.6] -> [0.908, 1.29, 2.6]<br>COL_Kabin: [-0.72, 1.24, -0.67] -> [0.72, 1.8, 0.95] |
| Arac_HafifTicari | [0.77, 0.31, 1.38] | [0.78, 0.31, -1.37] | 0.195 / 0.195 | [0, 0.58, 0.1] | COL_Govde_Alt: [-0.6425, 0.28, -2.18] -> [0.6425, 1.1, 2.18]<br>COL_Govde_Ust: [-0.9, 0.65, -2.14] -> [0.9, 1.1, 2.14]<br>COL_Kabin: [-0.84, 1.1, -2.16] -> [0.84, 1.86, 1.25] |
| Arac_Minibus | [0.87, 0.36, 2.05] | [0.86, 0.36, -1.61] | 0.215 / 0.215 | [0, 0.78, 0.15] | COL_Govde_Alt: [-0.7225, 0.34, -2.98] -> [0.7225, 1.33, 2.98]<br>COL_Govde_Ust: [-1.0, 0.75, -2.94] -> [1.0, 1.33, 2.94]<br>COL_Kabin: [-0.95, 1.33, -2.96] -> [0.95, 2.6, 2.0] |
| Arac_Kamyon | [1.0, 0.48, 2.3] | [0.84, 0.48, -1.7] | 0.295 / 0.6 | [0, 0.95, -0.25] | COL_Govde_Alt: [-0.5, 0.4, -3.55] -> [0.5, 1.12, 3.58]<br>COL_Kabin: [-1.11, 0.99, 1.68] -> [1.11, 2.8, 3.58]<br>COL_Kasa: [-1.15, 1.1, -3.6] -> [1.15, 3.3, 1.6] |
| Arac_Traktor | [0.75, 0.4, 1.15] | [0.79, 0.75, -1.1] | 0.235 / 0.42 | [0, 0.78, -0.35] | COL_Govde_Alt: [-0.34, 0.4, -1.92] -> [0.34, 1.46, 1.95]<br>COL_Kabin: [-0.7, 1.62, -1.43] -> [0.7, 2.7, 0.15] |

Sol tekerler ayni konumun x'i eksi isaretli hali. Kutu duzeni:

- `COL_Govde_Alt`: tekerlerin ic yuzleri arasinda (3 cm pay), tam boy, tabandan bel cizgisine. Burun ve kuyruk ortasini kapsar.
- `COL_Govde_Ust`: tam genislik, teker tepesinin 3 cm ustunden bel cizgisine (camurluk ve kapi bandi).
- `COL_Kabin`: cam ev / kabin / kutu kasa.
- Butun kutularin tabani yerden en az 0,25 m yukarida. Durgun halde hicbir kutu bir teker hacmine girmiyor (verify sinadi).

## Malzemeler (`Araclar_malzemeler.json`)

Renkler sRGB 0-1. Duz renk, prosedurel dugum yok.

| Ad | Renk | Metallic | Smoothness | Emission | Randomize |
|---|---|---|---|---|---|
| AR_Boya | gumus (varsayilan) | 0.25 | 0.55 | - | `arac_boya` |
| AR_Cam | opak koyu fume | 0.10 | 0.82 | - | - |
| AR_Trim | siyah plastik | 0 | 0.32 | - | - |
| AR_Krom | acik gri metal (az kullanildi: sedan/taksi izgara cubugu, pickup izgara cubugu) | 0.85 | 0.72 | - | - |
| AR_Far | sicak beyaz | 0 | 0.80 | hafif [0.30, 0.27, 0.20] | - |
| AR_Stop | koyu kirmizi | 0 | 0.70 | 0 (Unity frende yakar) | - |
| AR_Sinyal | amber | 0 | 0.70 | - | - |
| AR_Lastik | koyu kaucuk | 0 | 0.12 | - | - |
| AR_Jant | gumus-gri celik | 0.55 | 0.50 | - | - |
| AR_Sasi | koyu gri metal | 0.30 | 0.28 | - | - |
| AR_Taksi_Sari | donuk taksi sarisi | 0.20 | 0.55 | - | yok (sabit) |
| AR_Taksi_Lamba | krem | 0 | 0.60 | cok hafif | - |
| AR_Kasa | beyaz | 0.10 | 0.40 | - | `kamyon_kasa` |
| AR_Kasa_Ici | koyu gri kasa kaplamasi | 0 | 0.18 | - | - |
| AR_Traktor_Boya | donuk kirmizi | 0.15 | 0.45 | - | `traktor_boya` |

`palettes`: `arac_boya` 12 renk (beyaz, kirik beyaz, gumus, fume, siyah, lacivert, koyu kirmizi, bej, koyu yesil, acik mavi-gri, bordo, kum), `kamyon_kasa` 4 renk (beyaz, acik gri, mavi-gri, krem), `traktor_boya` 3 renk (donuk kirmizi, donuk mavi, donuk yesil).

Onizlemelerde araclar farkli palet renkleriyle gosterildi (yalniz render betiginde gecici kopya). FBX'te herkes ayni `AR_Boya` malzemesini kullanir.

## Kontrol sonucu (`verify_araclar.py`)

Son calistirma: **VERIFY TEMIZ: 9 arac, butun kontroller gecti.**

Sinananlar:

1. Olculer (aynalar haric Govde + tekerler) hedefin %3'u icinde; orijin taban izinin ortasinda; en alt nokta Z=0; aynalar sag/sol simetrik (5 mm).
2. Tekerler: yaricap (en dis tepe noktasi) hedefin %0,5'i icinde, lastik alti Z=0 (1,5 mm), merkez yuksekligi = r, sol/sag tarafi, kesit merkezi eksende, 8 dilimde donel simetri, aks mesafesi (1 cm), manifest konumu/yaricap/direksiyon bayragi.
3. Direksiyon: on tekerler -35..+35 derece, 5 derece adimla dondurulup BVH ile Govde'ye karsi sinandi; arka tekerler durgun halde. Hicbir acida kesisme yok.
4. COL kutulari: 8 kose / 6 yuz, eksenlere hizali, donus 0, olcek 1, taban >= 0,25 m, durgun haldeki teker hacimlerini kapsamiyor.
5. Govde ve tekerlerde butun parcalar kapali kabuk (acik kenar 0) ve normaller disa donuk (parca basina isaretli hacim pozitif).
6. Ucgen butceleri; manifestteki ucgen sayisi ile olculen esit.
7. FONT nesnesi yok; butun nesneler MESH; bos ya da kullanilmayan malzeme yuvasi yok; butun malzemeler JSON'da; paletler 12/4/3; `AR_Stop` emission 0; taksi sarisi randomize degil.
8. FBX yeniden ice aktarma: her FBX bos sahneye alindi; nesne adlari ayni, her nesnenin dunya sinir kutusu .blend ile 3 mm icinde, teker orijinleri teker merkezinde, Govde malzeme listesi ayni.

## Bilinen sinirlar

- Cam opak (ic mekan yok). Govde kapali kabuk oldugu icin camin arkasinda bosluk gorunmez.
- Govde kabugu (loft) boolean'dan once simetrik kosegenle ucgenlenir; aksi halde EXACT boolean duzlemsel olmayan dortgenleri sag ve solda farkli bolup govdeyi 3-4 cm asimetrik yapiyordu (ayna ve cam panelleri kayiyordu). Simdi sag/sol yuzey farki en fazla 2 mm. Kasitli tek tarafli parcalar: minibus surgulu kapi cizgileri, surgu rayi ve basamak (sag), kamyon yakit deposu (sol) ve aku kutusu (sag), traktor egzozu (sag) ve basamagi (sol).
- Binek ve ticari araclarda on tekerlek 35 dereceye cevrildiginde gorsel teker meshinin ic kosesi `COL_Govde_Alt` kutusuna girer (kutu durgun haldeki ic yuzlere gore boyutlandi; kamyon ve traktorde girmez). WheelCollider isinla calistigi ve teker meshinin collider'i olmadigi icin fizik etkisi yok.
- Suspansiyon yolu icin ayrica pay hesaplanmadi; teker yuvalarinda teker tepesinin ustunde 5-8 cm bosluk var. Unity'de suspansiyon sikismasi bunu gecerse teker ustten camurluga girebilir (yalniz gorsel). Onerilen suspansiyon mesafesi 0,08-0,12 m.
- Kapi, kaput, bagaj ayri nesne degil; acilmaz. Yalniz cizgi olarak var.
- Traktor arka lastik tirnaklari ayri kutular; en dis tepe yaricapi 0,7533 m (0,75 hedefinin %0,44 ustu). Temas yaricapi tam 0,75.
- Hatchback, Sedan, Taksi ve SUV ayni kesit sistemiyle uretildi; on ve arka yuzleri farkli tasarlandi ama ayni "aile" dili var.
- Onizleme render'i ortamda yaklasik 3 dakikadan uzun suren tek Blender surecinde zaman zaman kesildi; bu yuzden onizlemeler `AR_ONLY` ile 2-3 araclik parcalar halinde alindi. Betikte sorun yok.

## Onizlemelere bakarak gordugum zayif noktalar

- **Genel form yasi:** Binekler 1990-2000'ler kasaba arabasi gibi duruyor: duz kaput ve bagaj yuzeyleri genis, bel cizgisi duz. Modern araclarin keskin far/izgara dilini sade blok dilde vermek zor; Turk kasabasi icin inandirici ama "yeni model" degil.
- **Tekerler:** Jantlar (r'nin %58-64'u) lastik yanagina gore kucuk, yanak kalin. Gercek olculere yakin ama uzaktan teker biraz "kucuk" okunuyor.
- **Ucgenlenmis yuzeyler:** Duz golgelemede duzlemsel olmayan dortgenler kosegenden kirik gorunuyor (ozellikle hatchback arka camurlugu, A diregi dibi). Servis minibusunun yuzeyli diline uyuyor ama yer yer gelisiguzel kirik gibi.
- **Sedan/taksi:** Arka cikinti ve C diregi genis, bagaj kapagi uzun ve bos. Taksi damali seridi yok (yazi/desen yasagi geregi).
- **SUV:** Kompakt crossover'dan cok 2000'lerin yuksek station/SUV'si gibi. Koyu boyalarda siyah kaplamalar seciliyor ama acik renklerde daha iyi okunur.
- **Hafif ticari ve minibus onu:** Farlar govdeye gore kucuk, kaput ile tampon arasinda genis bos beyaz alan kaliyor. Minibusun on kapi cami A direginden geride; kabin biraz "kapali" gorunuyor.
- **Lambalar:** Farlar ve stoplar govdeye yapistirilmis ince paneller. Yakin bakista cerceve ile lens arasinda cok ince bir golge kirigi gorunebilir; oyun mesafesinde okunuyor.
- **Kamyon:** Kutu kasa duz ve bos (beyaz). Kabin kasaya gore biraz alcak; kabinin tavaninda ruzgarlik yok.
- **Traktor:** En inandirici olanlardan biri. Kabin cami tek blok oldugu icin ic tarafi (koltuk, direksiyon) yok; arka 3 nokta askisi sade cubuklar.
