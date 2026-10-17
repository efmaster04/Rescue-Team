# Prosedürel animasyon klipleri (r3)

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
  Diğerleri tek sefer (Loop Time kapalı).
- **Bake Into Pose** (bütün kliplerde Root Transform Rotation, Position (Y), Position (XZ) üçü de **açık**, Based Upon **Original**):
  - Yerinde klipler kalçayı yatayda hiç kaydırmaz; dikey zıplama/çömelme pozun içinde kalır (Feet yerine Original seçilmeli, yoksa Jump yüksekliği düzleşir).
  - GetUp_FromBack: kalça yataydan dikeye döner ve yükselir. Rotation'ı bake etmezseniz Unity bunu kök dönüşü sayıp
    bütün objeyi devirir. Üçü de açık + Original ile klip yazıldığı gibi oynar; karakter ayakta, başlangıç noktasında biter.
    Kalkışın oyunda objeyi gerçekten taşıması istenirse yalnız Position (XZ) kapatılabilir (kalça ~12 cm oynar).
- `Lying_*` klipleri Patient_LiePose gibi dik tanımlıdır: oyunda hasta objesi sedyeye yatırılır (sırt aşağı, -90° X).

## Doğrulama kriterleri (verify)
11 dosya, tek aksiyon, kemik adları ve bağ pozu RescueCrew ile aynı; kare sayısı ±2; döngülerde ilk/son kare farkı
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
