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

    // Kamyonetin arkası: arka tamponun 1.2 m gerisi, zeminde (hırsızlar burada iner ve eşyayı buraya atar).
    public Vector3 RearPoint
    {
        get
        {
            Vector3 fwd = transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 p = transform.position - fwd * (Follower.rearOverhang + 1.2f);
            if (GroundProbe.Find(p + Vector3.up * 1.5f, 4f, out RaycastHit g)) p = g.point;
            return p;
        }
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
        Vector3 rear = RearPoint;
        Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, fwd) * ((Spawned % 2 == 0) ? -0.45f : 0.45f);
        Vector3 at = rear + side;
        if (GroundProbe.Find(at + Vector3.up * 1.5f, 4f, out RaycastHit g)) at = g.point;
        var t = Instantiate(prefab, at, Quaternion.LookRotation(-fwd, Vector3.up));
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
