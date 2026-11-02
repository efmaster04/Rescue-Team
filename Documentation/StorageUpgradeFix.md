# Ek depo rafının ofise taşması

1 Kasım 2026. “Ek depo rafı” geliştirmesi bütün `G01_` parçalarını kopyalıyordu. Bu önek tek rafı değil üç bölmelik garaj raf sırasını içerdiğinden, grup merkezini hedefe taşımak iki bölmeyi ofisin içine sokuyordu. Görselde masanın önünü kapatan metal raf kaynak ofis mobilyası değil bu geliştirme kopyasıydı.

Geliştirme artık ilk raf bölmesinin kompakt sürümünü kullanır. Rafın merkezi garajın batı duvarında `(0,50; 0,91; -1,50)` olur; yatay kapladığı alan yaklaşık X `0,21–0,79`, Z `-2,13–-0,87` metredir. Ofis, kapı önü, mevcut bitki, kepenk ve ana erişim yolu dışında kalır. Rafın tek BoxCollider'ı görünen bölmeyle eşleşir.

`UpgradeVisual.PrepareLayout()` satın alma ve kayıt yükleme sırasında, görsel etkinleştirilmeden/statik gruplama yapılmadan önce uygulanır. Eski şirket kaydındaki “storage” sahipliği ve ücret bilgileri değişmez. Genel sahne kurucusu da aynı düzeltmeyi uygular. Açık sahnedeki hatalı kopya tek doğru bölmeyle değiştirildi; özgün ofis ve araç nesneleri değiştirilmedi.

[Önce](UrgentShelfBefore.png), [düzeltilmiş ofis](UrgentShelfAfter.png), [garajdaki raf](UrgentShelfGarageAfter.png). `Logs/urgent_shelf_layout_regression.txt` eski üç bölmeli düzeni yeniden oluşturarak tek bölmeye indirmeyi, ofis/yol açıklığını ve collider eşleşmesini doğrular. Bütünleşik Play Mode testi ve son hedefli garaj testi satın alma ve yükleme sonrasında da konumu kontrol eder.

Kaynak denetimi 2.643 özgün dönüşümü kontrol eder. Eski sayıya yanlışlıkla dahil edilen 244 `(Clone)` geliştirme parçası özgün ortam modeli olarak sınıflandırılmaz; bunların yerleşimi ayrı raf regresyonuyla sınanır. 989 korunan ofis mesh/malzeme referansı ve kaynak ortam FBX'i korunur.
