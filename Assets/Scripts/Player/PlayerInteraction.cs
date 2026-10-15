using UnityEngine;

// E tuşu etkileşimleri (kullanıcı kararları 8, 9, 10). Her karede o an ne yapılabileceğini bulur (ipucu metni),
// Interact() çağrılınca onu yapar. Öncelik: tutuyorsan bırak → hastayı sedyeye al → sedye ucunu tut.
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

    public const string HintGrab = "E: Sedyeyi tut";
    public const string HintRelease = "E: Bırak";
    public const string HintLoadPatient = "E: Hastayı sedyeye al";

    public string CurrentHint { get; private set; } = "";

    private enum Action { None, Release, PlacePatient, Grab }

    private Action action;
    private StretcherController targetStretcher;
    private Patient targetPatient;
    private bool targetFront;

    private void Update()
    {
        Evaluate();
    }

    private void Evaluate()
    {
        action = Action.None;
        targetStretcher = null;
        targetPatient = null;
        CurrentHint = "";
        if (player == null) return;

        if (player.HeldStretcher != null)
        {
            action = Action.Release;
            CurrentHint = HintRelease;
            return;
        }

        Vector3 me = player.transform.position;

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

        // En yakın boş sap
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
        if (targetStretcher != null)
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
        }
        Evaluate();
    }
}
