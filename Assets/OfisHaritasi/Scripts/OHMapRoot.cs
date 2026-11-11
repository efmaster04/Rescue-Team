using UnityEngine;

// Ofis haritasının kök bileşeni. Kurucu (OHMapBuilder) kapattığı eski dış çevre parçalarını burada tutar;
// "Haritayı Kaldır" komutu bunları geri açar. Oyuncuya özel görünmez duvarlar trafik araçlarıyla çarpışmaz.
namespace OfisHaritasi
{
    public class OHMapRoot : MonoBehaviour
    {
        public GameObject[] disabledLegacy = new GameObject[0];
        public Renderer[] hiddenRenderers = new Renderer[0];   // eski dış parçalar: nesne açık kalır, yalnız çizimi kapanır
        public Collider[] playerOnly = new Collider[0];
        [TextArea(3, 12)] public string info;
    }
}
