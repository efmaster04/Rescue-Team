using UnityEngine;

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

    public string CurrentHint { get; private set; } = "";
    // Kaldırılmakta olan arkadaş (HUD ilerleme çubuğu için)
    public PlayerKnockdown ReviveTarget { get; private set; }

    private enum Action { None, Release, PlaceCone, Revive, PlacePatient, Grab, PickCone }

    private Action action;
    private StretcherController targetStretcher;
    private Patient targetPatient;
    private TrafficCone targetCone;
    private bool targetFront;
    private bool interactHeld;

    private void Update()
    {
        Evaluate();
        if (interactHeld && action == Action.Revive && ReviveTarget != null && player != null && player.HasControl)
            ReviveTarget.TryRevive(player, Time.deltaTime);
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
        CurrentHint = "";
        if (player == null || player.IsDown) return;

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
                    if (helper != null && helper.CanAssist)
                        helper.Assist(player, targetStretcher, targetFront);
                    targetStretcher.NotifyIfUnderstaffed();
                }
                break;
            case Action.PickCone:
                player.HeldCone = targetCone;
                targetCone.PickUp(player);
                break;
        }
        Evaluate();
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
