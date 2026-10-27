# Sivil trafik araçları — görsel inceleme

Tarih: 2026-10-15. Yazan: codex (trafik-binek orkestra işçisi).
Kaynaklar: `ArtSource/Traffic/references/01_traffic_lineup.jpg` alt sıradaki ilk üç araç ve `02_minivan_style_match.png`. İki kaynak görsel doğrudan açılıp incelendi; yalnız stil ve renk kaynağı olarak kullanıldı. Ölçüler brief'ten alındı.

## Tur 01 — ilk üretim ve görsel bulgular

- `renders/cars/round_01/`: üç araç için ön üç çeyrek, sol ve arka üç çeyrek; lineup, sekiz boya örneği, minibüs ölçek karşılaştırması ve beş karşılaştırma panosu. Her PNG 1920×1080.
- Sedan 4.532, Hatchback 4.532, Pickup 4.908 üçgen. Hardal / adaçayı / bordo gövde ayrımı ve minibüsün pahlı, düz normal dili korunuyor. Sürücü camdan seçiliyor. Pikap tek kabinli ve iki bağımsız balyalı.
- Yakın görünüşlerde tavan altı ve ön/arka cam kenarlarında açıklıklar; kabin içinden görünen açık tekerlek kemerleri; hatchback arka kapısında fazla yüksek alt çizgi; pikap alt arka panelinde eksik yüzey görüldü. Füme ilk örneği morumsu kaldı.
- İlk yan renderda köşe parlaklık kontrolü gri uzak zemini karanlık kare olarak reddetti. Kontrol eşiği korunarak stüdyo zeminine yumuşak emisyon eklendi; turun tamamı yeniden alındı. Karanlık kareye karşı üç deneme ve OIDN'siz 192 örnek fallback korunuyor.
- Tur 01 tamamlanan kamera render süresi: 315,000 saniye. İlk başarısız deneme bu toplama dahil değildir.

## Tur 02 — uygulanan düzeltmeler

- Camlar sütunların iç sınırına genişletildi; tavan altı eşiği eklendi. Tekerleklerin arkasına koyu davlumbazlar ve orta taban eklendi.
- Arka kapı alt çizgisi kemerin dışından geçecek şekilde düzenlendi. Hatchback arka tavanı uzatılarak daha küt bir arka siluet yapıldı.
- Pikap alt arka paneli ve kabin arka alt duvarı kapatıldı. Kasa üstü açık kaldı; balya adları, taban pivotları ve gövdeye bağlı kayışlar korundu.
- Füme `#545252` ile nötrleştirildi. Kapı kolları gövde ölçüsüne çekildi. Sol ve arka kameralar gerçek ortografik yan/arka görünüş oldu.
- `TrafficCars.blend` dosyasında araçlar inceleme için yan yana yerleştirilir. Her FBX ayrı olarak sıfır kök konumunda export edilir; meta koordinatları köke göredir.
- Tur 02 kapanışı (claude, Codex limitinden devralma): son kaynak düzenlemesinden sonraki build, FBX export'undan sonra render sırasında kesilmişti; FBX yeni, `stats_cars.json` eski kaldı. Build iki kez yeniden çalıştırıldı (aynı SHA256 `663de5e8…`), verify OK. Render'larda siyah leke, karanlık kare veya eksik parça yok.

## Teslim sözleşmesi

| Araç | Body | Interior | Glass | Wheel FL / FR / RL / RR | Hay 01 / 02 | Toplam |
|---|---:|---:|---:|---:|---:|---:|
| Sedan | 2656 | 552 | 72 | 424 / 424 / 424 / 424 | — | 4976 |
| Hatchback | 2656 | 552 | 72 | 424 / 424 / 424 / 424 | — | 4976 |
| Pickup | 2656 | 552 | 48 | 424 / 424 / 424 / 424 | 164 / 164 | 5280 |

- Boya varyantları sırası: Mustard `#95713C`, Sage `#506056`, Burgundy `#4D2731`, Charcoal `#545252`. Varsayılanlar Sedan=Mustard, Hatchback=Sage, Pickup=Burgundy. Pikabın yalnız bordo varyantı var.
- Renkler 5×5 medyan ile örneklendi; örnek piksel koordinatları meta dosyalarında. Füme, konseptte füme boyalı araç bulunmadığı için alt sıradaki nötr siluet renginden seçildi. Hex → lineer Base Color; FBX DiffuseColor üzerinde ek sRGB düzeltmesi yok.
- Trim `#85868A`, Tire `#292C30`, Rim `#777A80`, Glass `#A3BBC0`; cam ayrı mesh, alpha 0.30. Tüm malzemelerin hex değerleri her aracın meta dosyasında.
- Ölçü hedefleri U×G×Y: 4.50×1.80×1.45; 4.00×1.75×1.50; 5.30×1.90×1.85 m. Aynalar ölçü dışında. Burun -Y, sol +X, yukarı +Z, tekerlek dönüş ekseni +X.
- Pikap kasa tabanı Z=0.86 m. Balya taban merkezleri (0,0.93,0.86) ve (0,1.96,0.86); ana gövde ölçüsü XYZ=(1.30,0.87,0.62) m. Lif çizgileri X zarfını toplam 0.01 m büyütür.

## Konseptten bilinçli sapmalar / açık sorular

- Konseptin kısa, abartılı oranları yerine verilen metre ölçüleri kullanıldı; araçlar konseptten daha uzun ve alçak görünür. Tek parçalı sade tamponlar ve daha az yüzey çizgisi minibüsün üretim diliyle eşleştirildi.
- Saman balyaları dokusuz, pahlı iki blok ve birkaç lif çizgisiyle temsil edilir; ek yük yok. Sürücü yalnız kafa+gövde, rig ve yüz yok. Arka koltuk eklenmedi; brief yalnız ön koltukları ister.
- Açık soru/blokaj yok. Belirtilmeyen aks aralıkları simetrik ve metadata'da açık; plaka boş dikdörtgen, logo/yazı yok. Unity materyal kopyalama ve URP şeffaf cam ayarı bu işin kapsamı dışında.
- Git komutu çalıştırılmadı. Doğrulayıcı çalışma ağacındaki dosyaların içerik hash'i, boyutu ve zaman damgasını önce/sonra karşılaştırır; yalnız stdout ve sistem TEMP'ine rapor yazar.
