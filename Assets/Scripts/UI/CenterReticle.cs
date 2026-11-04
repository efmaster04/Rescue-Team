using UnityEngine;
using UnityEngine.UI;

// Ekran ortası nişangahı (kullanıcı kararı 2026-11-04: her oynanış sahnesinde). Sahnenin HUD'u kurulurken Create ile eklenir,
// HUD her karede Refresh(aiming, suppressed) çağırır:
//  - Oyuncu kontrol ederken (sürüş dahil) ortada küçük yarı saydam nokta.
//  - Nişan alırken (PlayerCombat.Aiming) nokta gizlenir, büyük artı (Crosshair, iki çubuk) çıkar.
//  - Gizli: sahne kendi nedeniyle bastırınca (görev bitti, oyuncu yerde, menü açık), kararma / yükleme ekranında (görev köprüsü)
//    ve imleç serbestken (yalnız nokta: nişan zaten girdi ister; editörde oyun penceresi odak dışındayken testler bozulmasın).
// Yeni bölüm: HUD kanvasına CenterReticle.Create(kanvas) ve karede bir Refresh yeter.
public sealed class CenterReticle : MonoBehaviour
{
    // Boyutlar 1920×1080 referans kanvasına göre; farklı referanslı kanvas Create'e ölçek verir.
    public const float DotSize = 6f;
    public const float CrosshairSize = 26f, CrosshairThickness = 3f;
    public static readonly Color DotColor = new Color(.95f, .95f, .91f, .66f);
    public static readonly Color CrosshairColor = new Color(1f, 1f, 1f, 0.95f);

    public GameObject Dot { get; private set; }
    public GameObject Crosshair { get; private set; }
    public bool DotVisible { get { return Dot != null && Dot.activeSelf; } }
    public bool CrosshairVisible { get { return Crosshair != null && Crosshair.activeSelf; } }

    // Görev köprüsünün kararması ya da yükleme ekranı sürüyor mu (köprü yoksa oluşturmaz)
    public static bool TransitionActive
    {
        get
        {
            if (!MissionBridgeRunner.Exists) return false;
            var bridge = MissionBridgeRunner.Instance;
            return bridge.FadeAlpha > 0.01f || bridge.LoadingVisible;
        }
    }

    // İmleç serbest mi (bilgisayar ekranı, menü, editörde Esc). Batch modda imleç hiç kilitlenmez: serbest sayılmaz.
    public static bool CursorFree { get { return !Application.isBatchMode && Cursor.lockState != CursorLockMode.Locked; } }

    // parent: HUD kanvası (ya da onun altındaki tam ekran nesne). existingDot: sahnenin kendi noktası varsa (ofis-garaj odak noktası)
    // yenisi kurulmaz, görünürlüğü bu bileşen yönetir. scale: kanvas referansı 1920×1080 değilse boyut çarpanı.
    public static CenterReticle Create(Transform parent, GameObject existingDot = null, float scale = 1f)
    {
        var go = new GameObject("CenterReticle", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var reticle = go.AddComponent<CenterReticle>();

        if (existingDot != null) reticle.Dot = existingDot;
        else
        {
            var dot = MakeImage("CenterDot", go.transform, Vector2.one * (DotSize * scale), DotColor);
            reticle.Dot = dot.gameObject;
        }

        reticle.Crosshair = new GameObject("Crosshair", typeof(RectTransform));
        reticle.Crosshair.transform.SetParent(go.transform, false);
        var cr = reticle.Crosshair.GetComponent<RectTransform>();
        cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f);
        cr.anchoredPosition = Vector2.zero;
        cr.sizeDelta = Vector2.one * (CrosshairSize * scale);
        foreach (var size in new[] { new Vector2(CrosshairSize, CrosshairThickness), new Vector2(CrosshairThickness, CrosshairSize) })
        {
            var bar = MakeImage(size.x > size.y ? "H" : "V", reticle.Crosshair.transform, size * scale, CrosshairColor);
            var outline = bar.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
        }
        reticle.Crosshair.SetActive(false);
        return reticle;
    }

    private static Image MakeImage(string name, Transform parent, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = Vector2.zero;
        r.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    // aiming: yerel oyuncu nişan alıyor; suppressed: sahne kendi nedeniyle gizliyor (görev bitti, oyuncu yerde, menü…).
    public void Refresh(bool aiming, bool suppressed)
    {
        bool on = !suppressed && !TransitionActive;
        bool cross = on && aiming;
        bool dot = on && !aiming && !CursorFree;
        if (Crosshair != null && Crosshair.activeSelf != cross) Crosshair.SetActive(cross);
        if (Dot != null && Dot.activeSelf != dot) Dot.SetActive(dot);
    }
}
