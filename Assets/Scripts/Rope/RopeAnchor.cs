using System.Collections.Generic;
using UnityEngine;

// Halat noktası (D2 kullanıcı kararları 3, 4): patika kenarında kazık (SM_RopeAnchor), kaya çıkıntısının tam üstünde.
//  - Kazıkta (topStand yakınında) ya da çıkıntıda halatın dibinde E → halata bağlan.
//  - Halatta: S iner (Rope_Descend, 0.6 m/sn), W çıkar (Rope_Climb, 0.3 m/sn), beklerken Rope_Hang_Idle.
//    Tırmanış hattı, kaya duvarına klibin duvar düzlemi kadar (0.36 m) uzakta, alt ve üst nokta arasında düz bir çizgidir.
//  - Dipte (kayaya basınca) E → halattan çözülür, kayada yürür; tepeye varınca kendiliğinden patikaya (topStand) çıkar.
//  - Sırtında hasta varken kendi tırmanamaz: kazıktaki arkadaş E'yi basılı tutarak çeker (Pull; Rope_Pull,
//    halattaki Piggyback_Rope_Climb, 0.25 m/sn). Multiplayer gelene kadar test botu kazığa ışınlanıp çeker.
// Halat LineRenderer ile çizilir: RopeAttach → (çeken varsa elleri) → kenar → halattakinin üst eli → alt eli → sarkan uç.
[DefaultExecutionOrder(200)] // oyuncu ve bot hareket ettikten, Animator kemikleri yazdıktan sonra çizim (LateUpdate)
public class RopeAnchor : MonoBehaviour
{
    public static readonly List<RopeAnchor> All = new List<RopeAnchor>();

    [Tooltip("Halatın bağlandığı yaka (SM_RopeAnchor/RopeAttach).")]
    public Transform ropeAttach;
    public LineRenderer line;
    [Tooltip("Uçurum kenarındaki halat dönüm noktası (patika yüzeyinde).")]
    public Transform edgePoint;
    [Tooltip("Tırmanış hattının altı: kaya çıkıntısında, halattaki kişinin kökü (ayak).")]
    public Transform climbBottom;
    [Tooltip("Tırmanış hattının üstü: kenarın hemen altında asılı kişinin kökü.")]
    public Transform climbTop;
    [Tooltip("Patikada bağlanma / çıkış noktası (kazığın yanında), yönü patikaya doğru.")]
    public Transform topStand;
    [Tooltip("Çeken kişinin durduğu yer (kazığın arkası), yönü uçuruma.")]
    public Transform pullerStand;

    [Tooltip("Bağlanma / çekme menzili (m): topStand'e, dipte halat köküne ya da pullerStand'e yatay uzaklık (U1 kararı 1: 1.0).")]
    public float attachRange = 1.0f;
    [Tooltip("U1 kararı 1: bağlanma ve çekme yalnız yüz hedefe dönükken (yatay ileri yön ile hedefe yön arasındaki açı ≤ bu kadar °).")]
    public float facingAngle = 30f;
    [Tooltip("Hedefe bundan yakınken (m) yön koşulu aranmaz (yön belirsiz; örn. tam topStand'de).")]
    public float facingIgnoreDistance = 0.2f;
    [Tooltip("İniş hızı (m/sn, dikey; Rope_Descend vertical_speed_mps).")]
    public float descendSpeed = 0.6f;
    [Tooltip("Boşken tırmanış hızı (m/sn, dikey; Rope_Climb).")]
    public float climbSpeed = 0.3f;
    [Tooltip("Çekilme hızı (m/sn, dikey; Piggyback_Rope_Climb 0.25 = Rope_Pull halat hızı 0.255'e en yakın).")]
    public float pullSpeed = 0.25f;
    [Tooltip("Halat tutma noktası: el kemiğinden avuca (m).")]
    public float gripOffset = 0.05f;

    // Halattaki kişi ve hattaki konumu (0 dip, 1 tepe)
    public ThirdPersonPlayer Climber { get; private set; }
    public float T { get; private set; }
    public float VerticalSpeed { get; private set; }
    public bool AtBottom { get { return Climber != null && T <= 0.0001f; } }
    // Son çeken (bot ya da oyuncu) ve son çekme zamanı
    public Transform Puller { get; private set; }
    public bool BeingPulled { get { return Time.time - lastPull < 0.15f; } }
    public float PulledDistance { get; private set; }

    private float lastPull = -10f;
    private float pullThisFrame;
    private Animator pullerAnimator;

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    public float Height { get { return climbTop.position.y - climbBottom.position.y; } }
    public Vector3 ClimbPoint(float t) { return Vector3.Lerp(climbBottom.position, climbTop.position, Mathf.Clamp01(t)); }
    // Halattaki kişi kaya duvarına bakar (climbBottom'un ileri yönü)
    public Quaternion ClimbRotation { get { return climbBottom.rotation; } }

    // Patikadan aşağı mı (kaya çıkıntısı seviyesi): sırttaki hastayı bırakma bunu kullanır.
    public bool IsBelowEdge(Vector3 p) { return p.y < topStand.position.y - 2f; }

    public bool NeedsPull { get { return Climber != null && Climber.Piggyback != null && T < 1f; } }

    // Oyuncu (p, yatay ileri yön forward) hedef noktaya yakın VE yüzü dönük mü (U1 kararı 1).
    public bool Near(Vector3 p, Vector3 forward, Vector3 target)
    {
        if (FlatDistance(p, target) > attachRange || Mathf.Abs(p.y - target.y) >= 1.2f) return false;
        return Facing(p, forward, target);
    }

    public bool Facing(Vector3 p, Vector3 forward, Vector3 target)
    {
        Vector3 to = target - p; to.y = 0f;
        if (to.magnitude < facingIgnoreDistance) return true;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f) return false;
        return Vector3.Angle(forward, to) <= facingAngle;
    }

    // Bağlanabilir mi: halat boş ve oyuncu ≤ attachRange yakında, yüzü bağlanma noktasına (kazıkta topStand, dipte halat kökü) dönük
    public bool CanAttach(Vector3 p, Vector3 forward, out bool atBottom)
    {
        atBottom = false;
        if (Climber != null) return false;
        if (Near(p, forward, topStand.position)) return true;
        if (Near(p, forward, climbBottom.position)) { atBottom = true; return true; }
        return false;
    }

    // Çekebilir mi: halatta sırtında hastayla biri var, oyuncu pullerStand'de (≤ attachRange) ve yüzü kazığa (halata) dönük
    public bool CanPull(Vector3 p, Vector3 forward)
    {
        if (!NeedsPull || pullerStand == null) return false;
        if (FlatDistance(p, pullerStand.position) > attachRange || Mathf.Abs(p.y - pullerStand.position.y) > 1.2f) return false;
        return Facing(p, forward, ropeAttach != null ? ropeAttach.position : pullerStand.position + pullerStand.forward);
    }

    public bool Attach(ThirdPersonPlayer p)
    {
        if (!CanAttach(p.transform.position, p.transform.forward, out bool bottom)) return false;
        Climber = p;
        T = bottom ? 0f : 0.98f;
        VerticalSpeed = 0f;
        PulledDistance = 0f;
        p.AttachRope(this);
        return true;
    }

    // Dipte çözül (kayaya bas): kişi kaya çıkıntısında serbest yürür.
    public bool CanDetachAtBottom(ThirdPersonPlayer p) { return Climber == p && AtBottom; }

    public void Detach(ThirdPersonPlayer p, Vector3 at, Quaternion rotation)
    {
        if (Climber != p) return;
        Climber = null;
        VerticalSpeed = 0f;
        p.DetachRope(at, rotation);
    }

    public void DetachAtBottom(ThirdPersonPlayer p)
    {
        if (!CanDetachAtBottom(p)) return;
        Vector3 at = climbBottom.position;
        if (GroundProbe.Find(at + Vector3.up * 1f, 3f, out RaycastHit g)) at.y = g.point.y + 0.02f;
        Detach(p, at, Quaternion.LookRotation(-Flat(climbBottom.forward), Vector3.up));
    }

    // Çeken taraf her kare çağırır (E basılı / bot). Yalnız sırtında hasta olan kişi çekilir.
    public bool Pull(Transform puller, Animator animator, float dt)
    {
        if (!NeedsPull) return false;
        Puller = puller;
        pullerAnimator = animator;
        lastPull = Time.time;
        pullThisFrame += pullSpeed * dt;
        return true;
    }

    // Halattaki oyuncunun hareketi (ThirdPersonPlayer.Update çağırır). input.y > 0: W, < 0: S.
    public Vector3 Step(ThirdPersonPlayer p, Vector2 input, bool hasControl, float dt)
    {
        float h = Mathf.Max(0.01f, Height);
        float dy = 0f;
        if (pullThisFrame > 0f) dy = pullThisFrame;                          // çekiliyor
        else if (hasControl && input.y < -0.5f) dy = -descendSpeed * dt;     // S: iniş (yükle de)
        else if (hasControl && input.y > 0.5f && p.Piggyback == null) dy = climbSpeed * dt; // W: yalnız boşken
        pullThisFrame = 0f;
        float before = T;
        T = Mathf.Clamp01(T + dy / h);
        VerticalSpeed = (T - before) * h / Mathf.Max(dt, 1e-5f);
        if (dy > 0f && BeingPulled) PulledDistance += (T - before) * h;
        if (T >= 1f && dy > 0f)
        {
            // Tepeye vardı: patikaya çık (topStand), halattan kendiliğinden çözül.
            Vector3 at = topStand.position;
            if (GroundProbe.Find(at + Vector3.up * 1.5f, 4f, out RaycastHit g)) at.y = g.point.y + 0.02f;
            Detach(p, at, topStand.rotation);
            return at;
        }
        return ClimbPoint(T);
    }

    private void LateUpdate()
    {
        if (line == null || ropeAttach == null) return;
        var pts = new List<Vector3> { ropeAttach.position };
        if (BeingPulled && pullerAnimator != null)
            AddHands(pts, pullerAnimator, true);
        pts.Add(edgePoint.position);
        if (Climber != null && Climber.animator != null)
        {
            AddHands(pts, Climber.animator, false);
            Vector3 low = pts[pts.Count - 1];
            pts.Add(new Vector3(low.x, Mathf.Min(low.y - 0.8f, climbBottom.position.y + 0.05f), low.z));
        }
        else
            pts.Add(climbBottom.position + Vector3.up * 0.05f); // boş halat dibe sarkar
        line.positionCount = pts.Count;
        line.SetPositions(pts.ToArray());
    }

    // İki elin avucu: halat çizgisinde üstteki önce (halattaki kişi) ya da kazığa yakın olan önce (çeken).
    private void AddHands(List<Vector3> pts, Animator a, bool puller)
    {
        Vector3 l = Grip(a, HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm);
        Vector3 r = Grip(a, HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm);
        bool lFirst = puller ? (l - ropeAttach.position).sqrMagnitude < (r - ropeAttach.position).sqrMagnitude : l.y > r.y;
        if (puller) { pts.Add(lFirst ? l : r); pts.Add(lFirst ? r : l); }
        else { pts.Add(lFirst ? l : r); pts.Add(lFirst ? r : l); }
    }

    public Vector3 Grip(Animator a, HumanBodyBones hand, HumanBodyBones forearm)
    {
        var h = a.GetBoneTransform(hand);
        var f = a.GetBoneTransform(forearm);
        if (h == null) return a.transform.position;
        Vector3 dir = f != null ? (h.position - f.position).normalized : Vector3.zero;
        return h.position + dir * gripOffset;
    }

    private static float FlatDistance(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }
}
