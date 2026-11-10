# Ofis Haritası: kasaba, yollar ve fizikli trafik

Ofis + garaj sahnesinin (`Assets/_Project/Scenes/OfficeGarage.unity`) etrafına kurulan küçük kasaba haritası. Sahnenin kendisi bu dalda **değişmedi**; harita menüden kurulur.

![Ofisin önündeki cadde](OfisHaritasi/02_ofis_onu_sokak.png)

## Kurmak

1. Projeyi aç, içe aktarmanın bitmesini bekle.
2. **Rescue Team > Ofis Haritası > Haritayı Kur** (1-2 dk). `OfficeGarage` sahnesini kaydeder.
3. İstersen **Doğrula (simülasyon + görüntü)** ve **Play Modunda Dene**. Raporlar `Logs/OfisHaritasi/` altına yazılır.

**Haritayı Kaldır** her şeyi geri alır. `ProjectBuilder` gibi sahneyi sıfırdan kuran bir komut çalışırsa harita gider; **Haritayı Kur**'u tekrar çalıştır.

Kurucu sahnede şunları yapar:
- `20_OfisHaritasi` adlı tek bir kök ekler (arazi, yollar, binalar, ağaçlar, park etmiş araçlar, trafik).
- Haritanın yerini aldığı eski dış çevre parçalarını (E00 çim, E01 asfalt, E02 çizgiler, E04 bordürler, E11/E12 arka plan tepeleri, iki kaldırım) silmez, yalnız **renderer'larını kapatır**.
- `00_WorldCollision/Ground` kutusunu kapatır, yerine bina tabanına ve ofis önündeki beton döşemeye kutu ekler. Oyuncu dışarıda gerçek zemine basar.
- `OHWorldSettings` Play'de kameranın uzak düzlemini 1500 m'ye çıkarır ve hafif mesafe sisi açar.

Paket, layer ya da proje ayarı eklenmedi.

## İçerik

- **Alan:** oynanabilir kısım yaklaşık 1000 × 700 m (x −500…500, z −420…280), çevresinde tepe kuşağı.
- **Ana Cadde:** ofisin önündeki yol; iki ucu tünellere gider.
- **Sanayi şeridi:** atölyeler, doğu kavşağında benzinlik, batı kavşağında market.
- **Mahalle:** dükkânlı apartmanlar, renkli sıvalı bahçeli evler, park (kaydırak, salıncak, çeşme).
- **Arazi:** hafif tepeler, iki köprülü dere, doğuda tarlalar ve meyve bahçeleri.
- **Yollar:** 12 kavşak; ikisi trafik ışıklı ve yaya geçitli, diğerlerinde tali yol yol verir.
- **Trafik:** 44 araç, 9 tip (hatchback, sedan, taksi, SUV, pikap, panelvan, dolmuş, kamyon, traktör), rastgele renk.
  - Rigidbody + WheelCollider ile sürülür; tekerlekler döner, direksiyon kırar, frende stop yanar.
  - Işıkta durur, kavşakta yol verir, önüne çıkan oyuncuya fren yapıp korna çalar.
  - Oyuncudan 175 m'den uzaktaki araçlar şeride bağlı kayar; yaklaşınca tam fiziğe geçer.
  - Tünele giren araç başka bir tünelden çıkar.
- **Park etmiş araçlar:** 223 araç; çarpınca itilir.

| | |
|---|---|
| ![Batı kavşağı](OfisHaritasi/04_bati_kavsak.png) | ![Doğu kavşağı ve benzinlik](OfisHaritasi/05_dogu_kavsak_benzinlik.png) |
| ![Mahalle sokağı](OfisHaritasi/06_mahalle_sokagi.png) | ![Tepeden manzara](OfisHaritasi/12_tepe_manzara.png) |

## Dosyalar

| Yol | Ne |
|---|---|
| `Assets/OfisHaritasi/Scripts/` | Çalışma anı: trafik ağı, araç (OHVehicle), yönetici, trafik ışığı, motor/korna sesi, dünya ayarı |
| `Assets/OfisHaritasi/Editor/` | Harita tanımı (`OHLayout`: yollar, kavşaklar, dere, tepeler), şerit/kavşak hesabı, yol/arazi mesh'i, yerleşim, kurucu, doğrulama |
| `Assets/OfisHaritasi/Shaders/` | Arazi (köşe renkli faset) ve dere suyu |
| `Assets/OfisHaritasi/Modeller/` | Blender'dan gelen FBX'ler + manifest ve malzeme JSON'ları (araçlar, ticari binalar, konutlar, donatı) |
| `Assets/OfisHaritasi/Malzemeler/`, `Prefablar/` | JSON'dan üretilen URP malzemeleri ve prefab'lar (kurucu yeniler) |
| `Art/Blender/OfisHaritasi/` | Modelleri üreten Blender betikleri, .blend dosyaları, setlerin README'leri |

Harita mesh'leri (`Assets/OfisHaritasi/Uretilen/`, ~28 MB) depoya girmez; kurucu her makinede üretir.

## Ayarlar

`20_OfisHaritasi/Trafik` üstünde:
- `OHTrafficManager.movingCount` (44): araç sayısı.
- `weights` / `maxPerType`: araç tiplerinin oranı ve üst sınırı.
- `physicsEnter` / `physicsExit` (130 / 175 m): fizik mesafesi.

Işık süreleri `OHTrafficNetwork` kavşak kayıtlarında (yeşil 17 sn, sarı 3 sn, tüm kırmızı 2 sn). Haritanın düzeni `Editor/OHLayout.cs` ve `Editor/OHPlacement.cs` içinde; değiştirip **Haritayı Kur** yeterli, aynı tohumdan aynı harita çıkar.

## Modelleri yeniden üretmek

Her set kendi betiğiyle sıfırdan üretilir (Blender 5.2): `Art/Blender/OfisHaritasi/Uretim/build_<set>.py`, `verify_<set>.py`, `render_<set>.py`. Betiklerin başındaki `ROOT`/`KOK` yolu, üretildiği makinedeki klasörü (`C:\Users\uur\RescueTeam_OfisHaritasi`) gösterir; başka yerde üretmek için bunu değiştir. Stil kuralları `Art/Blender/OfisHaritasi/ORTAK_KURALLAR.md`'de: yazı/logo/doku yok, sade mid-poly, düz gölgeleme.

FBX'ler Unity'ye Y ekseni etrafında 180° dönük gelir (Blender +Y = Unity −Z). `OHAssetPrep` model düğümünü çevirip düzeltir; FBX'leri elle döndürme.

## Doğrulama sonuçları (2026-11-08, Unity 6000.5.5f1)

- Edit modu simülasyonu, 180-240 sn, 44 araç: kaza/sıkışma 0, üst üste binme 0, ters duran 0, her araç en az ~1 km yol aldı.
- Play modu, 75 sn: araçlar doğdu, oyuncu ofiste/kaldırımda/yolda zeminde, hata günlüğü boş.
- `Rescue Team > Ofis-Garaj > Doğrula (çok odalı ofis)`: 13/13 geçti, ön kapıdan dışarı çıkış dahil.
- `Company > Validate Play Loop` haritadan bağımsız olarak kalıyor. Test eski proje durumuna göre yazılmış: 6 iş / 9 ekipman, 3000'den fazla etkin ortam renderer'ı ve birinci şahıs gövde bekliyor.

## Bilinen sınırlar

- Yaya yok. Gece aydınlatması yok; lambaların ışık yüzeyi parlar ama ışık vermez.
- Tali yoldan ana caddeye çıkan araçlar yoğunlukta 40-60 sn bekleyebilir.
- Fizikte bordür köşesine takılan araç 5 sn sonra kısa süre raya geçip kurtulur. Devrilen ya da yoldan çıkan araç 20 sn sonra (görünmüyorsa) bir tünelden yeniden çıkar.
- 200 m'den yüksekten bakınca arazinin bittiği yer görünür.
