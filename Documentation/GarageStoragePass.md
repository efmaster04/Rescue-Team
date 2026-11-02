# Garaj kasaları ve parça kutuları

31 Ekim 2026. Garaj raflarındaki kutuların tekrarlanan basit yüzeyleri yenilendi. Ofis tasarımı, rafların konumları, aracın park yeri, yürüme yolları ve fiziksel çarpışma düzeni korunur.

## Modeller

- Dokuz kapalı kasada ayrı gövde, contalı kapak, üstte katlanmış taşıma kolu, yan tutamaklar, küçük ayaklar, ön metal kilitler ve arka menteşeler bulunur. Mavi gri, sıcak gri ve koyu gri olmak üzere üç plastik ailesi kullanılır. Kaynak kasaların taban yüksekliği, ölçüleri ve açılı yerleşimi kaynak mesh üzerinden okunur.
- Altı açık ağızlı stok kutusu ile beş bölmeli parça kutusunda ince duvar, gerçek iç boşluk, taban ve ayırıcı bulunur. İçteki somun ve pullar delikli geometridir; birkaç yatık bağlantı parçası eklenir. Etiket görünümünde yeni slogan, yapay yazı veya rastgele barkod üretilmez.
- Raf üzerindeki modeller dekor olarak kalır. Asıl taşınabilir ekipmanın ve satın alınan modüllerin görselleri, kimlikleri ve elle montaj davranışı bu turda değiştirilmez.

190 kaynak dekor renderer'ı kapatılarak yeni modeller dört mekânsal grupta birleştirildi: arka sol raf, arka sağ raf, yan raf ve zemindeki büyük kasa. Her grubun birden fazla alt malzemesi vardır; dört çizim çağrısı veya belirli bir FPS artışı anlamına gelmez. Yeni collider, gerçek zamanlı ışık, ses veya çalışma anında çalışan davranış eklenmez. Kaynak FBX ve ofis malzemeleri değiştirilmez.

## Unity görüntüleri

[Önce — yan raf](StorageDetailBefore_SideShelf.png), [sonra — aynı açı](StorageDetailAfter_SideShelf.png).

[Önce — arka raf](StorageDetailBefore_RearShelf.png), [sonra — aynı açı](StorageDetailAfter_RearShelf.png).

[Garaj geneli](StorageDetailAfter_Wide.png), [Play Mode oyuncu kamerası](StorageDetailPlayView.png). Görseller gerçek Unity modellerinden alınır; son görüntüler açılarak incelenir.

## Yeniden uygulama

`Company > Refine Garage Storage Cases` mevcut sahneyi günceller. Genel atölye model ve yüzey kurulumları da aynı depolama modellerini uygular. Açık editörde `Temp/CompanyUIValidation.request` içine `storage-detail` yazmak uygulama ve tam Play Mode kontrolünü, `storage-preview` yalnız görsel düzenlemeyi çalıştırır.

Önceki sahne, atölye kurulum kodu, garaj kontrolü ve test raporu `Documentation/Backups/GarageStoragePass` içindedir. Model listesi ve kaynak taban ölçüleri `Logs/garage_storage_geometry.txt` dosyasına yazılır.

## Doğrulama

`Logs/garage_storage_full_validation.txt`: 10 PASS ve Validation complete. Kapı geçişleri, 16 çekmece, arka basamaklardan gerçek W ile giriş/çıkış, lift ve modülsüz kullanım, elle modül montajı, ekipman taşıma, bilgisayar ve şirket kayıt döngüsü geçti. Arka rotanın düşük FPS denemesi `Logs/garage_storage_rear_walk.txt` içindedir. Önceki turda bir kez görülen arka yürüyüş zaman aşımı bu koşuda tekrarlanmadı; önceki neden belirsizliği korunur.

Kaynak denetiminde 2.887 dönüşüm, 989 ofis mesh/malzeme bağlantısı ve ortam FBX SHA-256 özeti değişmedi. Şirket kaydı test sonunda aynı SHA-256 özetiyle geri geldi; durum `Logs/garage_storage_finish_state.json` içindedir. Son modeller, geniş açı ve gerçek Play Mode oyuncu kamerasında açılıp incelendi.

Tekrar uygulama denetiminde 12 mesh/malzeme varlığının SHA-256 özeti aynı kaldı (`Logs/garage_storage_reapply_check.json`). Sahne kaydedildi. Yeni modellerin tamamı çalışma anında yeni davranış çalıştırmadan mevcut statik gruplama düzenine katılır.

`Logs/garage_storage_performance.json`: 1600×900 aynı açı statik gruplama A/B ölçümünde 2.958 renderer, CPU render gönderim medyanı 9,47 → 8,13 ms. Bu sayılar bağımsız oyun FPS veya GPU sunum ölçümü değildir. Önceki sürümlerle farklı koşuların süreleri üzerinden FPS artışı iddia edilmez.
