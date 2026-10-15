using UnityEngine;

// Fareyle dönen yörünge kamera (kullanıcı kararı 15). Hedefin arkasında durur, araya duvar girerse yaklaşır.
public class OrbitCamera : MonoBehaviour
{
    public Transform target;
    [Tooltip("Hedefe uzaklık (m).")]
    public float distance = 4.5f;
    [Tooltip("Bakılan nokta, hedefin ayak hizasından bu kadar yukarıda (m).")]
    public float pivotHeight = 1.3f;
    [Tooltip("Fare hassasiyeti (derece / piksel).")]
    public float sensitivity = 0.12f;
    public float minPitch = -20f, maxPitch = 70f;

    public float Yaw { get; set; }
    public float Pitch { get; set; } = 18f;

    private static readonly RaycastHit[] hits = new RaycastHit[16];

    private void Start()
    {
        if (target != null) Yaw = target.eulerAngles.y;
        if (!Application.isBatchMode)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // Girdi: fare hareketi (piksel).
    public void Rotate(Vector2 delta)
    {
        Yaw += delta.x * sensitivity;
        Pitch = Mathf.Clamp(Pitch - delta.y * sensitivity, minPitch, maxPitch);
    }

    private void LateUpdate()
    {
        if (target == null) return;
        Vector3 pivot = target.position + Vector3.up * pivotHeight;
        Quaternion rot = Quaternion.Euler(Pitch, Yaw, 0f);
        Vector3 dir = rot * Vector3.back;
        float length = distance;
        // Kamera ile hedef arasına giren statik engel varsa yaklaş (taşınan sedye, hastalar ve karakterler engel sayılmaz).
        int n = Physics.SphereCastNonAlloc(pivot, 0.2f, dir, hits, distance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (hits[i].distance <= 0f || GroundProbe.IsMovingGameObject(hits[i].collider)) continue;
            length = Mathf.Min(length, Mathf.Max(0.6f, hits[i].distance - 0.1f));
        }
        transform.SetPositionAndRotation(pivot + dir * length, rot);
    }
}
