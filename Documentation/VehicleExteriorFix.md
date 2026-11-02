# Araç iç yüzeyleri, arka kapılar ve dış modül

28 Ekim 2026. Kaynak ofisin tasarımı ve modelleri korunarak mevcut `OfficeGarage.unity` sahnesine uygulandı. Önceki sahne ve ilgili kod `Backups/VehicleExteriorFix` içinde korunur.

29 Ekim güncellemesi: Kullanıcının son isteğiyle dış taşıyıcı yan yüzeyden arka kapıya taşındı. Kapılar 180 derece açılır, yeni basamaklardan yük bölmesine yürünür. Aşağıdaki maddeler önceki iç yüzey/kol düzeltmesinin kaydıdır; güncel konum ve davranış [RearWorkshopRefresh.md](RearWorkshopRefresh.md) içindedir.

## Değişenler

- Kabin gövdesi, camlar ve kapı yüzeylerinin iç tarafları doğru yönde yüzeylerle tamamlandı. Ayak boşluğu, ön bölme ve ön cam direklerinin dönüş yüzeyleri kapatıldı. İç yüzeyler ait oldukları kapıyla hareket eder; kaynak FBX değiştirilmez.
- Arka kapı kaynağında aynı nesne, bir taraftaki panelle karşı taraftaki kolu birlikte içeriyordu. Parçalar artık gerçek üçgen konumuna göre iki kanada ayrılır. Kollar kendi kanadında kalır; ortadaki fitil de bir kanada bağlanır.
- İç yük bölmesindeki modül montaj düzeneği kaldırıldı. Servis kaseti, taşıyıcı, dört cıvata ve bağlantı aracın **yan dış yüzeyine** taşındı. Arka kapılar yalnız yüklemeye hizmet eder; modül takmak için açılmaz.
- Mevcut kayıt formatı korunur. Takılı modül ve yarım kalan cıvata/bağlantı durumu yeni dış yuvaya yüklenir. Lift, gövde ile dış taşıyıcının altındaki oyuncu/eşyayı ayrı hacimlerde kontrol eder.

## Kontroller

Tam Play Mode doğrulamasının dokuz grubu geçti. Son ön cam direği düzeltmesinden sonra garaj testi tekrar geçti. Arka panel ve kolların 3.336 mesh köşesi doğru yarıda; 120 içeriden görünürlük örneği, backface sorguları kapalıyken opak araç yüzeyine çarpıyor. Fiziksel sökme/takma ve yarım montaj kayıt/yükleme, arka kapılar kapalıyken dış yuvada çalıştırıldı. [Sonuçlar ve günlükler](TestResults.md).

## Görüntüler

- [Açık ön kapı ve kabin](CabOpenInteriorFixed.png)
- [Açık arka kapılar ve boş yük bölmesi](CargoOpenInteriorFixed.png)
- [Tek arka kapı açıkken kollar](RearDoor_Right_HardwareFixed.png)
- [Dış modülün konumu](ExteriorModuleMounted.png)

[Elle montaj adımları](ManualGarageSystems.md).
