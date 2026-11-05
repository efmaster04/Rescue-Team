using UnityEngine;

namespace SelVadisi
{
    /// Keeps the rain emitter above (and slightly ahead of) the active camera.
    public class RainFollow : MonoBehaviour
    {
        public float height = 24f;
        public float ahead = 14f;

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Transform c = cam.transform;
            Vector3 f = c.forward; f.y = 0f;
            Vector3 p = c.position + f.normalized * ahead;
            transform.position = new Vector3(p.x, c.position.y + height, p.z);
        }
    }
}
