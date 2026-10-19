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
//
// U3 (kullanıcı kararı 2026-10-19): BÜTÜN E etkileşimlerinde "yüz dönük" kuralı — hedef, oyuncunun önündeki ~60° koni içinde
// (yatay ileri yön ile hedefe yön ≤ facingAngle = 30°; U1 halat kuralıyla aynı yardımcı: RopeAnchor.FacingTarget). Mesafeler
// mevcut değerlerde kalır; hedef ≤ 0.2 m'deyse yön aranmaz. Değerli eşyalar: valiz / hasta eşyası tek elle taşınır (koni kuralı),
// güvenli yere (çekici arkası teslim noktası ya da durakta bekleyen ambulansın arka kapısı) konunca para yazılır.
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
    // U1: eylem değil, uyarı (sırtta hasta varken burada bırakılamıyor); HUD soluk uyarı renginde gösterir
    public const string NoticeCannotPutDown = "Burada bırakılamaz";
    // U2: halat zinciri (dolap → kangal → kazık → atış)
    public const string HintOpenLocker = "E: Dolabı aç";
    public const string HintCloseLocker = "E: Dolabı kapat";
    public const string HintTakeCoil = "E: Halatı al";
    public const string HintDropCoil = "E: Kangalı bırak";
    public const string HintTieStake = "E: Halatı kazığa bağla";
    public const string HintThrowRope = "E: Halatı at";
    // U3: değerli eşyalar ve para
    public const string HintTakeLuggage = "E: Valizi al";
    public const string HintTakePatientItem = "E: Değerli eşyasını al";
    public const string HintDropLoot = "E: Eşyayı bırak";
    public const string HintSecureLoot = "E: Güvenli yere koy";

    [Header("U3: yüz dönük kuralı (bütün E etkileşimleri)")]
    [Tooltip("Hedef, yatay ileri yönden en fazla bu kadar ° sapabilir (~60° koni).")]
    public float facingAngle = 30f;
    [Tooltip("Hedef bundan yakınken (m) yön aranmaz.")]
    public float facingIgnoreDistance = 0.2f;
    [Tooltip("Güvenli yer menzili: oyuncu kökünden teslim noktasına / ambulans yükleme noktasına yatay uzaklık (m).")]
    public float safeDropRange = 1.5f;
    [Tooltip("Eşya bırakılırken oyuncunun bu kadar önüne (m) konur.")]
    public float lootPlaceDistance = 0.7f;
    public LootItem LootTarget { get; private set; }
    // Yüz kuralı yüzünden gösterilmeyen ipucu sayısı (ölçüm): hedef menzilde ama sırt dönük
    public int FacingRejections { get; private set; }

    [Header("U2: halat zinciri (klip zamanları, clips_meta.json; kare numaraları 1 tabanlı, t = (kare−1)/(kare sayısı−1) × süre)")]
    [Tooltip("Cabinet_TakeCoil süresi (sn) ve kangalın ellere geçtiği an (grab_frame 11 / 43 kare).")]
    public float takeCoilSeconds = 1.4f, takeCoilGrabTime = 1.4f * 10f / 42f;
    [Tooltip("Dolaptan alırken yaklaşma: kangal slotu oyuncunun bu kadar önünde (m) (cabinet_coil_root_local_m y −0.40; kasa collider'ı için 0.45).")]
    public float takeCoilApproach = 0.45f;
    [Tooltip("Rope_TieStake süresi (sn); kangalın yerde olduğu aralık (coil_ground_frames 17–45 / 61) ve düğüm anı (stake_hand_frames 21).")]
    public float tieSeconds = 2.0f, tieGroundStart = 2.0f * 16f / 60f, tieKnotTime = 2.0f * 20f / 60f, tieGroundEnd = 2.0f * 44f / 60f;
    [Tooltip("Bağlarken kazık oyuncunun bu kadar önünde (m) (stake_root_local_m y −0.51).")]
    public float tieStakeAhead = 0.51f;
    [Tooltip("Bağlarken yerdeki kangal: oyuncu kök uzayında (m) ve yatay dönüşü (°) (coil_ground_root_local_m, Blender (0.27, −0.43, 0.222) → Unity (−0.27, 0.222, 0.43)).")]
    public Vector3 tieCoilLocal = new Vector3(-0.27f, 0f, 0.43f);
    public float tieCoilYaw = -35f;
    [Tooltip("Rope_Throw süresi (sn) ve bırakış anı (release_frame 22 / 43 kare).")]
    public float throwSeconds = 1.4f, throwReleaseTime = 1.4f * 21f / 42f;
    [Tooltip("Kangal yere bırakılırken oyuncunun bu kadar önüne (m) konur.")]
    public float coilPlaceDistance = 0.6f;

    [Header("D2")]
    [Tooltip("Sırta alma menzili: oyuncu kökünden hastanın kalçasına uzaklık (m).")]
    public float piggybackRange = 1.5f;
    // Halatı çekme menzili ve yön kuralı RopeAnchor.attachRange / facingAngle'dadır (U1 kararı 1: bağlanmayla aynı).
    [Tooltip("Sırta alma: hasta Piggyback_PickUp'ın bu anından itibaren sırtta (kare 17 / 30).")]
    public float piggybackAttachTime = 17f / 30f;
    [Tooltip("Bırakma: hasta Piggyback_PutDown'ın bu anına kadar sırtta (kare 16 / 30).")]
    public float piggybackReleaseTime = 16f / 30f;
    [Tooltip("Bırakılan hastanın kalçası oyuncu kökünün bu kadar arkasına (m) yatar. Hastanın katı gövdesi (NpcBody, kalçadan ayağa 0.78 m) oyuncunun kapsülüne (0.25 m) değmesin diye ≥ 1.03 m.")]
    public float putDownBehind = 1.05f;
    [Tooltip("U1 kararı 3: sırttaki hasta en fazla bu eğimdeki (°) zemine bırakılır; hasta zemin normaline göre yatar.")]
    public float putDownMaxSlope = 35f;
    [Tooltip("U1 kararı 3: bırakılan gövdenin her noktası uçurum kenarından (zeminin bittiği / bu kadardan çok düştüğü yer) en az bu kadar (m) uzak olmalı.")]
    public float putDownEdgeClearance = 1.0f;
    [Tooltip("Kenar sayılan düşüş (m): çevre örneğinde zemin beklenen düzlemden bundan çok aşağıdaysa ya da yoksa.")]
    public float putDownEdgeDrop = 0.7f;
    [Tooltip("Gövde örneklerinin bırakma düzleminden sapma payı (m): çukur/basamak/kaya varsa bırakılmaz.")]
    public float putDownBodyTolerance = 0.25f;

    public Patient PiggybackTarget { get; private set; }
    public RopeAnchor PullTarget { get; private set; }
    private bool piggybackBusy;

    public string CurrentHint { get; private set; } = "";
    // U1: CurrentHint bir eylem değil uyarı mı ("Burada bırakılamaz")
    public bool HintIsWarning { get; private set; }
    // U1: ipucunun hedefi (beyaz vurgu); hedefsiz eylemlerde (bırak, koy, yere bırak) null
    public Transform HighlightTarget { get; private set; }
    // Kaldırılmakta olan arkadaş (HUD ilerleme çubuğu için)
    public PlayerKnockdown ReviveTarget { get; private set; }

    private enum Action { None, Release, PlaceCone, Revive, PlacePatient, Grab, PickCone,
        OpenTailgate, Extract, AttachRope, DetachRope, Piggyback, PutDownBack, PullRope,
        OpenLocker, CloseLocker, TakeCoil, DropCoil, TieStake, ThrowRope,
        TakeLoot, DropLoot, SecureLoot }
    private MinivanTailgate targetTailgate;
    private Transform targetSafe;   // U3: güvenli yerin vurgu hedefi (halka ya da ambulans kapısı)
    private RopeAnchor targetRope;
    private TowTruckLocker targetLocker;
    private RopeCoil targetCoil;
    private bool coilBusy; // U2: dolaptan alma / bağlama / atma klibi sürüyor

    private Action action;
    private StretcherController targetStretcher;
    private Patient targetPatient;
    private TrafficCone targetCone;
    private bool targetFront;
    private bool interactHeld;

    private CrewAnimation crewAnimation;

    private void Awake()
    {
        crewAnimation = GetComponent<CrewAnimation>();
        // U1: hedef vurgusu (yerel oyuncu); sahne dosyasında ayrıca kurulmaz
        var highlight = GetComponent<InteractionHighlight>();
        if (highlight == null) highlight = gameObject.AddComponent<InteractionHighlight>();
        if (highlight.interaction == null) highlight.interaction = this;
    }

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
        EvaluateAction();
        HighlightTarget = ResolveHighlightTarget();
    }

    // U1: ipucunun hedef nesnesi. Yalnız bir nesneye yönelen eylemlerde; bırak/koy/yere bırak ve uyarıda null.
    private Transform ResolveHighlightTarget()
    {
        switch (action)
        {
            case Action.Grab: return targetStretcher != null ? targetStretcher.transform : null;
            case Action.PlacePatient: return targetPatient != null ? targetPatient.transform : null;
            case Action.PickCone: return targetCone != null ? (targetCone.target != null ? targetCone.target : targetCone.transform) : null; // koninin görseli (denetleyicinin çocuğu değil)
            case Action.Revive: return ReviveTarget != null ? ReviveTarget.transform : null;
            case Action.OpenTailgate: return targetTailgate != null ? (targetTailgate.tailgate != null ? targetTailgate.tailgate : targetTailgate.transform) : null;
            case Action.Extract: return targetPatient != null ? targetPatient.transform : null;
            case Action.AttachRope:
            case Action.DetachRope:
            case Action.PullRope:
            case Action.TieStake:
            case Action.ThrowRope: return targetRope != null ? targetRope.transform : null;
            case Action.Piggyback: return PiggybackTarget != null ? PiggybackTarget.transform : null;
            // U2: dolap kapağı, dolaptaki / yerdeki kangal; bırakma hedefsiz
            case Action.OpenLocker:
            case Action.CloseLocker: return targetLocker != null ? (targetLocker.door != null ? targetLocker.door : targetLocker.transform) : null;
            case Action.TakeCoil: return targetCoil != null ? targetCoil.transform : null;
            // U3: eşya (valiz prop'u / hasta kutusu) ve güvenli yer işareti; bırakma hedefsiz
            case Action.TakeLoot: return LootTarget != null ? LootTarget.transform : null;
            case Action.SecureLoot: return targetSafe;
            default: return null;
        }
    }

    private void EvaluateAction()
    {
        action = Action.None;
        targetStretcher = null;
        targetPatient = null;
        targetCone = null;
        ReviveTarget = null;
        targetTailgate = null;
        targetRope = null;
        targetLocker = null;
        targetCoil = null;
        PiggybackTarget = null;
        PullTarget = null;
        LootTarget = null;
        targetSafe = null;
        CurrentHint = "";
        HintIsWarning = false;
        if (player == null || player.IsDown || piggybackBusy || coilBusy) return;

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
            if (CanPutDown(out _, out _, out _))
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
            else
            {
                // U1 kararı 3: bırakılamayan yerde uyarı (eylem yok)
                CurrentHint = NoticeCannotPutDown;
                HintIsWarning = true;
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
        // ---- U3: değerli eşya elde: güvenli yerdeysen koy, değilse bırak (elde 1 şey kuralı) ----
        if (player.HeldValuable != null)
        {
            if (FindSafeZone(out Transform safeHl))
            {
                targetSafe = safeHl;
                action = Action.SecureLoot;
                CurrentHint = HintSecureLoot;
            }
            else
            {
                action = Action.DropLoot;
                CurrentHint = HintDropLoot;
            }
            return;
        }
        // ---- U2: kangal elde: bağlıysa yalnız at (yüz kenara dönük), değilse kazığa bağla ya da yere bırak ----
        if (player.HeldCoil != null)
        {
            var heldCoil = player.HeldCoil;
            var heldRope = heldCoil.anchor;
            Vector3 cp = player.transform.position;
            Vector3 cf = player.transform.forward;
            if (heldRope != null && heldRope.State == RopeAnchor.RopeState.Tied)
            {
                // Bağlanmış ama atılmamış halat çözülemez; yanlış yöne bakarken ipucu yok
                if (heldRope.CanThrow(cp, cf)) { targetRope = heldRope; action = Action.ThrowRope; CurrentHint = HintThrowRope; }
            }
            else if (heldRope != null && heldRope.CanTie(cp, cf))
            {
                targetRope = heldRope;
                action = Action.TieStake;
                CurrentHint = HintTieStake;
            }
            else if (heldRope == null || heldRope.State == RopeAnchor.RopeState.Stowed)
            {
                action = Action.DropCoil;
                CurrentHint = HintDropCoil;
            }
            return;
        }

        Vector3 me = player.transform.position;

        // Düşmüş takım arkadaşı (yanında)
        foreach (var c in CarrierRegistry.All)
        {
            var mate = c as ThirdPersonPlayer;
            if (mate == null || mate == player || !mate.IsDown) continue;
            if (!mate.knockdown.InReviveRange(player)) continue;
            if (!Facing(mate.knockdown.GroundPosition) && !Facing(mate.knockdown.ChestPosition)) { FacingRejections++; continue; } // U3 yüz kuralı
            ReviveTarget = mate.knockdown;
            action = Action.Revive;
            CurrentHint = HintRevive;
            return;
        }

        // ---- U2: çekici dolabı ve halat kangalı (yüz dönük, ≤ 1 m; koniden önce: dolabın önünde deckteki koni de menzilde olabilir) ----
        Vector3 fwd = player.transform.forward;
        foreach (var l in TowTruckLocker.All)
        {
            if (l == null) continue;
            // Kapak açık ve kangal içindeyse önce kangal hedeflenir
            if (l.IsOpen && l.coil != null && l.coil.InLocker && l.coil.CanPickUp && l.PlayerOutside(me) && l.coil.CanInteract(me, fwd))
            {
                targetCoil = l.coil;
                action = Action.TakeCoil;
                CurrentHint = HintTakeCoil;
                return;
            }
            if (l.CanInteract(me, fwd))
            {
                targetLocker = l;
                action = l.IsOpen ? Action.CloseLocker : Action.OpenLocker;
                CurrentHint = l.IsOpen ? HintCloseLocker : HintOpenLocker;
                return;
            }
        }
        foreach (var c in RopeCoil.All)
        {
            if (c == null || !c.OnGround || !c.CanPickUp || !c.CanInteract(me, fwd)) continue;
            targetCoil = c;
            action = Action.TakeCoil;
            CurrentHint = HintTakeCoil;
            return;
        }

        // ---- D2 ----
        foreach (var r in RopeAnchor.All)
        {
            // U1 kararı 1: çekme de bağlanma kuralıyla (≤ 1.0 m, yüz halata dönük)
            if (r == null || r.Climber == player || !r.CanPull(me, player.transform.forward)) continue;
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
                if (!Facing(p.Position)) { FacingRejections++; continue; } // U3 yüz kuralı
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
                if (!Facing(h)) { FacingRejections++; continue; } // U3 yüz kuralı
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
            if (!Facing(c.Center)) { FacingRejections++; continue; } // U3 yüz kuralı
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
        // U3: değerli eşya al (valiz prop'u / hastanın eşyası): ≤ menzil, yüz dönük; sedye ve koniden sonra
        if (FindLoot(out var loot))
        {
            LootTarget = loot;
            action = Action.TakeLoot;
            CurrentHint = loot.kind == LootKind.PatientItem ? HintTakePatientItem : HintTakeLuggage;
            return;
        }
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
            if (p != null && !Facing(p.Position)) { FacingRejections++; p = null; } // U3 yüz kuralı
            if (p != null)
            {
                targetTailgate = tg;
                targetPatient = p;
                action = Action.Extract;
                CurrentHint = HintExtract;
                return;
            }
            // U3 yüz kuralı: kapı geniş; yüz, kapının oyuncuya en yakın noktasına dönük olmalı
            if (tg.CanOpen(me) && !Facing(ClosestOnRenderers(tg.tailgate != null ? tg.tailgate : tg.transform, me))) { FacingRejections++; continue; }
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
            // U2
            case Action.OpenLocker:
            case Action.CloseLocker:
                targetLocker.Toggle();
                break;
            case Action.TakeCoil:
                StartCoroutine(TakeCoilRoutine(targetCoil));
                break;
            case Action.DropCoil:
                DropCoil();
                break;
            case Action.TieStake:
                StartCoroutine(TieStakeRoutine(targetRope));
                break;
            case Action.ThrowRope:
                StartCoroutine(ThrowRopeRoutine(targetRope));
                break;
            // U3
            case Action.TakeLoot:
                if (LootTarget != null && LootTarget.CanPickUp)
                {
                    player.HeldValuable = LootTarget;
                    LootTarget.PickUp(player);
                }
                break;
            case Action.DropLoot:
                DropLoot();
                break;
            case Action.SecureLoot:
                SecureLoot();
                break;
        }
        Evaluate();
    }

    // ---- U3: yüz kuralı ve değerli eşyalar ----

    // Yüz hedefe dönük mü (U1 halat kuralıyla aynı yardımcı; hedef ≤ facingIgnoreDistance ise evet).
    public bool Facing(Vector3 target)
    {
        return RopeAnchor.FacingTarget(player.transform.position, player.transform.forward, target, facingAngle, facingIgnoreDistance);
    }

    // Geniş hedefler (bagaj kapısı) için: renderer sınırlarının oyuncuya en yakın noktası.
    private static Vector3 ClosestOnRenderers(Transform t, Vector3 p)
    {
        var rs = t.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return t.position;
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b.ClosestPoint(p);
    }

    // Alınabilir en yakın eşya: menzilde ve yüz dönük.
    private bool FindLoot(out LootItem best)
    {
        best = null;
        Vector3 me = player.transform.position;
        float bestD = float.MaxValue;
        foreach (var l in LootItem.All)
        {
            if (l == null || !l.CanPickUp || !l.InRange(me)) continue;
            if (!Facing(l.Center)) { FacingRejections++; continue; }
            float d = Vector3.Distance(me, l.Center);
            if (d < bestD) { bestD = d; best = l; }
        }
        return best != null;
    }

    // Güvenli yer: teslim noktası halkası ya da durakta bekleyen (kapıları açık) ambulansın yükleme noktası; menzilde ve yüz dönük.
    private bool FindSafeZone(out Transform highlight)
    {
        highlight = null;
        Vector3 me = player.transform.position;
        foreach (var sp in SafeDropPoint.All)
        {
            if (sp == null || !sp.InRange(me)) continue;
            if (!Facing(sp.Center)) { FacingRejections++; continue; }
            highlight = sp.HighlightTarget;
            return true;
        }
        var mission = MissionDirector.Instance;
        var amb = mission != null ? mission.WaitingAmbulance : null;
        if (amb != null && amb.LoadingOpen && amb.loadPoint != null)
        {
            Vector3 d = amb.loadPoint.position - me;
            float dy = Mathf.Abs(d.y);
            d.y = 0f;
            if (d.magnitude <= safeDropRange && dy <= 1.5f && Facing(amb.loadPoint.position))
            {
                highlight = amb.doorLeft != null ? amb.doorLeft : amb.transform;
                return true;
            }
        }
        return false;
    }

    // Eşya oyuncunun önüne, zemine konur (zemin yoksa elde kalır).
    private void DropLoot()
    {
        var item = player.HeldValuable;
        if (item == null) return;
        Vector3 fwd = Flat(player.transform.forward);
        fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
        Vector3 at = player.transform.position + fwd * lootPlaceDistance;
        if (!GroundProbe.Find(at + Vector3.up * 1.5f, 4f, out RaycastHit hit)) return;
        player.HeldValuable = null;
        item.Drop(hit.point, Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg);
    }

    // Güvenli yere koy: eşya kaybolur, para yazılır (LootRegistry).
    private void SecureLoot()
    {
        var item = player.HeldValuable;
        if (item == null) return;
        var registry = LootRegistry.Instance;
        player.HeldValuable = null;
        if (registry != null) registry.Secure(item);
        else item.Secure();
        foreach (var sp in SafeDropPoint.All) if (sp != null && sp.InRange(player.transform.position)) { sp.CountDelivery(); break; }
    }

    // ---- U2: halat zinciri ----

    // Dolaptan: oyuncu slotun önüne hizalanır (yüz dolaba), Cabinet_TakeCoil oynar, grab karesinde kangal ellere geçer.
    // Yerden: anında elde (genel PickUp klibi Carry değişince tetiklenir).
    private IEnumerator TakeCoilRoutine(RopeCoil coil)
    {
        if (coil == null || coilBusy || !coil.CanPickUp) yield break;
        coilBusy = true;
        if (coil.InLocker)
        {
            Vector3 n = coil.locker != null ? coil.locker.outward : Flat(player.transform.position - coil.GripMid);
            n.y = 0f;
            n = n.sqrMagnitude > 1e-6f ? n.normalized : Vector3.forward;
            Vector3 at = coil.GripMid + n * takeCoilApproach;
            if (GroundProbe.Find(new Vector3(at.x, player.transform.position.y + 1f, at.z), 3f, out RaycastHit g)) at.y = g.point.y + 0.02f;
            else at.y = player.transform.position.y;
            player.Teleport(at, Quaternion.LookRotation(-n, Vector3.up));
            player.SuspendControl(takeCoilSeconds);
            if (crewAnimation != null) crewAnimation.PlayAction(CrewAnimation.StateTakeCoil, takeCoilSeconds);
            yield return new WaitForSeconds(takeCoilGrabTime);
            if (!player.IsDown && coil.CanPickUp && player.HeldCoil == null)
            {
                player.HeldCoil = coil;
                coil.PickUp(player);
            }
            yield return new WaitForSeconds(Mathf.Max(0f, takeCoilSeconds - takeCoilGrabTime - 0.2f));
        }
        else if (player.HeldCoil == null)
        {
            player.HeldCoil = coil;
            coil.PickUp(player);
        }
        coilBusy = false;
    }

    // Kangal oyuncunun önüne, zemine yatay konur (zemin yoksa elde kalır). Genel PutDown klibi Carry değişince oynar.
    private void DropCoil()
    {
        var coil = player.HeldCoil;
        if (coil == null) return;
        Vector3 fwd = Flat(player.transform.forward);
        fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
        Vector3 at = player.transform.position + fwd * coilPlaceDistance;
        if (!GroundProbe.Find(at + Vector3.up * 1.5f, 4f, out RaycastHit hit)) return;
        player.HeldCoil = null;
        coil.PutDown(hit.point, hit.normal, Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg);
    }

    // Rope_TieStake: oyuncu kazığın tieStakeAhead gerisine, yüzü kazığa; kare 17–45 arasında kangal kazığın yanında yerde dik,
    // kare 21'de halat ucu kazığa bağlı (çizgi kazıktan kangala); sonra kangal tekrar ellerde.
    private IEnumerator TieStakeRoutine(RopeAnchor rope)
    {
        var coil = player.HeldCoil;
        if (rope == null || coil == null || coilBusy || rope.State != RopeAnchor.RopeState.Stowed) yield break;
        coilBusy = true;
        Vector3 toStake = Flat(rope.transform.position - player.transform.position);
        toStake = toStake.sqrMagnitude > 1e-6f ? toStake.normalized : Flat(player.transform.forward).normalized;
        Vector3 at = rope.transform.position - toStake * tieStakeAhead;
        if (GroundProbe.Find(new Vector3(at.x, rope.transform.position.y + 1f, at.z), 3f, out RaycastHit g)) at.y = g.point.y + 0.02f;
        else at.y = player.transform.position.y;
        player.Teleport(at, Quaternion.LookRotation(toStake, Vector3.up));
        player.SuspendControl(tieSeconds);
        if (crewAnimation != null) crewAnimation.PlayAction(CrewAnimation.StateTieStake, tieSeconds);
        yield return new WaitForSeconds(tieGroundStart);
        if (player.HeldCoil == coil)
        {
            Vector3 cp = player.transform.TransformPoint(new Vector3(tieCoilLocal.x, 0f, tieCoilLocal.z));
            if (GroundProbe.Find(cp + Vector3.up * 1f, 3f, out RaycastHit cg)) cp.y = cg.point.y;
            else cp.y = player.transform.position.y;
            coil.BeginTiePose(cp, Quaternion.AngleAxis(tieCoilYaw, Vector3.up) * player.transform.forward);
        }
        yield return new WaitForSeconds(Mathf.Max(0f, tieKnotTime - tieGroundStart));
        rope.Tie();
        yield return new WaitForSeconds(Mathf.Max(0f, tieGroundEnd - tieKnotTime));
        coil.EndTiePose();
        yield return new WaitForSeconds(Mathf.Max(0f, tieSeconds - tieGroundEnd - 0.15f));
        coilBusy = false;
    }

    // Rope_Throw: bırakış karesinde kangal ellerden çıkar, halat dibine uçar (RopeAnchor sürer); inince halat hazır.
    private IEnumerator ThrowRopeRoutine(RopeAnchor rope)
    {
        var coil = player.HeldCoil;
        if (rope == null || coil == null || coilBusy || rope.State != RopeAnchor.RopeState.Tied) yield break;
        coilBusy = true;
        player.SuspendControl(throwSeconds);
        if (crewAnimation != null) crewAnimation.PlayAction(CrewAnimation.StateRopeThrow, throwSeconds);
        yield return new WaitForSeconds(throwReleaseTime);
        if (player.HeldCoil == coil && rope.State == RopeAnchor.RopeState.Tied)
        {
            player.HeldCoil = null;
            rope.BeginThrow();
        }
        yield return new WaitForSeconds(Mathf.Max(0f, throwSeconds - throwReleaseTime - 0.2f));
        coilBusy = false;
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
            if (d > piggybackRange || d >= best) continue;
            if (player.Rope == null && !Facing(p.Position)) { FacingRejections++; continue; } // U3 yüz kuralı (halattayken yön halattan)
            best = d; PiggybackTarget = p;
        }
        return PiggybackTarget != null;
    }

    private bool FindAttachableRope(out RopeAnchor rope)
    {
        rope = null;
        foreach (var r in RopeAnchor.All)
            if (r != null && r.CanAttach(player.transform.position, player.transform.forward, out _)) { rope = r; return true; }
        return false;
    }

    // U1 kararı 3: sırttaki hasta ≤ putDownMaxSlope eğimli, yürünebilir her yere bırakılabilir (halatta ve kaya çıkıntısında değil).
    // Önce arka, sonra sol, sağ, ön denenir; ilk uygun yer seçilir. head = hastanın baş yönü (oyuncudan bırakma yerine).
    private bool CanPutDown(out Vector3 point, out Vector3 normal, out Vector3 head)
    {
        point = default; normal = Vector3.up;
        Vector3 fwd = Flat(player.transform.forward);
        fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
        head = -fwd;
        if (player.Rope != null) return false;
        Vector3 me = player.transform.position;
        foreach (var r in RopeAnchor.All) if (r != null && r.IsBelowEdge(me)) return false; // kaya çıkıntısı kuralı aynı
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        foreach (var dir in new[] { -fwd, -right, right, fwd })
            if (TryPutDownSpot(me, dir, out point, out normal)) { head = dir; return true; }
        return false;
    }

    // Oyuncudan dir yönünde putDownBehind uzaktaki yer bırakmaya uygun mu: eğim, gövdenin düzleme oturması, kenar payı, engel.
    private bool TryPutDownSpot(Vector3 me, Vector3 dir, out Vector3 point, out Vector3 normal)
    {
        point = default; normal = Vector3.up;
        float minNormalY = Mathf.Cos(putDownMaxSlope * Mathf.Deg2Rad);
        Vector3 at = me + dir * putDownBehind;
        if (!GroundProbe.Find(at + Vector3.up * 1f, 2.5f, out RaycastHit g) || g.normal.y < minNormalY) return false;
        if (Mathf.Abs(g.point.y - me.y) > 0.8f) return false; // basamak / duvar üstü / çukur
        point = g.point; normal = g.normal;
        Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
        // Gövde (kalçadan başa 0.9 m, ayaklara 0.8 m, iki yan 0.4 m) aynı eğik düzlemde ve ≤ eğim sınırında olmalı
        foreach (var o in new[] { dir * 0.9f, dir * 0.45f, -dir * 0.4f, -dir * 0.8f, side * 0.4f, -side * 0.4f })
            if (!GroundAt(point, normal, o, out float dy, out float ny) || Mathf.Abs(dy) > putDownBodyTolerance || ny < minNormalY) return false;
        // Uçurum kenarı: gövdenin her noktasından putDownEdgeClearance uzakta (8 yön) zemin düzlemden putDownEdgeDrop'tan çok düşmemeli.
        // O yönde kaya duvarı varsa (yatay ışın engellenir) kenar değildir; ışın duvarın içinden atılıp zemin bulamasın diye atlanır.
        Vector3 lift = normal * 0.5f;
        foreach (var body in new[] { Vector3.zero, dir * 0.9f, -dir * 0.8f })
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                Vector3 ring = (dir * Mathf.Cos(a) + side * Mathf.Sin(a)) * putDownEdgeClearance;
                Vector3 from = point + body + lift;
                if (GroundProbe.LineBlocked(from, from + ring)) continue;
                if (!GroundAt(point, normal, body + ring, out float dy, out _) || dy < -putDownEdgeDrop) return false;
            }
        // Duvar / araç içi: oyuncudan bırakma yerine ve yatan gövde boyunca (zeminden 0.5 m yukarıda) statik engel olmamalı
        Vector3 hip = point + lift;
        if (GroundProbe.LineBlocked(me + Vector3.up * 0.5f, hip)) return false;
        foreach (var o in new[] { dir * 0.9f, -dir * 0.8f, side * 0.45f, -side * 0.45f })
            if (GroundProbe.LineBlocked(hip, hip + o)) return false;
        return true;
    }

    // point+o altındaki zemin: dy = zeminin, (point, normal) düzleminden beklenen yüksekliğe göre sapması; ny = zemin normalinin y'si.
    private static bool GroundAt(Vector3 point, Vector3 normal, Vector3 o, out float dy, out float ny)
    {
        dy = 0f; ny = 0f;
        float expected = point.y - (normal.x * o.x + normal.z * o.z) / Mathf.Max(0.3f, normal.y);
        if (!GroundProbe.Find(new Vector3(point.x + o.x, expected + 1.5f, point.z + o.z), 4f, out RaycastHit h)) return false;
        dy = h.point.y - expected;
        ny = h.normal.y;
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
        if (piggybackBusy || !CanPutDown(out Vector3 point, out Vector3 normal, out Vector3 head)) yield break;
        piggybackBusy = true;
        // U1: bırakma yeri yanda/önde seçildiyse sırt o yöne dönsün (hasta sırttan geriye kayar, baş bırakma yönünde)
        Vector3 faceDir = -head;
        if (Vector3.Angle(Flat(player.transform.forward), faceDir) > 1f) player.Teleport(player.transform.position, Quaternion.LookRotation(faceDir, Vector3.up));
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
