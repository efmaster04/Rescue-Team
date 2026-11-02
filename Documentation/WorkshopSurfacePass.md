# Garaj yüzeyleri ve servis alanı

Bu geçiş, kaynak ofisi ve tam minibüs modelini/konumunu koruyarak garaj tezgâhını ayrıntılandırır. Önceki sahne, kurulum kodları ve iki karşılaştırma görseli `Backups/WorkshopSurfacePass` içindedir.

Tezgâh, kulplar ve metal tepsilerde fırçalanmış çeliğin albedo, normal ve pürüzlülük haritaları kullanılır. Boyalı dolaplar ince toz boya dokusuna sahiptir. Mevcut takım panosunun garajdaki arkalığına 25 mm aralıklı delik görünümü eklenmiştir; delikler bir yüzey dokusudur. Kaynak takımların yerleri ve ölçüleri korunur. Tüm dokular `Art/Blender/Scripts/gen_workshop_detail_surfaces.py` ile yerel ve tekrarlanabilir olarak üretilmiştir.

Tezgâh üstünde üç sığ gövde, altı kapak, havalandırma yarıkları ve kısa metal kulplar bulunur. Bu üst dolaplar dekoratiftir; alttaki on altı etkileşimli çekmece aynı şekilde çalışır. Üst dolap gövdelerinin kutu çarpışmaları vardır ve bütün hacimleri 2,51 m üstündedir. Tezgâh önündeki ince, kenarı yuvarlatılmış kauçuk paspasın ayrı çarpışması yoktur; mevcut düz zemin üzerinden yürünür.

Yan duvardaki hava hortumu makarası metal montaj, iki yan kapak, sarılı hortum ve aşağıda duran kuplajdan oluşur. Makara gövdesi fiziksel olarak katıdır. İnce asılı hortum, küçük eşyalara veya oyuncuya takılan bir çarpışma üretmez.

Garaj girişinin sağındaki yere dağılmış dört dekoratif kolinin yerine iki raflı servis arabası konur. Arabanın tekerlekleri, kauçuk itme kolu ve iki küçük metal parça tepsisi vardır. Ana gövdesinin çarpışması bulunur; araç servis şeridinin dışındadır. Bu araba ve makara dekoratiftir, gerçek stok/sipariş/teslimat verilerini değiştirmez. Kaynak dekoratif parçalar taşınmaz: yalnız garajın bu köşesindeki G06 kolilerinin render'ı kapatılır. Ofisteki koliler etkilenmez.

Garajın mevcut dört tavan ışığı hafif sıcak beyazdır; açık kapıdan gelen daha serin ışık ve sabit oda yansımaları korunur. Yeni tabela veya slogan yoktur.

Kurulum `Company > Polish Workshop Details` menüsüyle yapılır. Tam garaj kurulumları aynı kodu kullanır. `WorkshopPolishBuilder.ApplyAndValidate`, yansımaları yeniler, sahneyi kaydeder, üç ayrıntı görüntüsü alır ve mevcut Play Mode doğrulamasını çalıştırır. `WorkshopFinishedBench.png`, `WorkshopAirReel.png`, `WorkshopPartsCart.png` kaydedilmiş sahnenin Unity render'larıdır; `GarageRefinedPreview.png` ve `WorkbenchRefinedPreview.png` Play Mode görüntüleridir.

Son doğrulama `Logs/workshop_surface_delivery_editor.log` ile normal kapandı. Altı mevcut test grubu PASS verdi; araç, kapı, çekmece, taşıma, bilgisayar ve şirket oyun döngüsü korundu. Son görseller incelendi. Kaynak koruma denetimleri 989 ofis mesh/malzeme bağlantısı ile 2.887 kaynak dönüşümünde değişiklik bulmadı; FBX hash'i aynı kaldı. İnsanla serbest oynanış ve performans kıyaslaması bu geçişte yapılmadı.
