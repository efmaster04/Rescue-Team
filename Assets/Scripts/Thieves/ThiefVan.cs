using System;
using System.Collections.Generic;
using UnityEngine;

// U4: hırsız kamyoneti (trafik PanelVan prefab'ının koyu kopyası). Sürüş ve trafik kuralları (kuyruk, koni, oyuncuya yavaşlama,
// çarpma, şerit sonunda kaybolma) aynen TrafficVehicle'da; bu bileşen yalnız durağı ve parkı yönetir:
//  Approaching: dış durak (TrafficVehicle.externalStopS) = ThiefVanStop'un şeritteki s'si; son pullOffLength metrede yanal
//               ofset şerit kenarının dışına (bankete) kayar.
//  Parked:      TrafficVehicle.parked → sürülmez; şerit kaydından (LaneOccupancy) çıkar, yani şeridi kapatmaz, arkadaki trafik
//               geçer. Arka kapılar açılır ("tak"), hırsızlar unloadInterval arayla iner. Hepsi dönünce ya da vanWaitSeconds sonra kalkar.
//  Departing:   arkadan gelen araçla boşluk yoksa bekler; şeride kaydolur, kapılar kapanır, yanal ofset geri gelir, trafik gibi gider.
//               Geride kalan hırsızlar kaçar (Thief.Flee).
[RequireComponent(typeof(LaneFollower))]
[RequireComponent(typeof(TrafficVehicle))]
public class ThiefVan : MonoBehaviour
{
    public enum VanPhase { Approaching, Parked, Departing }

    public ThiefSettings settings;
    [Tooltip("Arka kapaklar (builder kurar; pivot menteşede, kök uzayında dikey eksen).")]
    public Transform doorLeft, doorRight;
    public float doorLeftOpenAngle = -150f, doorRightOpenAngle = 150f;
    public AudioSource doorSource;

    public LaneFollower Follower { get; private set; }
    public TrafficVehicle Vehicle { get; private set; }
    public VanPhase Phase { get; private set; } = VanPhase.Approaching;
    public float StopS { get; private set; } = float.PositiveInfinity;
    public float ShoulderLateral { get; private set; }
    public float BaseLateral { get; private set; }
    public int WaveIndex { get; private set; }
    public int PlannedThieves { get; private set; }
    public int Spawned { get; private set; }
    public int Boarded { get; private set; }
    public int Fled { get; private set; }
    public int LootCount { get; private set; }
    public float ParkedTime { get; private set; }
    public float ParkedAtElapsed { get; private set; } = -1f;
    public float DoorAmount { get; private set; }
    public bool WaitingForGap { get; private set; }
    public bool IsGone { get; private set; }
    public bool IsParked { get { return Phase == VanPhase.Parked; } }
    public bool Departing { get { return Phase == VanPhase.Departing || WaitingForGap; } }
    public readonly List<Thief> Thieves = new List<Thief>();
    public readonly List<LootItem> Loot = new List<LootItem>();

    public event Action<ThiefVan> Parked;
    public event Action<ThiefVan> Departed;
    public event Action<ThiefVan> Despawned;
    public event Action<ThiefVan, Thief> ThiefSpawned;

    private ThiefDirector director;
    private Thief[] prefabs;
    private float nextUnloadAt;
    private Quaternion doorLeftClosed, doorRightClosed;
    private int variantOffset;
    private bool doorSoundDone, departSoundDone;

    private void Awake()
    {
        Follower = GetComponent<LaneFollower>();
        Vehicle = GetComponent<TrafficVehicle>();
        if (doorLeft != null) doorLeftClosed = doorLeft.localRotation;
        if (doorRight != null) doorRightClosed = doorRight.localRotation;
        if (doorSource == null) doorSource = gameObject.AddComponent<AudioSource>();
        doorSource.clip = SynthClips.Cached("VanDoor", SynthClips.VanDoor);
        doorSource.playOnAwake = false;
        doorSource.spatialBlend = 1f;
        doorSource.minDistance = 4f;
        doorSource.maxDistance = 120f;
        doorSource.volume = 0.8f;
    }

    private void OnDestroy()
    {
        IsGone = true;
        Despawned?.Invoke(this);
    }

    // İniş / atış noktası: arka tamponun 1.3 m gerisi, NavMesh'te ve engelsiz. Arkası kapalıysa (durak başka bir aracın burnuna
    // yakın, ör. çekicinin önü) banket tarafındaki yan kapı hizası (arka tekerin yanı, gövdeden 0.9 m dışarıda), o da olmazsa yol tarafı.
    public bool SideExit { get; private set; }
    public Vector3 RearPoint { get { return ExitPoint(out _); } }

    public Vector3 ExitPoint(out Vector3 spread)
    {
        if (exitFrame == Time.frameCount) { spread = exitSpread; return exitPoint; }
        Vector3 fwd = transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        float rear = Follower != null ? Follower.rearOverhang : 2.7f;
        float halfW = Vehicle != null ? Vehicle.bodySize.x * 0.5f : 1f;
        float sideSign = ShoulderLateral >= 0f ? 1f : -1f;
        Vector3 bumper = transform.position - fwd * rear + Vector3.up * 0.8f;
        Vector3 rearCand = transform.position - fwd * (rear + 1.3f);
        Vector3 sideCand = transform.position - fwd * (rear * 0.5f) + right * sideSign * (halfW + 0.9f);
        Vector3 roadCand = transform.position - fwd * (rear * 0.5f) - right * sideSign * (halfW + 0.9f);
        // Yola bağlı olmalı: kamyonetin oyduğu NavMesh ile banket eğimi arasında kalan şerit yoldan kopuk olabilir
        Vector3 road = Follower != null && Follower.lane != null ? Follower.lane.PointAt(Follower.Distance) : transform.position - right * ShoulderLateral;
        Vector3 rearNav = rearCand, sideNav = sideCand, roadNav = roadCand;
        bool rearOk = !GroundProbe.LineBlocked(bumper, rearCand + Vector3.up * 0.8f) && !StaticObstacle(rearCand, fwd) && OnNavMesh(rearCand, out rearNav) && ConnectedTo(rearNav, road);
        if (rearOk) { SideExit = false; exitSpread = right; exitPoint = rearNav; }
        else if (!StaticObstacle(sideCand, fwd) && OnNavMesh(sideCand, out sideNav) && ConnectedTo(sideNav, road)) { SideExit = true; exitSpread = fwd; exitPoint = sideNav; }
        else if (OnNavMesh(roadCand, out roadNav)) { SideExit = true; exitSpread = fwd; exitPoint = roadNav; }
        else
        {
            SideExit = true;
            exitSpread = fwd;
            exitPoint = GroundProbe.Find(roadCand + Vector3.up * 1.5f, 4f, out RaycastHit gs) ? gs.point : roadCand;
        }
        exitFrame = Time.frameCount;
        spread = exitSpread;
        return exitPoint;
    }

    private static bool OnNavMesh(Vector3 p, out Vector3 onMesh)
    {
        if (UnityEngine.AI.NavMesh.SamplePosition(p, out UnityEngine.AI.NavMeshHit hit, 0.8f, UnityEngine.AI.NavMesh.AllAreas)) { onMesh = hit.position; return true; }
        onMesh = p;
        return false;
    }

    private static UnityEngine.AI.NavMeshPath connectPath;
    private static bool ConnectedTo(Vector3 from, Vector3 to)
    {
        if (!UnityEngine.AI.NavMesh.SamplePosition(to, out UnityEngine.AI.NavMeshHit hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) return true; // yol NavMesh'te değilse kontrol yapılamaz
        if (connectPath == null) connectPath = new UnityEngine.AI.NavMeshPath();
        return UnityEngine.AI.NavMesh.CalculatePath(from, hit.position, UnityEngine.AI.NavMesh.AllAreas, connectPath) && connectPath.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
    }
    private int exitFrame = -1;
    private Vector3 exitPoint, exitSpread;
    private static readonly Collider[] exitOverlap = new Collider[16];

    // İniş alanında (1.6 × 1.6 m, 0.3–1.9 m yükseklik) hareketsiz bir collider (çekici, korkuluk, kaya) var mı?
    private bool StaticObstacle(Vector3 at, Vector3 fwd)
    {
        int n = Physics.OverlapBoxNonAlloc(at + Vector3.up * 1.1f, new Vector3(0.8f, 0.8f, 0.8f), exitOverlap, Quaternion.LookRotation(fwd, Vector3.up), ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = exitOverlap[i];
            if (c == null || c.transform.IsChildOf(transform)) continue;
            if (c is MeshCollider mc && !mc.convex) continue; // zemin / yol (korkuluk hariç tutulmaz: ince ama dik; aşağıda LineBlocked yakalar)
            if (GroundProbe.IsMovingGameObject(c)) continue;
            return true;
        }
        return false;
    }

    // Kamyonet gövdesine (BoxCollider) en yakın uzaklık (yatay): hırsız eşyayı buradan da atabilir (iniş noktası dar bir aralıktaysa).
    public float DistanceToBody(Vector3 p)
    {
        var box = GetComponent<BoxCollider>();
        if (box == null) return Vector3.Distance(p, transform.position);
        Vector3 q = box.ClosestPoint(new Vector3(p.x, transform.position.y + 1f, p.z));
        q.y = 0f; p.y = 0f;
        return Vector3.Distance(p, q);
    }

    // Yönetmen çağırır: şeridin başına koyar, durağı verir.
    public void Begin(LanePath lane, float stopS, float shoulderLateral, int thieves, int waveIndex, ThiefDirector dir, Thief[] thiefPrefabs, float startSpeed)
    {
        if (Follower == null) Follower = GetComponent<LaneFollower>();
        if (Vehicle == null) Vehicle = GetComponent<TrafficVehicle>();
        director = dir;
        prefabs = thiefPrefabs;
        StopS = stopS;
        ShoulderLateral = shoulderLateral;
        BaseLateral = Follower.lateralOffset;
        PlannedThieves = thieves;
        WaveIndex = waveIndex;
        variantOffset = waveIndex;
        Phase = VanPhase.Approaching;
        Follower.Place(lane, 0f, startSpeed);
        Vehicle.externalStopS = stopS;
        Vehicle.parked = false;
        SetDoors(0f);
    }

    private void FixedUpdate()
    {
        if (Follower.lane == null) return;
        float dt = Time.fixedDeltaTime;
        float pull = settings != null ? settings.pullOffLength : 12f;
        switch (Phase)
        {
            case VanPhase.Approaching:
            {
                float u = Mathf.InverseLerp(StopS - pull, StopS, Follower.Distance);
                Follower.lateralOffset = Mathf.Lerp(BaseLateral, ShoulderLateral, Mathf.SmoothStep(0f, 1f, u));
                if (Follower.AtStop && Follower.StopDistance >= StopS - 0.001f) Park();
                break;
            }
            case VanPhase.Parked:
            {
                ParkedTime += dt;
                float doorSec = settings != null ? settings.doorSeconds : 0.6f;
                if (!WaitingForGap) SetDoors(Mathf.Clamp01(ParkedTime / doorSec));
                // İndirme
                if (!WaitingForGap && DoorAmount >= 0.999f && Spawned < PlannedThieves && Time.time >= nextUnloadAt)
                {
                    SpawnThief();
                    nextUnloadAt = Time.time + (settings != null ? settings.unloadInterval : 0.8f);
                }
                // Kalkış kararı
                bool allBack = Spawned >= PlannedThieves && Boarded + Fled >= Spawned;
                bool timeUp = ParkedTime >= (settings != null ? settings.vanWaitSeconds : 90f);
                if (!WaitingForGap && (allBack || timeUp))
                {
                    WaitingForGap = true;
                    if (!departSoundDone) { departSoundDone = true; doorSource.Play(); }
                    // Geride kalanlar panikler
                    foreach (var t in Thieves) if (t != null && !t.IsGone) t.OnVanLeaving();
                }
                if (WaitingForGap)
                {
                    SetDoors(Mathf.Clamp01(DoorAmount - dt / doorSec));
                    if (DoorAmount <= 0.001f && GapClear()) Depart();
                }
                break;
            }
            case VanPhase.Departing:
            {
                float u = Mathf.InverseLerp(StopS, StopS + pull, Follower.Distance);
                Follower.lateralOffset = Mathf.Lerp(ShoulderLateral, BaseLateral, Mathf.SmoothStep(0f, 1f, u));
                break;
            }
        }
    }

    private void Park()
    {
        Phase = VanPhase.Parked;
        ParkedTime = 0f;
        ParkedAtElapsed = director != null ? director.Elapsed : Time.time;
        Vehicle.parked = true;
        LaneOccupancy.Unregister(Follower); // şeridi kapatmaz
        Follower.lateralOffset = ShoulderLateral;
        nextUnloadAt = Time.time + (settings != null ? settings.doorSeconds : 0.6f);
        if (!doorSoundDone) { doorSoundDone = true; doorSource.Play(); }
        Parked?.Invoke(this);
    }

    private void Depart()
    {
        WaitingForGap = false;
        Phase = VanPhase.Departing;
        LaneOccupancy.Register(Follower);
        Vehicle.parked = false;
        Vehicle.externalStopS = float.PositiveInfinity;
        Follower.ClearStop();
        Departed?.Invoke(this);
    }

    // Şeride dönüş için boşluk: arkadan gelen aracın önüyle en az minGap + hız × 2 sn; önde minGap.
    private bool GapClear()
    {
        var s = Vehicle.settings;
        float minGap = s != null ? s.minGap : 4f;
        float headway = s != null ? s.timeHeadway : 2f;
        foreach (var f in LaneOccupancy.Followers)
        {
            if (f == null || f == Follower || f.lane != Follower.lane) continue;
            if (f.Distance < Follower.Distance)
            {
                if (Follower.RearS - f.FrontS < minGap + f.Speed * headway) return false;
            }
            else if (f.RearS - Follower.FrontS < minGap) return false;
        }
        return true;
    }

    private void SpawnThief()
    {
        if (prefabs == null || prefabs.Length == 0) return;
        var prefab = prefabs[(Spawned + variantOffset) % prefabs.Length];
        Vector3 exit = ExitPoint(out Vector3 spread);
        Vector3 at = exit + spread * ((Spawned % 2 == 0) ? -0.45f : 0.45f);
        if (GroundProbe.Find(at + Vector3.up * 1.5f, 4f, out RaycastHit g)) at = g.point;
        Vector3 away = exit - transform.position; away.y = 0f;
        if (away.sqrMagnitude < 1e-6f) away = -transform.forward;
        var t = Instantiate(prefab, at, Quaternion.LookRotation(away.normalized, Vector3.up));
        t.name = prefab.name + "_W" + (WaveIndex + 1) + "_" + (Spawned + 1);
        t.settings = settings;
        t.BeginUnload(this, director);
        Thieves.Add(t);
        Spawned++;
        if (director != null) director.RegisterThief(t);
        ThiefSpawned?.Invoke(this, t);
    }

    public void OnThiefBoarded(Thief t) { Boarded++; }
    public void OnThiefGone(Thief t) { Fled++; }
    public void AddLoot(LootItem item) { if (item != null) Loot.Add(item); LootCount++; }

    private void SetDoors(float amount)
    {
        DoorAmount = amount;
        if (doorLeft != null) doorLeft.localRotation = doorLeftClosed * Quaternion.AngleAxis(doorLeftOpenAngle * amount, Vector3.up);
        if (doorRight != null) doorRight.localRotation = doorRightClosed * Quaternion.AngleAxis(doorRightOpenAngle * amount, Vector3.up);
    }
}
