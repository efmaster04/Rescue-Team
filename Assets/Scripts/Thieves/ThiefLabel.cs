using UnityEngine;
using UnityEngine.UI;

// U4: hırsızın başının üstünde taşıdığı eşyanın adı ve değeri ("Laptop'lu valiz 320 ₺"; world-space, kameraya döner;
// MissIndicator / HealthBarIndicator deseni). Yalnız okur; eşya yokken gizli. Kovalanacak hedef belli olsun diye.
public class ThiefLabel : MonoBehaviour
{
    public Thief thief;
    [Tooltip("Kökün bu kadar üstünde durur (m).")]
    public float height = 1.95f;

    public Text Text { get; private set; }
    public bool Visible { get { return canvasGo != null && canvasGo.activeSelf; } }

    private GameObject canvasGo;

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
        rect.sizeDelta = new Vector2(420f, 44f);
        rect.localScale = Vector3.one * 0.004f; // 420 px → 1.7 m
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline));
        go.transform.SetParent(canvasGo.transform, false);
        var tr = go.GetComponent<RectTransform>();
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = Vector2.zero; tr.offsetMax = Vector2.zero;
        Text = go.GetComponent<Text>();
        Text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Text.fontSize = 30;
        Text.alignment = TextAnchor.MiddleCenter;
        Text.color = new Color(1f, 0.92f, 0.5f);
        Text.horizontalOverflow = HorizontalWrapMode.Overflow;
        Text.raycastTarget = false;
        var outline = go.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);
        canvasGo.SetActive(false);
    }

    private void LateUpdate()
    {
        if (thief == null) { if (canvasGo != null) canvasGo.SetActive(false); return; }
        bool show = thief.gameObject.activeInHierarchy && !thief.IsGone && thief.Carried != null;
        if (canvasGo.activeSelf != show) canvasGo.SetActive(show);
        if (!show) return;
        Text.text = thief.Carried.LabelText;
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
