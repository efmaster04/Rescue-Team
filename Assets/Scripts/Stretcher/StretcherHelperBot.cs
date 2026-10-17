using UnityEngine;

// Test botu (kullanıcı kararı 9): multiplayer gelene kadar sedyenin öbür ucunu tutar.
// Araç çarpınca (şef kararı 6) bot da ragdoll olup düşer ve getUpDelay sn sonra kendi kendine GetUp_FromBack ile kalkar
// (bot test yardımcısıdır; oyuncunun "yalnız arkadaş kaldırır" kuralı değişmez).
// Oyuncu bir ucu tutunca bot diğer uca ışınlanır. Sonra "römork" modeliyle izler: sedye ekseni iki taşıyıcı
// arasındaki çizgidir, bot lidere olan uzaklığı sap aralığında tutar. Y'sini zemine raycast'le bulur.
// Ayakta NpcBody teması engeller ve yürüyen oyuncudan sınırlı itme alır.
// Oyuncu bırakınca bot da AYNI ANDA bırakır (güvenli iniş; bırakmayı PlayerInteraction yapar).
// İleride ikinci oyuncu bu botun yerine geçecek: ikisi de ICarrier.
[DefaultExecutionOrder(50)] // oyuncu hareket ettikten sonra, sedye çözümünden önce
public class StretcherHelperBot : MonoBehaviour, ICarrier
{
    [Tooltip("Açıkken oyuncu sedye tutunca bot öbür ucu tutar ve araç çarpıp düşen oyuncuyu kaldırır. Kapatınca oyuncu tek başına kalır.")]
    public bool assistEnabled = true;
    [Tooltip("Oyuncu düştükten bu kadar sonra (sn) bot yanına ışınlanır ve kaldırmaya başlar (kaldırma süresi PlayerKnockdown'da).")]
    public float reviveDelay = 3f;
    public CarryRules rules;
    public Animator animator;
    [Tooltip("Animator'daki hız parametresi.")]
    public string speedParameter = "Speed";
    [Tooltip("Animasyon tetikleyicisi (Revive_Helper, GetUp vb.).")]
    public CrewAnimation crewAnimation;

    [Header("Ragdoll (şef kararı 6)")]
    [Tooltip("Kalça kemiği (Humanoid Hips).")]
    public Transform hips;
    public Rigidbody[] ragdollBodies = new Rigidbody[0];
    public Collider[] ragdollColliders = new Collider[0];
    [Tooltip("Araç çarpınca yerde kalma süresi (sn); sonra kendi kendine kalkar.")]
    public float getUpDelay = 3f;

    public bool IsDown { get; private set; }
    public float DownSince { get; private set; } = -1f;
    public int TimesDowned { get; private set; }
    public int TimesStoodUp { get; private set; }
    // Düşmüş botun yeri: kalça (ragdoll kökten ayrılır)
    public Vector3 GroundPosition { get { return IsDown && hips != null ? hips.position : transform.position; } }

    public Transform Root { get { return transform; } }
    public StretcherController HeldStretcher { get; set; }

    private ICarrier leader;
    private Vector3 lastPosition;

    public bool CanAssist { get { return assistEnabled && isActiveAndEnabled && HeldStretcher == null && !IsDown; } }

    private void Awake()
    {
        if (crewAnimation == null) crewAnimation = GetComponent<CrewAnimation>();
        SetRagdoll(false);
        if (GetComponent<NpcBody>() == null) gameObject.AddComponent<NpcBody>();
    }

    private void OnEnable()
    {
        CarrierRegistry.Add(this);
        lastPosition = transform.position;
    }

    private void OnDisable()
    {
        if (HeldStretcher != null) HeldStretcher.Release(this);
        CarrierRegistry.Remove(this);
    }

    public Vector3 GetHandPoint(Vector3 towards)
    {
        return CarrierRegistry.HandPoint(transform, Height, Reach, towards);
    }

    private float Height { get { return rules != null ? rules.handHeight : 0.72f; } }
    private float Reach { get { return rules != null ? rules.handReach : 0.3f; } }

    // Lider sedyenin bir ucunu tuttu: bot öbür uca ışınlanır ve o ucu tutar.
    public bool Assist(ICarrier leaderCarrier, StretcherController stretcher, bool leaderFront)
    {
        if (!CanAssist || !stretcher.CanGrab(!leaderFront)) return false;
        leader = leaderCarrier;
        Transform otherHandle = leaderFront ? stretcher.handleRear : stretcher.handleFront;
        Transform leaderHandle = leaderFront ? stretcher.handleFront : stretcher.handleRear;
        Vector3 axis = Flat(otherHandle.position - leaderHandle.position, -leader.Root.forward);
        PlaceAt(leader.GetHandPoint(stretcher.transform.position), axis, stretcher.HandleSpan);
        lastPosition = transform.position;
        return stretcher.Grab(this, !leaderFront, false);
    }

    public void ReleaseHeld()
    {
        if (HeldStretcher != null) HeldStretcher.Release(this);
        leader = null;
    }

    // Araç çarptı: taşıdığı sedyeyi bırakmış sayılır, ragdoll olup itkiyle savrulur (şef kararı 6).
    // Ragdoll kurulmamışsa (eski sahne) şeridin dışına itilir.
    public void HitByVehicle(LanePath lane, Vector3 velocity)
    {
        ReleaseHeld();
        if (IsDown) return;
        if (ragdollBodies.Length > 0 && hips != null)
        {
            IsDown = true;
            DownSince = Time.time;
            TimesDowned++;
            SetRagdoll(true);
            foreach (var rb in ragdollBodies)
                if (rb != null) rb.linearVelocity = velocity;
            return;
        }
        Vector3 p = transform.position;
        if (lane != null)
        {
            float s = lane.Project(p, out float lateral, out _);
            float side = Vector3.Dot(velocity, lane.RightAt(s)) >= 0f ? 1f : -1f;
            float target = side * (lane.width * 0.5f + 0.8f);
            p += lane.RightAt(s) * (target - lateral);
        }
        if (GroundProbe.Find(p + Vector3.up * 1.5f, 6f, out RaycastHit hit)) p.y = hit.point.y;
        transform.position = p;
        lastPosition = p;
    }

    // Araç çarpıp düşen oyuncu (kullanıcı kararı 6): reviveDelay sonra yanına ışınlanır, sedye tutuyorsa önce güvenle
    // bırakır, sonra E basılı tutar gibi kaldırır (2 sn).
    private bool ReviveDownedPlayer(float dt)
    {
        if (!assistEnabled || !isActiveAndEnabled || IsDown) return false;
        if (crewAnimation != null && crewAnimation.GettingUp) return false;
        PlayerKnockdown target = null;
        foreach (var c in CarrierRegistry.All)
        {
            var p = c as ThirdPersonPlayer;
            if (p != null && p.IsDown && Time.time - p.knockdown.DownSince >= reviveDelay) { target = p.knockdown; break; }
        }
        if (target == null) return false;
        if (HeldStretcher != null) ReleaseHeld(); // tek tutan bot: sedye güvenle yere iner
        if (!target.InReviveRange(this))
        {
            Vector3 hips = target.GroundPosition;
            // Göğsün yanında diz çöker (Revive_Helper elleri ~0.4 m önde omuzları sarsar)
            Vector3 chest = target.ChestPosition;
            Vector3 dir = transform.position - chest;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.right;
            Vector3 pos = chest + dir.normalized * 0.55f;
            if (GroundProbe.Find(new Vector3(pos.x, hips.y + 1.5f, pos.z), 6f, out RaycastHit hit)) pos.y = hit.point.y;
            Vector3 look = chest - pos; look.y = 0f;
            transform.SetPositionAndRotation(pos, look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look.normalized, Vector3.up) : transform.rotation);
            lastPosition = pos;
        }
        if (crewAnimation != null) crewAnimation.MarkReviving();
        target.TryRevive(this, dt);
        return true;
    }

    private void LateUpdate()
    {
        if (IsDown)
        {
            if (Time.time - DownSince >= getUpDelay) StandUp();
            if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null) animator.SetFloat(speedParameter, 0f);
            lastPosition = transform.position;
            return;
        }
        bool reviving = ReviveDownedPlayer(Time.deltaTime);
        if (!reviving && HeldStretcher == null) PullRope(Time.deltaTime);
        else Pulling = false;
        if (!reviving && HeldStretcher != null)
        {
            // Lider bıraktıysa ya da yardım kapatıldıysa bot da bırakır.
            if (!assistEnabled || leader == null || leader.HeldStretcher != HeldStretcher)
                ReleaseHeld();
            else
                Follow();
        }

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f;
        if (animator != null && animator.runtimeAnimatorController != null)
            animator.SetFloat(speedParameter, delta.magnitude / dt, 0.1f, dt);
        lastPosition = transform.position;
    }

    // D2 kararı 4: sırtında hastayla halatta olan arkadaşı yukarı çeker (multiplayer gelene kadar). Kazığın arkasına
    // (pullerStand) ışınlanır, E basılı tutar gibi her kare çeker (Rope_Pull). Yardım kapalıysa çekmez, halattaki aşağıda kalır.
    public bool Pulling { get; private set; }
    private bool PullRope(float dt)
    {
        Pulling = false;
        if (!assistEnabled || !isActiveAndEnabled || IsDown) return false;
        if (crewAnimation != null && crewAnimation.GettingUp) return false;
        foreach (var rope in RopeAnchor.All)
        {
            if (rope == null || !rope.NeedsPull || rope.pullerStand == null) continue;
            transform.SetPositionAndRotation(rope.pullerStand.position, rope.pullerStand.rotation);
            lastPosition = transform.position;
            if (!rope.Pull(transform, animator, dt)) continue;
            if (crewAnimation != null) crewAnimation.MarkPulling();
            Pulling = true;
            return true;
        }
        return false;
    }

    // Kendi kendine kalkış: kök kalçanın altındaki zemine gelir, ragdoll kapanır, GetUp_FromBack ragdoll pozundan oynar.
    // Kalçanın altında zemin yoksa (uçurum) çarpılmadan önceki yerinde kalkar.
    private void StandUp()
    {
        CrewAnimation.PoseSnapshot pose = animator != null ? CrewAnimation.Capture(animator) : null;
        Vector3 at = hips.position;
        Vector3 ground = lastPosition;
        bool found = GroundProbe.Find(at + Vector3.up * 1.0f, 4f, out RaycastHit hit);
        if (found) ground = hit.point;
        SetRagdoll(false);
        IsDown = false;
        TimesStoodUp++;
        transform.position = ground;
        lastPosition = ground;
        if (crewAnimation != null)
            crewAnimation.BeginGetUp(found ? pose : null, (p, r) => transform.SetPositionAndRotation(p, r));
    }

    private void SetRagdoll(bool on)
    {
        foreach (var rb in ragdollBodies)
        {
            if (rb == null) continue;
            if (!on && !rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            rb.isKinematic = !on;
            rb.useGravity = on;
            rb.interpolation = on ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
        }
        foreach (var c in ragdollColliders)
            if (c != null) c.enabled = on;
        if (animator != null) animator.enabled = !on;
    }

    // Römork modeli: eksen = liderin elinden botun eline (yatay); bot ucu sap aralığı kadar uzakta.
    private void Follow()
    {
        var s = HeldStretcher;
        Vector3 leaderHand = leader.GetHandPoint(s.transform.position);
        Vector3 axis = Flat(GetHandPoint(s.transform.position) - leaderHand, transform.position - leader.Root.position);
        PlaceAt(leaderHand, axis, s.HandleSpan);
    }

    // Bot elini leaderHand + axis·span noktasına getirecek yere durur, sedyeye (lidere) bakar.
    private void PlaceAt(Vector3 leaderHand, Vector3 axis, float span)
    {
        Vector3 hand = leaderHand + axis * span;
        Vector3 root = hand + axis * Reach;
        root.y = hand.y - Height;
        if (GroundProbe.Find(new Vector3(root.x, hand.y + 1.0f, root.z), 40f, out RaycastHit hit))
            root.y = hit.point.y;
        transform.SetPositionAndRotation(root, Quaternion.LookRotation(-axis, Vector3.up));
    }

    private static Vector3 Flat(Vector3 v, Vector3 fallback)
    {
        v.y = 0f;
        if (v.sqrMagnitude < 1e-6f) { v = fallback; v.y = 0f; }
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
    }
}
