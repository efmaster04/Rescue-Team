using UnityEngine;

namespace SelVadisi
{
    /// Occasional lightning: brightens the sun, ambient light, sky and rain for a double flicker.
    public class LightningFlash : MonoBehaviour
    {
        public Light sun;
        public float minDelay = 14f, maxDelay = 38f;
        public float strength = 1f;

        static readonly int FlashId = Shader.PropertyToID("_SV_Flash");

        /// Raised at each strike (people flinch and look up).
        public static event System.Action Struck;
        float baseIntensity, next, start = -100f;

        void OnEnable() => Shader.SetGlobalFloat(FlashId, 0f);
        void OnDisable()
        {
            Shader.SetGlobalFloat(FlashId, 0f);
            if (sun) sun.intensity = baseIntensity > 0f ? baseIntensity : sun.intensity;
        }

        void Start()
        {
            if (sun) baseIntensity = sun.intensity;
            next = Time.time + Random.Range(5f, 12f);
        }

        void Update()
        {
            if (Time.time > next)
            {
                start = Time.time;
                next = Time.time + Random.Range(minDelay, maxDelay);
                Struck?.Invoke();
            }
            float e = Time.time - start;
            float f = 0f;
            if (e < 0.9f)
                f = Mathf.Exp(-e * 10f) + 0.7f * Mathf.Exp(-Mathf.Pow((e - 0.22f) * 14f, 2f)) + 0.35f * Mathf.Exp(-Mathf.Pow((e - 0.48f) * 16f, 2f));
            f *= strength;
            Shader.SetGlobalFloat(FlashId, f);
            if (sun) sun.intensity = baseIntensity * (1f + f * 1.6f);
        }
    }
}
