# Bölüm 1 — Uçurum: görsel ve teknik kontrol

## 2. tur

- `r2_round_01`: 01/02/03 referanslarıyla üç karşılaştırma açıldı; 04 zirve görseli ayrıca incelendi. Dağ ve yan devamlar eklendi, ama zirve tabanı havada görünüyordu; genel kamera görev alanını aşağıda kesiyordu. Kaya yüzlerinde eklenen kümelerin bir kısmı eski duvarın arkasında kalmıştı. Zirve tabanı dağ gövdesine gömüldü, kaya kümeleri görünür yüze taşındı.
- `r2_round_02`: üç render ve yanlarındaki 01/02/03 yeniden açıldı. Dağ artık tek kütle, uzak gri bloklar kaldırılmış; fakat kümeler düzenli sıralar oluşturuyordu. Dağılım düzensizleştirildi ve üst yamaçtaki taşlar gerçek yüzeye raycast ile yerleştirildi. Yakın kamera minibüsün tamamını alacak şekilde aşağı çevrildi. FBX ara kontrolünde bütün ölçüler geçti; ardışık SHA kontrolü son sürüme bırakıldı.
- `r2_round_03`: üç karşılaştırma tekrar açıldı. Yakın karede arka tekerlek, minibüsün burun aşağı pozu ve iki kırık korkuluk ucu okunuyor. Genel karede zirve ve araç kadraj sınırına yakın; ek kamera çalışması gerekiyor. Sis hacmindeki sabit emisyonun boş hacmi de aydınlatarak düz tavan etkisi oluşturduğu bulundu; emisyon yoğunluğa bağlandı. İlk turun kayıtları ve `round_01..05` arşivleri korunmuştur.

Referanslar salt okunur kullanıldı. `compare_*.png` dosyalarında sol taraf referans, sağ taraf gerçek Cycles render'ıdır. Her turda üç karşılaştırma açılarak gözle incelendi. İlk incelemede 00 eskizi ve 04 zirve referansı da açıldı. Ölçü kaynağı görev brief'idir; eski snapshot'taki X=-30 patika başlangıcı ve 03'teki tüneller kullanılmadı.

## Görsel turlar

- Tur 1 — `renders/round_01/`: 01/02/03 ile genel, plan ve yakın render'lar birlikte açıldı. Genel kadraj görev alanını ve zirveyi kesiyordu; turuncu dünya ışığı kayaları ve sisi kahverengiye çekiyordu. Yüz başına iki farklı materyal kullanımı yamaçta kareli bir görüntü oluşturuyordu. Renk geçişi tek kaya materyaline alındı, yüzey noktaları düzensizleştirildi, gökyüzü ile dolgu ışığı ayrıldı. Prop pivotları ve FBX'te birleşen çift taraflı çimen üçgenleri düzeltildi.
- Tur 2 — `renders/round_02/`: üç render ve üç referans tekrar açıldı. Gri zirve, kum renkli kaya ve mor-turuncu gök ayrıştı. Genel kamera fazla uzak, yakın kamera fazla cepheden kalıyordu. Kamera konumları yaklaştırıldı; yamaçtaki düz ızgara düzeni azaltıldı. Rampa yol ağzındaki yüzey çakışmaları gerçek mesh raycast ölçümüyle giderildi; viraj yollarına kaya desteği eklendi.
- Tur 3 — `renders/round_03/`: üç karşılaştırma açıldı. Yakın planda minibüsün burun aşağı pozu ve açık kırık korkuluk okunur hâle geldi. Genel render'da zirve ucu ve minibüs burnu kadraj sınırına çok yakındı. Kadraj payı, iri duvar çıkıntıları ve daha yatay uzak sırtlar için ek tur yapıldı.
- Tur 4 — `renders/round_04/`: üç karşılaştırma açıldı; 01 ayrıca özgün çözünürlükte yeniden incelendi. Paletin kaya/toprak/olive örnek noktaları yüzeylerin merkezine düzeltildi. Zirve ve minibüs için güvenli kadraj uygulandı; kaya duvarına iri, ayrı siluet veren çıkıntılar eklendi. Ölçek ve konum tablosu korunarak daha geniş bir genel kadraj tercih edildi.
- Son rötuş — `renders/round_05/`: üç karşılaştırma açıldı. W korkuluklara 22 mm kalınlık ve uç kapakları eklendi; yol tarafından da görülebilirler. Gevşek kayaların gerçek en büyük boyutu 0.6–1.5 m aralığına oturtuldu. Bagaj tabanları zemine raycast ile yerleştirildi. Tekrarlanabilirlikteki BMesh yüz sırası sorunu sabit halkalı pah geometrisiyle kaldırıldı.

## Üretim ve kapsam

- 24 ayrı FBX; `B1_Ucurum.blend`; `markers.json`; `stats.json`; 1920×1080 üç kamera ve 3840×1080 üç karşılaştırma. Önceki görsel turlar kendi alt klasörlerinde saklanır.
- Sabit seed: 120126. Metre, Blender Z yukarı. Catmull–Rom yol; trafik şeritleri seyahat yönünde 1 m aralıklı. Modül pivotları dünya orijininde; prop/kaya pivotları gerçek sınır kutusunun taban ortasında. Yerleşimler JSON içindedir.
- FBX: `FBX_SCALE_ALL`, `-Z` ileri / `Y` yukarı, `FACE`, `use_mesh_modifiers=False`. Vertex renkleri korunur; Unity'de bu varyasyonu görmek için vertex color okuyan materyal gerekir. Unity import bu işte yapılmadı.
- On ortak yüzey materyali ve vertex renkleri; görüntü texture'ı yok. Gökyüzü ve prosedürel hacim yalnızca sunum içindir. `PH_Minivan`, `PH_TowTruck`, kameralar, ışık ve sis FBX'e alınmaz. Backdrop collider bayrağı kapalıdır.
- Minibüs 5×1.9×2 m kaba beyaz kutu + dört silindir, 28° burun aşağı; çekici kaba sarı/turuncu kutulardır. Gerçek araç modeli, karakter, ağaç, tünel, bina, trafik aracı veya yazılı tabela eklenmedi.

## Kabul

Nihai sonuçlar ve yeniden içe aktarılan modül başına sayılar `stats.json` içindedir. `build_acceptance_1.log`, `build_acceptance_2.log` ve `verify_acceptance.log` son kabul çalıştırmalarını kaydeder.

- İki ardışık tam üretim: exit 0 / exit 0. Bağımsız FBX doğrulaması: exit 0, hata listesi boş.
- İki üretimde aynı SHA256: `af58a70b9c97b2b52890a729651f02778dd6cb78eb253181080e771d00afdeaa`.
- Blender 5.2.2 LTS; OPTIX / NVIDIA GeForce RTX 5080; AgX, 64 sample + denoiser.
- Son üretim: üç kamera toplam 21.738 saniye; karşılaştırmalar ve arşiv dahil render paketi 24.400 saniye; tam üretim 25.059 saniye.
- Ölçülen yol genişliği 7.999992–8.000007 m; patika Z=-9.000031…-8.999939 m; en dik rampa yüzü 17.344574°; yamaç ortalaması 48.586038°. Trafik uçları 16/16 görüş ışınında gizli; çıkıntı Z=-14 düz yüzey.

| Modül | Üçgen |
|---|---:|
| SM_B1_Ground | 4.244 |
| SM_B1_Road | 756 |
| SM_B1_RoadMarkings | 2.004 |
| SM_B1_Guardrail | 8.096 |
| SM_B1_Vegetation | 2.104 |
| SM_B1_Backdrop | 880 |
| SM_B1_LooseRock_01…08 | her biri 52; toplam 416 |
| SM_B1_Prop_Suitcase_01…02 | her biri 120; toplam 240 |
| SM_B1_Prop_Bag_03…05 | her biri 120; toplam 360 |
| SM_B1_Prop_Chevron_West/East_1/2 | her biri 32; toplam 128 |
| SM_B1_Prop_FallingRock | 50 |
| Oynanabilir / bütçe | 17.204 / 60.000 |
| Arka plan / bütçe | 880 / 30.000 |
| Prop + gevşek kaya | 1.194 |
| Bütün FBX'ler | 19.278 |

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' -b --factory-startup --python tools/blender/build_bolum1_ucurum.py
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' -b --factory-startup --python tools/blender/build_bolum1_ucurum.py
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' -b --factory-startup --python tools/blender/verify_bolum1_ucurum.py
```

Doğrulayıcı boş sahneye FBX'leri yeniden yükler: gerçek üçgenler, pivotlar, flat shading, yüzey raycast ölçüleri, rampa en dik yüzü, yamaç kesitleri, patika Z'si, çıkıntı düzlüğü, 16 trafik görünmezliği ışını, Blender/JSON işaret eşliği, minibüs gerçek ölçüleri, PNG çözünürlükleri ve iki ardışık SHA eşitliği kontrol edilir. Başarısızlık gerçek süreç exit 1 üretir.

## Görsel sınırlar ve ölçü yorumları

- Ana genel kadraj referansın yakın sahne kompozisyonundan daha geniştir; 220 m zirve ile görev mekânı aynı fiziksel sahnede ve aynı karede korunur. Yakın ve plan kameraları görev düzenini tamamlar. Referansla birebir kompozisyon eşleşmesi elde edilmedi.
- Sis hacimseldir ve üst sınırı Z=-40 m'dir; referanstaki kabarık bulut denizine göre üst yüzeyi daha düzgündür. Uzak sırtlar ve kaya yüzleri daha sade kalır.
- Yaklaşık X=-35 / Z=-1'den X=-12 / Z=-9'a inen rampanın ölçülen eğimi yaklaşık 17.35°'dir; brief'teki yaklaşık 20°'den düşük, 22° üst sınırının altındadır. Rampa genişliği 1.8 m'dir.
- Kaza cebinin önünde yamaç yer yer daha diktir; banketin ek 5 m derinliği ile patikanın konumu birlikte korunmuştur. Yürünemez ana kesitlerin ortalaması yaklaşık 48.59°'dir.

## AÇIK SORULAR

- Levhalarda istenen nihai renk standardı nedir? Bu teslim nötr sarı zemin + siyah sembol kullanır; hiçbir tabela yazısı içermez.

## 2. tur (Fable)

Codex'in `r2_round_04` durumunu devraldım. Bölümün üstündeki tablo ve sayılar eski turlardan kalmadır; güncel değerler aşağıda ve `stats.json` içindedir. Her turda üç render ile 01/02/03 referansları (`compare_*.png`) açıldı. Arşiv: `renders/r2f_round_01…09`.

- `r2f_round_01`: S yol uygulandı (kullanıcı kontrol noktaları, uçlarda ±44,49 uzatması korundu). Merkez kesitler X ±46 ile sınırlandı. Ötesinde tek bir yükseklik fonksiyonu (dağ gövdesi + yol yarması + aynı iniş profili) çalışıyor: uçurum ve yamaç virajların ötesinde sürüyor, dağ gövdesi zirveye bağlanıyor. |x|≤60 ve y≤35 hücreleri Ground'a, kalanı Backdrop'a gidiyor. Kaza düzeni yola göre yeniden kuruldu: çıkıntı, bagaj ve fren izleri yol eksenine bağlı. Levhalar beyaz zemin + kırmızı çerçeveli tehlike üçgeni ve kırmızı zeminde beyaz oklu chevron oldu. Sorunlar: yamaç büyük düz yüzlü, kaza izi geniş turuncu bir yarık gibi; genel kamera alçak, zirve tepesi kesik.
- `r2f_round_02`: Yamaç 4 ara noktalı ve daha sık kesitli, yüzeyi kaya materyali (puantiyenin kaynağı turuncu toprak zemin + tek tek kayalardı). Kayma izi X −1.5…+5 toprak şeridine daraltıldı. Rampa üstünde ve kaza ağzının iki yanında yarı gömülü kümeler var. Sorun: uçurum alnında hâlâ tek tek bloklar, minibüs kadrajın altında kesik.
- `r2f_round_03`: Uçurum blokları birbirine kaynayan dikey sütunlara çevrildi. Batı/doğu gövdesi uzağa doğru yükseltildi. Yakın kamera 02 gibi yolun üstüne alındı. Sis düz beyaz bir duvar gibi duruyordu.
- `r2f_round_04`: Sis keskin üst yüzeyli, iki ölçekli gürültülü, pembe-lila bir bulut denizine çevrildi. Yamaçtaki rastgele toprak hücreleri (şerit görüntüsü) kaldırıldı. Fren izleri görünmüyordu.
- `r2f_round_05`: Uç parçalar ±1000 m'ye uzatıldı, yamaç kümeleri sıklaştırıldı. Ölçüm: fren izleri yerinde, ama yakın kameranın açısından korkuluğun arkasında kalıyor.
- `r2f_round_06`: Yakın kamera yükseltildi. y<35 dağ gövdesi alçaltıldı: viraj ötesinde yamaç yol seviyesinde sürüyor, gövde arkada yükseliyor. Genel kamera sisi sola aldı ama zirve tepesi taştı.
- `r2f_round_07`: Genel kamera, nokta izdüşümüyle hesaplandı (zirve ucu y≈65 px, minibüs y≈956 px). Metal olmayan yüzeylerde parlama azaltıldı, asfalt biraz açıldı. Fren izleri artık CAM_Yakin'de kırık noktaya kıvrılan iki koyu iz olarak okunuyor.
- `r2f_round_08`: Yan ufuk sırtları denendi ama soluk sivri ek zirveler gibi okundu (spesifikasyon dışı görünüm).
- `r2f_round_09` (son): 08'deki yükseltme geri alındı. Geometri 07 ile aynı (aynı SHA).

Son durum: 3 kabul çalıştırması exit 0 (iki build + verify), SHA256 `384de5d7726e95c504c8c1092680f5259a2860334ed8696b949e6c2ec7965529`; 3 ardışık build'de aynı.

| Modül | Üçgen |
|---|---:|
| SM_B1_Ground | 23.176 |
| SM_B1_Road | 792 |
| SM_B1_RoadMarkings | 2.164 |
| SM_B1_Guardrail | 8.624 |
| SM_B1_Vegetation | 3.222 |
| Oynanabilir (hedef 30–45k) | 37.978 |
| SM_B1_Backdrop (hedef 8–20k) | 15.522 |
| LooseRock_01…08 (her biri farklı) | 46/44/80/60/68/96/84/140 = 618 |
| Prop'lar (bagaj, levhalar) | 718 |
| Toplam | 54.836 |

Ölçümler: yol 7.99999–8.00001 m; patika Z −9.00003…−8.99997; rampa en dik 18.88°; yamaç ortalaması 48.22°; trafik uçları 16/16 ışında gizli; 8 benzersiz gevşek kaya.

Kalan sapmalar:
- CAM_Genel 13 mm geniş açıdır. Zirve (−20,180) ve 220 m yüksekliğiyle kaza noktasından ~45° yukarıda kalıyor. Kaza bandı kadrajın alt üçte birinde; 01'deki kadar yakın değil.
- Fren izleri yalnız CAM_Yakin'de okunuyor. Genel kamera yol seviyesinde olduğu için asfaltı görmüyor.
- Viraj sonrası yol uzatmasının ucu kayaya gömülü biter. Yalnız plan görüntüsünde görünür.
- Banket önündeki yamaç (X 5…25) 9 m'yi ~2.8 m yatayda iner, çok diktir (patika konumu korunduğu için).

### AÇIK SORULAR (Fable)
- Zirve 01'deki gibi kadrajda doğal açıyla görünsün mü? Bunun için zirvenin ~2 kat uzağa (y≈+400) alınması gerekir; ya da genel kamera bu geniş açıyla kalır.
- Chevron'larda kırmızı zemin + beyaz ok seçildi. Ters düzen (beyaz zemin + kırmızı ok) mi isteniyor?
- Tehlike üçgenindeki sembol siyah "düşen kaya" silueti olarak bırakıldı. Uygun mu?

## 3. tur (Fable)

Kullanıcı kararları (2026-10-13) uygulandı. Arşiv: `renders/r3_round_01…05`. Her turda CAM_Genel 01 ile, CAM_Yakin 02 ile karşılaştırıldı; CAM_Levha ayrıca açıldı. Kamera kadrajları önce önemli noktaların (zirve ucu, minibüs, kırık nokta, çekici, rampa başı, sis) izdüşümüyle hesaplandı, sonra render'da kontrol edildi.

- `r3_round_01`: Zirve (−180,420) konumuna taşındı ve uzak dağ ölçeğine indirildi; sağında küçük ikinci zirve var. Yamaç üstündeki kare yeşil hücreler kaldırıldı. Kaya araları için düzensiz tutam kümeleri eklendi (SM_B1_Vegetation). Kaya yamacı dört kırık noktayla yoldan dik kalkıyor; raf bölgesinde Z≤9.3 tutuluyor, raf yamaçta çıkıntı olarak okunuyor. CAM_Levha eklendi. Sorunlar: yolun üstü hâlâ plato gibiydi, genel kadrajın yarısı sis ve batı masasıydı, chevron'lar kayaların arkasındaydı.
- `r3_round_02`: Merkezde sırt tanımlandı: önü dik, y≈45'ten sonra arkası iniyor ve zirveye görüş açılıyor. Doğu omzu (kadrajın sağı) yüksek kayalık dağ olarak tırmanıyor. Genel kamera (28,−46,18), 16 mm ile kaza bandı kadrajı dolduruyor. Sorunlar: zirvenin yalnız ucu görünüyordu; chevron'lardan biri kısmen görünüyordu.
- `r3_round_03`: Omuz doğuya kaydırıldı, eyer alçaltıldı, sırta yarı gömülü kayalar konuldu. İkinci zirve ana zirvenin hemen sağına alındı. Chevron'lar virajın başına yaklaştırıldı.
- `r3_round_04`: Batıdaki pürüzsüz kubbe (zirveyi kapatan batı sırası) kaldırıldı. Zirve sırtın arkasından yükseliyor. CAM_Levha'da tehlike üçgeni ve iki chevron tam okunuyor.
- `r3_round_05` (son): 04 ile aynı geometri ve kamera, kabul öncesi temiz üretim.

Son durum: kabul exit 0 (iki build + verify), SHA256 `1bb560bda0208558701b9c0255c7d35a0a898ba7cbb225a74dfeef8e1653fe6e`, ardışık build'lerde aynı. FBX'ten yeniden okunan sayılar: Ground 22.560, Road 792, RoadMarkings 2.164, Guardrail 8.624, Vegetation 5.173 → oynanabilir 39.313; Backdrop 14.178; prop + gevşek kaya 1.336; toplam 54.827. Ölçümler 2. turla aynı: yol 8.000 m, patika Z −9.000, rampa 18.88°, yamaç 48.23°, trafik 16/16 gizli.

Notlar:
- Zirve kararla uzağa taşındı; konum (−180,420), tepe ≈150 m. X de değişti, çünkü sağda görünmesi için kameraya göre o yönde olması gerekiyor. Sırt arkası zirveye görüş açmak için iniyor; batı ufkunda yüksek sıra yok, orada sis var.
- Blender `--python` betikte sözdizimi hatası olursa da exit 0 döner. Doğrulamada `VERIFY OK` satırı ayrıca aranmalı.

### AÇIK SORULAR (3. tur)
- Kadraj sıkı ama zirve ile kaza bandının açı farkı nedeniyle minibüs kadrajın alt %15'inde. Zirve biraz daha alçak/uzak olsun mu, yoksa minibüs ortaya mı alınsın (zirve daha küçük görünür)?
- Batı ucunda masa gibi yükselen uçurum sırtı korunacak mı, yoksa daha alçak/kayalık mı olsun?

## 4. tur (Fable)

Kullanıcı kararları (2026-10-13) uygulandı. Arşiv: `renders/r4_round_01…03`. PH_Minivan'a dokunulmadı (ölçü ve poz aynı).

- `r4_round_01`: Izgara izi kaldırıldı. Oyun alanı dışındaki yükseklik alanında iç noktalar konuma bağlı gürültüyle kaydırılıyor (paylaşılan dikiş noktaları hariç). Her hücrenin köşegeni ve her üçgenin tonu konumdan türeyen bağımsız rastlantıyla seçiliyor; geniş ölçekli ton değişimi çok düşük genlikli. CAM_Plan'daki dama deseni kalmadı. Oyun alanı içindeki uç parçaları (|x|≤60) aynı yoldan geçiyor. Batı ucu: masa gibi düz üst yerine 16 m'lik kademelerle inen sırt (+22 → −6) ve üstünde 1–3'lü yarı gömülü kaya kümeleri. Zirve alçaltıldı (tepe ≈130 m). Sorun: kamera uzak, minibüs ortada ama sahne küçük; zirve dik yamacın arkasında kalıyor.
- `r4_round_02`: Zirvenin görüş hattı kaza noktasının arkasındaki yamaç sırtından geçiyordu. Bu hatta (x≈−2 merkezli) doğal bir eyer açıldı, sırt kayaları o koridordan çekildi. Raf (Z=10) ve gevşek kayalar yerinde. İkinci zirve, eyerden görünecek şekilde ana zirvenin ~5° sağına ve biraz öne (−120,390) alındı. Kamera izdüşüm + görünürlük ışını taramasıyla seçildi. Sorun: kadrajın alt yarısı doğu uçurum yüzü.
- `r4_round_03` (son): Minibüsün dikeyde merkez bölgede kalmasına izin veren taramayla kamera (28.4,−48.4,4), 17 mm seçildi. Minibüs ortada; yol, kırık korkuluk, cep/çekici, rampa ve patika kadrajda; sis solda; zirve ve ikinci zirve sağ üstte, eyerin üstünden görünüyor.

Kabul `--python-exit-code 1` ile çalıştırıldı: iki build + verify exit 0, log'da `VERIFY OK`. SHA256 `0baa133784190a57de15856df9ee03532041280a8a28d40713ad25613a471754` (ardışık build'lerde aynı). FBX'ten yeniden okunan sayılar: Ground 21.086, Road 792, RoadMarkings 2.164, Guardrail 8.624, Vegetation 5.037 → oynanabilir 37.703; Backdrop 17.104; prop + gevşek kaya 1.336; toplam 56.143. Ölçümler değişmedi: yol 8.000 m, patika Z −9.000, rampa 18.88°, yamaç 48.23°, trafik 16/16 gizli.

### AÇIK SORULAR (4. tur)
- Zirve ancak eyerden görünür. Eyer kaza noktasının arkasındaki yamacı x≈−15…+10 arasında ~9 m alçaltıyor. Bu kabul mü, yoksa yamaç yüksek kalıp zirve daha küçük mü görünsün?
- Minibüs merkezde olduğundan zirve kadrajda küçük (uzak dağ). Daha büyük görünmesi için zirve yükselirse kamera uzaklaşmak zorunda kalır.

## 4b (Fable)

Kullanıcı kararları (2026-10-13) uygulandı. Arşiv: `renders/r4b_round_01…02`. PH_Minivan'a dokunulmadı.

- Eyer geri alındı: `bline` içindeki −9 m'lik Gauss çukuru, sırt kayalarındaki iki görüş koridoru boşluğu (`row>=2 and -14<x<10`, `-16<x<12`) kaldırıldı. Kaza arkasındaki yamaç ve sırt kayaları 3. turun yükseklik ve dizilimine döndü. Eyer için kaydırılmış zirve değerleri de 3. turdaki hâline (ana tepe ≈150 m, ikinci zirve (−135,470)) alındı.
- 4. turdan kalanlar: batı ucunda kademeli inen kayalık sırt ve ızgarasız ton (CAM_Plan'da dama deseni yok).
- CAM_Genel kadrajı `world_to_camera_view` + görünürlük ışını taramasıyla seçildi. Hedef minibüs gövde merkezi; minibüs, çekici, kırık korkuluk noktası, yolun iki ucu, rampa başı/sonu ve patika uçları kadrajın %5–95'i içinde ve görünür olmalıydı. Ayrıca alt yarıda dik uçurum yüzüne (|n.z|<0.45, Z<−9.5) çarpan ışın oranı ölçüldü. Alçak kameralar (eğim 14–29°) gökyüzü ve zirveyi gösteriyor ama alt yarının %40–58'i uçurum duvarı oluyor (4. turun sorunu). Bu yüzden yüksek 3/4 bakış seçildi (01 referansındaki gibi); duvar oranı ≈%28.
- `r4b_round_01`: kamera (36.7,−38.5,24.1), 18 mm, eğim ≈41°. Minibüs tam ortada; yol, kırık korkuluk, cep/çekici, rampa ve patika kadrajda; sol yarıda sis denizi; alt sağda kayalık raf ve yamaç. Zirve kadraj dışında (zorlanmadı).
- `r4b_round_02` (son): kamera (38.1,−39.3,22.1), 18 mm, eğim ≈38°; biraz daha az tepeden bakış, içerik 01 ile aynı. Ara adayların hızlı render'ları (eğim 23–44°) karşılaştırıldı, alçak olanlar duvar ağırlıklı olduğu için elendi.

Kabul `--python-exit-code 1` ile: iki build + verify exit 0, log'da `VERIFY OK`. SHA256 `44d106cf2fa329884f776119b61d1dd27af05e33439efdc3345efc395dbfc94c` (ardışık build'lerde aynı). Sayılar: Ground 22.538, Road 792, RoadMarkings 2.164, Guardrail 8.624, Vegetation 4.916 → oynanabilir 39.034; Backdrop 17.698; prop + gevşek kaya 1.336; toplam 58.068. Ölçümler değişmedi: yol 8.000 m, rampa 18.88°, yamaç 48.23°, trafik 16/16 gizli. Temiz klon kabulü commit'ten sonra ayrıca çalıştırıldı (sonuç raporda).

### AÇIK SORULAR (4b)
- Minibüs ortada ve yamaç yüksekken zirve genel kadraja girmiyor. Zirveyi gösteren ayrı bir sunum kamerası (ör. 3. turun kadrajı) istenir mi?
- Zirve konumu eyer için 4. turda değiştirilmişti; eyerle birlikte 3. tur değerlerine döndürüldü. Kararın kapsamı bu mu?

## 5. tur (minibüs yerleştirme)

2026-10-13, Codex. `PH_Minivan` yerine `Export/Minivan/SM_Minivan.fbx` kapalı kaynak pozuyla içe aktarıldı. Kaynak asset ve üretim betiği değiştirilmedi. Minibüs yalnız `.blend` sunum sahnesindedir; mekân FBX modüllerine dahil edilmez. `PH_TowTruck`, mekân geometrisi, ışıklar ve 4b'nin dört kamerası korundu.

- `renders/r5_round_01`: Dört kamera yeniden render edildi ve açılıp incelendi; `Export/Minivan/renders/threequarter_front_left.png` ile model karşılaştırıldı. Genel kadrajda açık mavi minibüs merkezde, yakın kadrajda burun ve ön tekerlekler boşlukta; arka tekerlekler patika kenarında. Kapılar kapalı, port bagajdaki kahverengi ve mavi iki bavul yerinde. Plan görüntüsünde uzun eksen −Y yönünde; levha kamerası minibüsü görmez, mevcut levhaları gösterir. Görsel düzeltme gerektiren gömülme görülmedi.
- `renders/r5_round_02`: Aynı yerleşim tekrar üretildi; dört render ve aynı minibüs referansı tekrar açılıp incelendi. İlk turdaki poz, model silueti, kapalı kapılar ve iki bavul korundu. Kamera veya araziyi değiştirmeyi gerektiren sorun bulunmadı. Bu tur son görsel durumdur.

### Yerleşim ve ölçüm

`markers.json` içindeki `markers.MinivanPlacement` (aynı kayıt `placements.MinivanPlacement` içinde de vardır), Blender koordinatlarında metre cinsinden kök dönüşümüdür. Quaternion sırası açıkça **WXYZ**, Euler sırası **XYZ** olarak kaydedilir; Unity koordinat dönüşümü uygulanmış değerler değildir.

| Alan | Değer |
|---|---|
| Kök XYZ (m) | (4.000000, −19.028999, −9.710909) |
| Euler XYZ (derece) | (28.000000, 0, 0) |
| Quaternion WXYZ | (0.970295727, 0.241921902, 0, 0) |
| Ölçek | (1, 1, 1) |
| İki arka tekerin en alt Z değeri | −8.999999 m |
| Patika yüzeyine en büyük temas farkı | 0.000031472 m (0.032 mm) |
| En büyük ölçülen gömülme | 0.000000 m |

Yerleşim, dönmüş arka tekerlek mesh'lerinin gerçek alt noktalarından hesaplanır. Her iki temas noktası patika kenarının en az 12 cm içinde kalacak Y seçilir; tabanlar Z=−9 m'ye oturtulur. Gömülme kontrolü bütün minibüs mesh'lerinin üçgen köşelerini ve içlerini en fazla 2 cm örnek aralığıyla tarar (3.356.190 yüzey örneği); arazi BVH'sine düşey ışınla ölçülen yüzey altı derinliği en fazla 2 cm olabilir. Arka tekerlerin tüm en alt köşeleri ayrıca ±3 cm temas toleransıyla kontrol edilir. Tüm parça mesh'leri ve köke göre dönüşümleri kaynak FBX'le karşılaştırılır; bu kontrol kapalı kapıları ve yerinde kalan bavulları da kapsar.

Mekân geometri SHA256'sı **4b ile aynı**: `44d106cf2fa329884f776119b61d1dd27af05e33439efdc3345efc395dbfc94c`. Minibüs yerleşimi/kaynak SHA kaydının özeti: `de2a7e903db2f10f5f72b40670c4edde5ee00c24f953b8170cd3a27ab6dc6890`. Bunlar geometri/yerleşim özetleridir; zaman bilgisi içeren `.blend`, FBX veya `stats.json` dosyalarının ham bayt özeti değildir.

Üçgen sayıları değişmedi: Ground 22.538; Road 792; RoadMarkings 2.164; Guardrail 8.624; Vegetation 4.916 → oynanabilir **39.034**. Backdrop **17.698**; prop + gevşek kaya **1.336**; mekân toplamı **58.068**. Ayrı minibüs **6.552** üçgendir ve bu toplama dahil değildir.

### Doğrulayıcının yazma davranışı

`verify_bolum1_ucurum.py` artık `stats.json` veya başka bir repo dosyası yazmaz. Sonuç stdout'a ve `tempfile.gettempdir()/verify_bolum1_ucurum_<worktree-hash>.json` dosyasına gider. `stats.json` yalnız build tarafından yazılır. Git komutu ve commit çalıştırılmadı.

Kabul: `blender -b --factory-startup --python-exit-code 1 --python tools/blender/build_bolum1_ucurum.py` iki kez, tur argümanı olmadan çalıştırıldı: ikisi de exit 0 / `BUILD OK`, yukarıdaki iki SHA256 her iki çalıştırmada aynı. Log'lar `build_acceptance_1.log` ve `build_acceptance_2.log`. Ardından aynı Blender seçenekleriyle `verify_bolum1_ucurum.py`: **exit 0 / VERIFY OK**, hata listesi boş. Verify öncesi/sonrası worktree'deki gizli dosyalar dahil **389 dosyanın SHA256 karşılaştırması: 0 değişen, 0 eklenen**. Böylece git çalıştırmadan dosya yazma yan etkisi sınandı; `git status` kartın yasağı gereği çalıştırılmadı. Rapor sistem geçici klasöründeki `verify_bolum1_ucurum_cd1402f9293f.json` dosyasında.

Negatif kontroller (yalnız bellekte, sahne kaydedilmedi): minibüsü 5 cm aşağı kaydırmak gömülme sınırını, 5 cm yukarı kaydırmak teker temas sınırını, MinivanPlacement kaydını kaldırmak zorunlu işaret kontrolünü başarısız kıldı. Üç kontrol de beklenen hatayı verdi.

### AÇIK SORULAR (5. tur)

- Yeni açık soru yok. Sabit kameralarda minibüs detayları küçük olduğundan görsel kontrol, mesh teması ve kaynak poz karşılaştırmasıyla desteklendi; ek kamera eklenmedi.

### Devir (Fable)

2026-10-13, Claude Fable. Codex kullanım limitine takıldığı için iş e308b26'dan devralındı. Kod değiştirilmedi; beş madde yeniden ölçülerek doğrulandı:

- Build `--python-exit-code 1` ile iki kez: exit 0 / `BUILD OK`; geometri SHA256 `44d106cf…c94c` ve yerleşim SHA256 `de2a7e90…6890` iki çalıştırmada aynı.
- Verify: exit 0 / `VERIFY OK`, hata yok. Ölçüm: burun 28.000°, en büyük gömülme 0.000 m (3.356.190 örnek), arka teker tabanları Z=−9.000 m, patikaya en büyük fark 0.031 mm.
- Verify öncesi ve sonrası `git status --porcelain --untracked-files=no` aynı; verify izlenen dosya yazmıyor.
- `CAM_Yakin` ve `CAM_Genel` yeniden açıldı ve `Export/Minivan/renders/threequarter_front_left.png` ile karşılaştırıldı: arka tekerler patika kenarında, burun boşlukta, kapılar kapalı, iki bavul port bagajda.

## 6. tur (çekici yerleştirme)

2026-10-13, Codex. `PH_TowTruck` yerine `Export/TowTruck/SM_TowTruck.fbx` içe aktarıldı. Burun doğuya (+X), arka kasadaki vinç döner tabanı batıya, kazaya yakın uca bakıyor. Vinç dinlenme pozunda, ayaklar toplu, kapılar kapalı; dört koni kasadaki tutucuda. Çekici ayrı asset olarak yalnız sunum `.blend` dosyasında bulunur; mekân FBX modüllerine eklenmez. Kaynak çekici dosyalarına yazılmadı. Lineer renk kuralı korundu; sRGB export düzeltmesi eklenmedi.

- `renders/r6_round_01`: `CAM_Genel`, `CAM_Plan`, `CAM_Yakin`, `CAM_Levha` açılıp incelendi; `Export/TowTruck/renders/threequarter_front_left.png` ile karşılaştırıldı. Genel görüntüde turuncu çekici yol ile dış korkuluk arasındaki ceptedir. Plan görüntüsünde doğuya bakan kabin ve banket sınırları okunuyor. Yakın görüntüde vinç tabanı minibüse yakın batı ucunda, görünür tekerler yüzeyde; belirgin gömülme veya havada kalma görülmedi. Levha kamerası çekiciyi görmez; mevcut levha kadrajı korundu.
- `renders/r6_round_02`: Aynı dört kamera yeniden render edildi; dördü ve çekici referansı tekrar açılıp incelendi. İlk turdaki doğru yön, yatık kol, kapalı kapılar, toplu ayaklar ve dört koni korundu. Yerleşimi veya kameraları değiştirmeyi gerektiren görsel sorun yok. Bu tur son görsel durumdur. Sabit kadrajlardaki küçük ayrıntılar aşağıdaki mesh ölçümleri ve kaynak poz karşılaştırmasıyla desteklendi.

### Yerleşim ve ölçüm

`markers.json` içindeki `markers.TowTruckPlacement` ve `placements.TowTruckPlacement` aynı kayıttır. Minibüsle aynı biçimde Blender koordinatları, metre, quaternion **WXYZ**, Euler **XYZ/derece** ve kaynak FBX SHA256 bilgisi taşır.

| Alan | Değer |
|---|---|
| Kök XYZ (m) | (15.000000, −8.910153389, 0.463874489) |
| Euler XYZ (derece) | (−2.404589228, 0, 90.000002504) |
| Quaternion WXYZ | (0.706951082, −0.014836839, −0.014836839, 0.706951141) |
| Ölçek | (1, 1, 1) |
| RecoveryTruckSlot'a göre XYZ farkı (m) | (0, 0, −0.005306751) |
| En büyük ölçülen gömülme | 0.000038177 m = 0.038 mm |
| Altı tekerde en büyük mutlak temas farkı | 0.002900213 m = 2.900 mm |
| Banket dışına / yola taşan yüzey örneği | 0 / 0 |
| Korkulukla kesişen üçgen çifti | 0 |

Slot içinde **yatay kaydırma yapılmadı**. Banketin boyuna eğimi dingillerin altındaki gerçek arazi yüzeyinden hesaplandı; yalnız araç kökü eğildi. Ardından altı lastiğin mesh köşelerinin yüzeye oturmasını sağlayan en küçük kök Z değeri seçildi. Kaynak parçaların birbirine göre konumları ve dönüşleri değiştirilmedi.

| Teker | Taban Z (m) | Banket yüzeyine fark (mm) |
|---|---|---|
| F_L | 0.572959185 | +0.938 |
| F_R | 0.572959185 | +0.938 |
| R1_L | 0.400940984 | +2.900 |
| R1_R | 0.400940984 | +2.870 … +2.900 |
| R2_L | 0.354789704 | −0.038 |
| R2_R | 0.354789704 | +0.023 |

Doğrulayıcı, banketin 40 çakıl yüzey üçgenini gerçek mesh'ten ayırır. Bütün çekici mesh'lerinde üçgen köşeleri ve içleri en fazla 2 cm aralıkla taranır: **6.983.759 örnek**. Araziye gömülme ≤2 cm, her teker tabanının bankete mesafesi ±3 cm, tüm aracın banket içinde ve yol dışında kalması kontrol edilir. Korkulukla üçgen kesişimi ayrıca BVH üzerinden ölçülür. FBX yeniden içe aktarılarak her parçanın mesh köşeleri, köke göre dönüşümü ve parent ilişkisi karşılaştırılır; dinlenme pozu ve dört koni böylece de doğrulanır.

İlk ölçümde banket filtresi yol altlığından 17 üçgeni de seçti; filtre gerçek 1 m genişlikteki, yaklaşık 5 m enindeki banket şeritleriyle sınırlandı. Yerleşim değişmedi. İlk verify'daki diğer hata, henüz ikinci çekici build'inin bulunmamasıydı; iki ardışık build sonrası giderildi.

### Kabul ve korunan öğeler

- Karttaki `blender -b --factory-startup --python-exit-code 1 --python tools/blender/build_bolum1_ucurum.py` komutu tur argümanı olmadan iki kez çalıştırıldı: **ikisi de exit 0 / BUILD OK**. Log'lar `build_r6_acceptance_1.log`, `build_r6_acceptance_2.log`.
- Mekân geometri SHA256'sı iki build'de de öncekiyle aynı: **`44d106cf2fa329884f776119b61d1dd27af05e33439efdc3345efc395dbfc94c`**. Üçgenler değişmedi: oynanabilir 39.034, arka plan 17.698, prop 1.336; toplam 58.068.
- Çekici yerleşim SHA256'sı iki build'de aynı: `796106e69af001fecfc7cbb21034cd7017f68ef27e1d97c3f65f796b58e15e48`. Minibüsün mevcut yerleşim SHA256'sı değişmedi: `436721b3b8f7b83cc93151baeb81319cc0ba5d06e79f054d3ef06d76d67055a4`.
- Aynı Blender seçenekleriyle `verify_bolum1_ucurum.py`: **exit 0 / VERIFY OK**, hata listesi boş. Verify öncesi/sonrası gizli dosyalar dahil **467 worktree dosyasının SHA256 karşılaştırması: 0 değişen, 0 eklenen, 0 silinen**. Git yasağı nedeniyle `git status` çalıştırılmadı. Kontrol tamamlandıktan sonra çağıran kabuk `acceptance_r6.json` ve `verify_r6_acceptance.log` kanıt dosyalarını yazdı; verify'ın kendisi yalnız stdout ve sistem geçici klasöründeki `verify_bolum1_ucurum_c154ecd2f2ac.json` dosyasına yazdı.
- Negatif kontroller yalnız bellekte yapıldı, sahne kaydedilmedi: kayıt kaldırma, kökü 6 cm aşağı/yukarı alma, yola 3 m ve korkuluğa 2.5 m kaydırma. Beşi de ilgili kontrolü başarısız kıldı; son ikisi sırasıyla 4.966.683 yol örneği ve 617 korkuluk kesişimi yakaladı. Kanıt: `negative_checks_r6.log`.
- Ana sanat reposunun mevcut `.blend` dosyasıyla çekici dışındaki **62 nesne** karşılaştırıldı: nesne türleri, dünya dönüşümleri, mesh köşeleri/yüzleri ve kamera lens/ortografik ölçekleri birebir aynı. Mekân, minibüs, diğer öğeler ve dört kamera korundu.
- Temiz klon kabulü şef tarafından yapılacak. Git komutu çalıştırılmadı, commit atılmadı; commit hash'i yok, orkestra betiğine bırakıldı.

### AÇIK SORULAR (6. tur)

- Yeni açık soru yok. Temiz klon kabulü devirde bekliyor.
