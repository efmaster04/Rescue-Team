# İş fişi arayüzü

İlk uygulamanın dayanağı kullanıcının seçtiği [ikinci taslaktır](UI-Selected-WorkOrder.png). Sonraki okunurluk ve sadelik isteğiyle 29 Ekim'de bölüm gezintisi üst sekmelere taşındı; eski taslağın sol bölüm sütunu artık kullanılmıyor.

Bilgisayarda yedi uygulama, düz kömür rengi masaüstü, sıcak kâğıt yüzeyler, mürekkep yazı ve sınırlı servis mavisi kullanılır. Sekmeler 21 px, tablo içeriği 20 px, başlık satırları 18 px oldu. İş fişinde müşteri/konum/mesafe/süre iki sütunlu düz bir bilgi alanıdır; ek dekoratif kart yoktur. Fiyat, sözleşme ve ana işlem alt bölümde birlikte kalır. Raporun toplam tutarı ve tahsilat düğmesi de sabit alt alandadır; uzun ayrıntılar bunları ekran dışına itmez. Kabartmalı pencere kenarları, karşılama sloganları ve eski piksel ikonları kullanılmaz.

## Uygulamalar

- **İşler:** İlanlar, Aktif iş ve Raporlar. İş fişi; müşteri, konum, mesafe, süre, gerekenler ve bedeli gösterir. İsteğe bağlı sözleşme onay kutusuyla seçilir; tek kabul düğmesi gerçek şirket durumunu değiştirir. İtibar veya aktif iş nedeniyle kapalı işlemde sebep yazılır.
- **Araç:** Özgün minibüsün önizlemesi, gerçek durumu, modüller ve fiziksel yük manifestosu.
- **Depo:** Tek tek fiziksel eşyalar; Garajda, Elde veya Araçta konumu ve gerçek dayanıklılık. Teslimatlar ayrı bölümde.
- **Mağaza:** Kategori, görselli ürün listesi, seçilen ekipman bilgisi ve sabit sipariş alanı. Bakiye ve kapasite kısıtları gerçek ekonomi kurallarını kullanır.
- **Finans:** Gerçek hesap hareketleri ve bakiye.
- **Şirket:** Genel bakış, geliştirmeler ve denetim.
- **Posta:** Mesaj listesi ve ayrıntısı; ilgili uygulamaya açılan işlemler. Okundu bilgisi mevcut UI oturumu boyunca tutulur; yeni kayıt alanı eklenmedi.

Eski fiziksel kısayol hedefleri (`Dispatch`, `Reports`, `BuildingManager`, `Safety`) yeni uygulamanın doğru bölümüne yönlenir. Pencere sürükleme, sınırlandırma, büyütme/geri alma ve görev çubuğundan küçültme/geri açma korunur.

## Kaynak varlıklar

- Yazı: Adobe **Source Sans 3 Regular / Semibold**, OFL lisansı `Assets/_Project/Resources/Fonts/SourceSans3-OFL.md`.
- İkon: Resmî **Phosphor Regular**, MIT lisansı `Art/UI/Phosphor/LICENSE.txt`. Kaynak SVG'ler korunur, oyunda 192 px saydam UI sprite'ları kullanılır.
- Ekipman ve araç önizlemeleri mevcut prefab/FBX'lerden Unity'de ortak açı ve ışıkla render edilir. Üretilmiş alternatif araç resmi kullanılmaz.
- İndirme adresleri ve SHA-256 özetleri: `Art/UI/asset-manifest.json`; tekrar hazırlama: `Art/prepare_workorder_ui.py`.
- Kaynak ofis modeli, ofis yerleşimi, materyal bağlantıları ve minibüs gövdesi bu arayüz çalışmasında değiştirilmedi.

## Dünya arayüzü

Tab iş fişi ve eldeki ekipman bilgisi aynı yazı/renk ailesini paylaşır. Eşya bilgisi sol alttadır; sağdaki taşınan modeli kapatmaz. Bildirimler kısa metin yüzeyleridir. ESC menüsünde solda menü, sağda ayarlar veya kontroller bulunur. Gerçek ses, fare ve eşya salınımı ayarları ile duraklatma/kayıt davranışı korunur.

## Doğrulama

- `Company/Validate Work Order UI`: yedi uygulama, bölüm gezintisi, boş/kilitli durumlar, sözleşme kutusu, pencere ve görev çubuğu işlemleri; 1280×720, 1600×900, 1920×1080 görüntüleri.
- `Company/Validate Play Loop`: mevcut araç, taşıma, kapı, çekmece, HUD ve tam şirket döngüsü regresyonları.
- Açık editör için `Temp/CompanyUIValidation.request` dosyasına `ui` veya `full` yazılabilir. Köprü derleme sürerken, Play Mode açıkken veya sahnede kaydedilmemiş değişiklik varken sahneyi değiştirmez.
- Kullanıcı şirket kaydı test başında yedeklenir ve test sonunda geri yüklenir.
- Görsel karşılaştırma ve son kabul: proje kökündeki `design-qa.md`. Güncel test sonucu: `Logs/office_garage_validation.txt`.

Bu çalışma saha görevleri, sürüş veya ağ çok oyunculu sistem eklemez; mevcut ofis/garaj döngüsünün arayüzünü değiştirir.
