using UnityEngine;
using UnityEngine.UI;

// U3: arkadaşın (ikinci oyuncu) başının üstünde ince can barı (world-space, kameraya döner; MissIndicator deseni).
// Yalnız okur. Yerel oyuncunun barı ekranda (MissionHud).
public class HealthBarIndicator : MonoBehaviour
{
    public PlayerHealth health;
    [Tooltip("Kökün bu kadar üstünde durur (m).")]
    public float height = 1.85f;

    public Image Fill { get; private set; }
    public bool Visible { get { return canvasGo != null && canvasGo.activeSelf; } }

    private GameObject canvasGo;

    public static HealthBarIndicator Create(PlayerHealth h, Transform parent)
    {
        var root = new GameObject("HealthBar_" + h.name);
        root.transform.SetParent(parent, false);
        var hb = root.AddComponent<HealthBarIndicator>();
        hb.health = h;
        hb.Build();
        return hb;
    }

    private void Build()
    {
        canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rect = canvasGo.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(150f, 16f);
        rect.localScale = Vector3.one * 0.004f; // 150 px → 0.6 m
        var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(canvasGo.transform, false);
        var br = bg.GetComponent<RectTransform>();
        br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one; br.offsetMin = Vector2.zero; br.offsetMax = Vector2.zero;
        bg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(bg.transform, false);
        var fr = fill.GetComponent<RectTransform>();
        fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.offsetMin = new Vector2(2f, 2f); fr.offsetMax = new Vector2(-2f, -2f);
        Fill = fill.GetComponent<Image>();
        Fill.type = Image.Type.Filled;
        Fill.fillMethod = Image.FillMethod.Horizontal;
        Fill.fillOrigin = (int)Image.OriginHorizontal.Left;
    }

    private Vector3 DisplayPosition()
    {
        var p = health.player;
        Vector3 at = p != null && p.IsDown && p.knockdown != null ? p.knockdown.GroundPosition + Vector3.up * 0.6f : health.transform.position + Vector3.up * height;
        return at;
    }

    private void LateUpdate()
    {
        if (health == null) { if (canvasGo != null) canvasGo.SetActive(false); return; }
        bool show = health.gameObject.activeInHierarchy;
        if (canvasGo.activeSelf != show) canvasGo.SetActive(show);
        if (!show) return;
        var cam = Camera.main;
        transform.position = DisplayPosition();
        if (cam != null) transform.rotation = cam.transform.rotation;
        float f = health.Fraction;
        MissionHud.SetFill(Fill, f);
        Fill.color = Color.Lerp(new Color(0.95f, 0.3f, 0.25f), new Color(0.3f, 0.9f, 0.4f), f);
    }
}
