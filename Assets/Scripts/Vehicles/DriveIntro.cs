using UnityEngine;

// Sürüş girişi (kullanıcı kararı 2026-11-04): Bölüm 1, ekip servis minibüsünün içinde EastBound şeridinde (kaza yerinin
// ~100–115 m gerisinde) başlar. Sahne dosyasında minibüs park pozunda kalır (bütün kurucular ve edit zamanı doğrulamalar
// o düzeni görür); Play başında bu bileşen minibüsü yükle birlikte şeridin başına ışınlar, ekibi oturtur, görev saatini ve
// hırsız dalgalarını tutar. Trafik normal akar (sürüş canlı olsun ve şerit kapatma etkileşimi görülsün). Ekip E ile inince:
// görev saati (ambulans takvimi, saha süresi) başlar, hırsız yönetmeni açılır, park sonucu bildirilir.
// Testler: SkipForTests açıkken hiçbir şey yapmaz (eski başlangıç: minibüs parkta, ekip PlayerSpawn'da). Bayrak
// SubsystemRegistration'da sıfırlanmaz: doğrulamalar Play'e girmeden önce (edit modunda) açar, bitince kapatır.
[DefaultExecutionOrder(-10)]
public class DriveIntro : MonoBehaviour
{
    public static bool SkipForTests;
    public static DriveIntro Instance { get; private set; }

    public ServiceVanDriver van;
    public LanePath lane;
    [Tooltip("Başlangıç yeri: şeridin bu s'sinde (m), şerit ortasında.")]
    public float startS = 12f;
    public MissionDirector mission;
    public ThiefDirector thieves;
    [Tooltip("Ekrandaki ipucu (GameNotice ile periyodik).")]
    public string hintDriving = "Kaza yerine sürün: banketteki sarı çerçeveye park edin (W/S gaz-fren, A/D direksiyon)";
    public string hintStopped = "Durdunuz: E ile inin (park alanı: sarı çerçeve, şeridin dışında)";
    public string goodParkText = "İyi park: minibüs bankette, şerit açık";
    public string badParkText = "Kötü park: minibüs şeridi kapatıyor, trafik 5 sn sonra çarpacak";
    public float hintSeconds = 4f;

    public bool Active { get; private set; }
    public bool Done { get; private set; }
    public bool GoodParking { get; private set; }
    public float ExitTime { get; private set; } = -1f;
    public Vector3 StartPosition { get; private set; }
    public float ChosenStartS { get; private set; }

    private float nextHint;

    private void Awake()
    {
        Instance = this;
        if (SkipForTests || van == null || lane == null)
        {
            // Normal Play'de de görünür olsun: bayat bir doğrulama anahtarı girişi sessizce atlamasın (review P2-5)
            if (SkipForTests && !Application.isBatchMode) Debug.LogWarning("DriveIntro atlandı (SkipForTests): bir doğrulama anahtarı dolu ya da test koşuyor; minibüs parkta, ekip PlayerSpawn'da başlar.");
            enabled = false;
            return;
        }
        if (mission == null) mission = MissionDirector.Instance;
        if (thieves == null) thieves = ThiefDirector.Instance;
        if (mission != null) mission.ClockHeld = true;
        if (thieves != null) thieves.enabled = false; // Start ertelenir; inince açılır

        // Başlangıç: startS'ten ileriye 4 m adımlarla, minibüs gövdesinin statik collider'la (uç kayalar, korkuluk) kesişmediği ilk s
        float s = Mathf.Clamp(startS, 0f, lane.Length);
        for (int i = 0; i < 16; i++)
        {
            Vector3 pos = lane.PointAt(s);
            var rot = Quaternion.LookRotation(-lane.TangentAt(s), Vector3.up); // kök +Z arka: ileri = -forward
            van.TeleportTo(pos, rot, false);
            var overlaps = van.OverlapReport();
            if (overlaps.Count == 0) break;
            Debug.Log("DriveIntro: s " + s.ToString("F1") + " kesişiyor (" + string.Join(", ", overlaps) + "), ileri deneniyor");
            s = Mathf.Min(lane.Length, s + 4f);
        }
        ChosenStartS = s;
        StartPosition = van.transform.position;
        van.SetFallRespawn(van.transform.position, van.transform.rotation); // uçuruma düşerse buraya döner
        van.SeatCrew();
        van.Exited += OnExited;
        van.Boarded += OnBoarded;
        Active = true;
        nextHint = Time.time + 1f;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (van != null) { van.Exited -= OnExited; van.Boarded -= OnBoarded; }
    }

    // Yeniden binme: sürüş ipuçları yeniden akar (görev saati ve hırsızlar ilk inişte başladı, bir daha tutulmaz)
    private void OnBoarded(ServiceVanDriver v)
    {
        Active = true;
        nextHint = Time.time + hintSeconds;
    }

    private void Update()
    {
        if (!Active || van == null) return;
        if (Time.time >= nextHint)
        {
            nextHint = Time.time + hintSeconds;
            // Henüz hiç hareket etmediyse "sürün" ipucu (ilk saniyede "Durdunuz: inin" çıkmasın; review P2-4)
            GameNotice.Show(van.MaxSpeedReached > 0.5f && Mathf.Abs(van.Speed) < 0.3f ? hintStopped : hintDriving);
        }
    }

    private void OnExited(ServiceVanDriver v)
    {
        Active = false;
        GoodParking = v.GoodParking;
        if (!Done)
        {
            // Yalnız ilk iniş: görev saati ve hırsız dalgaları başlar. Sonraki inişler yalnız park sonucunu bildirir.
            Done = true;
            ExitTime = Time.time;
            if (mission != null) mission.ClockHeld = false;
            if (thieves != null) thieves.enabled = true;
        }
        GameNotice.Show(GoodParking ? goodParkText : badParkText + (v.parkingZone != null ? " (" + v.parkingZone.LastReason + ")" : ""));
    }
}
