using System.Collections;
using UnityEngine;

// D2 (minibüs, halat, sırtta taşıma) öncelikleri: halattaysan sırtına al → dipte halattan çözül; sırtında hasta varsa
// yalnız "yere bırak" (sedye/koni yok); sonra mevcut sıra, arada: halatı çek (basılı tut) → kayadaki hastayı sırtına al →
// (hastayı sedyeye al, sap / koni) → halata bağlan → minibüsten hastayı çek → bagaj kapısını aç.

// E tuşu etkileşimleri (kullanıcı kararları 8, 9, 10; trafik 4, 6). Her karede o an ne yapılabileceğini bulur (ipucu metni),
// Interact() (E'ye basıldı) ya da SetInteractHeld (E basılı) çağrılınca onu yapar.
// Öncelik: sedye tutuyorsan bırak → koni tutuyorsan koy → düşmüş arkadaşı kaldır (basılı tut) → hastayı sedyeye al →
// en yakın sedye ucu ya da koni.
// Elde 1 şey: koni tutan sedye tutamaz, sedye tutan koni alamaz (tutarken öncelik hep "bırak / koy").
public class PlayerInteraction : MonoBehaviour
{
    public ThirdPersonPlayer player;
    [Tooltip("Test botu (multiplayer gelene kadar). Boşsa ya da kapalıysa oyuncu tek kalır.")]
    public StretcherHelperBot helper;

    [Tooltip("Sap tutma menzili: oyuncu kökünden sapa yatay uzaklık (m).")]
    public float grabRange = 1.5f;
    [Tooltip("Sap tutma: sap, oyuncunun ayak hizasından en fazla bu kadar yukarıda olabilir (m) (çekici kasası).")]
    public float grabMaxHeight = 2.3f;
    [Tooltip("Oyuncunun hastaya en fazla uzaklığı (m).")]
    public float patientRange = 2f;
    [Tooltip("Sedyenin (PatientAnchor) hastaya en fazla uzaklığı (m).")]
    public float stretcherToPatientRange = 1.5f;
    [Tooltip("Koni alma menzili: oyuncu kökünden koni tabanına yatay uzaklık (m).")]
    public float coneRange = 1.5f;
    [Tooltip("Koni, oyuncunun bu kadar önüne konur (m).")]
    public float conePlaceDistance = 1.0f;

    public const string HintGrab = "E: Sedyeyi tut";
    public const string HintRelease = "E: Bırak";
    public const string HintLoadPatient = "E: Hastayı sedyeye al";
    public const string HintPickCone = "E: Koniyi al";
    public const string HintPlaceCone = "E: Koniyi koy";
    public const string HintRevive = "E (basılı tut): Arkadaşını kaldır";
    // D2
    public const string HintOpenTailgate = "E: Bagaj kapısını aç";
    public const string HintExtract = "E: Hastayı dışarı çek";
    public const string HintAttachRope = "E: Halata bağlan";
    public const string HintDetachRope = "E: Halattan çözül";
    public const string HintPiggyback = "E: Sırtına al";
    public const string HintPutDownBack = "E: Hastayı yere bırak";
    public const string HintPullRope = "E (basılı tut): Halatı çek";

    [Header("D2")]
    [Tooltip("Sırta alma menzili: oyuncu kökünden hastanın kalçasına uzaklık (m).")]
    public float piggybackRange = 1.5f;
    [Tooltip("Halatı çekme menzili: oyuncu kökünden çekme yerine (pullerStand) yatay uzaklık (m).")]
    public float pullRange = 1.5f;
    [Tooltip("Sırta alma: hasta Piggyback_PickUp'ın bu anından itibaren sırtta (kare 17 / 30).")]
    public float piggybackAttachTime = 17f / 30f;
    [Tooltip("Bırakma: hasta Piggyback_PutDown'ın bu anına kadar sırtta (kare 16 / 30).")]
    public float piggybackReleaseTime = 16f / 30f;
    [Tooltip("Bırakılan hastanın kalçası oyuncu kökünün bu kadar arkasına (m) yatar. Hastanın katı gövdesi (NpcBody, kalçadan ayağa 0.78 m) oyuncunun kapsülüne (0.25 m) değmesin diye ≥ 1.03 m.")]
    public float putDownBehind = 1.05f;

    public Patient PiggybackTarget { get; private set; }
    public RopeAnchor PullTarget { get; private set; }
    private bool piggybackBusy;

    public string CurrentHint { get; private set; } = "";
    // Kaldırılmakta olan arkadaş (HUD ilerleme çubuğu için)
    public PlayerKnockdown ReviveTarget { get; private set; }

    private enum Action { None, Release, PlaceCone, Revive, PlacePatient, Grab, PickCone,
        OpenTailgate, Extract, AttachRope, DetachRope, Piggyback, PutDownBack, PullRope }
    private MinivanTailgate targetTailgate;
    private RopeAnchor targetRope;

    private Action action;
    private StretcherController targetStretcher;
    private Patient targetPatient;
    private TrafficCone targetCone;
    private bool targetFront;
    private bool interactHeld;

    private CrewAnimation crewAnimation;

    private void Awake() { crewAnimation = GetComponent<CrewAnimation>(); }

    private void Update()
    {
        Evaluate();
        if (interactHeld && action == Action.Revive && ReviveTarget != null && player != null && player.HasControl)
        {
            if (crewAnimation != null) crewAnimation.MarkReviving(); // Revive_Helper
            ReviveTarget.TryRevive(player, Time.deltaTime);
        }
        if (interactHeld && action == Action.PullRope && targetRope != null && player != null && player.HasControl)
        {
            if (targetRope.Pull(player.transform, player.animator, Time.deltaTime) && crewAnimation != null) crewAnimation.MarkPulling(); // Rope_Pull
        }
    }

    // E basılı mı (her kare girdi okuyucusu ya da test verir). Kaldırma yalnız basılıyken ilerler.
    public void SetInteractHeld(bool held) { interactHeld = held; }

    private void Evaluate()
    {
        action = Action.None;
        targetStretcher = null;
        targetPatient = null;
        targetCone = null;
        ReviveTarget = null;
        targetTailgate = null;
        targetRope = null;
        PiggybackTarget = null;
        PullTarget = null;
        CurrentHint = "";
        if (player == null || player.IsDown || piggybackBusy) return;

        // ---- D2: halatta ----
        if (player.Rope != null)
        {
            // Kayaya basınca önce çözülme (yoksa dipte yanındaki hasta yüzünden halattan hiç inilemez);
            // halatta (dipte değilken) hastaya ≤ 1.5 m ise sırta alma.
            if (player.Rope.CanDetachAtBottom(player))
            {
                action = Action.DetachRope;
                targetRope = player.Rope;
                CurrentHint = HintDetachRope;
            }
            else if (player.Piggyback == null && FindPiggybackPatient())
            {
                action = Action.Piggyback;
                CurrentHint = HintPiggyback;
            }
            return;
        }
        // ---- D2: sırtında hasta: yalnız bırakma (sedye, koni, başka eylem yok) ----
        if (player.Piggyback != null)
        {
            if (CanPutDown(out _, out _))
            {
                action = Action.PutDownBack;
                CurrentHint = HintPutDownBack;
            }
            else if (FindAttachableRope(out var r))
            {
                // Kayada sırtında hastayla: halata yeniden bağlan (çekilmeyi bekle)
                targetRope = r;
                action = Action.AttachRope;
                CurrentHint = HintAttachRope;
            }
            return;
        }

        if (player.HeldStretcher != null)
        {
            action = Action.Release;
            CurrentHint = HintRelease;
            return;
        }
        if (player.HeldCone != null)
        {
            action = Action.PlaceCone;
            CurrentHint = HintPlaceCone;
            return;
        }

        Vector3 me = player.transform.position;

        // Düşmüş takım arkadaşı (yanında)
        foreach (var c in CarrierRegistry.All)
        {
            var mate = c as ThirdPersonPlayer;
            if (mate == null || mate == player || !mate.IsDown) continue;
            if (!mate.knockdown.InReviveRange(player)) continue;
            ReviveTarget = mate.knockdown;
            action = Action.Revive;
            CurrentHint = HintRevive;
            return;
        }

        // ---- D2 ----
        foreach (var r in RopeAnchor.All)
        {
            if (r == null || !r.NeedsPull || r.Climber == player || r.pullerStand == null) continue;
            if (Flat(r.pullerStand.position - me).magnitude > pullRange || Mathf.Abs(r.pullerStand.position.y - me.y) > 1.2f) continue;
            targetRope = r;
            PullTarget = r;
            action = Action.PullRope;
            CurrentHint = HintPullRope;
            return;
        }
        if (FindPiggybackPatient())
        {
            action = Action.Piggyback;
            CurrentHint = HintPiggyback;
            return;
        }
        // Yerdeki sedyeye yakın hasta
        float bestPatient = float.MaxValue;
        foreach (var p in Patient.All)
        {
            if (p == null || !p.CanBePickedUp) continue;
            foreach (var s in StretcherController.All)
            {
                if (!s.CanAcceptPatient(p, me, stretcherToPatientRange, patientRange)) continue;
                float d = Vector3.Distance(me, p.Position);
                if (d < bestPatient)
                {
                    bestPatient = d;
                    targetPatient = p;
                    targetStretcher = s;
                }
            }
        }
        if (targetPatient != null)
        {
            action = Action.PlacePatient;
            CurrentHint = HintLoadPatient;
            return;
        }

        // En yakın boş sap ya da koni
        float best = float.MaxValue;
        foreach (var s in StretcherController.All)
        {
            for (int i = 0; i < 2; i++)
            {
                bool front = i == 0;
                if (!s.CanGrab(front)) continue;
                Vector3 h = (front ? s.handleFront : s.handleRear).position;
                Vector3 flat = h - me;
                float dy = flat.y;
                flat.y = 0f;
                float d = flat.magnitude;
                if (d > grabRange || dy > grabMaxHeight || dy < -1f || d >= best) continue;
                best = d;
                targetStretcher = s;
                targetFront = front;
            }
        }
        foreach (var c in TrafficCone.All)
        {
            if (c == null || c.IsHeld) continue;
            Vector3 flat = c.BasePoint - me;
            float dy = flat.y;
            flat.y = 0f;
            float d = flat.magnitude;
            if (d > coneRange || dy > grabMaxHeight || dy < -1f || d >= best) continue;
            best = d;
            targetCone = c;
        }
        if (targetCone != null)
        {
            targetStretcher = null;
            action = Action.PickCone;
            CurrentHint = HintPickCone;
        }
        else if (targetStretcher != null)
        {
            action = Action.Grab;
            CurrentHint = HintGrab;
        }
        if (action != Action.None) return;
        // D2: halata bağlanma, minibüsten çekme, bagaj kapısı (en düşük öncelik: sedye ve koni önce gelir)
        if (FindAttachableRope(out var rope))
        {
            targetRope = rope;
            action = Action.AttachRope;
            CurrentHint = HintAttachRope;
            return;
        }
        foreach (var tg in MinivanTailgate.All)
        {
            if (tg == null) continue;
            var p = tg.ExtractTarget(me);
            if (p != null)
            {
                targetTailgate = tg;
                targetPatient = p;
                action = Action.Extract;
                CurrentHint = HintExtract;
                return;
            }
            if (tg.CanOpen(me))
            {
                targetTailgate = tg;
                action = Action.OpenTailgate;
                CurrentHint = HintOpenTailgate;
                return;
            }
        }
    }

    // E'ye basıldı.
    public void Interact()
    {
        if (player == null || !player.HasControl) return;
        Evaluate();
        switch (action)
        {
            case Action.Release:
            {
                // Oyuncu bırakınca bot da AYNI ANDA bırakır: güvenli iniş.
                var s = player.HeldStretcher;
                if (helper != null && helper.HeldStretcher == s) helper.ReleaseHeld();
                s.Release(player);
                break;
            }
            case Action.PlaceCone:
                PlaceCone();
                break;
            case Action.PlacePatient:
                targetStretcher.PlacePatient(targetPatient);
                break;
            case Action.Grab:
                if (targetStretcher.Grab(player, targetFront, false))
                {
                    // E2: iki taşıyıcı da sedyenin baş yönüne bakar (öndeki kollar arkada çeker, arkadaki önünde tutar).
                    Vector3 heading = targetStretcher.handleFront.position - targetStretcher.handleRear.position;
                    heading.y = 0f;
                    if (heading.sqrMagnitude > 1e-4f) player.transform.rotation = Quaternion.LookRotation(heading.normalized, Vector3.up);
                    if (helper != null && helper.CanAssist)
                        helper.Assist(player, targetStretcher, targetFront);
                    targetStretcher.NotifyIfUnderstaffed();
                }
                break;
            case Action.PickCone:
                player.HeldCone = targetCone;
                targetCone.PickUp(player);
                break;
            case Action.OpenTailgate:
                targetTailgate.Open();
                break;
            case Action.Extract:
                targetTailgate.Extract(targetPatient, player);
                break;
            case Action.AttachRope:
                targetRope.Attach(player);
                break;
            case Action.DetachRope:
                targetRope.DetachAtBottom(player);
                break;
            case Action.Piggyback:
                StartCoroutine(PiggybackRoutine(PiggybackTarget));
                break;
            case Action.PutDownBack:
                StartCoroutine(PutDownRoutine());
                break;
        }
        Evaluate();
    }

    // ---- D2 yardımcıları ----
    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // Kayadaki (sırta alınabilir) hasta menzilde mi
    private bool FindPiggybackPatient()
    {
        Vector3 me = player.transform.position;
        float best = float.MaxValue;
        foreach (var p in Patient.All)
        {
            if (p == null || !p.CanPiggyback) continue;
            float d = Vector3.Distance(me, p.Position);
            if (d <= piggybackRange && d < best) { best = d; PiggybackTarget = p; }
        }
        return PiggybackTarget != null;
    }

    private bool FindAttachableRope(out RopeAnchor rope)
    {
        rope = null;
        foreach (var r in RopeAnchor.All)
            if (r != null && r.CanAttach(player.transform.position, out _)) { rope = r; return true; }
        return false;
    }

    // Sırttaki hasta yalnız patikada (halatta ve kaya çıkıntısında değil) ve arkadaki yer güvenliyse bırakılır.
    private bool CanPutDown(out Vector3 point, out Vector3 normal)
    {
        point = default; normal = Vector3.up;
        if (player.Rope != null) return false;
        Vector3 me = player.transform.position;
        foreach (var r in RopeAnchor.All) if (r != null && r.IsBelowEdge(me)) return false;
        Vector3 back = -Flat(player.transform.forward).normalized;
        Vector3 at = me + back * putDownBehind;
        if (!GroundProbe.Find(at + Vector3.up * 1f, 2f, out RaycastHit g) || g.normal.y < 0.8f) return false;
        point = g.point; normal = g.normal;
        // Gövde boyunca (baş arkaya) ve iki yanda zemin olmalı; uçurum kenarına 1 m'den yakın bırakılmaz.
        Vector3 side = Vector3.Cross(Vector3.up, back);
        foreach (var o in new[] { back * 1.8f, back * 0.9f, -back * 0.8f, side * 0.6f, -side * 0.6f })
            if (!GroundProbe.Find(point + o + Vector3.up * 1f, 2f, out RaycastHit h) || Mathf.Abs(h.point.y - point.y) > 0.5f) return false;
        return true;
    }

    private IEnumerator PiggybackRoutine(Patient p)
    {
        if (p == null || piggybackBusy) yield break;
        piggybackBusy = true;
        // Sırtı hastaya dönük (halatta yön halattan gelir)
        if (player.Rope == null)
        {
            Vector3 away = Flat(player.transform.position - p.Position);
            if (away.sqrMagnitude > 1e-4f) player.Teleport(player.transform.position, Quaternion.LookRotation(away.normalized, Vector3.up));
        }
        player.SuspendControl(1.5f);
        if (crewAnimation != null) crewAnimation.PlayAction(CrewAnimation.StatePiggybackPickUp);
        yield return new WaitForSeconds(piggybackAttachTime);
        if (p.CanPiggyback && !player.IsDown) player.SetPiggyback(p);
        piggybackBusy = false;
    }

    private IEnumerator PutDownRoutine()
    {
        if (piggybackBusy || !CanPutDown(out Vector3 point, out Vector3 normal)) yield break;
        piggybackBusy = true;
        Vector3 head = -Flat(player.transform.forward).normalized; // baş arkaya (hasta sırttan geriye kayar)
        player.SuspendControl(1.2f);
        if (crewAnimation != null) crewAnimation.PlayAction(CrewAnimation.StatePiggybackPutDown);
        yield return new WaitForSeconds(piggybackReleaseTime);
        var p = player.ReleasePiggyback();
        if (p != null) p.PutDownFromBack(point, head, normal);
        piggybackBusy = false;
    }

    // Koni oyuncunun baktığı yere (önüne), zemine dik konur. Zemin bulunamazsa elde kalır.
    private void PlaceCone()
    {
        var cone = player.HeldCone;
        Vector3 fwd = player.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 at = player.transform.position + fwd * conePlaceDistance;
        if (!GroundProbe.Find(at + Vector3.up * 1.5f, 4f, out RaycastHit hit)) return;
        player.HeldCone = null;
        cone.PlaceAt(hit.point, Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg);
    }
}
