# Çekici (vinçli kurtarma aracı) — görsel inceleme ve teslim notları

Referanslar doğrudan açılıp incelendi: `01_turnaround_orange.jpg` (ana konsept),
`02_minivan_style_match.png` (detay/pah dili), `03_scene_style.jpg` (flat-shaded chunky low poly).
Konsept ölçü kaynağı değildir: 8.0 × 2.5 × 3.2 m sözleşmesi konseptteki kısa karikatür araca göre
daha uzun bir siluet verir. Burun −Y (minibüsle aynı), aracın solu +X, sağı −X; sürücü solda.
Yazı, logo, amblem, plaka yazısı yok.

## Tur 01 — ilk siluet ve mekanik yerleşim

- `renders/round_01/`: yedi teslim açısı, `scale_with_minivan.png`, dört `compare_*.png`.
- 6.612 üçgen. Kabin, 3 aks / 6 tekerlek, döner taban + kule, 6.0 m kol ve 2 teleskop kademesi, makara, kanca,
  4 ayak (kiriş + kriko), 4 dolap kapağı, çakar, 4 koni ayrı parça.
- Gözlem: arka kısım düz platform gibi duruyor; konseptteki dolaplı yüksek servis gövdesi yok.
  Dolaplar platform altında kalmış, araç kamyon-kasa gibi okunuyor.
- Gözlem: karşılaştırma görsellerinde model küçük kalıyor; ölçek görselinde araç kadrajdan taşıyor.
- Karar: arka bölümü 1.75 m üst kotlu dolaplı servis gövdesine çevir; döner taban ve koni tutucusunu gövde üstüne al.
  Karşılaştırmada render'ı aracın sınırına kırp; ölçek kamerasını iki aracı kapsayacak şekilde ayarla.

## Tur 02 — servis gövdesi ve dolap menteşeleri

- `renders/round_02/`: aynı set.
- 6.780 üçgen. Konseptin yakın plan incelemesinde öndeki uzun dolabın menteşe tırnakları ön dikey kenarda,
  tekerlek üstündeki dolapların menteşeleri üst kenarda görüldü; kapaklar buna göre ayrıldı.
- Gözlem: kabin kapısının ön kenarında 1 cm'lik kama boşluğundan arka plan sızıyor (yan görünümde ince açık çizgi).
- Gözlem: arka yüzde çift stop lambası (eski platform lambaları kalmış). Kapı camı küçük.
- BVH: çalışma pozunda en derin giriş öndeki dolap kapağı menteşe tırnağında 15 mm (sınır 20 mm).
- Karar: kabin yan panelinin çamurluk yayını kapı kenarına tam oturt; eski stopları kaldır; kapı camını büyüt;
  menteşe tırnaklarını incelt.

## Tur 03 — nihai kontrol

- `renders/round_03/`: front, rear, left, right, threequarter_front_left, threequarter_rear_right_work,
  interior_top, scale_with_minivan, compare_front / compare_rear / compare_side / compare_threequarter.
  `renders/` kökündeki PNG'ler bu turun kopyalarıdır.
- Kapı kenarı sızıntısı ve çift stop giderildi. Çalışma pozunda (taban sağa 90°, kol 35°, teleskop 10 m,
  4 ayak açık ve yerde, kabin kapıları 70°, dolap kapakları 90°, kanca CableTip'in 2 m altında) parçalar okunuyor.
- Üst görünümde iki koltuk, solda direksiyon ve panel görünür. Bu görüntü için yalnız render sırasında tavan yüzleri,
  kol zinciri, kanca ve çakar gizlenir (kol dinlenmede kabinin üstünden geçer). FBX ve Blend eksiksizdir.
- Minibüsle ölçek: minibüs 5.0 m, çekici 8.0 m; kabin tavanı (2.45 m) minibüs tavanından (2.0 m) yüksek.
- Son geometri **6.756 üçgen** (parça başına sayılar `stats.json` içinde).

## Konseptten bilinçli sapmalar

- Dinlenme pozunda kol kasaya yatık ve kapalı (konseptte kalkık çizilmiş). 6.0 m kol aracın arkasındaki
  menteşeden öne uzandığı için kabin tavanının üstünden geçer; kanca kabin tavanının 5 cm üstünde asılıdır.
- Koniler: tam 4 adet, tutucu içinde yan yana dik ve birbirine değmiyor (konseptte iç içe yığın).
- Kolu kaldıran hidrolik silindir yok: brief'teki parça listesinde yok, ayrıca ayrı eklemli parça gerektirir.
- Konseptteki 3. dolap kapağı, yakıt deposu, ön sinyal lambaları ve plaka yeri yok (brief: her yanda 2 kapak; ek aksesuar yok).
- Kabin konsepte göre daha köşeli ve alçak; low poly pah dili minibüsle uyumlu tutuldu.

## Teknik denetimin kapsamı

- `verify_towtruck.py` boş sahneye yalnız FBX'i yükler: adlar/hiyerarşi, üçgen bütçesi ve parça sayıları,
  ölçüler (aynalar hariç; yükseklik çakar hariç), tekerlek tabanları Z=0, pivotlar ve menteşe eksenleri,
  kol uzunluğu (kapalı 6.0 m, açık 10.0 m), çalışma pozunda BVH ile gövdeye giriş (≤ 2 cm), kriko tabanları Z=0,
  koni boyları ve konilerin birbirine/tutucuya girmemesi, ham FBX DiffuseColor = hex'in LİNEER karşılığı,
  iki ardışık build'de aynı geometri SHA256, FBX geometrisi = build geometrisi.
- Doğrulayıcı izlenen dosya yazmaz; raporu `tempfile.gettempdir()` altına yazar. `stats.json`'u yalnız build yazar.
- Renk: Linear URP projesi için Blender'ın varsayılan lineer çıktısı kullanılır, sRGB export düzeltmesi yoktur.
- Render: Cycles 64 sample, seed 0, denoiser, AgX, OptiX / RTX 5080. Süreler `stats.json` içindedir.
