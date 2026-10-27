# Trafik ticari araçları — görsel inceleme

Kaynak: `ArtSource/Traffic/references/01_traffic_lineup.jpg` alt sıra; pah ve detay dili: `02_minivan_style_match.png`.
Ölçü kaynağı çizim değil, 2026-10-15 brief'idir. Blender metre / Z yukarı / burun -Y. Unity aktarımı kapsam dışıdır.

## round_01 — ilk görsel tur

- Panelvanın yüksek, kapalı yük bölümü ve yalnız ön kapılardaki camlar doğru. Ön üç çeyrek, sol ve arka görüntüler incelendi.
- Kabin içindeki iki koltuk, direksiyon ve tek yüzsüz sürücü okunuyor; silindirik kafa fazla konik görünüyor. İkinci turda pahlı baş kullanılacak.
- Panelvan ve kamyon ön köşelerinde siyah üçgenler var: kaput yan poligonu alt yan panelin bandına taşıyor. Poligon `clip_band` ile ortak üst sınıra kırpılacak.
- Lastik ilk örneği `#3F3F54` gereğinden fazla mavi. Aynı alt sıra kamyonun daha nötr lastik pikseli ikinci tur için seçilecek.
- Kamyon tek uzun şasi, kısa lacivert kabin, yüksek kırık beyaz kasa ve arka tandem aks olarak okunuyor. Çizimden daha uzun görünmesi 9.50 m sözleşmesinden kaynaklanıyor.
- Kasa yan profilinin dış yüzü kasa düzlemiyle çakışıyor; kasa levhası 6 mm içeri alınacak. Arka stoplara şasiye bağlı sade taşıyıcı yüzey eklenecek.
- İlk stüdyo denemesinde büyük kamyon kadrajının köşesi koyu kaldı; karanlık kare kontrolü tekrarları tetikledi. Ortam aydınlatması yükseltildi. Koruma ve OIDN kapalı yeniden render yolu korunuyor.

## round_02 — planlanan düzeltmeler

Ön köşe kırpma, kasa kenarlarında yüzey ayrımı, nötr lastikler ve pahlı sürücü başı uygulanacak; son render incelemesi ve kabul sonuçları burada tamamlanacak.

Tur 02 kapanışı (claude, Codex limitinden devralma):
- Kök neden: son kaynak düzenlemesinden sonraki build, FBX export'undan sonra render sırasında kesilmişti; FBX yeni, `stats_trucks.json` eski kaldı.
- Yeniden build sonrası verify yeni bir hata buldu: `clip_band` kırpma çizgisinde doğrusal ara noktalar bırakıyordu, yan levhalarda sıfır alanlı üçgen oluşuyordu (her gövdede 4). Doğrusal noktalar atıldı (her araçta −16 üçgen).
- PanelVan üç çeyrek görünüşünde tavan kadraj dışında kalıyordu; ortho ölçeği yüksekliğe göre de genişletildi.
- Son durum: PanelVan 4832 (Body 2124, Interior 664, Glass 60, Wheel 4×496), BoxTruck 7084 (Body 2760, Interior 664, Glass 60, Wheel 6×600). İki ardışık build aynı SHA256 (`76f891e9…`), verify OK. Render'larda siyah leke, karanlık kare veya eksik parça yok.

## Konseptten sapmalar ve açık sorular

- Kaynak çizimde araçlar üst üste biniyor ve tam yan/arka görünüş yok. Arka sabit çift kapı, sade stop yerleri ve iç renkler nötr tamamlandı; yazı, logo, yük veya açılır parça eklenmedi.
- Kamyonun dingil yerleri ve lastik yarıçapı brief'te sayısal verilmedi: ön -3.35 m, arka +2.05/+3.35 m, yarıçap 0.57 m seçildi. Panelvan ±1.70 m, yarıçap 0.43 m. Kök pivot temas dikdörtgeninin merkezidir.
- Cam için Unity URP saydam materyal ayarı meta'da açıklanır; oyun motorunda son görünüm bu işte sınanmadı.
- Kullanıcı kararı bekleyen açık soru yok.
