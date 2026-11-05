# Servis modüllerinin yeniden modellenmesi

5 Kasım 2026. Beş taşınabilir servis modülü ve ortak kaide Blender'da sıfırdan, orta
yoğunlukta (mid-poly) yeniden modellendi. Araç, taşıyıcı, ofis, prefab yolları, katalog
kimlikleri, 24 kg fiziksel gövde, 0,66 × 0,44 × 0,46 m çarpışma kutusu, servis portunun
`(-0,29; 0,27; 0,20)` konumu, isim etiketi ve dört cıvatalı montaj değişmedi.

Tasarım dili servis minibüsüyle aynıdır: pahlı ana bloklar, düz renkli donuk malzemeler,
yazı/çıkartma/doku yok, küçük süs vidaları yok. Her modülün tanıtıcı bir rengi vardır;
kaide, çerçeveler ve gövde altları ortak antrasittir.

| Modül | Model | LOD0 / LOD1 / LOD2 üçgen | Önceki LOD0 |
| --- | --- | ---: | ---: |
| Çekici | Hardal döküm yan gövdeli elektrikli vinç: motor, planet dişli kutusu ve debriyaj kolu, sarılı çelik halat, bağlantı çubukları, üstte kontaktör kutusu, makaralı halat kılavuzu ve kanca | 5.624 / 1.876 / 540 | 195.896 |
| Su tahliye | Minibüs mavisi santrifüj pompa: kanatlı motor, fan kapağı, salyangoz gövde ve teğet basma ağzı, kamlok bağlantılar, boru koruma çerçevesi ve tutamaklar | 4.844 / 2.496 / 720 | 203.160 |
| Yangın | Kırmızı preslenmiş yanaklı hortum makarası: sarılı hortum, A ayaklı sehpa, kol, döner besleme, küresel vana, ön tutucuda lans | 6.244 / 2.100 / 632 | 31.884 |
| Teknik servis | Fildişi gövdeli servis güç ünitesi: eğik kumanda paneli (kapalı ekran, iki lamba, seçici), iki endüstriyel priz, ana şalter, yan panjurlar, taşıma sapı | 2.508 / 1.360 / 120 | 12.844 |
| Nakliye | Alüminyum, kaburgalı taşıma kasası: conta, köşe korumaları, iki ön mandal, katlanır yan saplar | 2.240 / 1.464 / 144 | 10.732 |
| Ortak kaide | Sac tabla, C profilli yan raylar, el açıklıklı yan levhalar, kauçuk ayaklar, servis soketi kulesi | 856 | — |
| Servis portu | Soket gövdesi ve iç yuva | 284 | — |

Elektrikli modüllerin (çekici, pompa, teknik servis) güç kablosu kaide üzerindeki soket
kulesine girer. Etiket rayı ön yan rayın yüzündedir; etiket tuvali eski yerinde durur.

## Kaynaklar ve aktarım

- Blender betiği: `Art/Blender/Scripts/gen_service_modules.py` (Blender 5.2). Beş modülün üç
  LOD'u aynı fonksiyonlardan üretilir; betik çarpışma zarfını ve servis portu boşluğunu denetler.
- Düzenlenebilir kaynak: `Art/Blender/Models/ServiceModules/service_modules.blend`.
- FBX: `Assets/_Project/Art/BlenderModules/Models/{towing,water,fire,tech,cargo}.fbx` ve
  `service_module_fixtures.fbx`. `module_manifest.json` artık beş modülü ve kaide/portu tanımlar.
- Renkler: `service_module_palette.json`; Unity malzemeleri `Assets/_Project/Art/ModuleEquipment/Materials/SM_*.mat`.
- Unity: `Company > Import Service Module Art` (`ServiceModuleArtImporter`). Cihazlar
  `BlenderModuleBuilder.ConfigureDevice` ile (artık beş modül), kaide ve port aynı eksen
  dönüşümüyle aktarılır; fizik ve port pozu değişirse işlem durur. `Company > Rebuild Portable
  Module Models` de kaide ve portu Blender kaynağından alır.
- Doğrulama isteği: `Temp/CompanyUIValidation.request` içine `service-modules-preview` (yalnız
  aktarım) veya `service-modules` (aktarım + elle montaj Play Mode testi).

Yeniden üretme:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' -b --factory-startup -P Art/Blender/Scripts/gen_service_modules.py
```

ardından Unity'de `Company > Import Service Module Art`; isteğe bağlı olarak
`Company > Render Service Modules In Garage` modülleri garaj sahnesinde taşıyıcıya takılı çizer.

## Görüntüler

Unity önizlemeleri: `ServiceModuleArt_{towing,water,fire,tech,cargo}.png`; garaj sahnesinde
taşıyıcıya takılı: `ServiceModuleGarage_<id>_yakin.png`, `ServiceModuleGarage_<id>_genis.png`.
Ofis mağazası küçük resimleri (`Resources/OfficeThumbnails`) yeniden çizildi.

## Doğrulama

- Blender betiği her LOD için çarpışma zarfını, servis portu boşluğunu ve LOD üçgen düşüşünü denetler.
- Unity aktarımı (`Logs/service_module_art.txt`): beş modülde Blender/Unity üçgen sayıları birebir
  aynı; kaide yön denetimi (soket kulesi −X'te, etiket rayı +Z'de) ve fizik/port pozu korunumu geçti.
- Görsel: beş Unity önizlemesi ve garaj sahnesinde taşıyıcıya takılı on görüntü incelendi.
- Elle montaj Play Mode testi (`Company > Validate Manual Module Installation`) bu makinede
  Unity 6000.5.5f1 ile koşturuldu ve **geçmedi**; eski modellerle de geçmedi (farklı bir adımda:
  "manual work position must not be inside Ground"). Yeni modellerle düştüğü adım bağlantı
  hedefinin ışın kontrolüdür; bu kontrol yalnız collider'lara bakar ve bu çalışma hiçbir collider'ı
  değiştirmez. Proje 6000.6.3f1 ile kaydedildiği için test 6000.6'da tekrar koşturulmalıdır.
