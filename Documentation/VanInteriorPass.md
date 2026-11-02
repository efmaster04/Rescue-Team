# Kabin ve yük bölmesi

29 Ekim 2026. Araç içindeki düz bloklar ve yük bölmesine taşan koltuk sırtları yenilendi. Gövde, park konumu, kapı menteşeleri, dış arka modül yuvası ve ofis tasarımı korunur.

- İki koltukta yuvarlatılmış minder, ayrı kumaş orta bölüm, yan destek, dikiş, başlık ayakları, emniyet kemeri ve tokalar bulunur. Koltuklar kabine çekildi; arka yüzleri yük bölmesi duvarının önünde kalır.
- Direksiyon yuvarlak kesitli çember, üç kol ve direksiyon kolonundan oluşur. Panelde iki sade analog kadran, havalandırma ızgaraları ve düğmeli radyo yüzü vardır. Vites kolu tabana bağlanır. Kadranlar ve radyo görsel ayrıntıdır.
- Yük alanının duvarları ve tekerlek kutuları ahşap kaplama, tabanı küçük kabartmalı koyu kontrplaktır. Altı alçak sabitleme halkası ve iki yan yük rayı vardır. Açık renk tavan kaplaması ile beş tavan takviyesi iç hacmi belirginleştirir.

Kumaş, ahşap ve zemin için 512 px boyutunda dokuz deterministik doku `Art/generate_vehicle_cabin.py` ile üretilir. Özgün modeller ve dokuları değiştirilmez. Mevcut koltuk/direksiyon/panel renderer'ları kullanılır; yeni küçük ayrıntılar tek ek MeshRenderer içinde birleştirilir. Alt malzemeler ayrı çizim gerektirebilir. Yeni collider veya gerçek zamanlı ışık eklenmez. Tavan malzemesindeki düşük sabit dolgu, kapalı iç hacimdeki aydınlatma eksikliğini yumuşatır.

## Unity görüntüleri

[Kabin](VanInteriorAfter_Cab.png), [panel ve direksiyon](VanInteriorAfter_Dashboard.png), [yük bölmesi](VanInteriorAfter_Cargo.png).

Önceki Play Mode görüntüleri `Backups/VanInteriorPass` klasöründedir. Eski görüntüde yük bölmesine taşan iki koltuk sırtı görülebilir. Yeni gerçek Play Mode görüntüleri `CabOpenInteriorFixed.png` ve `CargoOpenInteriorFixed.png` dosyalarıdır.

## Uygulama

`Company > Refine Van Cabin and Cargo Lining` mevcut sahneyi ve tekrar kullanılabilir van prefabını günceller. Genel araç/atölye ayrıntı kurulumları da bu iç mekânı çağırır. `Temp/CompanyUIValidation.request` içine `vehicle-interior` yazmak uygulama ile tam Play Mode kontrolünü, `interior-preview` yalnız uygulamayı çalıştırır.

Sahne ve önceki araç ayrıntı kurulum kodu `Documentation/Backups/VanInteriorPass` içindedir. Testler şirket kaydını yedekleyip geri yükler. Gerçek saha sürüşü bu görsel düzenlemenin kapsamında değildir.

## Doğrulama

`Logs/van_interior_full_validation.txt`: 10 PASS ve Validation complete. Yeni koltuklar ve tek ek iç ayrıntı renderer'ı geometri koşullarını geçti. 227 dış yüzey ve 120 iç yüzey ışın örneği, arka kolların kapı yarısı sahipliği, gerçek E ile araç kapıları, gerçek W ile arka erişim ve yük tabanına giriş/çıkış, modülsüz lift ve yarım modül kilidi, elle montaj, yeni yedek lastiğin alınması/bırakılması, bilgisayar ve kayıt döngüsü geçti. Son Play Mode kabin/yük görüntüleri açılıp incelendi.

Kaynak denetiminde 2.887 dönüşüm ve 989 korunan ofis mesh/malzeme bağlantısı değişmedi. Kaynak ortam FBX dosyası aynı SHA-256 özetini korur. Test bittiğinde Play Mode kapatıldı ve şirket kaydı geri yüklendi.

Tam koşudan sonra genel araç ayrıntı komutu tekrar uygulandı. `Logs/van_interior_reapply_check.json` içinde 26 araç/iç mekân mesh ve malzeme varlığının SHA-256 özetlerinin aynı kaldığı görülür; tekrarlanan kurulum geometri veya malzemeyi kaydırmadı. Sahne kaydedildi ve şirket kaydının özeti de değişmedi.

`Logs/van_interior_performance.json`: aynı açıdaki statik gruplama kontrolünde 3.360 renderer, CPU render gönderim medyanı 7,03 → 7,37 ms. Bu koşuda hızlanma görülmedi; bağımsız derlemede FPS/GPU ölçümü yapılmadı. Yeni ayrıntılar araçla birlikte hareket ettiğinden statik gruplamaya alınmaz.
