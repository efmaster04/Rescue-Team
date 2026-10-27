# RescueCrew — Oyun karakteri paketi

Rescue Team'in oyuncu karakteri: chibi kurtarma ekibi üyesi. 4 renk varyantı ve Mixamo animasyonlarıyla birlikte geliyor.

## Dosyalar
| Dosya | İçerik |
|---|---|
| `SK_RescueCrew_Orange.fbx` / `_Yellow` / `_Blue` / `_Green` | Karakter (aynı mesh ve rig, kask ve reflektör rengi farklı) |
| `Animations/Mixamo_Walking.fbx` | Yürüme, In Place, döngü |
| `Animations/Mixamo_Running.fbx` | Koşma, In Place, döngü |
| `Animations/Mixamo_Idle.fbx` | Bekleme, döngü |
| `Animations/Mixamo_Waving.fbx` | El sallama, tek sefer |
| `Animations/XBot_TPose.fbx` | Mixamo X Bot, T-pose, With Skin: kliplerin ortak kaynak avatarı (aşağıya bak) |

Animasyonlar Mixamo'dan "FBX for Unity, Without Skin, 30 fps" olarak indirildi; kaynak iskelet Mixamo X Bot. Humanoid retarget ile karaktere uygulanır.

## Karakter teknik bilgileri
- Boy 1.50 m, ayak tabanı y=0, yüz +Z'ye bakıyor, T-pose
- ~9.700 üçgen, 3 skinned mesh: `SK_RescueCrew_Body`, `SK_RescueCrew_Helmet`, `SK_RescueCrew_Face`
- Rig: `RescueCrew_Rig`, 31 kemik, `mixamorig:` isimli; Unity Humanoid'de 30/30 kemik otomatik eşleniyor
- Yüz ifadesi blend shape'leri (`SK_RescueCrew_Face`, 0–100): `Blink_L`, `Blink_R`, `Brows_Angry`, `Brows_Worried`, `Mouth_Smile`, `Mouth_Open`, `Mouth_Frown`, `Pupils_Small`, `Eyes_Happy`
  - Mutlu: `Eyes_Happy` + `Mouth_Smile` · Şaşkın: `Mouth_Open` + `Pupils_Small` · Kızgın: `Brows_Angry` + `Mouth_Frown` · Korkmuş: `Brows_Worried` + `Mouth_Open` + `Pupils_Small`
- Materyaller düz renk (texture yok); FBX'teki renkler hex değerlerinin lineer karşılığı (Blender varsayılanı; Linear URP'de doğru görünür)

## Unity import ayarları (karakter FBX'leri)
- Model: Scale Factor 1, Convert Units **açık**, **Bake Axis Conversion kapalı** (açıkken karakter ters döner), Import BlendShapes açık
- Rig: Animation Type **Humanoid**, Avatar Definition **Create From This Model**
- Materials: Location "Use External Materials (Legacy)" veya Extract Materials. URP kullanılıyorsa materyalleri URP/Lit'e çevir.

## Unity import ayarları (animasyon FBX'leri)
- Önce `Animations/XBot_TPose.fbx` (Mixamo X Bot, T-pose, With Skin) içe aktarılır: Animation Type **Humanoid**, Avatar Definition **Create From This Model**. Bu, tüm klipler için ortak kaynak avatardır.
- Klipler: Animation Type **Humanoid**, Avatar Definition **Copy From Other Avatar** → XBot_TPose avatarı.
  - **Neden:** "Without Skin" kliplerde "Create From This Model" seçilirse Unity, X Bot avatarını T-pozdan değil klibin ilk karesinden kurar. O karede sol ve sağ bacak 15–25° farklıdır ve retarget asimetrik olur. Örneğin Running'de sağ bacak 2.9° yerine 18.8° yana açılıyor, Walking'de adım fazı 0.48 yerine 0.23 çıkıyor (topallama). T-poz avatarla ölçümler kaynak klibin ±1–2° içine geliyor (`RescueTeam-UnityTest/Renders/symmetry_report.txt`).
- Animation: Walking / Running / Idle için **Loop Time** açık
- Walking / Running: Root Transform Rotation, Position (Y) ve Position (XZ) için **Bake Into Pose** açık

## Kaynak
Prosedürel olarak üretildi: `tools/blender/build_rescue_crew.py` (Blender 5.2). Varyantlar: `tools/blender/export_variants.py`. Doğrulama: `tools/blender/verify_fbx.py`.
