# Kurtarma Hangarı

Garajın doğusuna eklenen Kurtarma Hangarı garajı yaklaşık üç katına çıkarır. Hangarın iç alanı 22,4 × 9,0 m ≈ 202 m², garajınki 96,7 m²'dir. Toplam ≈ 299 m², yani yaklaşık 3,1 kat. Hangar `OfficeGarage.unity` sahnesinde `12_KurtarmaHangari` kökü altındadır ve kodla kurulur.

![Hangarın içi, batıdan](KurtarmaHangari_Genel.png)

## Giriş ve yapı

- Garajın doğu duvarının kuzey ucunda, hava hortumu makarasının yanında 1,3 m genişliğinde, 2,5 m yüksekliğinde bir geçiş açıldı (z 2,45–3,75). Geçişin iki yanı ve lentosu garajın sıva, mavi bant ve beton taban malzemeleriyle yeniden kuruldu; eşikte ızgara var.
- Geçişin önündeki zemin sandığı garajın kuzeydoğu köşesine alındı. Mor varilin üstündeki yedek lastik varilin ortasına kaydı. Hava hortumu makarası 1,3 m kuzeye taşındı. Böylece geçişin önü boş.
- Doğudaki araç kapısı (4,5 m genişlik, 4,6 m yükseklik) açıktır ve dışarı çıkar. Güneydeki sarmal kapı kapalıdır.
- Saçak 5,4 m, beşik çatının mahyası 6,4 m. Beş çelik makas, koruyuculu kolonlar, aşıklar, ışıklıklar, pencereler, epoksi zemin ve zemin çizgileri var.
- Işık: 10 LED armatür (ikisi gölge düşürür), ekipman duvarına bir yıkama ışığı, kapıların iki yanında dört dış aplik (toplam 15). Yansımalar için hangara özel pişmiş bir sonda kullanılır.
- Tabelalar: güney cephede KURTARMA HANGARI, doğu kapısının üstünde KURTARMA EKİBİ, içeride KURTARMA EKİPMANI, geçişin üstünde GARAJ · OFİS ve doğu kapısının üstünde ÇIKIŞ.

![Geçişten hangara bakış](KurtarmaHangari_Gecis.png)

## İçerik

| Bölüm | İçerik |
| --- | --- |
| Kurtarma konvoyu | Kurtarma vanı (arka kapıları açık; içinde drone istasyonu ve drone), çeki demiri, bot römorku ve üstünde kurtarma botu. Vanın önü doğu kapısına bakar. |
| Drone'lar | İstasyondaki drone, iniş matında ("H") hazır drone, tezgâhta bakımdaki drone; can yeleği, ip makarası ve telsiz yükleri. |
| Ekipman duvarı | İki pano: can simitleri, ağaç kayışları, kurtarma askısı, megafon ve drone yükleri rafı, ilk yardım dolabı, kask sırası, yangın söndürücüler. |
| Erzak ve mola | Üç palet (su paketleri ve erzak kolileri), mola masası ve sandalyeler, yiyecek-içecek seti (pizza, kola, çikolata ve diğerleri), su sebili, nöbet çizelgesi. |
| Atölye | Alet tezgâhı, delikli pano, elektrikli aletler (sütunlu matkap, akülü matkap ve şarj aleti, uç kutusu, avuç taşlama), kırmızı alet dolabı. |
| Köpek köşesi | Fındık'ın yatağı, mama ve su kapları, topu ve isim levhası. |

![Kurtarma konvoyu](KurtarmaHangari_Konvoy.png)

Hangardaki araçlar, drone'lar ve duvardaki eşyalar dekordur. Sürülmez, uçmaz, alınmaz. Hangar içeriği şirket kaydına, hazırlık listesine ve iş koşullarına bağlı değildir.

## Fındık

Ekip köpeği Fındık köşesindeki yatağında uyur. Oyuncu 1,6 m'ye yaklaşınca uyanır. Kendi alanında (yaklaşık 3,8 × 4,3 m) dolaşır, koklar, oturur, yatar ve kabından yer. Oyuncu 3,2 m içindeyken sevinir, ara sıra havlar. Ona bakınca "E — Fındık'ı sev" çıkar; E'ye basınca kuyruğunu sallar. Davranış `Assets/_Project/Scripts/Garage/KopekDavranisi.cs` dosyasındadır; 16 animasyon klibi `Kopek.controller` içindeki durumlara bağlıdır. Fındık'ın durumu kaydedilmez.

![Fındık'ın köşesi](KurtarmaHangari_Kopek.png)

## Ofise eklenenler

Ofis dekoruna aynı kaynak setlerden şunlar eklendi:

- dosya dolabının üstünde lazer yazıcı,
- masada çikolata ve bonbonlar,
- yan sehpada pizza tahtası ve pizza,
- kola kutusu ve bardağı.

Ofis ayrıntı dokuları artık doğrusal aktarılır (sRGB kapalı). Önceden URP'nin ayrıntı katmanı yüzeyleri yaklaşık %45 karartıyordu.

## Modeller ve aktarım

Kaynaklar `02_KAYNAK_MODELLER` klasöründeki Blender dosyalarıdır: Kurtarma_Botu, Bot_Romorku, Drone_Sistemi, kopek (Sokak_Kopegi), Yiyecek_Icecek, Elektrikli_Aletler_Yazici ve Sel_Kurtarma_Ekipmani. `Art/Blender/Scripts/export_rescue_gear.py` her biri için FBX üretir. Her FBX'in yanına malzeme değerlerini (`.materials.json`) ve bağlantı noktalarını (`.points.json`) yazar:

```
blender -b <dosya.blend> --python Art/Blender/Scripts/export_rescue_gear.py -- <iş> <çıktı klasörü>
```

İşler: `boat`, `trailer`, `drone`, `dog`, `food`, `van`, `tools`, `rescuekit`. FBX'ler `Assets/_Project/Art/Models/RescueGear/` altındadır. `Assets/Editor/RescueGearImportSettings.cs` bu klasördeki modelleri şöyle aktarır:

- Renk, metal, pürüzlülük, ışıma ve saydamlık Blender'ın değerlerinden yazılır. Doku yoktur.
- Kaynak malzemeler glTF'de iki yüzlü olduğundan bazı normaller terstir. Statik modellere ters sarımlı ikinci bir katman eklenir (köpek hariç); yoksa örneğin vanın yük bölmesi duvarı görünmez oluyordu.
- Köpek Generic iskeletle ve 16 klip olarak gelir. Döngüler işaretlenir, kök hareketi kilitlenir; yürüyüşü `KopekDavranisi` taşır.
- Meshler okunamaz aktarılır, böylece `GarageRuntimeOptimization` onları statik birleştirmeye katmaz.

`Assets/Editor/RescueGearAssembly.cs` konvoyu (van, istasyon, drone, kapılar, çeki demiri, römork, bot) bağlantı noktalarına göre kurar.

## Kurulum

- `Rescue Team > Ofis-Garaj > Garajı Büyüt: Kurtarma Hangarı` hangarı kurar. Yeniden çalıştırılabilir; önce eskisini kaldırır.
- `Rescue Team > Ofis-Garaj > Kurtarma Hangarını Kaldır` hangarı siler. Gizlenen çevre parçalarını, garajın duvar dilimini ve çarpıştırıcısını, makarayı, sandığı ve lastiği geri koyar.
- Batchmode: `-executeMethod GarageHangarBuilder.BuildBatch`. Rapor: `Captures/Hangar/hangar_build_report.txt`.
- Kurucu, hangar alanına düşen 146 çevre parçasını (ağaç, çalı, kaya, konteyner, tahta) gizler. Liste `Assets/_Project/Art/Hangar/hangar_hidden_parts.txt` dosyasındadır. Meshler `Assets/_Project/Art/Hangar/Hangar_Meshes.asset`, malzemeler `Assets/_Project/Art/Hangar/Materials` altındadır.
- Tabelalar `RescueTeam/HangarText` gölgelendiricisini kullanır. Bu gölgelendirici derinliği sınar ve yazı rengini doğrusal renge çevirir. Varsayılan yazı gölgelendiricisinde tabelalar duvarların arkasından görünüyor, renkler soluk çıkıyordu.
- `Company > Build Office and Garage` sahneyi baştan üretir ve hangarı siler. Garajın depo ve atölye kurulum menüleri de sandığı ve makarayı eski yerine koyabilir. Bunlardan sonra hangar menüsünü bir kez daha çalıştırın.

![Doğu kapısı, dışarıdan](KurtarmaHangari_DisCephe.png)

## Doğrulama

`Rescue Team > Ofis-Garaj > Doğrula (Kurtarma Hangarı)` (`GarageHangarVerify.Run`) raporunu `Captures/Hangar/hangar_verify_report.txt` dosyasına yazar.

- Düzenleme kipi:
  - hangar kökü ve gizli parça listesi var;
  - hangarın içinde çevre parçası kalmadı;
  - `Garage_East` çarpıştırıcısı geçişin güneyinde bitiyor;
  - geçiş hattında oyuncu kapsülünü engelleyen bir şey yok;
  - makara geçişin dışında;
  - 22 model yerinde;
  - ofis dekorunda yazıcı ve yiyecek-içecekler var;
  - pembe ya da eksik malzeme yok;
  - ışıklar ve yansıma sondası var;
  - meshler okunamaz.
- Play Mode:
  - hangar meshleri statik birleştirmeye girmiyor;
  - oyuncu garajdan geçişle hangara yürüyor, hangar boyunca ilerleyip doğu kapısından çıkıyor;
  - kapalı güney kapısından geçilmiyor;
  - Fındık uyuyor, oyuncu yaklaşınca uyanıyor; bakınca "E — Fındık'ı sev" çıkıyor ve E ile seviliyor.

6 Kasım 2026 koşusu: **GARAGEHANGAR VERIFY OK**. Aynı koşuda Ofis-Garaj, Ofis Genişletme, Sel Vadisi, Görev Köprüsü ve Bölüm 1 doğrulamaları da geçti. Koşular bu makinede Unity 6000.5.5f1'de, yalnız yerelde değiştirilen paket ve fizik ayarlarıyla yapıldı ([Sel Vadisi doğrulaması](SelVadisiSahaGorevi.md#doğrulama)). İnsanla serbest oynanış yapılmadı.
