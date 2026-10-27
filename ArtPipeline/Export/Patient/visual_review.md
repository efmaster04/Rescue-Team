# Patient görsel inceleme

Konsept: `ArtSource/Patient/references/01_patients_three_variants.jpg` (soldan sağa: Tourist, Civilian, Elder).
Her turda render'lar `renders/round_0N/` altında, son tur ayrıca `renders/` kökünde.

## Tur 1 (`round_01`, codex)
- Üç varyant T-pozda, renkler ve aksesuarlar konseptle uyumlu.
- Sorun: gri arka plan; yatma pozu, ölçek ve compare render'ları yok.

## Tur 2 (`round_02`)
- Tam render listesi üretildi (front/left/back, three_front, three_lie, scale_with_crew, compare_*).
- Yatma pozunda kollar koltuk altında gövdeye 4,1 cm giriyordu (verify BVH başarısız).
  Düzeltme: omuz ağırlık geçişi genişletildi (Shoulder→Arm, x 0,20–0,36), kol kökü x 0,19 → 0,20. Sonuç: en fazla 1,35 cm.
- Tourist: beyaz kafa bandajı şapka kenarının ve gözlerin arkasında kalıyordu (alında görünmüyordu).
- Tourist: çiçekler konsepte göre küçük ve seyrek.
- Elder: siperlik yanlarda da önü kadar geniş, şapka kenarı gibi okunuyordu.
- Civilian askı pozu: askı şeridi omuzda katlanıp kayboluyordu (Neck ile RightForeArm arasında ~180° dönüş, ikili LBS karışımı şeridi çökertiyor).
- OIDN denoiser bu makinede bellek yetersizliğiyle çöktü; denoise kapatıldı, örnek sayısı 16 → 48.

## Tur 3 (`round_03`, son)
- Tourist: şapka kenarı yükseltildi (tepe yine 1,50 m), bandaj genişletildi; bandaj alında, şapka ile gözler arasında görünüyor.
- Tourist: 7 ön çiçek, daha büyük (yaprak kenarı 25 köşe, bütçe için). Toplam 7.904 üçgen.
- Elder: siperlik önde geniş, yanlara doğru inceliyor.
- Civilian: askı şeridi ağırlık zinciri Neck → RightArm → RightForeArm. Şerit askı pozunda önkol sargısından göğüs önünden boyna çıkacak yere konup ters LBS ile bind konumuna çevrildi. Askı pozunda doğal, T-pozda boyundan kola gergin (brief'te kabul), düz yatma pozunda omuzdan sargıya iniyor.
- three_lie: kollar gövde yanında, bacaklar düz, kafa hafif yukarı; Civilian sağ önkol göğüs üstünde.
- scale_with_crew: boylar aynı (1,50 m).

## Konseptten bilinen sapmalar
- Gömlek deseni düz renkli büyük çiçek adaları; konseptteki yeşil yapraklar ve küçük desen yok (brief: pembe/turuncu çiçek).
- Kargo cepleri, kart üstündeki ikon/yazı, hırka kenar örgüsü yok (logo/yazı yasak, sade seçenek).
- Askı konseptteki gibi üçgen bez değil: önkol sargısı + iki şerit.
- Gözler konseptten biraz daha büyük ve yüz daha sade (RescueCrew stil dili).

## Tur 4 (`round_04`, r2 düzeltme turu)
- Civilian askı pozu: el çene/boyun hizasındaydı ("boğazını tutuyor" gibi). Üst kol gövde yanında hafif öne
  (RightArm yönü (-0,12; -0,60; -0,79)), önkol göğüs ortasında yatay (RightForeArm (0,84; -0,52; -0,12)),
  el karşı kaburga hizasında (RightHand (1; -0,02; -0,05)). Önkol ucu ~z 0,72, boyundan ~22 cm aşağıda.
- Askı sargısı bileğe kadar uzatıldı (x 0,400–0,552), kazak manşetini örtüyor. Askı bantları gerçek askı gibi:
  sol boyun → bilek ucu, sağ boyun → dirsek ucu; alt uçlar askı pozunda sargının üst yüzeyine oturuyor.
- Tourist: bandaj merkezi öne alındı ve genişletildi (z 1,333–1,411), şapka kenarı 2,2 cm yükseltildi;
  bant önden ve yatarken üstten şapka altında belirgin beyaz şerit. Konseptteki gibi kaşlar bandajın altında
  (Tourist'te kaş tüpleri çıkarıldı). Tourist boyu 1,515 m (tolerans ±0,03 içinde), 7.792 üçgen.
- BVH: askı pozunda en derin kol–gövde girişi 1,35 cm (sınır 2 cm).
- Kalan: T-pozda askı bantları göğüs önünde kırıklı/gergin görünüyor (brief'te T-poz gerginliği kabul).
