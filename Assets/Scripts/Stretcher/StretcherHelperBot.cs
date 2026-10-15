using UnityEngine;

// Test botu (kullanıcı kararı 9): multiplayer gelene kadar sedyenin öbür ucunu tutar.
// Oyuncu bir ucu tutunca bot diğer uca ışınlanır. Sonra "römork" modeliyle izler: sedye ekseni iki taşıyıcı
// arasındaki çizgidir, bot lidere olan uzaklığı sap aralığında tutar. Y'sini zemine raycast'le bulur, çarpışmaları yok sayar
// (collider'ı yok). Oyuncu bırakınca bot da AYNI ANDA bırakır (güvenli iniş; bırakmayı PlayerInteraction yapar).
// İleride ikinci oyuncu bu botun yerine geçecek: ikisi de ICarrier.
[DefaultExecutionOrder(50)] // oyuncu hareket ettikten sonra, sedye çözümünden önce
public class StretcherHelperBot : MonoBehaviour, ICarrier
{
    [Tooltip("Açıkken oyuncu sedye tutunca bot öbür ucu tutar. Kapatınca oyuncu tek başına kalır.")]
    public bool assistEnabled = true;
    public CarryRules rules;
    public Animator animator;
    [Tooltip("Animator'daki hız parametresi.")]
    public string speedParameter = "Speed";

    public Transform Root { get { return transform; } }
    public StretcherController HeldStretcher { get; set; }

    private ICarrier leader;
    private Vector3 lastPosition;

    public bool CanAssist { get { return assistEnabled && isActiveAndEnabled && HeldStretcher == null; } }

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

    private void LateUpdate()
    {
        if (HeldStretcher != null)
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
