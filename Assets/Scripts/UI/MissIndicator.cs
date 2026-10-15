using UnityEngine;
using UnityEngine.UI;

// Hastanın başının üstünde 3 kutucuklu kaçırma göstergesi (world-space, kameraya döner).
// Dolu kutucuk = kaçırılan ambulans. Hasta ambulanstayken (ya da kurtarılınca) gizli.
public class MissIndicator : MonoBehaviour
{
    public Patient patient;
    [Tooltip("Kalçanın bu kadar üstünde durur (m).")]
    public float height = 0.75f;
    public Color emptyColor = new Color(1f, 1f, 1f, 0.25f);
    public Color filledColor = new Color(0.95f, 0.25f, 0.2f, 1f);

    public Image[] Boxes { get; private set; }
    public bool Visible { get { return canvasGo != null && canvasGo.activeSelf; } }

    private GameObject canvasGo;

    public static MissIndicator Create(Patient p, Transform parent)
    {
        var root = new GameObject("MissIndicator_" + p.name);
        root.transform.SetParent(parent, false);
        var mi = root.AddComponent<MissIndicator>();
        mi.patient = p;
        mi.Build();
        return mi;
    }

    private void Build()
    {
        canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rect = canvasGo.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(150f, 50f);
        rect.localScale = Vector3.one * 0.004f; // 150 px → 0.6 m
        Boxes = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            // Kutucuk: siyah çerçeve + içi (dolu/boş)
            var frame = new GameObject("Box" + (i + 1), typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(canvasGo.transform, false);
            var fr = frame.GetComponent<RectTransform>();
            fr.sizeDelta = new Vector2(40f, 40f);
            fr.anchoredPosition = new Vector2((i - 1) * 50f, 0f);
            frame.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(frame.transform, false);
            var fl = fill.GetComponent<RectTransform>();
            fl.sizeDelta = new Vector2(30f, 30f);
            Boxes[i] = fill.GetComponent<Image>();
        }
    }

    private void LateUpdate()
    {
        if (patient == null)
        {
            canvasGo.SetActive(false);
            return;
        }
        bool show = !patient.IsAboard && patient.gameObject.activeInHierarchy;
        if (canvasGo.activeSelf != show) canvasGo.SetActive(show);
        if (!show) return;
        transform.position = patient.Position + Vector3.up * height;
        var cam = Camera.main;
        if (cam != null) transform.rotation = cam.transform.rotation; // kameraya dön (ekrana paralel)
        for (int i = 0; i < 3; i++)
            Boxes[i].color = i < patient.MissedAmbulances ? filledColor : emptyColor;
    }

    // Ekran görüntüsü için geçici kamerada da düzgün görünsün.
    public void FaceCamera(Camera cam)
    {
        if (patient == null) return;
        transform.position = patient.Position + Vector3.up * height;
        transform.rotation = cam.transform.rotation;
    }
}
