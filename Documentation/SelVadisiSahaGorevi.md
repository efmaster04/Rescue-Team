# Sel Vadisi saha görevi

Karadeniz'de selin bastığı büyük köy haritası (Sel Vadisi – Büyük Harita) oyuna saha görevi olarak eklendi. Ofisteki bilgisayarda İşler → İlanlar'da **Sel Vadisi: Mahsur Kalanlar** görünür: İl Afet Koordinasyon Merkezi, 64 km, ₺1.900, zorluk 4, risk 4. Araçta Nakliye Kasası modülü ve kurtarma halatı gerekli; sedye ve ekipman çantası önerilir. Haritada araç ve tekne yoktur. Görev, mahsur kalanları kıyıdan bulup telsizle bildirmektir.

## Haritayı üretme

Harita kodla üretilir. Arazi, akan sel suyu, 178 ev, köprüler, ağaçlar, eşyalar, sürüklenen enkaz, 217 köylü, yağmur ve ışık bu üretimin parçasıdır. Sahne ve yaklaşık 220 MB mesh Git'e girmez (`.gitignore`). Her makinede yeniden üretilir: haritanın kendisi yaklaşık 30 saniye, içe aktarma ve görev kurulumuyla birlikte açık editörde yaklaşık 1 dakika sürer.

- Proje harita olmadan açılınca Unity bir kez sorar. Harita var ama saha görevine çevrilmemişse (görev nesnesi yok) de sorar.
- Elle üretmek için: `Rescue Team > Sel Vadisi > Haritayı Yeniden Üret ve Görevi Kur`. `Saha Görevini Kur` haritayı yeniden üretmez; yalnız oyuncuyu, kamerayı, masayı, görevi ve iş kaydını kurar.
- `Tools > Sel Vadisi` menüleri (`BENIOKU.md`) bağımsız harita projesi içindir. Oyunun içinde onlar da `Haritayı Yeniden Üret ve Görevi Kur`'a yönlenir (`SVBuild.HostRebuild`). Böylece görevsiz uçuş haritası görev sahnesinin üstüne yazılmaz.
- Sahne her makinede aynı GUID'le üretilir (`SVBuild.HostSceneGuid`). Bu yüzden depodaki Build Settings kaydı makineye göre değişmez.
- Harita üretilmeden göreve çıkılırsa oyun "Saha sahnesi bulunamadı" uyarısı verir ve görev başlamaz.

## Oynanış

1. İşi kabul edin, kurtarma halatını araca yükleyin ve göreve çıkın. Sahne, vadinin kenarındaki kurtarma alanında açılır.
2. Çatılarda ve balkonlarda el sallayan aileleri bulun. 75 m içindeki bir aileye bakıp E'ye basınca aile telsizle bildirilir ve üstünde turuncu bir işaret yanar. Aileyle aranızda ev ya da yamaç varsa bildirilemez: kıyıda yer değiştirip ailenin en az bir üyesini açıkça görün.
3. Sekiz aile bildirilince kurtarma alanındaki koordinasyon masasına dönün ve E ile raporu verin. Görev biter. Enter ile ofise dönülür; rapor ve ödeme İşler → Raporlar'dadır.
4. Ayak hizasında 0,75 m'den derin suya giren oyuncu kurtarma alanında yeniden doğar. Sert düşüp yere yığılan oyuncu 4 saniye sonra kurtarma alanında kalkar.

Sol üstteki panel bildirilen aileleri, kişi sayısını ve geçen süreyi gösterir.

**Hızlı müdahale** sözleşmesi +₺260 öder. Raporu sahada 15 dakika içinde verirseniz koşul sağlanır. Oyundaki bütün sözleşmeler aşınmayla ölçülür (`CompanyRuntime`). 64 km'lik yol tek başına %19,2 araç aşınması getirir ve sahadaki her dakika %0,25 ekler. Bu yüzden bu işin araç aşınması sınırı %23'tür; varsayılan %10 sınırı hiçbir zaman sağlanamazdı. Sınır, `SelVadisiMission.QuickResponseMinutes` değerinden hesaplanır.

## Kod

| Dosya | Görev |
| --- | --- |
| `Assets/Scripts/Mission/IFieldMission.cs` | Saha görevi arayüzü ve `FieldMission.Current`. `FieldMissionTracker` artık yalnız Bölüm 1'in `MissionDirector`'ına bağlı değildir. |
| `Assets/Scripts/Mission/SelVadisiMission.cs` | Aileleri bulma ve bildirme, işaretler, masa raporu, derin su ve görev paneli. |
| `Assets/Scripts/Mission/SelVadisiShadowRange.cs` | Görev sırasında URP gölge mesafesini genişletir ve çıkışta geri alır. URP ayarı ofisle ortaktır. |
| `Assets/Editor/SelVadisiMissionSetup.cs` | Haritayı gerekirse üretir. Bölüm 1'in oyuncusunu ve kamerasını kopyalar; masayı, görevi, iş kaydını ve Build Settings'i kurar. |
| `Assets/Editor/SelVadisiAutoSetup.cs` | Harita yoksa ilk açılışta bir kez sorar. |
| `Assets/Editor/SelVadisiVerify.cs` | Doğrulama (aşağıda). |
| `Assets/_Project/Scripts/Core/CatalogAddon.cs`, `CompanyCatalog.cs` | İş, `OfficeGarage.unity` değişmeden `Resources/CompanyCatalogAddon.asset` üzerinden kataloğa eklenir. |
| `Assets/SelVadisi` | Harita üreticisi (`Editor/SVBuild*.cs`), çalışma anı betikleri (su, köylü animasyonu, yağmur), gölgelendiriciler ve `BENIOKU.md`. |

Oyunun içinde (`SVBuild.InHostGame`) harita, ofisle ortak URP ayarının gölge mesafesini değiştirmez. Build Settings'te sona eklenir; ofis ilk sahne olarak kalır.

## Doğrulama

`Rescue Team > Sel Vadisi > Doğrula` (`SelVadisiVerify.Run`) raporunu `Captures/SelVadisi/verify_report.txt` dosyasına yazar. Üç bölümü sınar:

- Veri: iş, ek katalog, Build Settings, sözleşme sınırı, sahne GUID'i; `Tools > Sel Vadisi` menüsünün görev kurulumuna yönlenmesi ve sahnenin görev sahnesi olması.
- Sahne: tek kamera ve dinleyici, doğma noktasının altında zemin.
- Play Mode: oyuncu doğduğu yerde durur. 36 ailenin hepsi kıyıdan bildirme menzilindedir (en yakın 18 m, ortanca 41 m, en uzak 70 m). Görüş hattıyla, en yakın kuru noktadan 4 aile, kıyıda dolaşınca 23 aile görülür (gerekenin en az iki katı istenir); öbürleri evlerin arkasında kalır. Sekiz aile görüldükleri noktadan bildirilir ve masada rapor verilir. `FieldMissionTracker` köprü oturumunda sonucu hesaplar. Derin suya giren oyuncu kurtarma alanında doğar.

6 Kasım 2026 koşusu: **SELVADISI VERIFY OK**. Bu makinede Unity 6000.6.3f1 kurulu olmadığından koşu 6000.5.5f1'de yapıldı. Paket listesi ve `ProjectSettings/DynamicsManager.asset` yalnız yerelde değiştirildi; bu değişiklikler depoya girmedi. 6000.6'nın fizik ayarı 6000.5'te yanlış okunur ve CharacterController zemine çarpmaz. Bu yüzden proje 6000.6.3f1 ile açılmalıdır. İnsanla serbest oynanış yapılmadı.
