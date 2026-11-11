# Ofis Haritası: ortak model kuralları

Bu klasör, Rescue Team oyununda ofis ve garajın bulunduğu dünyaya eklenecek kasaba haritasının kaynağıdır. Harita yaklaşık 1000 × 700 m olacak. İçinde küçük bir sanayi şeridi (atölyeler, benzinlik, market), karşı tarafta 2-3 katlı apartmanlar ve bahçeli müstakil evler, kavşaklar, trafik ışıkları ve fizikli trafik araçları bulunacak.

Modelleri alt agentler Blender betikleriyle üretir. Unity tarafını (arazi, yollar, trafik, yerleşim) ana agent kurar.

## 1. Klasörler (yalnızca bunlara yaz)

| Ne | Yol |
|---|---|
| Üretim betiği | `C:\Users\uur\RescueTeam_OfisHaritasi\Kaynak\Uretim\build_<set>.py` (+ `render_<set>.py`, `verify_<set>.py`) |
| .blend | `C:\Users\uur\RescueTeam_OfisHaritasi\Kaynak\Blender\<Set>.blend` |
| FBX + manifest + malzeme JSON | `C:\Users\uur\RescueTeam_OfisHaritasi\Oyun\Assets\OfisHaritasi\Modeller\<Set>\` |
| Önizlemeler | `C:\Users\uur\RescueTeam_OfisHaritasi\Kaynak\Onizlemeler\<Set>\` |
| Rapor | `C:\Users\uur\RescueTeam_OfisHaritasi\Kaynak\Raporlar\<Set>_README_TR.md` |

Setler: `Araclar`, `Ticari_Binalar`, `Konutlar`, `Donati`.

**Dokunma:** `C:\Users\uur\RescueTeam_Birlesik` (başka bir agent orada çalışıyor), `C:\Users\uur\Desktop\modeller\` (yalnızca okuyabilirsin), oyun kopyasında kendi `Modeller\<Set>` klasörün dışındaki her şey. Unity'yi açma; Unity işini ana agent yapar. `.meta` dosyası yazma.

## 2. Araçlar ve sürümler

- Blender 5.2: `"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b -P betik.py -- <argümanlar>`
- Tek betik bütün seti sıfırdan üretir (fabrika ayarlı boş sahne, `bpy.ops.wm.read_factory_settings(use_empty=True)`). Elle düzenleme yok.
- Önizleme: Cycles CPU, 48-64 örnek + denoise, 1400×950. Sade gri stüdyo zemini, iki yumuşak ışık (servis minibüsü önizlemeleri gibi).

## 3. Stil (kullanıcının kesin istekleri)

- **Yazı, logo, marka, plaka, numara, etiket, doku YOK.** Tabelalar boş düz renk paneldir.
- **Sade mid-poly.** Okunaklı ana formlar, tek segment pah (bevel), düz gölgeleme (`shade_smooth` yok; tekerlek lastiği gibi silindirlerde de düz yüz). Vida, cıvata, küçük süs, ince kablo gibi mikro detaylar yok.
- Renkler donuk ve gerçekçi; oyuncak gibi parlak turuncu-siyah plastik görünüm yok. Garajdaki servis minibüsüyle aynı dil: referans betik `C:\Users\uur\Desktop\modeller\02_KAYNAK_MODELLER\Servis_Minibusu\Uretim\build_van.py`, görüntü `...\Servis_Minibusu\Onizlemeler\van_preview_front_side.png`. Bu betiği oku; malzeme ve pah yaklaşımını oradan al.
- Gerçek ürün kopyası yok (belirli bir marka/model aracı birebir yapma); jenerik ama inandırıcı Türk kasabası formları.
- "Yapay zekâ yaptığı belli" şikâyetleri şunlardan geldi: fazla küçük detay, şişkin oyuncak formlar, jenerik kutu, havada asılı pozlar, uyumsuz renkler. Bunlardan kaçın.

## 4. Eksenler ve orijin

FBX dışa aktarımı bu çağrıyla yapılır (oyun reposunda doğrulanmış ayar):

```python
bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={'MESH','EMPTY'},
    axis_forward='Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
    use_space_transform=True, bake_space_transform=True, use_mesh_modifiers=True,
    mesh_smooth_type='FACE', use_triangles=True, add_leaf_bones=False, bake_anim=False, path_mode='AUTO')
```

Bu ayarla **Blender X = Unity X, Blender Y = Unity Z, Blender Z = Unity yukarı (Y)**.

- **Araçlar:** önü Blender **+Y** (Unity +Z). Sağ taraf Blender +X. Orijin: aracın taban izinin ortası, yer seviyesinde (lastiklerin altı Z=0).
- **Binalar, parseller, donatı:** sokağa bakan ön cephe Blender **−Y** (Unity −Z). Orijin: parselin sokak tarafındaki ön kenarının ortası, yer seviyesinde (Z=0). Parsel Blender +Y yönünde uzanır (X ∈ [−G/2, +G/2], Y ∈ [0, D]).
- Bütün nesneler uygulanmış dönüş/ölçekle dışa aktarılır (dönüş 0, ölçek 1), yalnızca hareketli parçaların orijini mafsalda durur.
- Bina ve duvar tabanları zeminin 0,6 m altına kadar uzansın (arazi hafif eğimliyse boşluk görünmesin).

## 5. Malzemeler

- Her set kendi önekini kullanır: `AR_` (araçlar), `TB_` (ticari binalar), `KN_` (konutlar), `DN_` (donatı).
- Malzemeler mesh düzeyinde atanır (nesne düzeyi atama dışa aktarımda kaybolur).
- Her set bir `<Set>_malzemeler.json` yazar:
  ```json
  {"materials":[{"name":"AR_Boya","color":[0.8,0.8,0.8],"metallic":0.2,"smoothness":0.55,
                 "emission":[0,0,0],"alpha":1.0,"randomize":"arac_boya"}]}
  ```
  `color` sRGB 0-1. `emission` yoksa [0,0,0]. `randomize` yalnızca Unity'nin her kopyada rengini değiştireceği malzemelerde (ör. `arac_boya`, `konut_siva`, `atolye_kaplama`).
- Unity bu JSON'dan URP/Lit malzemeleri kurar. Prosedürel düğüm kullanma (glTF/FBX taşımaz), yalnız düz renk.

## 6. Çarpışma kutuları

- Oyuncunun ve araçların çarpacağı hacimler için `COL_` önekli, **eksenlere hizalı kutu** mesh'leri ekle (8 köşeli dikdörtgen kutu, dönüşsüz). Unity bunları BoxCollider'a çevirip görünmez yapar.
- Binalarda: her kütle için bir kutu, bahçe duvarları için ince kutular. Araçlarda: 1-3 kutu (alt gövde + kabin).

## 7. Manifest

Her set `<Set>_manifest.json` yazar: her varlık için `id`, `fbx`, `boyut` (Unity ekseniyle [genişlik X, yükseklik Y, uzunluk Z] m), `ucgen` sayısı ve sete özel alanlar (her setin brifinde yazıyor). Koordinatlar Unity yerel ekseninde verilir: `[x, y, z]` = Blender `(x, z, y)`.

## 8. Kontrol

Her set bir `verify_<set>.py` ile kontrol edilir: ölçüler brifteki hedeflere uyuyor mu, üçgen bütçesi, bütün malzemeler JSON'da mı, `COL_` kutuları eksenlere hizalı mı, yazı nesnesi (`FONT`) yok mu, araçlarda tekerlek orijinleri doğru mu. Sonuç raporda yazılı olsun. Önizlemelere kendin bak ve beğenmediğin yeri düzelt.
