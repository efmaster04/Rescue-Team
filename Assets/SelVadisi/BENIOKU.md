# Sel Vadisi — Büyük Harita

Sahne: `Assets/SelVadisi/SelVadisi_BuyukHarita.unity` (1040 × 1520 m)

Unity menüsü: **Tools > Sel Vadisi > Büyük Haritayı Oluştur** haritayı kodla baştan üretir
(eski `Generated` klasörünü silip yeniden kurar). **Tools > Sel Vadisi > Sahneyi Aç** sahneyi açar.

## İçerik
- Kıvrılan vadi: alçak poligon tepeler, kayalık yamaçlar, teraslar, akış yukarısında dar kanyon, aşağıda geniş taşkın ovası
- Akan çamurlu sel suyu: akıntı haritası (evlerin, direklerin, köprü ayaklarının etrafında dönen akım),
  akıntı yönünde akan beyaz çizgiler, köpük, yağmur damlası halkaları, dalga
- Köy: suya gömülmüş ve kuru teraslardaki evler (kiremit kırma/beşik çatı, teras çatı, balkon, panjur,
  güneş enerjili su ısıtıcısı, baca, klima, dış merdiven, dükkân tenteleri, bahçe duvarları)
- Cami (minareli), okul (dalgalanan Türk bayrağı), taş kemerli köprü, otopark platosu ve suya inen parke taşlı rampa
- Kurtarma alanı: dubalar, can simitleri, can yelekleri, çalışan motopomp ve suya giden hortumlar,
  çadır, erzak kasaları, kum torbaları, ışık kulesi, dönen tepe lambası
- Eğik elektrik direkleri ve rüzgârda sallanan sarkık teller, sudaki sokak lambaları
- Şelale ve dağ deresi, seralar, saman balyaları, tarlalar, orman, kayalar, çimen ve çiçekler
- Akıntıyla sürüklenen ~280 eşya (buzdolabı, çamaşır makinesi, varil, kasa, tüp, lastik, kütük, dal,
  saman balyası, palet, kapı, yatak, kanepe...). Evlerin etrafından dolaşır, birbirine çarpar,
  vadiden çıkınca yukarıdan tekrar gelir
- İnsanlar (~217 kişi), hepsi farklı: 16 karakter tipi (anne, baba, nine, dede, genç kız, genç, çocuk, küçük çocuk,
  çiftçi, balıkçı, esnaf, muhtar, kurtarıcı, ekip şefi, sağlıkçı, gönüllü). Her birinin kendi yüzü (göz, kaş, burun,
  ağız, kulak, ten rengi), saçı, sakal/bıyığı, başörtüsü / bere / kasket / kapüşon / kaskı, katmanlı kıyafeti
  (gömlek, kazak, hırka, yelek, mont, şişme mont, yağmurluk, uzun palto, etek, şalvar, iş tulumu, reflektörlü yelek)
  ve renkleri var; yan yana duranlar birbirine benzemesin diye renkler ve kıyafetler seçilerek dağıtılır
- Hikâye anlatan yerleşim: çatılarda ve balkonlarda aileler (çocuğuna sarılan anne, kucağında bebek, sandalyede oturan
  dede, YARDIM yazılı çarşaf), köprüde ve kıyıda bakanlar, çadırların önünde tahliye edilenler, kurtarma alanında ekip
- 28 hareket: el sallama, zıplama, bağırma, telefon, işaret etme, sarılma, teselli, titreme, oturma, diz çökme,
  çömelme, yürüme, yön gösterme, telsiz, dizlere dayanma, eller başta, video çekme, bebek tutma, el ele tutuşma,
  bacağa sarılma, konuşma, korkuluğa yaslanma, dua, bez sallama, taşıma... Kişiler kendi kendine hareket değiştirir;
  38 kemikli iskelet, yere basan ayaklar, nefes, göz kırpma, etrafa bakma, şimşekte irkilme
- Yağmur, sürüklenen bulutlar, ara sıra şimşek, ağaçlar/çimenler/teller rüzgârda sallanır
- Araba ve tekne YOK

## Oynatma
Play'e bas:
- Sağ tık + fare: etrafa bak, WASD / QE: uç, Shift: hızlı, tekerlek: hız
- 1–7: hazır kamera noktaları, C: sinematik tur, H: yardımı gizle

## Kod
- `Scripts/Valley.cs` — arazi, su seviyesi ve akıntının ortak matematiği
- `Scripts/DebrisDrift.cs`, `FlagWave.cs`, `SimpleMotion.cs`, `LightningFlash.cs`, `RainFollow.cs`, `FlyCamera.cs`
- `Scripts/PeopleAnimator.cs` (davranış seçimi), `PeopleBehaviours.cs` (hareketler), `PeopleSolver.cs` (IK ve iskelet), `SVRig.cs` (kemikler)
- `Editor/SVBody.cs` (gövde ve kıyafet), `Editor/SVHead.cs` (yüz, saç, başlık), `Editor/SVPeople.*.cs` (karakter tipleri, yerleşim, eşyalar)
- `Shaders/` — su (`SV_FloodWater`), şelale, yağmur, gökyüzü ve alçak poligon yüzey shader'ı
- `Editor/SVBuild*.cs` — haritayı üreten editör kodu
