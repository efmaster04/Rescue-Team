using UnityEngine;
using UnityEngine.InputSystem;

namespace SelVadisi
{
    /// Free-fly camera for exploring the valley, with saved viewpoints and a slow cinematic orbit.
    public class FlyCamera : MonoBehaviour
    {
        [System.Serializable]
        public struct Shot
        {
            public string name;
            public Vector3 position;
            public Vector3 euler;
        }

        public Shot[] shots = new Shot[0];
        public float speed = 25f;
        public float lookSensitivity = 0.12f;
        public bool cinematic;
        public Vector3 orbitCenter = new Vector3(0f, 4f, 0f);
        public float orbitRadius = 240f, orbitHeight = 95f, orbitSpeed = 3f;
        public bool showHelp = true;

        float yaw, pitch, orbitAngle;
        bool moving;
        Vector3 targetPos;
        Quaternion targetRot;
        GUIStyle style;

        void Start() => SyncAngles();

        void SyncAngles()
        {
            Vector3 e = transform.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180f ? e.x - 360f : e.x;
        }

        public void GoTo(int i)
        {
            if (i < 0 || i >= shots.Length) return;
            cinematic = false;
            moving = true;
            targetPos = shots[i].position;
            targetRot = Quaternion.Euler(shots[i].euler);
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            float dt = Time.unscaledDeltaTime;
            if (kb != null)
            {
                for (int i = 0; i < shots.Length && i < 9; i++)
                    if (kb[Key.Digit1 + i].wasPressedThisFrame) GoTo(i);
                if (kb.cKey.wasPressedThisFrame)
                {
                    cinematic = !cinematic;
                    moving = false;
                    Vector3 d = transform.position - orbitCenter;
                    orbitAngle = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                }
                if (kb.hKey.wasPressedThisFrame) showHelp = !showHelp;
            }

            if (cinematic)
            {
                orbitAngle += orbitSpeed * dt;
                float a = orbitAngle * Mathf.Deg2Rad;
                Vector3 want = orbitCenter + new Vector3(Mathf.Sin(a) * orbitRadius, orbitHeight + Mathf.Sin(a * 2f) * 15f, Mathf.Cos(a) * orbitRadius);
                transform.position = Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-dt * 1.5f));
                Quaternion look = Quaternion.LookRotation(orbitCenter - transform.position);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-dt * 2f));
                SyncAngles();
                return;
            }

            if (moving)
            {
                float k = 1f - Mathf.Exp(-dt * 2.8f);
                transform.position = Vector3.Lerp(transform.position, targetPos, k);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, k);
                if ((transform.position - targetPos).sqrMagnitude < 0.05f && Quaternion.Angle(transform.rotation, targetRot) < 0.3f)
                {
                    transform.SetPositionAndRotation(targetPos, targetRot);
                    moving = false;
                }
                SyncAngles();
            }

            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue();
                yaw += d.x * lookSensitivity;
                pitch = Mathf.Clamp(pitch - d.y * lookSensitivity, -89f, 89f);
                moving = false;
            }
            if (!moving) transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            if (mouse != null)
            {
                float sc = mouse.scroll.ReadValue().y;
                if (sc > 0.01f) speed = Mathf.Min(speed * 1.15f, 400f);
                else if (sc < -0.01f) speed = Mathf.Max(speed / 1.15f, 2f);
            }

            if (kb != null)
            {
                Vector3 mv = Vector3.zero;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) mv += Vector3.forward;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) mv += Vector3.back;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) mv += Vector3.left;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) mv += Vector3.right;
                if (kb.eKey.isPressed || kb.spaceKey.isPressed) mv += Vector3.up;
                if (kb.qKey.isPressed || kb.leftCtrlKey.isPressed) mv += Vector3.down;
                if (mv != Vector3.zero)
                {
                    moving = false;
                    float sp = speed * (kb.leftShiftKey.isPressed ? 4f : 1f);
                    transform.position += transform.TransformDirection(mv.normalized) * (sp * dt);
                }
            }
        }

        void OnGUI()
        {
            if (!showHelp) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, wordWrap = true };
                style.normal.textColor = Color.white;
                style.padding = new RectOffset(10, 10, 8, 8);
            }
            string list = "";
            for (int i = 0; i < shots.Length && i < 9; i++) list += "\n" + (i + 1) + "  " + shots[i].name;
            GUI.Box(new Rect(12, 12, 300, 150 + shots.Length * 17),
                "SEL VADİSİ\nSağ tık + fare: etrafa bak\nWASD / QE: uç, Shift: hızlı\nTekerlek: hız (" + speed.ToString("0") + ")\nC: sinematik tur   H: yardımı gizle\n" + list, style);
        }
    }
}
