# Ofis ve garaj sistemleri

## Uygulanan akış

`Assets/_Project/Scenes/OfficeGarage.unity` kaynak Blender binasının ofis, garaj ve depo bölümlerini kullanır. Birinci şahıs çalışan, bilgisayar, iki kapı, görev panosu, tamir tezgâhı, özgün araç liftleri, kaynak sahnedeki V01–V22 servis vanı ve fiziksel ekipmanlar aynı sahnededir. Van, kaynakta bulunduğu garaj park konumundadır; önceki üretilmiş kamyon sahnede kullanılmaz.

İşler'de kabul edilen iş `CompanyRuntime` içindeki tek aktif iş durumuna geçer. Hazırlık kontrolü gerçek yükü, fiziksel modülün dört cıvata ve bağlantısını, kapıları ve lifti okur. `ManualModuleSystem` satın alınan modülü teslim eder; oyuncu taşıyıp aracın arka dış yüzeyindeki yuvaya yerleştirir, `ModuleFastener` ile E basılı tutarak dört cıvatayı sıkar ve bağlantıyı takar. Eksik montaj yetenek kazandırmaz. Özgün vanın gövdesi korunur; servis kaseti arka kapıyla hareket eden dış taşıyıcıya takılır ve uzmanlık küçük tavan sinyaliyle belirtilir. Montaj sırasında taşıyıcının bağlı olduğu arka kapı kapalı olmalıdır. [Elle modül montajı](ManualGarageSystems.md).

Özgün V01–V22 vanın kaportası, farları ve arka kapı donanımı korunur. Açılabilen kapılara ait cam, kol, ayna ve kaporta parçaları menteşeyle birlikte hareket eder. İç yüzeyler ayrı kaplamalarla tamamlanır; kaynak ofis/garaj FBX'i değiştirilmez. Birinci şahıs karakter modeli sahnede gölge üretir, fakat kameranın içinde çizilmez.

Ofisin kaynak modeli ve yerleşimi değiştirilmedi. Kaynak `A15` kapı kolu parçaları ile ön kapı duyurusu hareket eden kapı kanadına bağlandı. Duvar, masa, dolap, raf, tezgâh, lift kolonu ve garaj dubalarının çarpışma hacimleri kaynak modelin ölçülerine göre ayrı olarak kuruldu. İnce dekoratif parçalar oyuncuyu takmıyor; yere bırakılan ekipmanın fizik ve etkileşim çarpışması korunurken oyuncu kapsülüyle teması yok sayılıyor. Bilgisayar ve diğer görünmez etkileşim hedefleri geçişi engellemeyen tetikleyici olarak ayarlandı.

Saha sahnesi henüz yoktur. `Depart()` görevi `Deployed` durumuna geçirir; ilerideki görev sahnesi aynı iş kimliğini ve kayıtlı araç/ekipman durumunu kullanıp `ReturnFromField(FieldJobResult)` çağırmalıdır. Sonuç kimliği, iş kimliği, başarı, masraf, bonus, aşınma ve tüketilen ekipman kimlikleri içerir. Sonuç bir kez işlenir. Ofiste rapor onaylanınca ödeme tek kez yapılır. Geliştirme derlemesinde F9 kontrollü örnek saha sonucu üretir.

Şirket durumu `Application.persistentDataPath/office_garage_v1.json` dosyasına geçici dosya + atomik değiştirmeyle yazılır. Sürüm 5; para, sipariş, itibar, ekipman ve yüklerin yanında fiziksel modül konumunu, teslimatını, dört cıvata bit maskesini ve bağlantı durumunu saklar. Sürüm 1–4 yükseltilir. Yüklenemeyen bozuk kayıt için başlangıç durumu kullanılır.

JobDesk'te standart iş veya isteğe bağlı "Özenli teslimat" sözleşmesi seçilebilir. Sözleşme, saha sonucundaki ekipman ve araç aşınmasını ve kayıp ekipmanları değerlendirir. Koşul sağlanırsa ek ödeme rapora girer; sağlanmazsa yalnız temel iş ödemesi geçerlidir. Reports ekranı müşteri, kullanılan ekipmanlar, aşınma, saha bonusu, sözleşme bonusu ve masraf kalemlerini otomatik gösterir. Başlangıçta ileri düzey işler itibar koşuluyla kilitlidir; tamamlanan işler itibarı artırıp yeni ilanları açar.

EquipmentShop siparişi bakiyeyi düşürür, kısa süre sonra teslimat noktasında taşınabilir prefab üretir. Ekipman ve araç bakımı gerçek para ve yedek parça tüketir. Araç durumu eşik altındaysa göreve çıkış engellenir. Ücretler ve başlangıç kaynakları `Data/Economy.asset` içinden düzenlenir. Depo, ışık ve operasyon geliştirmeleri satın alınınca ilgili sahne nesneleri etkinleşir; depo kapasitesi ve operasyon bonusu da değişir. CompanyBank işlem geçmişini gösterir.

Seyrek güvenlik denetimi tamamlanan iş sayısına göre açılır. MailBox bildirim verir; Safety ekranı araç ve sahnedeki fiziksel ekipmanların durumunu gösterir. Oyuncu bakımı tamamlayıp denetimi onaylayabilir. Sonuç para ve itibar hareketi oluşturur, bir kez işlenir ve kaydedilir. Denetim görevlisi NPC'si bulunmaz.

## Bilgisayar masaüstü ve insan ölçeği

Bilgisayar koyu masaüstünde iki sütuna yerleşen yedi uygulamayla açılır: İşler, Araç, Depo, Mağaza, Finans, Şirket ve Posta. İlanlar, aktif iş ve raporlar İşler içinde; hazırlık ve modüller Araç içinde; geliştirmeler ve denetim Şirket içinde gruplanır. Pencereler sıcak kâğıt, koyu yazı ve servis mavisi kullanır. Kaydırılan içerikten ayrı işlem alanları, gerçek model önizlemeleri, Source Sans 3 yazısı ve resmî Phosphor simgeleri kullanılır. Alt çubukta masaüstü, saat, kayıt ve çıkış bulunur. Sürükleme, büyütme, küçültme ve görev çubuğundan geri açma çalışır. [Görsel dil ve kaynaklar](WorkOrderUI.md).

İşler ekranı ilan listesi ve seçili iş fişinden oluşur; tek kabul düğmesi ve isteğe bağlı sözleşme kutusu bulunur. Mağaza ürün önizlemesi, ağırlık, stok ve birim fiyatı gösterir; yetersiz bakiye veya dolu depoda sipariş engellenir. Depo her fiziksel ekipmanın konumunu/durumunu ve bekleyen teslimatları gösterir. Finans gerçek para hareketlerini listeler. Araç ekranından modül satın alınır; montaj garajda elle tamamlanır. Hazırlık gerçek modül, yük, kapı ve lift durumunu karşılaştırır. Rapor iş bedeli, bonuslar, masraflar ve tahsil edilecek toplamı gösterir. Sayılar, teslimatlar ve ödemeler `CompanyRuntime` durumundan okunur.

Kaynak ofisin mobilya ölçüleri ve yerleşimi korunur. Yalnız `O08` monitör yüzeyinin Unity sahnesindeki malzemesi siyah olarak değiştirilir; kaynak model yeniden dışa aktarılmaz. Yaklaşık 2 m üretilmiş karakter görseli 1,72 m'ye normalize edildi; kapsül 1,72 m / 0,28 m yarıçap, göz hizası 1,57 m, kamera görüş açısı 62 derece olarak eşleştirildi. Yürüme/koşma hızları 3,2 / 5 m/sn, etkileşim mesafesi 2,4 m, elde taşıma uzaklığı 0,82 m'dir.

## Mimari

Işık, kamera, fiziksel ekipman kullanımı, kapı hareketi ve ses sunumu `Presentation.md` içinde açıklanır. Mevcut sahneye uygulanan kurulum `Editor/OfficePresentationBuilder.cs`, ek Play Mode denetimi `Editor/GameFeelValidation.cs` içindedir.

Garajın yüzey ve armatürleri, yön levhaları ve iki yönlü kapı fiziği `GarageRefinement.md` içinde açıklanır. Kurulum `Editor/GarageRefinementBuilder.cs`, gerçek E/W girdisiyle kapı denetimi `Editor/DoorRegressionValidation.cs` içindedir. Kaynak ofisin mesh ve malzeme bağlantıları ayrıca denetlenir.

`VanAndWorkshop.md` tam minibüs kaynağını ve yeni atölyeyi açıklar. `VanRestorationBuilder` yalnız aracın görselini günceller; `WorkshopOverhaulBuilder` garajdaki kaplama, dolap/tezgâh, tavan ve küçük kumandayı kurar. `VehicleVisualValidation` kaba araç çarpışma kutusunu atlayarak doğrudan dış yüzeyleri sınar ve beş açıdan görüntü üretir.

`WorkshopDetails.md` hareketli tezgâh çekmecelerini, fiziksel güvenlik payını ve masaüstü pencere kullanımını açıklar. `WorkshopDrawer` çekmece hareketini, `DesktopWindowDrag` pencere sürüklemesini yönetir. Atölye ayrıntıları `WorkshopPolishBuilder` ile kurulup `WorkshopInteractionValidation` ile denetlenir.

`HudAndEquipment.md` sade dünya göstergelerini, gerçek bakım/yük kontrolünü, duraklatma menüsünü ve taşınabilir ekipman yüzeylerini açıklar. `GameHud` ve `PauseMenu`, `ComputerUI` sınıfının parçalarıdır; masaüstü görünümüne yeni alan eklemez. `EquipmentPresentationBuilder` dokuz ekipman prefab'ının yalnız kendi mesh/materyal kopyalarını günceller. `HudInteractionValidation` menü/ayar/kayıt/gerçek hazırlık durumlarını denetler.

- `Scripts/Core`: veri tanımları, hazırlık kuralları, iş durumu, ekonomi, kayıt.
- `Scripts/Interaction`: oyuncu, nesne taşıma, yükleme, modül istasyonu, kapı ve tezgâh etkileşimleri.
- `Scripts/UI`: çalışma zamanı oluşturulan bilgisayar uygulamaları, hazırlık paneli ve bildirimler.
- `Scripts/Audio`: eski çağrılar için sessiz uyumluluk API'si; mevcut ses efektleri kaldırıldı.
- `Editor/ProjectBuilder.cs`: sahne, ScriptableObject ve prefabların tekrarlanabilir kurulumu.
- `Editor/ProjectValidation.cs`: Play Mode bütünleşik doğrulama.

Tanım verileri ScriptableObject, değişken şirket durumu `CompanySave` içindedir. Sahne yeniden üretilebilir; mevcut `SampleScene` korunur.

## Sınırlar

Gerçek saha görevleri, araç sürüşü, NPC ziyaretleri, fiziksel denetçi NPC'si, evrak ve tamir mini oyunları, sosyal etkileşimler ve 2–4 oyunculu ağ eşitlemesi henüz uygulanmadı. Projede `com.unity.multiplayer.center` bulunuyor ancak çalışan bir ağ taşıyıcısı/oyun senkronizasyonu bulunmadığından bu sürüm tek oyunculu. Sözleşme bonusu altyapısı saha sonucunu işler; bu sürümde koşulu sağlayacak gerçek saha oynanışı yoktur. Bunlar tamamlanmış özellik olarak değerlendirilmemelidir.
