using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Sel Vadisi geniş bir vadi: gölgeler projenin 50 m'sinde kesilince uzaktaki evler düz görünür. Bu bileşen sahne açıkken
// URP gölge mesafesini uzatır, sahne kapanınca eski değere döndürür (ofis ve Bölüm 1 50 m'de kalır, asset dosyası değişmez).
public sealed class SelVadisiShadowRange : MonoBehaviour
{
    [Tooltip("Sahne açıkken kullanılacak gölge mesafesi (m).")]
    public float shadowDistance = 160f;

    private UniversalRenderPipelineAsset asset;
    private float previous = -1f;

    private void OnEnable()
    {
        asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (asset == null) return;
        previous = asset.shadowDistance;
        asset.shadowDistance = Mathf.Max(previous, shadowDistance);
    }

    private void OnDisable()
    {
        if (asset != null && previous >= 0f) asset.shadowDistance = previous;
        previous = -1f;
    }
}
