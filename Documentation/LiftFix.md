# Lift kullanım düzeltmesi

29 Ekim 2026. Kullanıcının kayıt durumunda iki modül de garajda, takılı modül kimliği boştu. Liftin eski kontrolü `IsSecured == false` durumunu, araçta modül bulunmasa da eksik montaj sayarak bütün hareketi reddediyordu.

Artık boş taşıyıcıyla lift kullanılabilir. Bağlantı kontrolü, yalnız taşıyıcı üzerinde gerçekten bir modül varsa uygulanır. Açık kapı ve devam eden modül işlemi kilitleri korunur. Kumanda, başlatmayı engelleyen nedeni E'ye basılmadan önce gösterir; hareket alanındaki oyuncu veya eşya için de açıklama vardır.

Önceki geniş araç güvenlik kutusu, kumandanın yakınında fiziksel olarak boş olan operator konumunu da kapsıyordu. Ana gövde hacmi gerçek gövde genişliğine çekildi, dışa taşan tekerlekler aks konumlarında ayrı kontrol edilir. Arka taşıyıcı ve basamak koruması korunur; güvenlik sorguları kaldırılmadı.

## Doğrulama

`Company/Validate Lift, Vehicle Doors and FPS` içinde gerçek Input System E girdileriyle çalıştırıldı:

- Modülsüz aracı kaydedip yükleme; taşıyıcı boş kalırken kaldırma.
- Kumandanın ön yanında, katı collider içinde olmayan `(6.03, 0.01, -0.35)` konumundan kullanma.
- E ile kaldırma, hareketi durdurma, yeniden başlatıp üst konuma ulaşma ve indirme.
- Gövde, arka taşıyıcı/basamak ve dış tekerlek bölgesinde oyuncu varken bekleme; alan boşalınca tamamlanma.
- İki cıvatası takılmış, bağlantısı açık modülde açıklayıcı ipucu ve E'yle hareketin reddedilmesi.
- Mevcut dört kapı, arka yürüyüş rotası, seslerin kaldırılması, FPS sayacı ve statik gruplama kontrolleri.

Sonuç: `Logs/lift_usability_validation.txt` — PASS ve Validation complete. [Modül takılı değilken üst konum](LiftWithoutModuleFixed.png). Test sırasında şirket kaydı yedeklenip sonunda geri yüklenir; kullanıcının modülleri kendiliğinden takılmaz. Ofis tasarımı, araç modelleri ve sahne yerleşimi bu düzeltmede değiştirilmedi.
