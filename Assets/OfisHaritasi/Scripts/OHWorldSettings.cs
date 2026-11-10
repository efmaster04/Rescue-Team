using UnityEngine;

// Ofis sahnesinin kamerası 45 m'ye kadar çiziyor (yalnız iç mekân vardı). Harita eklenince
// kameraların uzak düzlemini büyütür ve hafif mesafe sisi açar. Sahne ayarlarını kalıcı değiştirmez.
namespace OfisHaritasi
{
    public class OHWorldSettings : MonoBehaviour
    {
        public float farClip = 1500f;
        public bool fog = true;
        public Color fogColor = new Color(0.72f, 0.78f, 0.83f);
        public float fogStart = 320f;
        public float fogEnd = 2200f;
        float timer;

        void OnEnable() { Apply(); }

        void Update()
        {
            timer -= Time.unscaledDeltaTime;
            if (timer <= 0) { timer = 1f; ApplyCameras(); }
        }

        public void Apply()
        {
            ApplyCameras();
            if (fog)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogStartDistance = fogStart;
                RenderSettings.fogEndDistance = fogEnd;
            }
        }

        void ApplyCameras()
        {
            foreach (var c in Camera.allCameras)
                if (c && c.cameraType == CameraType.Game && c.farClipPlane < farClip && c.targetTexture == null) c.farClipPlane = farClip;
        }
    }
}
