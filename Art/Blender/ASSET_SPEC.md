# Firma — Model & Sahne Şartnamesi (Blender → Unity)

Bu belge, Unity oyun koduyla birebir eşleşen model adlarını, boyutlarını, soket/animasyon
parçalarını ve sahne yerleşimini tanımlar. **Adlar sözleşmedir**: Unity tarafındaki kurulum
scriptleri nesneleri bu adlarla bulur. Bir adı değiştirmek gerekiyorsa rapor et.

## 0. Araçlar
- Blender 5.2: `"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b --python <script.py>`
- Ortak kit: `C:\Users\uur\oyunnn\Art\Blender\Scripts\firma_kit.py`
  (`import sys; sys.path.insert(0, r"C:\Users\uur\oyunnn\Art\Blender\Scripts"); import firma_kit as k`)
  Kit fonksiyonları: `reset_scene, mat, box, cylinder, cone, torus, uv_sphere, profile_extrude,
  tube_path, empty, collider, root, parent_all, set_parent, set_origin, join, apply_transform,
  export_fbx(root, "<Grup>/<Ad>", anim=False), save_blend("<Grup>/<Ad>"), render_preview(root, "<Grup>/<Ad>", extra_views=True)`.
  Kiti değiştirme; ihtiyaç olursa kendi scriptinde yardımcı fonksiyon yaz.
- Kaynak sahne (salt okunur, ÜZERİNE YAZMA): `C:\Users\uur\Desktop\modeller\01_GARAJ_OFIS_TESLIMI\01_Blender\Sahneler\Garaj_Tam_Sahne.blend`
- Tekil kaynak modeller (salt okunur): `C:\Users\uur\Desktop\modeller\01_GARAJ_OFIS_TESLIMI\01_Blender\Modeller\...`
  ve `C:\Users\uur\Desktop\modeller\02_KAYNAK_MODELLER\...`
- Script çıktıları: `.blend` → `C:\Users\uur\oyunnn\Art\Blender\Models\<Grup>\`,
  FBX → `C:\Users\uur\oyunnn_staging\Assets\_Project\Art\Models\<Grup>\`,
  önizleme PNG → `C:\Users\uur\oyunnn\Art\Previews\<Grup>\`.
  Scriptler: `C:\Users\uur\oyunnn\Art\Blender\Scripts\gen_<grup>.py` (tekrar çalıştırılabilir olmalı).

## 1. Sanat tarzı (zorunlu)
Stylized Mid-Poly / Semi-Realistic Indie. Gerçek oranlar, hafif bevel (kitin `bevel=` parametresi),
temiz silüet, mantıklı malzeme ayrımı, hafif kullanım izi (ör. ikinci ton malzeme ile aşınmış kenar,
çizik şeridi, kir lekesi decal'i). Minecraft/blok görünüm YOK, keskin low-poly YOK, fotogerçekçi
aşırı detay YOK. Malzemeleri mümkünse kitin PALETTE adlarından seç (mevcut sahneyle aynı görünür).
Yeni malzeme gerekiyorsa adı `F ` ile başlasın (ör. `F Fire engine red`), mevcut paletle uyumlu,
doygunluğu düşük-orta tonlar kullan. Tipik üçgen bütçeleri: küçük el eşyası 150–900, orta eşya
600–2500, mobilya 400–3000, araç gövdesi 8k–20k, modül 4k–12k.
Primitif kutu/silindir başlangıç olarak kullanılabilir ama son model tanınabilir alt parçalardan
(kulp, menteşe, vida, panel çizgisi, lastik, etiket, kablo) oluşmalı. Her modeli önizleme render'ına
bakarak değerlendir; zayıf görünüyorsa iyileştir.

## 2. Eksen ve isim kuralları
- 1 birim = 1 m, Z yukarı. Modelin ÖNÜ Blender **+Y** yönüne bakar (Unity'de +Z olur).
  Dışa aktarım eşlemesi: Unity(x, y, z) = Blender(x, z, y).
- Orijin: yere temas eden merkez (aksi belirtilmedikçe). Kök nesne = boş (`k.root("<Ad>")`), tüm
  parçalar onun altında; kök adı = dosya adı.
- `SOCKET_<Ad>` boş: bağlantı/yerleştirme noktası (yerel +Y ileri, +Z yukarı).
- `ANIM_<Ad>`: hareketli parça; orijini pivot (menteşe ekseni, tekerlek merkezi, sürgü başlangıcı).
  Alt parçaları varsa onların ebeveyni olur. Menteşe ekseni Blender Z ise `ANIM_` adı yeterli;
  başka eksen için nesneye özel özellik ekle: `obj["axis"] = "X"` (ve `obj["range"] = "0,95"` derece
  veya metre olarak açılma aralığı).
- `COL_<Ad>`: görünmez kutu çarpıştırıcı (k.collider). Her eşyanın en az bir COL_ kutusu olsun;
  karmaşık şekillerde 2–6 kutu ile kaba hacmi kapla.
- `LIGHT_<Ad>`: Unity ışık eklenecek boş. `SCREEN_<Ad>`: ekran yüzeyi (düz, öne bakan dörtgen,
  UV 0..1 tam kaplar).
- Eşya kökünde özel özellikler (obj["..."]): `mass_kg` (gerçekçi kütle), `carry` = "one_hand" |
  "two_hand" | "heavy" (heavy = iki oyuncu önerilir).

## 3. Model grupları ve ad listesi

### 3.1 Araçlar (`Vehicles`)
**Vehicle_ServiceTruck** — modüler servis kamyonu (kabin + şasi). Van modeli (V01–V22) ile aynı
aileden: beyaz/fildişi kabin (`02 Sunlit ivory` / `01 Warm ivory paint`), mavi şerit
(`05 Desaturated blue stripe`), koyu ızgara, turuncu sinyal, yuvarlatılmış kabin.
Ölçüler (yerel, ön +Y): toplam uzunluk 6.8 (y −3.4..+3.4), genişlik 2.3, kabin yüksekliği 2.75.
Kabin y +1.4..+3.4. Ön aks y=+2.3, arka aks y=−1.6, lastik yarıçapı 0.42, iz genişliği 1.9.
Şasi kirişleri üst yüzeyi z=0.95, kabin arkasından y=−3.4'e kadar; arka tampon + plaka + stop.
- `SOCKET_ModuleMount` (0, −1.3, 0.95): modül orijini buraya oturur.
- `ANIM_Lock_1..4` twist-lock pimleri (±0.85, −3.05 ve +0.45, z 0.95), Z ekseninde 90° döner.
- `SOCKET_Connector_Hydraulic` (0.45, 1.33, 1.15), `SOCKET_Connector_Electric` (−0.45, 1.33, 1.15)
  kabin arkasında bağlantı soketi gövdeleri ile.
- `ANIM_Wheel_FL/FR/RL/RR` (tekerlek merkezi pivot, X ekseninde döner), çift arka lastik olabilir.
  Ön tekerlekler ayrıca direksiyonla Z ekseninde döner (aynı nesne; Unity halleder).
- `ANIM_Door_L` / `ANIM_Door_R` kabin kapıları, menteşe ön kenarda (dikey Z), açılma 0..70°.
- `SOCKET_Seat_Driver` (−0.45, 2.3, 1.25), `SOCKET_Seat_Passenger` (0.45, 2.3, 1.25),
  `SOCKET_Exit_L` (−1.6, 2.3, 0), `SOCKET_Exit_R` (1.6, 2.3, 0), `SOCKET_SteeringWheel`.
- `LIGHT_Head_L/R`, `LIGHT_Beacon` (kabin üstü turuncu tepe lambası modeli ile).
- İç mekân: koltuklar, direksiyon, gösterge paneli (camdan görünür olmalı).
- COL_ kutuları: kabin, şasi, tamponlar (tekerlekler hariç).

**Vehicle_ServiceVan** — mevcut sahnedeki van (REUSED | V01-V22) yeniden kullanılır, YENİDEN
MODELLENMEZ. Kaynak sahneden V parçalarını al, dünya konumunu orijine taşı (zemin merkezi,
ön +Y'ye bakacak şekilde döndür: kaynakta ön −Y yönünde), parçaları ayır:
`ANIM_Wheel_FL/FR/RL/RR` (lastik+jant birleşik, pivot tekerlek merkezi), `ANIM_Door_L/R` (V03 ön
kapılar, menteşe ön kenar), `ANIM_RearDoor_L/R` (V21 arka çift kapı, menteşe dış kenar, açılma
0..100°), `SOCKET_Seat_Driver`, `SOCKET_Exit_L/R`, `SOCKET_CargoArea` (yük bölümü taban merkezi),
`SOCKET_Cargo_01..06` (yük bölümü zemininde 2x3 ızgara, 0.55 m aralık), COL_ kutuları
(gövde, yük tabanı ayrı: `COL_CargoFloor`). V22 yük bölmesi iç mekânı korunur.

### 3.2 Modüller (`Modules`) — kamyon `SOCKET_ModuleMount`'una oturur
Ortak: orijin = modül alt çerçevesinin merkezi (kamyona oturan yüzey), ayak izi 2.3 (x) × 4.0 (y),
modül y −2.0..+2.0 (ön +Y kabine bakar), yükseklik ≤ 2.4. Alt çerçeve: iki boyuna kiriş + twist-lock
yuvaları (±0.85, −1.75 ve +1.75 — kamyondaki kilitlerle hizalı). Ön yüzde hidrolik/elektrik
bağlantı kutusu. Kaldırma: `SOCKET_LiftPoint_1..4` üst köşelerde (vinç kancaları için göz/halka
modeli ile). `SOCKET_Cargo_NN`: ekipman yerleştirme noktaları (yerel yukarı +Z), sayısı modüle göre.
- **Module_Towing** (çekici): sarı-antrasit; arkada çekme kolu/bom (`ANIM_Boom`, pivot öndeki
  mafsal, X ekseninde 0..35° kalkar), hidrolik piston, vinç tamburu (`ANIM_Winch`), halat + kanca
  (`SOCKET_Hook`), yan takım dolapları, arka çalışma lambaları, `SOCKET_Cargo_01..04`.
- **Module_Fire** (itfaiye): `F Fire engine red` + gümüş; su tankı, pompa paneli (manometreler),
  hortum makarası (`ANIM_HoseReel`), kepenkli yan dolaplar (statik), merdiven rafı, `SOCKET_Cargo_01..05`.
- **Module_Cargo** (nakliye): düz kasa platform, katlanır yan kapaklar, ön siper, kayış rayları,
  `SOCKET_Cargo_01..08` (2x4 ızgara), ahşap taban.
- **Module_TechService** (teknik servis): kapalı servis kasası, yan kepenk dolaplar, jeneratör ünitesi,
  çalışma lambası direği, iç raf görünümü; `SOCKET_Cargo_01..06`.
- **Module_WaterPump** (su tahliye): pompa kızağı (dizel pompa), iki hortum makarası, emiş hortumu
  rafı, IBC tank; mavi-gri tonlar; `SOCKET_Cargo_01..04`.

### 3.3 Modül istasyonu ve kapılar (`Station`)
- **Station_Gantry** (köprü vinç), hangar iç ölçüsüne göre: koşu rayları y boyunca, x=9.6 ve x=16.4
  hatlarında z=5.0'da (rayları taşıyan konsollar duvarlara gider). Model orijini (13.0, −0.7, 0)
  hangar zemin merkezi; raylar yerel y −4.3..+4.3. Parçalar: `ANIM_Bridge` (köprü kirişi, y'de
  kayar; orijin köprü merkezi), onun altında `ANIM_Trolley` (araba, x'te kayar), onun altında
  `ANIM_Hook` (kanca bloğu + 4 zincirli taşıyıcı kiriş/spreader, z'de iner-kalkar; orijin spreader
  alt merkezi, varsayılan yükseklik z=4.2), `ANIM_Cable` (araba ile kanca arası halat, dikey 1 m
  boyunda, Unity Z ölçekler). Sarı-siyah uyarı bantları, motor kutusu, uyarı lambası
  (`LIGHT_Beacon`). Spreader'ın 4 kancası modül kaldırma noktalarına iner (x ±1.0, y ±1.8).
- **Station_ModuleStand**: modül sehpası (sarı çelik, yükseklik 0.95, ayak izi 2.1 × 3.6),
  üstte 4 twist-lock yuvası. Orijin zemin merkezi.
- **Station_ControlPanel**: ayaklı kontrol pulpiti (≈1.3 m), eğik üst panel; `SCREEN_Main`
  (0.42 × 0.28 m), büyük yeşil/kırmızı butonlar, acil stop, anahtar; `SOCKET_UseView`
  (oyuncu kamerası buraya bakar: panelin 0.6 m önü, 1.6 m yükseklik).
- **Door_RollerCurtain_Bay** (garaj 1 ön kapısı, açıklık 8.4 × 3.5) ve
  **Door_RollerCurtain_Hangar** (hangar açıklığı 3.6 × 4.4): `ANIM_Curtain` lamel perde,
  orijini üst kenar (perde aşağı doğru uzanır, Unity Z ölçekle kısaltır), alt profil `ANIM_BottomBar`,
  yan kızaklar statik. Lamel yatay oluk çizgileri belirgin.
- **Station_Upgrade_Motor** (istasyon geliştirmesi görseli: büyük motor kutusu + ek kablo kanalı +
  dönerli uyarı lambası; gantry trolley üstüne oturur, orijini trolley üst merkezi).

### 3.4 Ekipman (`Equipment`) — taşınabilir eşyalar
Her biri ayrı FBX; kökte `mass_kg`, `carry`. `SOCKET_Hold` (tutuş noktası), COL_ kutuları.
Mevcut tekil modellerden alınacaklar (kaynak .blend'leri aç, ölçek/pivot düzelt, gerekiyorsa
kapak gibi parçaları ANIM_ yap): `Equipment_Toolbox` (G11), `Equipment_EquipmentCase` (G09),
`Equipment_MechanicCase` (G13), `Equipment_PartsCrate` (G10), `Equipment_HydraulicOil` (G14),
`Equipment_FuelCan` (G15), `Equipment_MotorOil` (G16), `Equipment_GreaseTin` (G17),
`Equipment_AbsorbentPail` (G18), `Equipment_SpareTire` (G23), `Equipment_TrafficCone` (E19+E20),
`Equipment_FloorJack` (T24 + T25 kol birleşik; tekerlekli ağır kriko, carry=heavy),
`Equipment_StorageBin` (G12, depo stok kutusu), `Equipment_Tote` (G08 + G21 kapak `ANIM_Lid`).
Yeni modellenecekler:
`Equipment_TowStrap` (sarılı sarı kurtarma kayışı + kancalar), `Equipment_TowChain` (sarılı zincir,
kanca), `Equipment_WheelChocks` (çift lastik takoz, ip ile bağlı), `Equipment_BottleJack`
(şişe kriko, kırmızı), `Equipment_FireExtinguisher` (6 kg kırmızı tüp, hortum, etiket),
`Equipment_FirstAidKit` (yeşil kutu, beyaz haç), `Equipment_HoseRoll` (yassı itfaiye hortumu rulosu,
rakorlu), `Equipment_PortablePump` (benzinli su pompası, kafes çerçeve), `Equipment_Generator`
(taşınabilir jeneratör, kafes çerçeve, sarı-siyah), `Equipment_Shovel` (kürek), `Equipment_WorkLight`
(tripod LED projektör), `Equipment_RatchetStraps` (cırcır spanzet demeti), `Equipment_TractionMats`
(turuncu çamur plakası çifti), `Equipment_HiVisPack` (yelek paketi + kask), `Equipment_AnimalCrate`
(hayvan taşıma kafesi, kapı `ANIM_Door`), `Equipment_CableReel` (elektrik uzatma makarası),
`Equipment_SuctionHose` (spiral emiş hortumu rulosu), `Equipment_FuseKit` (küçük elektrik
sigorta/kablo kiti çantası), `Equipment_RepairParts` (yedek parça kutusu: karton, etiketli),
`Item_DeliveryCrate` (ahşap teslimat sandığı 0.9×0.6×0.55, kapak `ANIM_Lid`, sevkiyat etiketi,
`carry=two_hand`), `Item_ReportPaper` (A4 kağıt, 1 sayfa, hafif kıvrık), `Item_CoffeeCup` (O13
kupası tabanlı; `ANIM_Coffee` iç yüzey diski — Unity Z ölçekleyerek doldurur).

### 3.5 Ofis ve sosyal eşyalar (`Office`)
`Office_CoffeeMachine` (filtre/espresso karışımı, `ANIM_Nozzle`, `SOCKET_Cup`, ışıklı düğme),
`Office_MiniFridge` (≈0.85 m, kapı `ANIM_Door` menteşe sol, iç raflar + birkaç içecek),
`Office_Radio` (retro masaüstü radyo, `ANIM_Dial`, hoparlör ızgarası), `Office_Printer`
(lazer yazıcı, çıkış tepsisi, `SOCKET_PaperOut` kağıt çıkış noktası), `Office_PrinterCabinet`
(0.6 × 0.5 × 0.7 dolap, yazıcı üstüne oturur), `Office_DeskPhone` (`ANIM_Handset`),
`Office_TaskBoard` (mantar pano 1.4 × 0.9, ahşap çerçeve, raptiyeli kağıtlar; `SCREEN_Board`
1.3 × 0.8 dinamik içerik yüzeyi panonun hafif önünde), `Office_ReceptionCounter` (1.4 m karşılama
bankosu, müşteri tarafı yüksek tezgâh), `Office_VisitorChair` (metal ayaklı bekleme sandalyesi),
`Office_OpsDesk` (operasyon masası 1.4 m: telsiz baz istasyonu, ikinci monitör, mikrofon;
`SCREEN_Ops`), `Office_TrophyShelf` (duvar rafı + 3 boş kupa yuvası `SOCKET_Trophy_1..3`),
`Office_Trophy_A/B/C` (kupa/plaket/çerçeveli fotoğraf), `Office_HazmatCabinet` (sarı yanıcı madde
dolabı, çift kapı `ANIM_Door_L/R`, iç raflar; `SOCKET_Shelf_1..4`), `Office_FirstAidWallBox`,
`Office_ExtinguisherBracket` (duvar askısı), `Office_SafetySign_*` (3–4 tabela: "Yangın Tüpü",
"Forklift / Araç Geçiş Yolu", "Baret Takınız", "Tehlikeli Madde"), `Office_WallPoster_A/B`
(dekor posterler), `Office_KitchenCounter` (1.3 m mutfak dolabı + tezgâh), `Office_Mug_Rack`.
Dinlenme odası (geliştirme): `Office_Sofa`, `Office_CoffeeTable`, `Office_WaterDispenser`,
`Office_TV` (`SCREEN_TV`), `Office_DiningTable`, `Office_DiningChair`.
Geliştirme görselleri: `Upgrade_ToolChest` (kırmızı takım dolabı, çekmeceler), `Upgrade_DiagCart`
(arıza tespit arabası, ekranlı), `Upgrade_RadioMast` (çatı anteni).

### 3.6 Karakterler (`Characters`)
Parçalı (segmented) stilize insan: gövde parçaları kemiklere rijit bağlı (skin weight gerekmez),
boy ≈1.78 m, orijin ayak arası zemin, ön +Y. Armature kemik adları: `Root, Hips, Spine, Chest,
Neck, Head, Shoulder_L/R, UpperArm_L/R, LowerArm_L/R, Hand_L/R, UpperLeg_L/R, LowerLeg_L/R,
Foot_L/R`. Aksiyonlar (FBX take adları): `Idle, Walk, Run, CarryIdle, CarryWalk, Interact, Sit,
Wave`. `SOCKET_HandR`, `SOCKET_Head` (isim etiketi).
- `Char_Worker` (oyuncu): tulum + iş ceketi, şapka/kask yok, eldiven; renk malzemesi adı
  `F Player suit` (Unity oyuncu rengiyle değiştirir).
- `Char_Customer_A` (gömlek + pantolon), `Char_Customer_B` (mont, şapka), `Char_Customer_C`
  (yaşlı, hırka), `Char_Inspector` (yelek + baret + kask altında, elinde klipsli dosya).

## 4. Sahne yerleşimi (`World`) — Blender koordinatları
Mevcut: Ofis x[−4.8,0] y[−5.2,1.2] (iç yükseklik ~3.15); Garaj-1 x[0,9.3] y[−5.2,5.2] (duvar 4.0).
Yol −Y tarafında. İç zemin z=0, dış çim ≈ −0.28, ön beton saha ≈ −0.07.

Yeni (temel):
1. **Depo kanadı** x[−4.8,0] y[1.2,5.2], ofisle aynı yükseklik ve dış cephe dili. Garaj ile bağlantı
   mevcut servis kepengi (A18/A19, x=0, y 1.61..3.09). Ofis arka duvarında kapı (A01 #05 yerine kapılı
   modül; açıklık x −4.55..−3.65, h 2.1) — harita O26 ve notlar O27 +0.45 x kaydırılır.
2. **Modül hangarı** x[9.25,16.75] y[−5.2,3.8], duvar yüksekliği 5.6, ön cephede araç açıklığı
   x[10.0,13.4] h 4.4; garaj-1 sağ duvarında (x=9.25) y 2.55..3.55 arası geçiş kapısı.
   Hangar önüne beton saha ve rampa. Göl, ağaç, kaya, çöp konteyneri, koni çakışanlar taşınır.
3. **Geliştirme yapıları** (varsayılan gizli koleksiyonlar):
   `UPG_office_breakroom` — dinlenme odası x[−8.2,−4.8] y[−2.0,1.2] + ofis sol duvarında kapı;
   `UPG_garage_carport` — dış park sundurması x[17.2,21.4] y[−5.0,0.6];
   Yükseltme ile gizlenecek mevcut parçalar `UPGHIDE_<upgradeId>__<orijinal ad>` olarak adlandırılır.
İşaretçiler (boş nesneler) ve bölgeler (gizli kutu mesh, `ZONE_` öneki) için World ajanının brifine bak.
