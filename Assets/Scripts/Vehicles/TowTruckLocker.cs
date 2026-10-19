using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// U2: çekicinin servis dolabı kapağı (SM_TowTruck_LockerDoor_L1; towtruck_meta.json parts → hinge). Halat kangalı başta
// bu dolapta durur. Oyuncu dolabın DIŞINDA, kapağa ≤ interactRange yakın ve yüzü kapağa dönükken E → kapak menteşede
// openSeconds içinde çalışma açısına açılır; açıkken aynı kuralla E → kapanır (kangal içindeyse önce kangal hedeflenir).
// Bu bileşen çekici hiyerarşisinde DEĞİL, ayrı bir kontrol objesindedir (Bolum1Verify: çekici parçalarında betik yok);
// kapağı (door) dışarıdan döndürür. Kapak parçasının pivotu menteşededir.
public class TowTruckLocker : MonoBehaviour
{
    public static readonly List<TowTruckLocker> All = new List<TowTruckLocker>();

    [Tooltip("Kapak parçası (pivot menteşede).")]
    public Transform door;
    [Tooltip("Menteşe ekseni, kapağın ebeveyn (çekici kökü) uzayında.")]
    public Vector3 hingeAxis = Vector3.up;
    [Tooltip("Açık açı (derece, işaretli; towtruck_meta work_pose_deg, dışa açılan yön).")]
    public float openAngle = 90f;
    [Tooltip("Açılma / kapanma süresi (sn).")]
    public float openSeconds = 0.4f;
    [Tooltip("Dolabın dış yönü (dünya, yatay): oyuncu bu tarafta olmalı.")]
    public Vector3 outward = Vector3.forward;
    [Tooltip("Dolaptaki kangal (varsa).")]
    public RopeCoil coil;
    [Tooltip("Etkileşim menzili: oyuncu kökünden kapağın en yakın noktasına yatay uzaklık (m) (U1 kuralı: 1.0).")]
    public float interactRange = 1.0f;
    [Tooltip("Yüz kapağa dönük: yatay ileri yön ile kapak merkezine yön arasındaki açı ≤ bu kadar °.")]
    public float facingAngle = 30f;

    public bool IsOpen { get; private set; }
    public bool IsMoving { get; private set; }
    public float Angle { get; private set; }
    public int Toggles { get; private set; }

    private Quaternion closedRotation;
    private Vector3 restCenter;
    private bool restKnown;

    private void Awake()
    {
        if (door != null) { closedRotation = door.localRotation; restCenter = DoorBounds.center; restKnown = true; }
    }

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    public Bounds DoorBounds
    {
        get
        {
            var rs = door != null ? door.GetComponentsInChildren<Renderer>() : new Renderer[0];
            if (rs.Length == 0) return new Bounds(door != null ? door.position : transform.position, Vector3.zero);
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }

    // Kapağın kapalı hâlindeki merkezi (açıkken de aynı yer hedeftir)
    public Vector3 DoorCenter { get { return restKnown ? restCenter : DoorBounds.center; } }

    // Oyuncu dolabın dış tarafında mı (kasanın üstünde ya da içinde değil)
    public bool PlayerOutside(Vector3 p)
    {
        Vector3 d = p - DoorCenter; d.y = 0f;
        return Vector3.Dot(d, outward.normalized) > 0.05f;
    }

    public bool CanInteract(Vector3 p, Vector3 forward)
    {
        if (door == null || IsMoving || !PlayerOutside(p)) return false;
        Vector3 c = DoorCenter;
        Vector3 closest = new Bounds(c, DoorBounds.size).ClosestPoint(p);
        Vector3 d = closest - p;
        float dy = Mathf.Abs(c.y - p.y);
        d.y = 0f;
        if (d.magnitude > interactRange || dy > 1.6f) return false;
        return RopeAnchor.FacingTarget(p, forward, c, facingAngle, 0.2f);
    }

    public void Toggle()
    {
        if (IsMoving || door == null) return;
        StartCoroutine(MoveRoutine(IsOpen ? 0f : openAngle));
    }

    private IEnumerator MoveRoutine(float target)
    {
        IsMoving = true;
        Toggles++;
        float from = Angle;
        for (float t = 0f; t < openSeconds; t += Time.deltaTime)
        {
            SetAngle(Mathf.Lerp(from, target, Mathf.SmoothStep(0f, 1f, t / openSeconds)));
            yield return null;
        }
        SetAngle(target);
        IsMoving = false;
        IsOpen = Mathf.Abs(target) > 0.5f;
    }

    // Test ve editör: açıyı doğrudan ver.
    public void SetAngle(float angle)
    {
        Angle = angle;
        if (door != null) door.localRotation = Quaternion.AngleAxis(angle, hingeAxis) * closedRotation;
    }
}
