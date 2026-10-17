using System;
using System.Collections.Generic;
using UnityEngine;

// Trafik aracı davranışı (kullanıcı kararları 3, 4, 5, 7). Sürüşün kendisi LaneFollower'da (ambulansla aynı).
//
// Her fizik adımında:
//  1) Şerit doluluk servisinden öndeki en yakın engel alınır: araç (ambulans dahil) ya da şeridi kapatan dik koni.
//     Araç: en az minGap boşlukla durulacak nokta + 2 sn kuralı hız sınırı. Koni: koninin coneStopMargin gerisinde dur.
//  2) Koninin önünde durduktan coneWaitSeconds (20 sn) sonra o koni yok sayılır: araç koniye çarpıp devam eder.
//  3) Önde (şeritte) taşıyıcı varsa korna ve hız en fazla %40 (durmaz; ambulanstaki yield mantığı).
//  4) Gövdeye değen oyuncu ragdoll olup düşer, bot sedyeyi bırakıp kenara itilir, yerdeki/sedyedeki hasta savrulur.
// Şeridin sonunda (TrafficDespawn_*) kaybolur.
[RequireComponent(typeof(LaneFollower))]
public class TrafficVehicle : MonoBehaviour
{
    public TrafficVehicleType type;
    public TrafficSettings settings;
    [Tooltip("Gövde kutusu (kök uzayında): temas kontrolü.")]
    public Vector3 bodyCenter = new Vector3(0f, 0.9f, 0f);
    public Vector3 bodySize = new Vector3(1.8f, 1.5f, 4.5f);
    public VehicleAudio sound;

    [Header("Renk varyantı (binek)")]
    [Tooltip("Boyası değişen gövde renderer'ları ve malzeme yuvası.")]
    public Renderer[] paintRenderers = new Renderer[0];
    public int paintMaterialIndex = 0;
    public Color[] paintColors = new Color[0];

    public LaneFollower Follower { get; private set; }
    public bool YieldingToCarrier { get; private set; }
    public LaneOccupancy.BlockerKind BlockerKind { get; private set; }
    public float GapToLeader { get; private set; } = float.PositiveInfinity;
    public int PaintIndex { get; private set; } = -1;
    // Koni bekleme (test ve hata ayıklama)
    public TrafficCone WaitingCone { get; private set; }
    public float ConeWaitTime { get; private set; }

    public event Action<TrafficVehicle> Despawned;
    public event Action<TrafficVehicle, TrafficCone, float> ConeRammed;   // koni, bekleme süresi
    public event Action<TrafficVehicle, ThirdPersonPlayer> HitPlayer;
    public event Action<TrafficVehicle, Patient> HitPatient;

    private readonly HashSet<TrafficCone> ignoredCones = new HashSet<TrafficCone>();
    private MaterialPropertyBlock block;

    private void Awake()
    {
        Follower = GetComponent<LaneFollower>();
        if (sound == null) sound = GetComponent<VehicleAudio>();
    }

    public void ApplyPaint(int index)
    {
        if (paintColors == null || paintColors.Length == 0) return;
        PaintIndex = Mathf.Clamp(index, 0, paintColors.Length - 1);
        if (block == null) block = new MaterialPropertyBlock();
        foreach (var r in paintRenderers)
        {
            if (r == null) continue;
            int slot = Mathf.Clamp(paintMaterialIndex, 0, r.sharedMaterials.Length - 1);
            r.GetPropertyBlock(block, slot);
            block.SetColor("_BaseColor", paintColors[PaintIndex]);
            block.SetColor("_Color", paintColors[PaintIndex]);
            r.SetPropertyBlock(block, slot);
        }
    }

    private void FixedUpdate()
    {
        if (Follower.lane == null || settings == null) return;
        float dt = Time.fixedDeltaTime;

        // 1) Öndeki engel
        var b = LaneOccupancy.NearestBlocker(Follower, ignoredCones, settings.coneUprightMaxAngle);
        BlockerKind = b.kind;
        float limit = float.PositiveInfinity;
        float stop = float.PositiveInfinity;
        GapToLeader = float.PositiveInfinity;
        if (b.kind == LaneOccupancy.BlockerKind.Vehicle)
        {
            float gap = b.edgeS - Follower.FrontS;
            GapToLeader = gap;
            stop = b.edgeS - settings.minGap - Follower.frontOverhang;
            limit = Mathf.Max(0f, gap) / settings.timeHeadway; // 2 sn kuralı: boşluk = hız × 2 sn
        }
        else if (b.kind == LaneOccupancy.BlockerKind.Cone)
            stop = b.edgeS - settings.coneStopMargin - Follower.frontOverhang;

        if (!float.IsPositiveInfinity(stop)) Follower.StopAt(Mathf.Max(stop, Follower.Distance));
        else Follower.ClearStop();

        // 2) Koni önünde bekleme
        bool waitingAtCone = b.kind == LaneOccupancy.BlockerKind.Cone && Follower.Speed <= 0.01f && Follower.Distance >= stop - 0.1f;
        if (waitingAtCone)
        {
            if (WaitingCone != b.cone) { WaitingCone = b.cone; ConeWaitTime = 0f; }
            ConeWaitTime += dt;
            if (ConeWaitTime >= settings.coneWaitSeconds)
            {
                ignoredCones.Add(b.cone);
                ConeRammed?.Invoke(this, b.cone, ConeWaitTime);
                WaitingCone = null;
                ConeWaitTime = 0f;
            }
        }
        else if (b.kind != LaneOccupancy.BlockerKind.Cone || b.cone != WaitingCone)
        {
            WaitingCone = null;
            ConeWaitTime = 0f;
        }

        // 3) Önde taşıyıcı: korna + en fazla %40
        YieldingToCarrier = CarrierAhead();
        if (YieldingToCarrier)
        {
            limit = Mathf.Min(limit, Follower.cruiseSpeed * settings.yieldSpeedFactor);
            if (sound != null) sound.PlayHorn();
        }

        Follower.Tick(dt, limit);
        if (sound != null) sound.SetSpeed(Follower.Speed);

        // 4) Temas
        HitContacts();

        if (Follower.AtEnd)
        {
            Despawned?.Invoke(this);
            Destroy(gameObject);
        }
    }

    // Ön tampondan lookAhead metre içinde, şerit genişliğinde taşıyıcı (oyuncu ya da bot) var mı?
    private bool CarrierAhead()
    {
        var lane = Follower.lane;
        float front = Follower.FrontS;
        foreach (var c in CarrierRegistry.All)
        {
            if (c == null || c.Root == null) continue;
            Vector3 p = CarrierPosition(c);
            float s = lane.Project(p, out float lateral, out Vector3 onLane);
            if (Mathf.Abs(p.y - onLane.y) > 2.5f) continue;              // başka kotta (ör. aşağıdaki patika)
            if (Mathf.Abs(lateral) > lane.width * 0.5f) continue;        // şeridin dışında
            float ahead = s - front;
            if (ahead >= -0.2f && ahead <= settings.lookAhead) return true;
        }
        return false;
    }

    // Düşmüş oyuncunun yeri kalçasıdır (ragdoll kökten ayrılır).
    private static Vector3 CarrierPosition(ICarrier c)
    {
        var player = c as ThirdPersonPlayer;
        if (player != null && player.knockdown != null && player.knockdown.IsDown) return player.knockdown.GroundPosition;
        if (c is StretcherHelperBot bot && bot.IsDown) return bot.GroundPosition;
        return c.Root.position;
    }

    private void HitContacts()
    {
        foreach (var c in CarrierRegistry.All.ToArray())
        {
            if (c == null || c.Root == null) continue;
            var player = c as ThirdPersonPlayer;
            if (player != null && (player.knockdown == null || player.knockdown.IsDown)) continue;
            if (c is StretcherHelperBot downBot && downBot.IsDown) continue; // yerdeki botu araç gövdesi (fizik) iter
            Vector3 root = c.Root.position;
            bool touch = Touches(root + Vector3.up * 0.3f, 0.3f) || Touches(root + Vector3.up * 0.9f, 0.3f) || Touches(root + Vector3.up * 1.4f, 0.3f);
            if (!touch || ApproachSpeed(root) <= settings.knockdownSpeedThreshold) continue;
            Vector3 v = HitVelocity(root);
            if (player != null)
            {
                player.KnockDown(v);
                HitPlayer?.Invoke(this, player);
            }
            else if (c is StretcherHelperBot bot)
                bot.HitByVehicle(Follower.lane, v);
        }
        foreach (var p in Patient.All.ToArray())
        {
            if (p == null || (p.State != PatientState.LyingGround && p.State != PatientState.OnStretcher)) continue;
            if (!Touches(p.Position, 0.3f) || ApproachSpeed(p.Position) <= settings.knockdownSpeedThreshold) continue;
            p.HitByVehicle(HitVelocity(p.Position));
            HitPatient?.Invoke(this, p);
        }
    }

    // Nokta (r yarıçaplı küre) gövde kutusuna değiyor mu?
    public float ApproachSpeed(Vector3 target)
    {
        Vector3 local = transform.InverseTransformPoint(target) - bodyCenter;
        Vector3 closest = new Vector3(Mathf.Clamp(local.x, -bodySize.x * .5f, bodySize.x * .5f),
            local.y, Mathf.Clamp(local.z, -bodySize.z * .5f, bodySize.z * .5f));
        Vector3 normal = local - closest;
        normal.y = 0f;
        if (normal.sqrMagnitude < .0001f) normal = local.z >= 0f ? Vector3.forward : Vector3.back;
        return Mathf.Max(0f, Vector3.Dot(Vector3.forward * Follower.Speed, normal.normalized));
    }

    public bool Touches(Vector3 world, float r)
    {
        Vector3 l = transform.InverseTransformPoint(world) - bodyCenter;
        Vector3 h = bodySize * 0.5f;
        return Mathf.Abs(l.x) <= h.x + r && Mathf.Abs(l.y) <= h.y + r && Mathf.Abs(l.z) <= h.z + r;
    }

    // Çarpma yönünde itki: aracın gidiş yönü + aracın merkezinden dışarı yan + yukarı.
    private Vector3 HitVelocity(Vector3 at)
    {
        Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        float side = Vector3.Dot(at - transform.position, right) >= 0f ? 1f : -1f;
        float speed = Mathf.Max(Follower.Speed * settings.hitSpeedMultiplier, settings.hitMinSpeed);
        return fwd * speed + right * side * settings.hitSideSpeed + Vector3.up * settings.hitUpSpeed;
    }
}
