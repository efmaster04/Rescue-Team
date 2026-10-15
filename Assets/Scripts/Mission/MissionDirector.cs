using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Bölüm 1 görevi: ambulans takvimi, hasta başına kaçırma sayacı, görev sonu (kullanıcı kararları 1, 4, 5, 6).
// Saat fizik adımında (FixedUpdate) ilerler; ambulans da fizik adımında sürüldüğü için varış zamanı tutarlıdır.
[DefaultExecutionOrder(-50)]
public class MissionDirector : MonoBehaviour
{
    public static MissionDirector Instance { get; private set; }

    public AmbulanceSchedule schedule;
    public AmbulanceController ambulancePrefab;
    public LanePath lane;
    [Tooltip("Sahnedeki taşınabilir durak objesi (AmbulanceStop).")]
    public Transform ambulanceStop;
    public Patient[] patients = new Patient[0];
    [Tooltip("Bir hasta bu kadar ambulansı kaçırınca görev başarısız olur.")]
    public int missLimit = 3;

    public const string SuccessText = "Hastaların hepsi ambulansta!";

    // Gelecekteki sonuç ekranı için.
    public event Action MissionSucceeded;
    public event Action<Patient> MissionFailed;
    public event Action<AmbulanceController> AmbulanceSpawned;

    public float Elapsed { get; private set; }
    public bool IsFinished { get; private set; }
    public bool Succeeded { get; private set; }
    public string ResultMessage { get; private set; } = "";
    public Patient FailedPatient { get; private set; }
    public float StopDistance { get; private set; }
    public float TravelTime { get; private set; }
    public int ArrivalCount { get; private set; }
    // Sahnedeki ambulanslar (biri şeridin sonuna giderken sıradaki gelebilir)
    public readonly List<AmbulanceController> Ambulances = new List<AmbulanceController>();
    // Durakta bekleyen ambulans (yoksa null)
    public AmbulanceController WaitingAmbulance { get { foreach (var a in Ambulances) if (a != null && a.Phase == AmbulancePhase.Waiting) return a; return null; } }
    public readonly List<float> ArrivalLog = new List<float>(); // gerçekleşen varış zamanları (test ve hata ayıklama)

    private AmbulanceSchedule active;
    private int spawnIndex;

    // Bir sonraki varışın planlanan zamanı.
    public float NextArrivalTime { get { return active != null ? active.ArrivalTime(ArrivalCount) : float.PositiveInfinity; } }
    public AmbulanceSchedule ActiveSchedule { get { return active; } }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        Begin(schedule);
    }

    // Görevi verilen takvimle (yeniden) başlatır. Testler kısa takvimle çağırır; asset dosyası değişmez.
    public void Begin(AmbulanceSchedule useSchedule)
    {
        active = useSchedule;
        Elapsed = 0f;
        spawnIndex = 0;
        ArrivalCount = 0;
        ArrivalLog.Clear();
        IsFinished = false;
        Succeeded = false;
        ResultMessage = "";
        FailedPatient = null;
        foreach (var a in Ambulances) if (a != null) Destroy(a.gameObject);
        Ambulances.Clear();

        StopDistance = lane.Project(ambulanceStop.position);
        var follower = ambulancePrefab.GetComponent<LaneFollower>();
        TravelTime = follower.TravelTime(0f, StopDistance, follower.cruiseSpeed, Time.fixedDeltaTime);
    }

    private void FixedUpdate()
    {
        if (active == null) return;
        Elapsed += Time.fixedDeltaTime;
        if (IsFinished) return; // görev bitince yeni ambulans gelmez

        float spawnAt = Mathf.Max(0f, active.ArrivalTime(spawnIndex) - TravelTime);
        if (Elapsed >= spawnAt)
            Spawn();
    }

    private void Spawn()
    {
        spawnIndex++;
        var amb = Instantiate(ambulancePrefab);
        amb.name = "Ambulance_" + spawnIndex;
        amb.Begin(lane, StopDistance, active.wait, amb.Follower.cruiseSpeed);
        amb.Arrived += OnArrived;
        amb.Departed += OnDeparted;
        amb.Despawned += OnDespawned;
        amb.PatientLoaded += OnPatientLoaded;
        Ambulances.Add(amb);
        AmbulanceSpawned?.Invoke(amb);
    }

    private void OnArrived(AmbulanceController amb)
    {
        ArrivalCount++;
        ArrivalLog.Add(Elapsed);
    }

    // Ambulans duraktan ayrıldı: o anda ambulansta olmayan, kurtarılmamış her hastanın sayacı +1.
    private void OnDeparted(AmbulanceController amb)
    {
        Patient failed = null;
        foreach (var p in patients)
        {
            if (p == null || p.IsAboard) continue;
            p.AddMissedAmbulance();
            if (p.MissedAmbulances >= missLimit && failed == null) failed = p;
        }
        if (failed != null && !IsFinished)
        {
            IsFinished = true;
            Succeeded = false;
            FailedPatient = failed;
            ResultMessage = "Görev başarısız: " + failed.displayName + " " + missLimit + " ambulansı kaçırdı";
            MissionFailed?.Invoke(failed);
        }
    }

    private void OnDespawned(AmbulanceController amb)
    {
        Ambulances.Remove(amb);
    }

    // 3 hastanın üçü de ambulansa yüklendiği anda görev başarılı.
    private void OnPatientLoaded(AmbulanceController amb, Patient p)
    {
        if (IsFinished) return;
        foreach (var q in patients)
            if (q != null && !q.IsAboard) return;
        IsFinished = true;
        Succeeded = true;
        ResultMessage = SuccessText;
        MissionSucceeded?.Invoke();
    }

    // R tuşu: görev bittiyse sahneyi yeniden başlatır.
    public void RequestRestart()
    {
        if (!IsFinished) return;
        RestartScene();
    }

    public static void RestartScene()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.buildIndex >= 0)
        {
            SceneManager.LoadScene(scene.buildIndex);
            return;
        }
#if UNITY_EDITOR
        // Sahne Build Settings'te değilse (editörde) yoldan yükle.
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
        SceneManager.LoadScene(scene.name);
#endif
    }
}
