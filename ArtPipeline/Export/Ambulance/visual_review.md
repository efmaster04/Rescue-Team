# Ambulans ve kanvas sedye — görsel inceleme

Referanslar: `ArtSource/Ambulance/references/01_turnaround_white_red.jpg` (ana konsept), `02_minivan_style_match.png` (oyun stil referansı). Referanslar görsel olarak açılarak incelendi. Araç yönü çekiciyle aynı: -Y; sol +X. Semboller/yazılar yok; sağ yan solun aynası ve sürgülü kapı yok.

## Tur 01 — siluet, hacim ve işlev

`renders/round_01/`: ön/arka/iki yan, ön üç çeyrek, 100 derece açık arka kapılar + 90 derece basamak + yerde LoadPoint sedyesi, tavansız üst görünüm, minibüs ölçeği, dört konsept karşılaştırması. Sedye render'ı `../Stretcher/renders/round_01/` altında.

- Konseptin kısa kaputu, yüksek panel van tavanı, iki kırmızı bandı, iki parçalı arka kapıları ve tek yan penceresi okunuyor. Cam düzeni iki yanda simetrik.
- Minibüsün düz normalli, sade materyalli, köşeleri pahlı dili sürdürüldü. Yeni aksesuar eklenmedi.
- Ön üç çeyrekte kaputun yan yüzünde kararma: kaput prizmasıyla alt panelin aynı düzlemde çakışması bulundu; ikinci turda örtüşen panel kaldırıldı.
- Ön tavan alın parçasının eksik genişliği ve kabin penceresi üzerindeki ince boşluk ikinci turda kapatıldı.
- Sedye alt destekleri kanvastan görünüyordu; ikinci turda aşağı alındı. Kanvasın gerçek enine sarkması korundu.
- İlk iç mekân örneği fazla koyuydu; konseptte yatak üstündeki daha açık piksele geçildi.
- İlk render'da GPU bellek hatası; CPU'ya geçildi. CPU turunda GPU kullanan denoiser bellek hataları görüldü; sonraki turda CPU denoising ve 256 px render tile ayarlandı. İlk turdaki ön görünümlerde gürültü arşivde korunmuştur.

## Tur 02 — kararmalar ve kemerler (devralan: claude)

`renders/round_02/` (tam set). Codex'in ilk tur 02 denemesi bellek hatasıyla 4 görüntüde yarım kalmıştı; bu tur yeniden render edildi.

- Ön tavan alnının yan yüzleri yan panellerle eş düzlemdeydi ve A sütunu üstünde siyah kama oluşturuyordu. Alın 1 cm içeri alındı.
- Ön kapı camı üst kenarında iki dikdörtgen üst üste biniyordu (B sütunu üstünde siyah leke). Çakışma kaldırıldı.
- Tekerlek kemerleri z=0.39'dan başladığı için gövde altına beyaz yaylar sarkıyordu. Kemerler gövde alt çizgisinde (z=0.54) başlıyor.
- Render: `use_auto_tile` kapatıldı (düşük sistem belleğinde EXR tile dosyası okunamıyordu).

## Tur 03 — son temizlik

`renders/round_03/` (tam set, kök `renders/` bu turun kopyası).

- Ön tamponun yan yüzü yan panelle eş düzlemdeydi (yan görünümde siyah köşe). Tampon 5 mm taşırıldı.
- Üst ince kırmızı bant ön alının eğik kenarına kadar uzatıldı; A sütunu üstündeki boşluk kapandı, bant konsepteki gibi öne dönüyor.
- Bellek darlığında OIDN hatası karanlık kare üretiyordu. Render köşe parlaklığıyla denetleniyor, bozuk karede OIDN kapalı ve 192 örnekle en fazla 2 kez tekrar ediliyor.
- Konseptle karşılaştırma (compare_*): beyaz gövde, iki kırmızı bant, kısa kaput, yüksek tavan, tek yan pencere, arka çift kapı, mavi çakar düzeni ve iç mekân (2 yatak, refakatçi koltuğu, dolap, şoför silueti) okunuyor. Kalan farklar aşağıda.

## Tur 04 — siluet yumuşatma, kemer lekeleri, tekerlek çapı (r2, şef incelemesi)

`renders/round_04/` (tam set, kök `renders/` bu turun kopyası).

- Siluet: yan siluet yolu boyunca süpürülen pahlı kabuk (`sweep`) eklendi. Ön camın üstünde öne taşan yuvarlak tavan "şapkası", düz tavan ve kavisli arka tavan kenarı tek kabuk; iki yanda değişken pah (uçlarda 1 cm, tavanda 6 cm). İnce üst kırmızı bant şapkanın önüne döner.
- Kaput 0.63 m'den 0.87 m'ye uzatıldı, öne eğimli ve burnu yuvarlatıldı (aynı pahlı kabuk); ön cam tabanı y -2.08'e, aynalar ve torpido buna göre geri alındı. Bel bandı konsepteki gibi kaput yanına iner.
- Yan levhalar renk bantlarına Sutherland-Hodgman kırpmasıyla bölünür; eş düzlemli çakışma yok. Arka dikey köşeler pahlı dikme (şeritler köşeyi döner).
- Kemer üstündeki bej lekeler: kemer boşluğundan görünen iç taban (bej Interior) kenarıydı. Koyu tekerlek havuzu + kemer tepesine kadar çıkan dış astar eklendi.
- Tekerlek yarıçapı 0.39 → 0.43 m (çap 0.86 m); kemer yarıçapı 0.51 m. Aks konumları, kök pivot ve Z=0 teması aynı. Arka yatak çerçevesi inceltildi (z .87–.90), tekerlek havuzunun üstüne oturur; PatientSlot ve yatak yüzeyi (0.98) değişmedi. Verify'daki beklenen tekerlek yarıçapı 0.43'e güncellendi (tolerans aynı).
- Toplam ölçü 6.0 × 2.0 × 2.65 m korunur. Arka tavan kavisi arka çakarların (y 2.51–2.69) düz zeminde kalması için y 2.70'ten sonra başlar; bu yüzden konseptten daha küçük yarıçaplıdır.

## Teknik sözleşme

- İki sedye ihtiyacı, tek `SM_Stretcher.fbx` dosyasının iki örneğiyle karşılanır (`required_game_instances: 2`). Tekrarlanan özdeş FBX dosyası üretilmez.
- Marker'ların yerel +Y ekseni baş yönüdür; Blender dünya -Y yönüne bakar. Root ve mesh'ler dinlenme pozunda dönüşsüzdür.
- Beyaz/kırmızı/trim/lastik/çakar ve iç renkler konseptten 5x5 medyanla örneklenir; piksel ve hex kayıtları meta içindedir. Turuncu kanvas #D97836 ve alüminyum #AAB3B8 brief varsayımıdır.
- FBX renkleri sRGB hex'ten lineere çevrilmiş Base Color olarak aynen çıkar; sRGB export düzeltmesi uygulanmaz.
- Verify yalnız stdout ve TEMP JSON yazar. Git komutu çalıştırılmaz; dosya değişmezliği tüm repo dosyalarının boyut/mtime snapshot'ıyla ayrıca sınanır.

## Konseptten bilinçli sapmalar / açık sorular

- Sağ sürgülü kapı kaldırıldı; iki yan kapalı ve simetrik. Yazı, logo, sembol yok.
- Siluet konseptten uzun: 6.0 m uzunluk sözleşmesi konseptteki kısa orandan uzundur. Tur 04'ten beri kaput ve tavan pahlı/kavisli; kavisler low poly segmentlerle verilir.
- Stoplar konseptteki uzun dikey lambalardan küçük. Ön cam konsepttekinden daha az yatık.
- Ölçüler çizimden alınmadı: 6.0 × 2.0 × 2.65 m sözleşmesi ve <=2.80 m çakar tepesi kullanıldı.
- İç yataklar sabit, sade platformlardır; konseptte görülen tekerlekli yatak mekanizması brief kapsamında değildir. Harici sedye tekerleksizdir.
- Sedye tek yeniden kullanılabilir asset olarak iki oyun örneği içindir. Oyun sahnesine yerleştirme, URP materyal kurma ve Unity testi kapsam dışıdır.
- AÇIK SORULAR: engelleyici soru yok; sedye miktarı aynı modelin iki örneği olarak yorumlandı.
