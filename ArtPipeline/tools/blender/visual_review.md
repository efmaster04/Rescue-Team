# Rescue Crew v2 — Görsel ve teknik kontrol

- Tur 1: v1 ön/arka render'ları ve iki referans açıldı; oranlar, kubbe/kaburgalar, eldivenler, botlar, cepler ve ağırlık kaynağı yenilendi. Yeni ön/yan/arka/üç çeyrek render'ları incelendi.
- Tur 2: taşan el kadrajı, kaybolan ense bandı, koyu stüdyo zemini ve fazla dışarı çıkan taban dişleri düzeltildi; ön/arka/üç çeyrek yeniden açıldı.
- Tur 3: gövde genişletmesinin el/manşetleri bozması giderildi; pozlama, ifadeler, varyantlar ve geçici subdivision hero üretildi. Dokuz görsel değerlendirildi.
- Son rötuş: yaka açıklıkları kapandı; varyantların dış elleri kadraja alındı; taban dişleri tabana gömüldü; pose_test yere oturtuldu. Ön, arka, varyant ve hero yeniden açıldı.

## Kabul

- build_rescue_crew.py: exit 0 (iki ardışık tam üretim).
- verify_fbx.py: exit 0; 9706 üçgen, 1.5 m, 3 skinned mesh, 31 kemik, 9 shape key.
- verify_presentation.py: exit 0; 9 PNG ve boyutları, AgX/Cycles, nötr rest pose, ortak kaynaktan kemik konumları, kayıtlı subdivision bulunmaması doğrulandı.
- İki üretimde aynı geometri/ağırlık/shape-key SHA256: e88a7d396dfdd88776139cd58c5ac8c2c0ddd043c4bdac685eb56fad2f99d0b6.
- Render: OPTIX: NVIDIA GeForce RTX 5080, 64 sample (hero 96), denoiser; tam set 45.55 saniye.
- Oranlar: kafa+kask %39,33; omuz–kalça %29,00; bacak+bot %31,37. Sınırlar stats.json içinde ölçülür.
- Hero subdivision yalnızca render sırasında eklenip finally bloğunda kaldırılır; FBX ayarları v1 ile aynıdır.

## Görsel sınırlar

Konseptteki kumaş kıvrımları sadeleştirilmiştir; oyun geometrisinde omuz/eldiven ve dizlik siluetleri hâlâ daha köşelidir. Eller ve kol açıklığı referanstan biraz daha geniştir. Unity içinde animasyon testi bu görevin kapsamına alınmadı.