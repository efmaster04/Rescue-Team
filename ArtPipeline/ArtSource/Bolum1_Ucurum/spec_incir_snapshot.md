# Bölüm 1 — Uçurum Mekânı

Tarih: 2026-10-12 · Yazan: claude · Etiketler: #3d #blender #level #bolum1 · İlgili: [[03_Oyun_Projesi]] · [[3D_Asset_Ustaligi]] · [[Gorsel_Gunlugu]]

## Özet
Rescue Team'in ilk görev mekânının (dağ yolu, uçurumda mahsur minibüs) spesifikasyonu. Mekân **Blender'da prosedürel Python betiğiyle** üretilecek (RescueCrew'daki hat): Codex `gpt-6-astra` yazar, Claude render'ları konseptle karşılaştırıp düzeltme turlarını yönetir. Bu iş Blender çıktısıyla biter, Unity'ye aktarma sonraki adım.

## Detay

### Referanslar
- Kullanıcı eskizi: `Desktop/aminoglu gorsel/yamac.png` (sivri dağ, eteğinden geçen 2 şeritli yol, yolun altında uçuruma sarkan araç)
- Gemini konseptleri: `Gorseller/bolum1-ucurum-genel_…`, `…-kusbakisi_…`, `…-arac-yakin_…` (2026-10-12). **Kullanıcının seçtiği: yakın plan** (ışık, kaya dili, sarkma pozu). Kuşbakışı yerleşim için yardımcı.

### Kullanıcı kararları (2026-10-12)
| Konu | Karar |
|---|---|
| Atmosfer | Gün batımı: altın/turuncu ışık, uzun gölgeler |
| Oyun alanı | Orta, ~120×80 m |
| Doğa | Büyük low poly kayalar, seyrek çalı ve çimen tutamı |
| Mahsur araç | **Minibüs/van**, yoldan aşağıdaki ayrı toprak patikada; **arka tekerlekler patikada, burnu boşluğa sarkık** |
| Patika yüksekliği | Yolun **~8–10 m** altında |
| İniş | Dar, dik toprak rampa (yürünür, kaygan) + ağır işler için halat/vinç |
| Kurtarma aracı | Yolun dış kenarındaki cepte (banket), kazanın üstünde |
| Harita sınırı | Yol iki uçta keskin virajla kayanın arkasına döner (görünmez duvar) |
| Vadi | Sisle dolu derinlik, dip görünmez |
| Düşme | Minibüs bölgesinin altında kaya çıkıntısı (düşen kalır, halatla çekilir), başka yerlerde boşluk → yeniden doğma |
| Detaylar | Kırık korkuluk + fren izi, minibüsten dökülen bavul/çantalar, yamaçta gevşek kayalar, sembollü yol levhaları (yazısız) |
| Trafik | Yol **trafiğe açık**, araçlar **dalgalar hâlinde** (bazen sakin, bazen arka arkaya 3–4), iki yönlü. Çarpılan oyuncu yere düşer, takım arkadaşları gelip kaldırır |
| Koni | Koni konan şeritte araçlar durur. **~20 sn**'den (ayarlanabilir) fazla bekleyen araç koniye çarpıp devam eder; çarpılan koni fizikle ya yol kenarına ya da uçurumdan aşağı düşer |

### Yerleşim (Blender, metre, Z yukarı)
Eksenler: **X** yol boyunca (batı → doğu), **+Y** dağa doğru, **−Y** vadiye doğru. Kaza noktasında yol yüzeyi Z = 0.

| Öğe | Konum ve ölçü |
|---|---|
| Oyun alanı | X −60…+60, Y −45…+35 |
| Yol | 8 m (2 × 3.5 m şerit + 2 × 0.5 m banket). Vadiye doğru dışbükey hafif yay, uçlarda (X ≈ ±55) keskin virajla +Y'ye kıvrılıp kaya çıkıntısının arkasında kaybolur. Batıdan doğuya hafif tırmanır (Z −1.5 → +2.5). Beyaz kenar çizgisi, kesik sarı orta çizgi (3 m çizgi, 4.5 m boşluk) |
| Dağ tarafı (+Y) | 1 m çakıl hendek, ardından 4–15 m yükselen kaya duvarı/yamaç. X −10…+15 arasında yolun 8–12 m üstünde gevşek kaya rafı |
| Korkuluk | Vadi tarafı boyunca, 0.75 m, 2 m'de bir direk. Boşluklar: banket ağzı, rampa başı, kırık nokta |
| Banket | Vadi tarafı, X +5…+25, yola ek 5 m derin çakıl cep. Kurtarma aracı yeri |
| Kırık nokta | Korkuluk X −2…+4 arası kopuk/eğik. Fren izleri X −25'ten kırık noktaya kıvrılır |
| Yamaç | Yol kenarından patikaya 9 m iniş, 45–55° kayalık toprak (yürünemez) |
| Patika | Z ≈ −9, ~5 m genişlik, X −30…+15, dış kenarı uçurum |
| Rampa | Yolda X ≈ −35'ten başlar, yamaç boyunca doğuya inerek X ≈ −12'de patikaya bağlanır. ~1.8 m genişlik, ~20° eğim |
| Minibüs | Arka tekerlekler patikanın uçurum kenarında (X ≈ +4), burnu −Y yönünde boşluğa ~25–30° eğik. Yer tutucu 5.0 × 1.9 × 2.0 m |
| Çıkıntı | Minibüsün altında, Z ≈ −14, X −4…+10, 3–4 m derin |
| Uçurum | Patikanın ve çıkıntının altında sise iner. Sis üstü Z ≈ −40, yeniden doğma tetikleyicisi Z ≈ −60 |
| Dökülen eşya | Kırık noktadan minibüse uzanan hatta 2 bavul, 3 çanta (ayrı objeler) |
| Levhalar | İki virajda chevron (vadi tarafı), X ≈ −20'de "düşen kaya" levhası (dağ tarafı) |
| Arka plan | Sivri ana zirve (gri kaya çizgili) yaklaşık (−20, +180), ~220 m yüksek, sağında ikinci küçük zirve. Vadinin karşısında uzak sırtlar. Collider yok |

### Unity için işaretler (Blender'da boş obje / eğri)
`PlayerSpawn` (yolda, X ≈ −45) · `RecoveryTruckSlot` (banket) · `TrafficLane_EastBound`, `TrafficLane_WestBound` (şerit orta eğrileri, viraj arkasından viraj arkasına) · `TrafficSpawn_*`, `TrafficDespawn_*` (kaya arkasında, görünmez) · `FallRespawnPlane` (Z −60) · `LedgeZone`

### Teknik hedefler
- Stil: düz gölgeli (flat-shaded) low poly, az sayıda düz renkli materyal (Asfalt, SarıÇizgi, BeyazÇizgi, Toprak, Kaya, Çimen, Metal, Levha). Texture yok veya en aza indirilmiş.
- Bütçe: oynanabilir alan ≤ 60k üçgen, arka plan ≤ 30k üçgen, prop'lar ayrı.
- Modüller ayrı obje: zemin/yamaç, yol, yol çizgileri, korkuluk parçaları, gevşek kayalar (her biri ayrı, fizik için), prop'lar, arka plan.
- Çıktı: `.blend` + modül başına FBX + konseptlerle aynı açıdan 3 render (genel, kuşbakışı, yakın plan).

## Kararlar / Dikkat
- **Karar (2026-10-12, kullanıcı):** İlk görev mekânı **Blender'da** yapılır; ProBuilder blockout aşaması atlandı. *Gerekçe:* Kullanıcının açık talimatı. Mekân, RescueCrew gibi Codex'in yazdığı tekrar üretilebilir bir betikle oluşur, ölçüler bu nottaki tablodan gelir.
- **Karar (2026-10-12, kullanıcı):** Kodlama `gpt-6-astra`; Codex limiti dolarsa iş Claude alt ajanıyla (**Fable**) sürer, yarım bırakılmaz, kullanıcıya haber verilir.
- **Dikkat:** Gemini görselleri ölçü kaynağı değildir; ölçüler yukarıdaki yerleşim tablosundan gelir. Görseller stil, ışık ve kompozisyon referansıdır.
- **Dikkat:** Trafik, koni, ragdoll ve kaldırma mekanikleri Unity işidir. Blender'da yalnızca bunlara yer ve işaret bırakılır.

## Kaynak
- Claude ile mekân tanımlama oturumu, 2026-10-12 (soru-cevap, 3 Gemini konsepti)
