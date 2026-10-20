using UnityEngine;
using UnityEngine.UI;

// U4: hırsızın başının üstünde taşıdığı eşyanın adı ve değeri ("Laptop'lu valiz 320 ₺"; world-space, kameraya döner;
// MissIndicator / HealthBarIndicator deseni). U4b: altında ince can barı (her zaman görünür; etiket yalnız eşya taşırken).
// Yalnız okur. Kovalanacak hedef ve vurulabilirlik belli olsun diye.
public class ThiefLabel : MonoBehaviour
{
    public Thief thief;
    [Tooltip("Kökün bu kadar üstünde durur (m).")]
    public float height = 1.95f;

    public Text Text { get; private set; }
    public Image HealthFill { get; private set; }
    public bool Visible { get { return textGo != null && textGo.activeSelf && canvasGo != null && canvasGo.activeSelf; } }
    public bool BarVisible { get { return barGo != null && barGo.activeSelf && canvasGo != null && canvasGo.activeSelf; } }

    private GameObject canvasGo, textGo, barGo;

    public static readonly Color HealthFull = new Color(0.95f, 0.85f, 0.3f), HealthLow = new Color(0.95f, 0.3f, 0.25f);

    public static ThiefLabel Create(Thief t)
    {
        var root = new GameObject("ThiefLabel_" + t.name);
        var l = root.AddComponent<ThiefLabel>();
        l.thief = t;
        l.Build();
        return l;
    }

    private void Build()
    {
        canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rect = canvasGo.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(420f, 70f);
        rect.localScale = Vector3.one * 0.004f; // 420 px → 1.7 m
        // Yazı: üst 44 px
        textGo = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline));
        textGo.transform.SetParent(canvasGo.transform, false);
        var tr = textGo.GetComponent<RectTransform>();
        tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f); tr.pivot = new Vector2(0.5f, 1f);
        tr.anchoredPosition = Vector2.zero; tr.sizeDelta = new Vector2(0f, 44f);
        Text = textGo.GetComponent<Text>();
        Text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Text.fontSize = 30;
        Text.alignment = TextAnchor.MiddleCenter;
        Text.color = new Color(1f, 0.92f, 0.5f);
        Text.horizontalOverflow = HorizontalWrapMode.Overflow;
        Text.raycastTarget = false;
        var outline = textGo.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);
        // U4b: can barı: alt 12 px, 150 px geniş (0.6 m), ortada
        barGo = new GameObject("HealthBar", typeof(RectTransform), typeof(Image));
        barGo.transform.SetParent(canvasGo.transform, false);
        var br = barGo.GetComponent<RectTransform>();
        br.anchorMin = new Vector2(0.5f, 0f); br.anchorMax = new Vector2(0.5f, 0f); br.pivot = new Vector2(0.5f, 0f);
        br.anchoredPosition = new Vector2(0f, 4f); br.sizeDelta = new Vector2(150f, 12f);
        var bg = barGo.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.7f);
        bg.raycastTarget = false;
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(barGo.transform, false);
        var fr = fill.GetComponent<RectTransform>();
        fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.pivot = new Vector2(0f, 0.5f);
        fr.offsetMin = new Vector2(2f, 2f); fr.offsetMax = new Vector2(-2f, -2f);
        HealthFill = fill.GetComponent<Image>();
        HealthFill.raycastTarget = false;
        HealthFill.type = Image.Type.Filled;
        HealthFill.fillMethod = Image.FillMethod.Horizontal;
        HealthFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        HealthFill.color = HealthFull;
        canvasGo.SetActive(false);
    }

    private void LateUpdate()
    {
        if (thief == null) { if (canvasGo != null) canvasGo.SetActive(false); return; }
        bool show = thief.gameObject.activeInHierarchy && !thief.IsGone;
        if (canvasGo.activeSelf != show) canvasGo.SetActive(show);
        if (!show) return;
        bool label = thief.Carried != null;
        if (textGo.activeSelf != label) textGo.SetActive(label);
        if (label) Text.text = thief.Carried.LabelText;
        float f = thief.HealthFraction;
        MissionHud.SetFill(HealthFill, f);
        HealthFill.color = Color.Lerp(HealthLow, HealthFull, f);
        var cam = Camera.main;
        transform.position = thief.transform.position + Vector3.up * height;
        if (cam != null) transform.rotation = cam.transform.rotation;
    }

    // Ekran görüntüsü için geçici kamerada da düzgün görünsün.
    public void FaceCamera(Camera cam)
    {
        if (thief == null || cam == null) return;
        transform.position = thief.transform.position + Vector3.up * height;
        transform.rotation = cam.transform.rotation;
    }
}
