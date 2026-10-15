using UnityEngine;
namespace RescueTeam.Bolum1
{
    public sealed class OrbitCamera : MonoBehaviour
    {
        public Transform target;
        public float distance = 5, sensitivity = 0.12f;
        float yaw, pitch = 22;
        public void Rotate(Vector2 delta) { yaw += delta.x * sensitivity; pitch = Mathf.Clamp(pitch - delta.y * sensitivity, -15, 70); }
        void Start() { if (!Application.isBatchMode) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; } }
        void LateUpdate()
        {
            if (!target) return;
            Vector3 pivot = target.position + Vector3.up * 1.2f;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
            Vector3 direction = rotation * Vector3.back;
            float length = distance;
            foreach (var hit in Physics.SphereCastAll(pivot, 0.15f, direction, distance, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(target) && !hit.collider.GetComponentInParent<StretcherController>() && !hit.collider.GetComponentInParent<Patient>()) length = Mathf.Min(length, Mathf.Max(0.5f, hit.distance - 0.1f));
            transform.SetPositionAndRotation(pivot + direction * length, rotation);
        }
        void OnDisable() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }
}
