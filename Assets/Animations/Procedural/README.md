# Prosedürel animasyon klipleri (r1)

Mixamo erişimi olmadığı için anahtar kareli olarak betikle üretilen klipler. Hepsi RescueCrew / Patient ortak
31 kemikli `mixamorig:` iskeleti için, 30 fps, her karede anahtar (bake). Ton: Dear Passengers gibi abartılı ve komik.

- Üretim: `tools/blender/build_procedural_anims.py` (Blender 5.2, deterministik; `-- --no-render` ile şerit render'sız)
- Doğrulama: `tools/blender/verify_procedural_anims.py` (dosya yazmaz)
- Klip başına bilgi: `clips_meta.json` (süre, kare, döngü, yerinde, kök hareketi, karakter, eğri SHA256)
- Önizleme: `renders/<Ad>_strip.png` (RescueCrew_Blue; `Lying_*` için Patient_Tourist, yalnız render'da yatırılmış)

| Klip | Süre | Döngü | Not |
|---|---|---|---|
| Carry_Stretcher_Walk | 1.0 sn | evet | Eller önde sedye saplarında (x ±0.30 m), ağır, kalça sallanan yürüyüş |
| Carry_OneHand_Walk | 0.9 sn | evet | Sağ el yanda koni tutar, sol kol sallanır |
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
