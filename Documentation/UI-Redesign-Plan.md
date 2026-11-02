# Arayüzü yeniden tasarlama planı

Durum: ikinci tasarım ilk uygulamaya temel oldu. Kullanıcının sonraki sadelik ve okunurluk isteğiyle 29 Ekim'de sol bölüm menüsü üst sekmelere taşındı; yazılar büyüdü ve iş bilgileri iki sütunlu düz alanda toplandı. Aşağıdaki plan önceki karar kaydıdır; güncel düzen ve varlık kaynakları [WorkOrderUI.md](WorkOrderUI.md), görsel kontrol [design-qa.md](../design-qa.md), bütünleşik doğrulama [TestResults.md](TestResults.md) içindedir.

## 1. Hedef

Oyuncunun küçük bir servis şirketini yönettiğini hissettiren, gündelik ve inandırıcı bir arayüz. Kimlik; iş evraklarının düzeninden, oyundaki gerçek ekipman görsellerinden, okunaklı yazıdan ve tutarlı etkileşimlerden gelecek.

Bilgisayar; masaüstü ve açılan uygulamalar olarak kalacak. Oyun içi göstergeler aynı yazı, renk ve ikon dilini kullanacak; yürürken dünya görünümünü açık bırakacak. Kaynak ofisin tasarımına ve özgün minibüsün gövdesine dokunulmayacak. Fiziksel monitör kullanılmıyorken siyah kalacak.

Kullanıcının sınırları: cyberpunk/futuristik görünüm, neon, parlama, hologram, dekoratif teknik çizgiler yok. Slogan, karşılama metni, hayali şirket verisi, sırf boşluk dolduran kart/grafik, büyük yuvarlak uygulama kutuları yok.

## 2. Mevcut görünümden çıkarılan sorunlar

Projedeki masaüstü, İş Panosu, Mağaza, Raporlar, eşya HUD'ı ve ESC görüntüleri incelendi. Bunlar önceki Play Mode çıktılarıdır; bu plan sırasında yeni bir canlı UX denetimi çalıştırılmadı.

- Masaüstündeki küçük piksel nesneleri, kabartmalı eski pencere kenarları ve koyu HUD aynı görsel aileyi paylaşmıyor.
- İş Panosu'nda açıklama, fiyat, gereksinimler ve iki kabul düğmesi benzer ağırlıkta. Oyuncunun önce neyi okuyacağı ve hangi işlemi seçeceği zayıf.
- Mağaza yalnız isim/sayı tablosu. Oyundaki model kimliği ve ekipmanı tanımaya yardım edecek görseller kullanılmıyor. Her satırın uzun düğmesi tabloyu ağırlaştırıyor.
- Rapor, geniş bir alana yayılmış etiket/değer ve tablo satırlarından oluşuyor. İşin sonucu ile alınacak ödeme birbirinden yeterince ayrışmıyor.
- HUD, eşya bilgisi, hazırlık ve duraklatmada tekrar eden koyu dikdörtgenler var. Eşya bilgisi, eşyanın göründüğü sağ alt köşeyi kaplıyor.
- Bazı temel metinler küçük, bazı başlıklar gereğinden kalın. Görsel kaliteyi yalnız renk değiştirerek çözmek mümkün değil; bilgi sırası ve ekranların görevleri de yeniden kurulacak.

## 3. Görsel dil

### Renkler

| Rol | Hedef renk | Kullanım |
| --- | --- | --- |
| Uygulama zemini | `#EDEBE4` sıcak açık gri | Pencerenin ana yüzeyi |
| İçerik yüzeyi | `#FAF9F5` kırık beyaz | İş fişi, ürün ve belge alanı |
| Ana yazı | `#252B2C` koyu mürekkep | Metin ve sayılar |
| İkincil yazı | `#62665F` | Açıklama, tarih, yardımcı bilgi |
| Ayırıcı | `#CDCBC2` | Satır ve bölüm ayrımı |
| Ana vurgu | `#36596A` servis mavisi | Seçim, ana işlem, etkin sekme |
| Hazır/başarılı | `#48664C` | Metin ve küçük durum işareti |
| Dikkat | `#986A2D` | Eksik ekipman, bakım ihtiyacı |
| Hata | `#9B4C43` | Başarısız işlem, ciddi hasar |
| Masaüstü | `#202728` | Düz, desensiz koyu arka plan |

Vurgu rengi bütün başlıklara yayılmayacak. Durumlar kısa sözcük ve işaretle birlikte anlatılacak; tek başına renge dayanılmayacak. Son kontrast ve dünya üzerinde okunurluk, taslak ve Unity aşamasında ölçülecek.

### Yazı, çizgi ve ölçü

- Tek yazı ailesi: Source Sans 3, Regular ve Semibold. Türkçe karakterler ve ₺ dahil glif kontrolü yapılacak. Resmî kaynak: https://github.com/adobe-fonts/source-sans .
- 1600×900 çalışma boyutunda ana metin 18 px, yardımcı metin 15–16 px, bölüm başlığı 20 px, ekran başlığı 26–28 px. Kalınlık esas olarak başlık ve seçili işlemde kullanılacak.
- Sayısal sütunlar sağa hizalanacak. Fiyat, ağırlık ve yüzde aynı biçimde yazılacak.
- 8 px aralık sistemi; pencere iç boşluğu 24 px, normal satır yüksekliği 44–48 px, ana düğme yüksekliği 40–44 px.
- Köşeler hafif, 3–4 px. Pencere çevresinde tek ince kenar ve ölçülü gölge. Kabartmalı bevel kaldırılacak.
- İkonlar tek bir kaynak aileden, aynı optik kalınlıkta olacak: Phosphor Regular yönü. Desktop, tablo ve tuş ipuçlarında aynı anlam aynı ikonla gösterilecek.
- Ürün/minibüs görselleri Unity'deki mevcut modellerden, ortak açı ve ışıkla alınacak. Yeni bir araç veya farklı ekipman resmi üretilmeyecek.

## 4. Bilgisayarın yapısı

Masaüstünde yedi başlatıcı olacak. Mevcut işlevler bu uygulamalarda bir araya getirilecek:

| Uygulama | İçerik |
| --- | --- |
| İşler | İlanlar, aktif iş/hazırlık/sevkiyat, sonuç raporları |
| Araç | Araç durumu, modül seçimi, gerçek yük listesi |
| Depo | Garajdaki ve araçtaki ekipman, durum, teslimatlar |
| Mağaza | Ekipman ürün listesi ve sipariş |
| Finans | Bakiye, hesap hareketleri, ödeme dökümleri |
| Şirket | Geliştirmeler, denetimler, şirket istatistikleri |
| Posta | Gelen bildirimler ve okunma durumu |

İlan, sevkiyat ve raporun aynı İşler uygulamasında olması işin kabulünden ödemesine kadar olan sırayı görünür kılacak. Mevcut kayıtlar ve işlemler korunacak.

Masaüstünde yalnız başlatıcılar ve alt çubuk bulunacak. 40 px ikon, altında kısa ad; ikonun etrafında sürekli bir kutu olmayacak. Seçili/üzerine gelinen öğe hafif bir yüzeyle ayrılacak. Alt çubukta açık uygulama, saat, kayıt ve bilgisayardan çıkış yer alacak. Otomatik kayıt sonucu kısa ve geçici bildirilecek.

Pencerelerin başlığı sade, içeriği ferah olacak. Sürükleme, büyütme, küçültme ve görev çubuğundan geri açma korunacak. Uygulama içindeki bölüm sekmeleri, masaüstü görev çubuğundan görsel olarak ayrılacak.

## 5. Ekranların kesin düzeni

### İşler: ilk tasarlanacak ana ekran

Sol tarafta yaklaşık 300 px genişlikte ilan listesi; sağda seçilen işin fişi. Liste satırında iş adı, müşteri/bölge ve bedel görünecek. Seçim ince mavi bir işaret ve açık zeminle belirtilecek.

Fişte bilgi sırası: iş adı ve iş bedeli → müşteri/konum/süre → kısa açıklama → gerekli modül ve ekipman → sözleşme koşulu → kabul işlemi. Gereksinimler küçük ekipman görseli ve kısa isimle okunacak.

Altta tek ana düğme: **İşi kabul et**. İsteğe bağlı sözleşme, koşulu ve bonusu yazan bir onay kutusu olacak. Böylece iki eşit ağırlıklı kabul düğmesi yerine tek karar ve tek işlem bulunacak. İtibarı yetersiz işte neden, düğmenin yanında açık yazılacak.

Aktif iş bölümü aynı fişin hazırlık durumunu gösterecek. Sevkiyat düğmesi eksikler giderilince kullanılabilecek. İş dönüşünde sonuç, masraf ve ödeme aynı iş kaydında yer alacak.

### Mağaza

Sol tarafta kısa kategori listesi. Ortada görselli ürün satırları: 64–72 px ekipman görseli, isim, ağırlık, stok ve fiyat. Sağda seçilen ürünün daha büyük önizlemesi ve sipariş özeti.

Her satıra geniş sipariş düğmesi konmayacak. Seçili ürün için tek işlem: **Sipariş ver — ₺120** gibi gerçek bedel. Yetersiz para veya depo doluluğu, aynı işlem alanında açıklanacak. Teslimatın yolda olduğu görünür olacak.

### Depo

Ekipman görseli, adı, durum yüzdesi ve konumu aynı satırda. Gerçek duruma göre **Garajda / Elde / Araçta**; bekleyen siparişler teslimatlar bölümünde **Yolda** olarak gösterilecek. Hasar metin ve küçük işaretle belirtilecek. Aynı türdeki farklı fiziksel eşyaların kayıtları karışmayacak.

### Araç

Orijinal minibüsün ölçülü bir görseli; yanında araç durumu ve takılı modül. Altında kullanılabilir modüller ve gerçek yük manifestosu. Modül satın alma/takma, aracın boş olması ve kapasite kuralları aynı bölümde okunacak. Gösteriş için hız göstergesi veya dairesel sayaç eklenmeyecek.

### Raporlar ve Finans

Rapor, iş fişinin sonuç bölümü gibi düzenlenecek. İş sonucu ve müşteri üstte; bedel, prim ve masraflar hizalı ödeme kalemleri olacak. **Tahsil edilecek tutar** altta tek toplam satırıyla vurgulanacak. Altında tek onay/tahsil işlemi bulunacak. Tahsil edilmiş raporda aynı işlemin tekrar yapılabileceği izlenimi verilmeyecek.

Finans ekranı tarih, açıklama, tutar ve bakiye sütunlu temiz bir hesap defteri olacak. Dekoratif grafik kullanılmayacak.

### Şirket ve Posta

Şirket içindeki geliştirme, denetim ve istatistikler açık bölüm adlarıyla ayrılacak. Aynı pencere bileşenleri kullanılacak. Posta, solda gönderen/konu listesi ve sağda mesaj içeriğinden oluşacak; okunmamış iletiyi yalnız renk ayırmayacak.

## 6. Oyun içi arayüz

- Normal yürüyüşte dünya açık kalacak. Etkileşimde küçük bir E tuşu ve kısa eylem adı görünecek; örneğin **E Bilgisayarı kullan**.
- Elde eşya bilgisi sağ alttan **sol alta** taşınacak. Böylece sağdaki gerçek eşya modeli açık kalacak. Ad, küçük durum göstergesi ve F/R kontrolleri yeterli olacak; ağırlık ve ayrıntı gerektiğinde genişletilecek.
- Tab ile sağ üstten kompakt iş fişi açılacak. Başlık, gerekli modül, ekipman ve eksik kontrol sayısı. Ekranın ortasına veya yere kadar uzanmayacak. İş yokken büyük bir boş panel açılmayacak.
- Bildirim, ekranın üst kenarında kısa bir bilgi satırı olacak. Oyuncunun müdahalesini gerektiren hata, ilgili işlemin yanında açıklanacak. Aynı mesaj tekrar tekrar yığılmayacak.
- Tekrar edilen “Operasyon durumu”, “Hazırlık aşamasında” türü başlıklar azaltılacak. Metinlerin işi, oyuncuya mevcut durumu ve bir sonraki işlemi söylemek olacak.

## 7. ESC ve ayarlar

Oyun görüntüsü hafifçe kararacak. Sol tarafta **Devam et, Kaydet, Ayarlar, Kontroller** menüsü; ayarlar seçildiğinde sağda ilgili içerik. Mevcut ortadaki uzun saydam kutu kaldırılacak.

Ses ve fare kontrolleri aynı ölçüde, net değerle gösterilecek. Eşya salınımı seçeneği korunacak. Arayüz ölçeği %100–125 aralığında değerlendirilecek; bu artış, metin veya işlem düğmelerini taşırmayacak. Kaydetme başarısı ve hatası ayrı durumlar olacak.

## 8. Etkileşim kuralları

- Her işlem bölümünde tek baskın ana düğme. İptal ve geri daha sakin.
- Hover: hafif zemin değişimi. Basılı: küçük renk/değer değişimi. Sürekli parlama, zıplama ve animasyon yok.
- Pencere/panel açılışı yaklaşık 120–160 ms; hızlı kullanıcı kontrolünü geciktirmeyecek.
- Devre dışı düğmenin nedeni okunacak. Tooltip tek bilgi kaynağı olmayacak.
- Boş, seçili, yükleniyor, başarılı, hatalı ve yetersiz kaynak durumları taslağa dahil edilecek.
- ESC: uygulama içi geri/masaüstü/oyuna dönme sırası tutarlı kalacak. Oyun duraklatması bu akışa karışmayacak.

## 9. Uygulama sırası ve kabul ölçütü

1. **Görsel pilot:** masaüstü ve İşler ekranının ayrıntılı görsel taslağı. Masaüstü kimliği, pencere, liste, metin ve tek ana işlem aynı çalışmada netleşecek. Bu plan ekran taslağının yerine geçmez.
2. **Ortak bileşenler:** pencere, sekme, satır, düğme, seçim kutusu, durum, bildirim ve tuş ipucu. Ölçüler ve renkler tek kaynaktan yönetilecek. Unity'de net metin için TextMeshPro/SDF kullanımı değerlendirilecek.
3. **Uygulamalar:** İşler → Mağaza/Depo → Araç → Rapor/Finans → Şirket/Posta. Önce işin kabulü ve hazırlığı, sonra satın alma ve ödeme akışı.
4. **Dünya arayüzü:** etkileşim, elde eşya, Tab fişi, bildirim ve ESC aynı görsel dile geçirilecek.
5. **Doğrulama:** 1280×720, 1600×900 ve 1920×1080; varsayılan ve büyük UI ölçeği. Türkçe glifler, metin taşması, uzun eşya/iş adları, açık renk garaj fonunda okunurluk ve fare/klavye erişimi.

Kabul için yalnız güzel bir sabit ekran yeterli olmayacak. İş kabulü, yanlış modül, eksik/hasarlı ekipman, sevkiyat, sipariş/teslimat, yetersiz bakiye, raporun tek seferlik ödemesi, kayıt ve ESC dönüşü gerçek verilerle çalışacak. Mevcut testler yeni uygulama gruplarına uyarlanacak; yalnız eski nesne adlarını korumak için tasarım kısıtlanmayacak.

Bir sonraki somut çıktı: bu plana göre masaüstü ve İşler ekranının birlikte görüldüğü, henüz oyun koduna uygulanmamış görsel taslak.
