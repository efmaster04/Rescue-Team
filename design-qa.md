# Arayüz görsel kontrolü — 29 Ekim 2026

İkinci taslak önceki uygulamaya temel olmuştu. Kullanıcının daha sonraki sadelik ve okunurluk isteği güncel düzenin esasını oluşturur. Eski sol bölüm menüsüne birebir benzerlik kabul ölçütü değildir. Önceki değerlendirme `Documentation/Backups/RearWorkshopRefresh/design-qa-before.md` içinde korunur.

## Güncel Unity çıktıları

- `Documentation/ComputerJobsPreview.png`: 1600×900 iş fişi.
- `Documentation/ComputerJobs720Preview.png`: 1280×720 aynı iş ve işlem alanı.
- `Documentation/ComputerJobs1080Preview.png`: 1920×1080 çıktı.
- `Documentation/ComputerShopPreview.png`: ürün listesi, gerçek prefab önizlemesi ve sabit sipariş alanı.
- `Documentation/ComputerReportPreview.png` ve `ComputerReport720Preview.png`: kayan rapor ayrıntıları ile sabit toplam/tahsilat alanı.
- `Documentation/ComputerDesktopPreview.png`: yedi uygulamalı masaüstü.

## Değişiklikler ve görsel sonuç

Uygulama içindeki sol bölüm sütunu kaldırıldı. Başlığın altındaki yatay sekmeler 21 px yazı ve ince mavi seçim çizgisi kullanır. İş listesi ile ayrıntı alanına daha fazla genişlik kaldı. Farklı görev çubuğu ve bölüm sekmesi davranışları korunur.

İş fişindeki müşteri, konum, mesafe ve süre iki sütunlu düz bilgi alanındadır. Ayrı dekoratif kutular yoktur. Ana başlık 34 px, açıklama ve ekipman 22 px, tablo içeriği 20 px oldu. Kaynak aile Source Sans 3 Regular/Semibold; Türkçe karakterler ve ₺ aynı gerçek fonttan gelir. Ana ve ikincil yazı kontrastı yükseltildi.

1280×720 ve 1600×900 çıktılarında iş bedeli, sözleşme kutusu ve kabul düğmesi aynı görünür alanda incelendi; kesilmiyor. Mağazanın ürün listesi ve ayrıntısı ayrı kayar, sipariş düğmesi alt bölümde kalır. Gerçek model önizlemesi ürün adından ve fiyatından ayrışır. Küçük çözünürlükte yardımcı yazı daha küçük görünür; ana işlemler ve ekipman listesi okunabilir kalır.

Bütünleşik kontrolde büyüyen satırlar rapor tahsilat düğmesini görünür alanın dışına itti. Toplam tutar ve tahsilat işlemi sabit alt bölüme alındı; rapor ayrıntıları ayrı kayar. Tahsilat, gerçek pointer tıklaması ve tek seferlik ödeme kontrolüyle tekrar sınanır.

Masaüstünde uygulama başlatıcıları ve alt çubuk vardır. Karşılama sloganı, dekoratif grafik, neon, hologram veya gereksiz bilgi kartı eklenmedi. Dünya etkileşim yazısı 19 px oldu; cıvata işleminde E tuşu ve basılı tutma açıklaması birlikte gösterilir.

## Etkileşim doğrulaması

Unity Play Mode testleri yedi uygulamayı, bölüm gezintisini, gerçek iş/sözleşme seçimini, siparişi, rapor ödemesini ve kayıt akışını denetler. Sürükleme, çift tıklamayla büyütme/geri alma, görev çubuğundan küçültme/geri açma ve çıkış da EventSystem olaylarıyla çalıştırılır. Güncel sonuç `Logs/rear_workshop_full_validation.txt`, kapsam `Documentation/TestResults.md` içindedir.

Bu değerlendirme Unity çıktılarının görsel incelemesi ve otomatik girdi testleridir; insanla kullanılabilirlik oturumu yapılmadı. Ekran görüntülerindeki FPS değerleri yakalama yükünü de içerir ve bağımsız oyun performansı ölçümü değildir.
