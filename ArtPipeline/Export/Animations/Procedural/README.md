# Prosedürel animasyon klipleri (r4b, 44 klip)

Mixamo erişimi olmadığı için anahtar kareli olarak betikle üretilen klipler. Hepsi RescueCrew / Patient ortak
31 kemikli `mixamorig:` iskeleti için, 30 fps, her karede anahtar (bake). Ton: Dear Passengers gibi abartılı ve komik.

- Üretim: `tools/blender/build_procedural_anims.py` (Blender 5.2, deterministik; `-- --no-render` ile şerit render'sız)
- Doğrulama: `tools/blender/verify_procedural_anims.py` (dosya yazmaz)
- Klip başına bilgi: `clips_meta.json` (süre, kare, döngü, yerinde, kök hareketi, karakter, eğri SHA256)
- Önizleme: `renders/<Ad>_strip.png` (RescueCrew_Blue; `Lying_*` için Patient_Tourist, yalnız render'da yatırılmış)

| Klip | Süre | Döngü | Not |
|---|---|---|---|
| Carry_Stretcher_Walk | 0.5 sn | evet | Eller önde sedye saplarında (x ±0.30 m), dizler kırık, yük altında sendeleyen paytak yürüyüş; **1.4 m/sn** için (adım 0.35 m) |
| Carry_OneHand_Walk | 0.4 sn | evet | Sağ el yanda koni tutar, sol kol sallanır; uçuş fazlı telaşlı seğirtme; **2.4 m/sn** için (adım 0.48 m) |
| PickUp | 1.0 sn | hayır | Önde taşıma pozunda biter (Carry'ye geçiş) |
| PutDown | 0.9 sn | hayır | Önde taşıma pozundan başlar, rahat duruşta biter |
| GetUp_FromBack | 1.6 sn | hayır | Kalça yerden (z≈0.14, yatay) ayağa kalkar; **kök hareketi var** |
| Revive_Helper | 1.2 sn | evet | Diz çökmüş, önündekinin omuzlarını sallar |
| Jump | 0.8 sn | hayır | Kalça dikey hareketi klipte, yatayda yerinde |
| Startled | 0.9 sn | hayır | Kollar havaya, sol bacak kalkar, geriye kaykılır |
| Lying_HelpWave | 1.4 sn | evet | Hasta; dik tanımlı yatma pozu, sol kol "yardım!" sallar |
| Lying_Groan | 2.0 sn | evet | Hasta; dik tanımlı yatma pozu, hafif kıvranma |
| Celebrate | 1.5 sn | hayır | Zıplama, yumruklar havada, kalça sallama |

## Unity import önerisi
- **Rig:** Animation Type **Humanoid**, Avatar Definition **Copy From Other Avatar** → `SK_RescueCrew_Blue` avatarı
  (aynı iskelet; `Lying_*` için de aynı avatar kullanılabilir). FBX'ler yalnız iskelet + 1 mm'lik bağ çapası üçgeni içerir.
- **Model:** Scale Factor 1, Convert Units açık, Bake Axis Conversion kapalı (karakter FBX'leriyle aynı).
- **Loop Time + Loop Pose açık:** Carry_Stretcher_Walk, Carry_OneHand_Walk, Revive_Helper, Lying_HelpWave, Lying_Groan.
  Bu ilk gruptaki diğerleri tek sefer (Loop Time kapalı); sonraki grupların döngüleri aşağıda ve meta'da belirtilir.
- **Bake Into Pose** (bütün kliplerde Root Transform Rotation, Position (Y), Position (XZ) üçü de **açık**, Based Upon **Original**):
  - Yerinde klipler kalçayı yatayda hiç kaydırmaz; dikey zıplama/çömelme pozun içinde kalır (Feet yerine Original seçilmeli, yoksa Jump yüksekliği düzleşir).
  - GetUp_FromBack: kalça yataydan dikeye döner ve yükselir. Rotation'ı bake etmezseniz Unity bunu kök dönüşü sayıp
    bütün objeyi devirir. Üçü de açık + Original ile klip yazıldığı gibi oynar; karakter ayakta, başlangıç noktasında biter.
    Kalkışın oyunda objeyi gerçekten taşıması istenirse yalnız Position (XZ) kapatılabilir (kalça ~12 cm oynar).
- `Lying_*` klipleri Patient_LiePose gibi dik tanımlıdır: oyunda hasta objesi sedyeye yatırılır (sırt aşağı, -90° X).

## Doğrulama kriterleri (verify)
Temel kontroller: tek aksiyon, kemik adları ve bağ pozu RescueCrew ile aynı; kare sayısı ±2; döngülerde ilk/son kare farkı
< 0.5°; yerinde kliplerde kalça yatay kayması < 2 cm; mesh'e uygulandığında kol→gövde ve bacak→bacak içe girmesi ≤ 3 cm
(her kare). İçe girme, kemik ekseninden dışa doğru yöne göre en yakın yüzeyin altında kalan köşe derinliğidir;
omuz eklemine 9 cm, kalça eklemine 7 cm yakın köşeler ve gövde ağırlığıyla karışık köşeler deri katlanma bölgesi
sayıldığından ölçüme katılmaz. Kol–kafa teması ölçülmez.

## r2 değişiklikleri
- FBX'ler artık **30 kare/sn** işaretli (GlobalSettings TimeMode 30). r1'de karakter FBX'inin içe aktarımı sahneyi 24'e
  çektiği için klipler 24 işaretlenmişti; Unity'de durum hızı yaması (×1.25) gerekmez.
- Taşıma döngüleri **oynatma hızı 1.0** iken hedef hızda ayak kaydırmaz (`clips_meta.json`: `target_speed_mps`,
  `step_length_m`, `stance_travel_m`). Ayak yerdeyken yerel olarak tam hedef hızla geriye kayar.
- verify ek kontrolleri: FBX kare hızı 30; taşıma kliplerinde klip hedef hızla ileri taşınmış sayılıp her yere basışta
  ayak bileğinin yatay kayması < 3 cm (bilek en alçak yüksekliğe 1.5 cm yakınken yerde sayılır).

## r3: minibüsten çıkarma, halat, sırtta taşıma (13 klip)

| Klip | Süre | Döngü | Karakter | Not |
|---|---|---|---|---|
| Patient_SitDazed | 2.0 sn | evet | Hasta | Koltukta sersem: kafa daire çizer, eller kucakta, kısa bacaklar sarkar. Koltuk yüzeyi kök z=0.262 |
| Patient_SitDazed_ArmSling | 2.0 sn | evet | Hasta (Civilian) | Aynısı, sağ kol askıda |
| Patient_DraggedOut | 1.5 sn | hayır | Hasta | Oturuştan geriye (+Y) çekilip sırt üstü düşer, bacaklar havada; **kök hareketi** (kalça +0.86 m Y, sonda z≈0.14 fiziksel yatış) |
| Piggyback_Rider | 1.0 sn | evet | Hasta | Sırttaki hasta; kök taşıyanın Spine1 soketine bağlanır |
| Extract_Pull | 1.5 sn | hayır | Kurtarıcı | Eğilip içeriden tutar, iki küçük + bir büyük çekiş, eller boşalır, geri sendeler |
| Rope_Descend | 1.0 sn | evet | Kurtarıcı | Halatta iniş, ayak başına döngüde 2 tıpış adım; kök hızı **-0.6 m/sn** |
| Rope_Climb | 1.0 sn | evet | Kurtarıcı | Elden ele tırmanış; kök hızı **0.3 m/sn** |
| Rope_Hang_Idle | 1.5 sn | evet | Kurtarıcı | Halatta asılı bekleme, sallanma, ürkek bakış |
| Piggyback_PickUp | 1.5 sn | hayır | Kurtarıcı | "Hadi bin!" çömelme; binen **kare 17**'den itibaren sırtta |
| Piggyback_Rope_Climb | 1.0 sn | evet | Kurtarıcı | Sırtta yükle tırmanış, öne eğik; kök hızı **0.25 m/sn** |
| Piggyback_Walk | 0.8 sn | evet | Kurtarıcı | Sırtta yükle ağır yürüyüş, **1.2 m/sn** (adım 0.48 m) |
| Piggyback_PutDown | 1.2 sn | hayır | Kurtarıcı | Binen **kare 16**'ya kadar sırtta; sonra bel ağrısıyla doğrulma |
| Rope_Pull | 1.0 sn | evet | Kurtarıcı | Kazık yanında ayaklar açık, halatı göğüs hizasında elden ele çeker (~0.25 m/sn halat) |

Önizleme şeritlerindeki koltuk, kaya duvarı, halat ve kazık yalnız render içindir; FBX'lerde yoktur.

### Halat (Unity LineRenderer)
- `clips_meta.json` → `rope_grip`: tutan kemikler `mixamorig:LeftHand` / `mixamorig:RightHand`, tutma noktası el kemiğinin
  ~5 cm ilerisi (avuç, el yerel `(0, 0.05, 0)`).
- Halat klipleri (Rope_Descend/Climb/Hang_Idle, Piggyback_Rope_Climb): halat kök yerelinde **x=0, y=-0.29** dikey çizgisi;
  kaya duvarı düzlemi **y=-0.36** (ayak tabanları). Kökü duvara göre bu ofsetle yerleştirin, dikey hızı `vertical_speed_mps`
  ile sürün (oynatma 1.0'da duvardaki ayak kaymaz). Halat: kazık → üstteki el → alttaki el → aşağı sarkan uç.
- Rope_Pull: halat önden (-Y, uçurum kenarı) gelir, kök yerelinde x=0, z≈0.80 çizgisinde y=-0.40 → -0.26 arası elden ele
  çekilir; halat: kenar → öndeki el → arkadaki el → kazık (`SM_RopeAnchor` → `RopeAttach`).

### Sırtta taşıma (Piggyback_*)
- Binen hasta `Piggyback_Rider` oynatır ve taşıyanın `mixamorig:Spine1` kemiğine **rijit** bağlanır: taşıyan bağ pozundayken
  soket taşıyan kökünün 0.312 m arkasında (Blender +Y = Unity -Z), 0.292 m yukarısında, dönüşü kökle aynı.
  Unity: taşıyan T-pozdayken Spine1 altına boş `PiggybackSocket` koyup hastayı ona sıfır yerel dönüşümle bağlayın.
- Taşıyanın elleri binenin diz dışını/altını kavrar (binen döngüsünün t=0 pozuna göre). Binen döngüsü (1.0 sn) taşıyan
  kliplerinden bağımsız döner.
- PickUp/PutDown'da binenin bağlı olduğu kareler `rider_attached_frames`; bağlı değilken hasta yerde yatar (DraggedOut sonu).

### Unity import (r3 ekleri)
- Rig ve Bake Into Pose ayarları r2 ile aynı (Humanoid, Copy From Other Avatar → SK_RescueCrew_Blue; Original).
- **Loop Time + Loop Pose açık:** Patient_SitDazed(_ArmSling), Piggyback_Rider, Rope_Descend, Rope_Climb, Rope_Hang_Idle,
  Piggyback_Rope_Climb, Piggyback_Walk, Rope_Pull. Diğerleri tek sefer.
- Patient_DraggedOut: kalça hem öteler hem yatar. Root Transform Rotation **Bake Into Pose açık** (yoksa obje devrilir);
  hareketin objeyi gerçekten taşıması istenirse Position (XZ) kapatılır (+0.86 m geriye). Minibüs zemini ile yol arasındaki
  yükseklik farkını oyun kodu kökü indirerek karşılar.
- Extract_Pull t≈0.45 sn'de Patient_DraggedOut başlatılırsa büyük çekiş ile hastanın devrilmesi eşleşir.

### Doğrulama (r3 ekleri)
24 dosya. Yeni kliplerde de kare sayısı, döngü eşitliği, 30 fps, kemik adları/bağ pozu, kol–gövde ≤ 3 cm;
Piggyback_Walk'ta 1.2 m/sn'de ayak kayması < 3 cm/adım; Piggyback_* kurtarıcı klipleri Piggyback_Rider ile birlikte
(soket üzerinden) oynatılıp iki mesh'in iç içe geçmesi ≤ 4 cm (yalnız binenin bağlı olduğu karelerde). İç içe geçme: bir
mesh'in köşesi öbürünün en yakın yüzeyinin hem yüz normaline hem iskelet ekseninden dışa yönüne göre içeride kalıyor ve
normal yönünde atılan ışın o yüzeyden çıkıyorsa, yüzeye uzaklığı derinlik sayılır (Tourist'in fotoğraf makinesi dahil).

## r4: sedyenin ön taşıyıcısı (1 klip)

| Klip | Süre | Döngü | Not |
|---|---|---|---|
| Carry_Stretcher_Front_Walk | 0.5 sn | evet | Sedye **arkada**: kollar arkada, iki el kalçanın arkasında saplarda (x ±0.293 m = sap aralığı 0.586 m), gövde öne eğik, yük çeker gibi. Carry_Stretcher_Walk ile aynı bacak ritmi, bob ve paytak ton; **1.4 m/sn** için (adım 0.35 m) |

- Öndeki taşıyıcı bu klibi, arkadaki Carry_Stretcher_Walk'ı oynatır (ikisi de yürüyüş yönüne bakar). Loop Time + Loop Pose açık.
- `clips_meta.json` → `handle_grip` (iki taşıma klibinde de): el kemikleri, avuç noktası (el kemiği başı + kemik yönünde
  5 cm), avuçların kök yerel ortalama konumu ve z aralığı, el yönü, sap ekseni, sedye işaretçisi (`Handle_Front` /
  `Handle_Rear`) ve sedye kökünün taşıyana göre konumu. İki klipte avuç yüksekliği aynı (z≈0.665): sedye düz durur.
  Unity IK'sı iki eli bu sap noktalarına kilitler. Carry_Stretcher_Walk (r2, değiştirilmedi) avucu döngüde ~4.6 cm
  oynar ve sap aralığı 0.556 m'dir; IK bunu kapatır.
- Doğrulama: 25 dosya. Yeni klipte kare sayısı, döngü, 30 fps, kemik adları, kol–gövde ≤ 3 cm, 1.4 m/sn'de ayak kayması
  < 3 cm/adım ve her karede iki avucun `handle_grip` sap noktasına ≤ 4 cm uzaklığı (meta sap aralığı sedyeyle ±1 cm).

## r4a: dolaptan alınan halat (4 hareket grubu, 5 FBX)

Başlangıçta kazıkta halat yoktur: **dolabı aç → kangalı al → taşı → kazığa bağla → uçurumdan at**.
Bu klipler yalnız RescueCrew içindir. Hepsi 30 fps ve yerindedir; kalça yatayda hareket etmez.

| Klip | Süre / kare | Döngü | Hareket / olay |
|---|---|---|---|
| Cabinet_TakeCoil | 1.4 sn / 43 | hayır | Göğüs/omuz hizasındaki kangalı yan griplerden kare 11'de kavrar; yüzün önüne kaldırmadan Carry_Coil pozuna çeker |
| Carry_Coil_Idle | 1.5 sn / 46 | evet | Kangal göğüs önünde, iki avuç yan griplerde; nefes ve baş hareketi |
| Carry_Coil_Walk | 0.4 sn / 13 | evet | 2.4 m/sn, 0.48 m/adım, 0.4 basma oranı; iki el kangalı sarar |
| Rope_TieStake | 2.0 sn / 61 | hayır | Çömelir, kangalı yere koyar; üç çekişle bağlar, tekrar kucağına alıp doğrulur |
| Rope_Throw | 1.4 sn / 43 | hayır | 145° hazırlık dönüşüyle kangal kalça arkasına gider; kare 22'de ileri-aşağı atılır; ileri uzanan kollar ve 0.5 sn sendeleme |

### Prop ve dolap

- `Export/Props/RopeCoil/SM_RopeCoil.fbx`: 686 üçgen, altı hafif düzensiz sarım ve dışa çıkan kısa rijit uç;
  çap yaklaşık 0.44 m, derinlik 0.113 m, halat yarıçapı 0.015 m.
  Eski halatla aynı sRGB `(0.95, 0.62, 0.12)` / en yakın hex `#F29E1F`; Base Color lineerdir.
  Pivot kangalın merkezinde, kangal XZ düzleminde. `Grip_L/R` merkezleri `(±0.204, 0, 0)` m.
- `SM_RopeAnchor_Bare.fbx`: 116 üçgen, yalnız eski kazığın metal geometrisi; aynı zemin pivotu ve aynı
  `RopeAttach=(0,0,0.27)` m. Eski `SM_RopeAnchor.fbx` 440 üçgendir ve geometrisi değiştirilmemiştir.
- **SM_TowTruck_LockerDoor_L1** seçildi. Taban 0.455 m; L2 tekerlek üstünde daha yüksektir. Bölüm 1'de çekici
  `road_at_x(15)+(0,-6.5,0)` konumunda, Z ekseninde +90° döner: yerel **+X**, yol/dağ tarafı **+Y** olur.
  Bu yüzden L1 uçurum tarafında değildir. Kangal kök-yerel `(1.10,-0.10,1.08)` m, XYZ Euler `(0,0,90)`° ile
  dolap içinde dik/askıda durur. Kapak iç yüzünden içeridedir; z=1.08 m tutuş yüksekliği ele erişir.
  Menteşe `(0,0,-1)` ekseninde 90° açılır. Konum/dönüş ve dolap adı `rope_coil_meta.json → towtruck_slot` içindedir.
  Çekici farklı yönlendirilirse oyun tarafı yol/şev yönünü yeniden değerlendirmelidir.

### Unity bağlama ve olay kareleri

- Rig: Humanoid, Copy From Other Avatar → `SK_RescueCrew_Blue`; Scale Factor 1, Convert Units açık,
  Bake Axis Conversion kapalı. Root Transform Rotation / Position Y / XZ: **Bake Into Pose, Original**.
- **Loop Time ve Loop Pose yalnız Carry_Coil_Idle / Carry_Coil_Walk için açık**; diğer üç yeni klipte ikisi de kapalı.
  Eski kliplerin döngü ayarları `clips_meta.json → loop` alanından alınır. Kökü oyun kodu hareket ettirir.
- `coil_grip`: `mixamorig:LeftHand/RightHand`, kemik-yerel avuç ofseti `(0,0.05,0)` m; her kare için
  `hands_root_local_m` ve `attached` kaydı. Değerler **karakter kök-yerel Blender +Z yukarı / -Y ileri** koordinatıdır.
  Unity konum dönüşümü `(-x,z,-y)`; Euler açılarını doğrudan bileşen değiştirerek çevirmeyin, eksen dönüşümünü matrisle yapın.
- Kangal merkezi iki avucun ortasıdır. +X ekseni sağ avuçtan sola, +Z ise taşıyanın yukarısına en yakın dikeydir.
  Bu rijit çerçeveyi kangala uygulayın, el IK hedeflerini `Grip_L/R` boşlarına kilitleyin. Çift yönlü parent/IK döngüsü
  kurmayın: animasyondan önce iki avuçla prop çerçevesini hesaplayın, sonra IK'yı uygulayın.
- Cabinet: dolap kapağı önceden açıktır; kangal kare 11'e dek dolap slotunda, bu kareden itibaren iki elde.
  Karakterin yaklaşma kökünü `cabinet_coil_root_local_m=(0.025,-0.40,0.78)` hedefi dolap slotuna gelecek şekilde hizalayın.
- TieStake: kazık kökü karaktere göre `(0,-0.51,0)` m. `stake_hand_frames=[21,40]`; üç çekişin dorukları
  `tug_frames=[22,29,36]`. `coil_ground_frames=[17,45]` sırasında kangalı `(0.27,-0.43,0.222)` m'de **dik**,
  Z ekseninde 35° dönük, kazığın yanında tutun. Alt kenarı zemine birkaç mm yakındır.
  Bu aralıkta eller griplerden ayrılıp kazık yakasında halat ucunu işler. Kare 46'da tekrar kavrar.
  Halat ucunun düğüm/geçiş çizimi Unity'dedir; yeni aksesuar export edilmemiştir.
- Throw: `release_frame=22` (t=21/30 sn). Bu karede `release_position_root_local_m`, iki elin konumu
  `release_hand_root_local_m` ve son iki örneğin hareketinden türetilen `throw_direction_root_local` kayıtlıdır.
  Bu karede kangalı ellerden ayırın; açılarak kayaya düşen halatı oyun kodu oluşturur. Şeritte kısa balistik uçuş da gösterilir.
  `stumble_frames=[25,40]`; halat şeritleri en az 8, atış şeridi bırakış karesi dahil 10 örnektir.
- TakeCoil sonu, TieStake başı/sonu, Throw başı ve Idle ilk karesi aynı taşıma pozudur. Yürümeye geçişi kısa bir blend ile yapın.
  Kare numaraları **1 tabanlı kaynak kareleridir**; Unity normalizedTime = `(frame-1)/(frames-1)`.

### Üretim ve doğrulama

Önce `build_rope_anchor.py` ve `build_rope_coil.py`, sonra `build_procedural_anims.py` çalıştırılır.
Üç `verify_*.py` betiği yeniden FBX içe aktarır ve yalnız stdout'a yazar; izlenen dosya değiştirmez.
Blender çağrılarında `--python-exit-code 1` kullanın. Tam build sonunda `ACTIONS_SHA256: <hex>` tek satırı
tüm 44 aksiyonun sıralı eğri hash'lerini birleştirir. Kaynak hash'i CRLF→LF normalleştirilmiştir.
`--only` yalnız seçili FBX/şeritleri üretir ve ortak meta kayıtlarını birleştirir.
`--audit-actions` bütün aksiyonları bellekte yeniden üretip mevcut meta hashleriyle karşılaştırır; export veya render yazmaz.

Yeni kliplerde tam kare sayısı, 30 fps, 31 kemik/bağ pozu, döngüde ilk/son bütün kemik matrislerinin eşitliği,
yerinde kalma ve el metadata'sının FBX'le 3 mm içinde eşleşmesi kontrol edilir. Bağlı karelerde iki el aralığı ve gerçek
Grip noktalarına uzaklık ≤3 cm; her kare kol–gövde ve kangal–gövde içe girme ≤3 cm; Walk kayması <3 cm/adım.
Kangal testi gerçek FBX üçgenlerini en fazla 1.5 cm yüzey aralığıyla örnekler; yerdeki ve dolaptaki kareler de dahil,
atış bırakışından sonraki uçuş hariçtir. Açık yaka/etek kenarlarının dışındaki noktaları içerde saymamak için yeni
temas testinde kemik ekseni ölçütüne ek olarak yüz normali ve çıkış ışını kullanılır (mevcut taşıyan–binen testiyle aynı).
Almada kafa boşluğu ≥3 cm olmalıdır; örnekleme aralığı ölçümden çıkarılarak muhafazakâr alt sınır kullanılır.
Bağlamada kangal–bacak ve kangal–gerçek kazık FBX'i içe girmesi ≤3 cm; prop verify'ı sarımları geometriden sayar,
halatın ~3 cm ve demetin 10–12 cm kalınlığını ölçer. Eski kliplerin kontrolleri korunur.
Her yeni şerit karesinde en parlak köşe sRGB parlaklığı en az 150/255 olmalıdır.

## r4b: oyuncu savunması ve hırsız (14 klip)

Hepsi 30 fps, yerinde, aynı 31 `mixamorig:` kemik. Hırsız henüz ayrı bir model olmadığından bütün yeni
şeritlerde `SK_RescueCrew_Blue` kullanılır. Şeritler hareket önizlemesidir; ganimet/düdük/karşı oyuncu FBX'e eklenmez.
Meta karakteri oyuncuda `crew`, hırsızda `thief`; önceki kliplerin karakter alanları korunur.

| Klip | Süre / kare | Döngü | Olay / tutuş |
|---|---|---|---|
| Push_Shove | 0.6 sn / 19 | hayır | İki elle itiş; `hit_frame=9` |
| Tackle_Dive | 0.9 sn / 28 | hayır | Öne sıçrama, yüzüstü iniş; `hit_frame=12`, `ground_frame=22` |
| Tackle_GetUp | 1.0 sn / 31 | hayır | Tackle_Dive son pozundan kalkış |
| Throw_Aim | 1.0 sn / 31 | evet | Sağ el yukarı-geride; `hold_grip` |
| Throw_Release | 0.5 sn / 16 | hayır | Throw_Aim sınır pozundan atış; `release_frame=8` |
| Whistle_Blow | 1.2 sn / 37 | hayır | Sağ el ağızda, nefes, açık kollarla titreme; `blow_frames=[15,27]` (dahil aralık) |
| HitReact | 0.5 sn / 16 | hayır | Geri sendeleme, savrulan kollar, toparlanma |
| Thief_Sneak_Walk | 0.6 sn / 19 | evet | 1.6 m/sn, çömelik sinsi adım, pati eller |
| Thief_Run_Carry | 0.4 sn / 13 | evet | 3.6 m/sn, ganimet göğüste, `loot_grip` |
| Thief_Run | 0.4 sn / 13 | evet | 3.6 m/sn, boş ellerle geniş kol salınımı |
| Thief_Grab | 0.8 sn / 25 | hayır | `grab_frame=11`; Thief_Run_Carry ilk pozunda biter |
| Thief_Punch | 0.8 sn / 25 | hayır | Yumruk omuz arkasında; büyük yay, 130° gövde dönüşü ve dengesiz takip; `hit_frame=14` |
| Thief_Stunned | 1.2 sn / 37 | evet | Sersem salınım, baş dairesi, sarkık kollar |
| Thief_ThrowInVan | 0.8 sn / 25 | hayır | İleri-yukarı iki elli atış; `release_frame=14` |

### Unity import ve olaylar

- Humanoid, Copy From Other Avatar → `SK_RescueCrew_Blue` avatarı; hırsız modeli de aynı iskeleti kullanmalı.
  Scale Factor 1, Convert Units açık, Bake Axis Conversion kapalı.
- Yeni grupta **Loop Time ve Loop Pose yalnız Throw_Aim, Thief_Sneak_Walk, Thief_Run_Carry, Thief_Run,
  Thief_Stunned için açık**. Tek seferliklerde ikisi de kapalı. Genel kaynak `clips_meta.json → loop`.
- Root Transform Rotation, Position Y, Position XZ: **Bake Into Pose açık, Based Upon Original**.
  Apply Root Motion kapalı; yatay hareket oyun kodundan. Tackle'da kalçanın alçalması ve gövdenin yatması
  animasyonda kalmalı; Feet seçimi dikey hareketi düzleştirebilir. Karakter objesini ayrıca yatırmayın.
- Olaylar 1 tabanlı kaynak kareleridir: Unity zaman saniyesi `(frame-1)/30`; normalizedTime `(frame-1)/(frames-1)`.
  Temas/kapma/fırlatma kodu ilgili olayda tetiklenir; uçan nesnenin yörüngesi klibe gömülü değildir.
- `hold_grip`: `mixamorig:RightHand`, kemik-yerel avuç ofseti `(0,0.05,0)` metre.
  `loot_grip`: iki el kemiği, aynı avuç ofseti, 0.408 m avuç aralığı. Ganimet kökü iki avucun ortasında;
  farklı nesne boyutlarında oyun el IK hedeflerini nesnenin grip noktalarına uyarlamalı.
- Tackle_Dive son → Tackle_GetUp ilk, Throw_Aim son → Throw_Release ilk ve Thief_Grab son →
  Thief_Run_Carry ilk ortak pozdan üretilir. Geçişi klip sonundan başlatın; Aim döngüsünün ortasında bırakılırsa
  küçük nişan salınımını kısa crossfade ile kapatın. Ölçüm FBX'ten yeniden içe aktarılarak yapılır.
- Yürüyüş/koşu hızları oynatma 1.0 içindir; hızı değiştirirken animasyon hızını aynı oranla ölçekleyin.

### r4b doğrulaması

Yeni kliplerde kare sayısı tam eşleşir; 30 fps, 31 kemik adı/bağ pozu, meta olay ve tutuş alanları kontrol edilir.
Döngülerin ilk/son kemik matrisleri 0.00001 toleransla eşit; bütün karelerde kol–gövde içe girmesi ≤3 cm.
Kangal grubundaki yüz normali + çıkış ışını filtresi yeni kliplere de uygulanır; eski kontroller korunur.
Ayak kayması her basışta, dört alt-kare örneğiyle, hedef hızda <3 cm olmalı. Tackle ve Throw geçişlerinde
kemik dönüş farkı ≤2°, Grab → Run_Carry geçişinde ≤5°; ek olarak kemik konum farkı ≤5 mm.
Tackle bitişinin yüzüstü ve yere yakın olduğu; Dive/GetUp boyunca mesh'in zemine 2 cm'den fazla girmediği de kontrol edilir.
Her şerit karesinin en parlak köşesi ≥150/255 olmalı. Verify hiçbir dosya yazmaz.

`kabul/art_anims.sh` iki animasyon üretimini ve verify'ı içerir; ayrıca `git status` ve halat prop üreticilerini
çalıştırır. Sadece r4b sahiplik izniyle çalışan işçi bu iki ek adımı çalıştıramaz; şef tam kabulü çalıştırmalıdır.
