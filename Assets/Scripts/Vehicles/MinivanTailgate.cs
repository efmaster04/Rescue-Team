using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Minibüsün arka bagaj kapısı ve içerideki hastaların dışarı çekilmesi (D2 kullanıcı kararı 2).
//  - Oyuncu minibüsün arkasındayken (rearPoint'e yatay ≤ openRange) E → kapı meta'daki açık açıya açılır. Açıkken E ile kapanmaz.
//  - Kapı açıkken, içerideki (InVehicle) hastaya yatay ≤ extractRange iken E → oyuncu Extract_Pull, hasta Patient_DraggedOut oynar;
//    hasta minibüsün arkasında patikada sırt üstü yatar (LyingGround), sonra mevcut kuralla sedyeye alınır.
//  - Minibüs sabit kalır (VehicleHold); sallanma yok. Kapı kapalıyken hastalar alınamaz.
// Kapı parçası (SM_Minivan_Tailgate) pivotu menteşededir; dönüş ekseni ve açı builder'ın minivan_meta.json'dan hesapladığı değerlerdir.
public class MinivanTailgate : MonoBehaviour
{
    public static readonly List<MinivanTailgate> All = new List<MinivanTailgate>();

    [Tooltip("Bagaj kapısı parçası (pivot menteşede).")]
    public Transform tailgate;
    [Tooltip("Menteşe ekseni, kapı parçasının ebeveyn (minibüs kökü) uzayında.")]
    public Vector3 hingeAxis = Vector3.right;
    [Tooltip("Açık açı (derece, işaretli; minivan_meta.json open_angle_deg, dışa açılan yön).")]
    public float openAngle = 85f;
    [Tooltip("Açılma süresi (sn).")]
    public float openSeconds = 0.8f;
    [Tooltip("Arka tampon ortası, yerde (oyuncunun 'arkasında' sayıldığı nokta).")]
    public Transform rearPoint;
    [Tooltip("Kapıyı açma menzili: oyuncu kökünden rearPoint'e yatay uzaklık (m).")]
    public float openRange = 2f;
    [Tooltip("Çekme menzili: oyuncu kökünden içerideki hastanın kalçasına yatay uzaklık (m).")]
    public float extractRange = 2f;

    [Header("Çekme (Extract_Pull + Patient_DraggedOut)")]
    [Tooltip("Hasta Patient_DraggedOut'a Extract_Pull'un bu anında başlar (clips_meta sync: 0.45 sn).")]
    public float dragStartDelay = 0.45f;
    [Tooltip("Patient_DraggedOut süresi (sn).")]
    public float dragSeconds = 1.5f;
    [Tooltip("Extract_Pull süresi (sn); oyuncu bu süre kontrolsüz kalır.")]
    public float pullSeconds = 1.5f;
    [Tooltip("Klipte kalçanın kök uzayındaki yer değiştirmesi (clips_meta hips_displacement_m, Unity ekseni).")]
    public Vector3 hipsShiftInRoot = new Vector3(0f, -0.227f, -0.86f);
    [Tooltip("Hastanın yatacağı yer: kalça, arka düzlemden bu kadar geride (m), koltuk hizasında.")]
    public float lieBehindRear = 1.2f;
    [Tooltip("Çeken oyuncu: arka düzlemden geride (m) ve koltuk hizasından dışa yana kayık (m).")]
    public float pullerBehindRear = 0.55f, pullerSideOffset = 0.55f;

    public bool IsOpen { get; private set; }
    public bool IsOpening { get; private set; }
    public float Angle { get; private set; }
    public int Extractions { get; private set; }

    private Quaternion closedRotation;

    private void Awake() { if (tailgate != null) closedRotation = tailgate.localRotation; }
    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    // Minibüsün geri yönü (yatay): arka tampondan dışarı.
    public Vector3 RearDirection
    {
        get
        {
            Vector3 d = rearPoint.position - transform.position;
            d.y = 0f;
            return d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.back;
        }
    }

    public bool PlayerBehind(Vector3 playerPos)
    {
        Vector3 d = playerPos - rearPoint.position;
        float dy = d.y;
        d.y = 0f;
        return d.magnitude <= openRange && Mathf.Abs(dy) < 1.5f;
    }

    public bool CanOpen(Vector3 playerPos) { return !IsOpen && !IsOpening && PlayerBehind(playerPos); }

    public void Open()
    {
        if (IsOpen || IsOpening) return;
        StartCoroutine(OpenRoutine());
    }

    private IEnumerator OpenRoutine()
    {
        IsOpening = true;
        for (float t = 0f; t < openSeconds; t += Time.deltaTime)
        {
            SetAngle(openAngle * Mathf.SmoothStep(0f, 1f, t / openSeconds));
            yield return null;
        }
        SetAngle(openAngle);
        IsOpening = false;
        IsOpen = true;
    }

    // Test ve editör: açıyı doğrudan ver.
    public void SetAngle(float angle)
    {
        Angle = angle;
        if (tailgate != null) tailgate.localRotation = Quaternion.AngleAxis(angle, hingeAxis) * closedRotation;
    }

    // Bu minibüsteki, çekilebilir en yakın hasta (kapı açık olmalı).
    public Patient ExtractTarget(Vector3 playerPos)
    {
        if (!IsOpen) return null;
        Patient best = null;
        float bestD = float.MaxValue;
        foreach (var p in Patient.All)
        {
            if (p == null || p.State != PatientState.InVehicle || p.BeingExtracted || !p.transform.IsChildOf(transform)) continue;
            Vector3 d = p.Position - playerPos;
            d.y = 0f;
            float m = d.magnitude;
            if (m <= extractRange && m < bestD) { bestD = m; best = p; }
        }
        return best;
    }

    // Hastanın yatacağı yer (kalça altındaki zemin) ve çekenin duracağı yer.
    public bool LieSpot(Patient p, out Vector3 point, out Vector3 normal)
    {
        Vector3 back = RearDirection;
        Vector3 seat = p.Position;
        Vector3 rearFlat = rearPoint.position;
        float along = Vector3.Dot(seat - rearFlat, back); // koltuk arka düzlemin önünde (negatif)
        Vector3 at = seat + back * (-along + lieBehindRear);
        point = at; normal = Vector3.up;
        if (!GroundProbe.Find(new Vector3(at.x, rearFlat.y + 2f, at.z), 5f, out RaycastHit hit)) return false;
        point = hit.point; normal = hit.normal;
        return true;
    }

    public Vector3 PullerSpot(Patient p, out Quaternion rotation)
    {
        Vector3 back = RearDirection;
        Vector3 side = Vector3.Cross(Vector3.up, back);
        Vector3 seat = p.Position;
        // Koltuk minibüsün hangi yanındaysa çeken o yana kayar (öbür koltuğun önünü kapatmaz)
        float s = Vector3.Dot(seat - rearPoint.position, side) >= 0f ? 1f : -1f;
        float along = Vector3.Dot(seat - rearPoint.position, back);
        Vector3 at = seat + back * (-along + pullerBehindRear) + side * s * pullerSideOffset;
        if (GroundProbe.Find(new Vector3(at.x, rearPoint.position.y + 2f, at.z), 5f, out RaycastHit hit)) at.y = hit.point.y + 0.02f;
        Vector3 look = seat - at; look.y = 0f;
        rotation = Quaternion.LookRotation(look.sqrMagnitude > 1e-4f ? look.normalized : -back, Vector3.up);
        return at;
    }

    // E: hastayı dışarı çek. Oyuncu çekme yerine hizalanır, Extract_Pull oynar; 0.45 sn sonra hasta Patient_DraggedOut.
    public bool Extract(Patient p, ThirdPersonPlayer player)
    {
        if (p == null || !IsOpen || p.State != PatientState.InVehicle || p.BeingExtracted) return false;
        if (!LieSpot(p, out Vector3 point, out Vector3 normal)) return false;
        Vector3 at = PullerSpot(p, out Quaternion rot);
        player.Teleport(at, rot);
        player.SuspendControl(pullSeconds);
        var crew = player.GetComponent<CrewAnimation>();
        if (crew != null) crew.PlayAction(CrewAnimation.StateExtract);
        StartCoroutine(DragLater(p, point, normal));
        Extractions++;
        return true;
    }

    private IEnumerator DragLater(Patient p, Vector3 point, Vector3 normal)
    {
        yield return new WaitForSeconds(dragStartDelay);
        // Baş minibüsten uzağa (hasta geriye, çekene doğru düşer)
        p.DragOut(point, RearDirection, normal, dragSeconds, hipsShiftInRoot);
    }
}
