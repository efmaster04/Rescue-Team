using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CompanySim
{
    // Ofis güzelleştirme (çalışma anında, sahne dosyasına dokunmadan): OfficeGarage açılınca kurulur.
    //  - Kamera çok odalı ofisin içindeyken sıcak bir iç mekân renk ayarı (ikinci global Volume, öncelik 11) devreye girer;
    //    ağırlık kameranın binaya uzaklığına göre yumuşakça değişir, garajda ve dışarıda eski görünüm aynen kalır.
    //  - Ofisin içinde üçüncü şahıs kamerası biraz yaklaşır (odalar 4,8-5 m genişliğinde; 4,5 m mesafe kamerayı duvara yaslıyordu).
    //  - Resources/OfficeDressing/OfisDekor prefab'ı varsa sahneye eklenir (çarpıştırıcısız süs eşyaları, küçük sıcak ışıklar).
    // Çarpıştırıcı ya da tetik eklenmez: CompanyPlayerBridge.FindTarget en yakın çarpıştırıcıyı seçtiği için bilgisayarı ve kapıları
    // gölgelememeli.
    public sealed class OfficeInteriorMood : MonoBehaviour
    {
        public const string SceneName = "OfficeGarage";
        public const string DressingResource = "OfficeDressing/OfisDekor";
        // Çok odalı ofisin ayak izi (OfficeExpansionSetup.FootprintMin/Max ile aynı)
        public static readonly Vector3 FootprintMin = new Vector3(-9.95f, -0.02f, -5.45f);
        public static readonly Vector3 FootprintMax = new Vector3(0.05f, 3.4f, 12.3f);

        [Tooltip("Ağırlığın 0'dan 1'e çıktığı kenar payı (m).")]
        public float blend = 0.8f;
        [Tooltip("Ofis içindeyken üçüncü şahıs kamera mesafesi (m).")]
        public float indoorCameraDistance = 3.0f;

        public static OfficeInteriorMood Instance { get; private set; }
        public float Weight { get; private set; }
        public GameObject Dressing { get; private set; }

        private Volume volume;
        private OrbitCamera orbit;
        private float outdoorDistance = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Build(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { Build(scene); }

        private static void Build(Scene scene)
        {
            if (scene.name != SceneName || (Instance != null && Instance.gameObject.scene == scene)) return;
            var go = new GameObject("18_OfficeMood (çalışma anı)");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<OfficeInteriorMood>();
        }

        private void Awake()
        {
            Instance = this;
            volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 11f;
            volume.weight = 0f;
            volume.sharedProfile = MakeProfile();
        }

        // Süs eşyaları GarageRuntimeOptimization sahnenin statik meshlerini birleştirdikten SONRA eklenir: üretilen meshler
        // (başka köşe düzeni) o statik gruba girince ofisin birleşik meshleri bozuluyordu (yanlış malzemeler, boş görüntü).
        private IEnumerator Start()
        {
            var optimizer = FindAnyObjectByType<GarageRuntimeOptimization>();
            float until = Time.realtimeSinceStartup + 3f;
            while (optimizer != null && optimizer.autoCombine && !optimizer.Ready && Time.realtimeSinceStartup < until) yield return null;
            yield return null;
            var prefab = Resources.Load<GameObject>(DressingResource);
            if (prefab != null && Dressing == null)
            {
                Dressing = Instantiate(prefab);
                Dressing.name = "18_OfficeDressing (çalışma anı)";
                SceneManager.MoveGameObjectToScene(Dressing, gameObject.scene);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (orbit != null && outdoorDistance > 0f) orbit.distance = outdoorDistance;
        }

        // Sıcak iç mekân: gölgeler hafif amber, beyaz dengesi sıcak, difüzörler hafifçe parlar, kenarlar hafif kararır.
        public static VolumeProfile MakeProfile()
        {
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            p.name = "OfficeInteriorGrade (çalışma anı)";
            var wb = p.Add<WhiteBalance>(true); wb.temperature.Override(9f); wb.tint.Override(2f);
            var ca = p.Add<ColorAdjustments>(true); ca.postExposure.Override(0.08f); ca.contrast.Override(8f); ca.saturation.Override(7f);
            var smh = p.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(1.02f, 0.98f, 0.92f, 0f));
            smh.highlights.Override(new Vector4(1.0f, 0.99f, 0.96f, 0f));
            var bloom = p.Add<Bloom>(true); bloom.intensity.Override(0.22f); bloom.threshold.Override(0.95f); bloom.scatter.Override(0.62f);
            var vig = p.Add<Vignette>(true); vig.intensity.Override(0.2f); vig.smoothness.Override(0.5f);
            return p;
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 c = cam.transform.position;
            // kameranın ayak izine yatay uzaklığı (içerideyken 0)
            float dx = Mathf.Max(FootprintMin.x - c.x, 0f, c.x - FootprintMax.x);
            float dz = Mathf.Max(FootprintMin.z - c.z, 0f, c.z - FootprintMax.z);
            float outside = Mathf.Sqrt(dx * dx + dz * dz);
            float target = 1f - Mathf.Clamp01(outside / Mathf.Max(0.01f, blend));
            Weight = Mathf.MoveTowards(Weight, target, Time.unscaledDeltaTime * 2.5f);
            volume.weight = Weight;

            // kamera mesafesini yalnız ofisin içindeyken yazar; dışarıda başka kodların (sürüş, garaj) yazdığı değeri izler
            if (orbit == null) orbit = cam.GetComponent<OrbitCamera>();
            if (orbit != null)
            {
                if (Weight <= 0.001f || outdoorDistance < 0f) outdoorDistance = orbit.distance;
                else orbit.distance = Mathf.Lerp(outdoorDistance, Mathf.Min(outdoorDistance, indoorCameraDistance), Weight);
            }
        }
    }
}
