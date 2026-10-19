using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// HUD (kullanıcı kararı 16), uGUI + legacy Text (TMP Essential Resources projede yok). Tüm metinler Türkçe.
//  - Sağ üst: "Ambulans: 2:47" (bir sonraki varışa kalan süre) ya da durakta "Ambulans bekliyor: 12"
//  - Varıştan 30 sn önce 4 sn boyunca: "Telsiz: Ambulans rampa başına yaklaşıyor!"
//  - Alt orta: etkileşim ipucu ve kısa uyarılar ("İki kişi gerekli", "Ambulans dolu")
//  - Orta: görev sonu mesajı ve "R: Yeniden başlat"
//  - Her hastanın başının üstünde 3 kutucuklu kaçırma göstergesi (MissIndicator)
//  - Kaldırma ilerlemesi (trafik kararı 6): arkadaş E'yi basılı tutarken "Kaldırılıyor [||||....] %60"
//  - U3 (kullanıcı kararı "Can ve para", 2026-10-19): sol üst can barı (yerel oyuncu; arkadaşlar için başının üstünde ince bar,
//    HealthBarIndicator); sağ üstte sayacın altında para "₺ 1.240" ve "Çalınan" çubuğu (çalınan / toplam, %50'de görev biter);
//    paranın altında uçan yazılar "+300 ₺ Laptop" (yeşil) / "-300 ₺ Sedye" (kırmızı); can 0'la yerdeyken ortada "Yeniden doğma: 12".
// Arayüz yalnız okur; oyun durumunu değiştirmez.
public class MissionHud : MonoBehaviour
{
    public MissionDirector mission;
    public PlayerInteraction interaction;
    [Tooltip("U3: yerel oyuncunun canı (boşsa interaction.player'dan bulunur).")]
    public PlayerHealth health;
    [Tooltip("U3: para ve çalınan kaydı (boşsa sahnede bulunur).")]
    public LootRegistry loot;
    [Tooltip("Kısa uyarıların ekranda kalma süresi (sn).")]
    public float noticeSeconds = 2.5f;
    public float radioLeadSeconds = 30f;
    public float radioShowSeconds = 4f;
    [Tooltip("U3: uçan para yazısının ömrü (sn) ve yükselme mesafesi (px).")]
    public float floatSeconds = 1.6f, floatRise = 70f;

    public const string RadioText = "Telsiz: Ambulans rampa başına yaklaşıyor!";
    public const string RestartText = "R: Yeniden başlat";
    public const string StolenLabelText = "Çalınan";
    public const string RespawnPrefix = "Yeniden doğma: ";
    public static readonly Color WarningHintColor = new Color(1f, 0.72f, 0.5f, 0.85f);
    public static readonly Color CreditColor = new Color(0.55f, 1f, 0.55f);
    public static readonly Color StolenColor = new Color(1f, 0.35f, 0.3f);

    public Canvas ScreenCanvas { get; private set; }
    public Text TimerText { get; private set; }
    public Text RadioLabel { get; private set; }
    public Text HintText { get; private set; }
    public Text NoticeText { get; private set; }
    public Text ResultText { get; private set; }
    public Text ReviveText { get; private set; }
    // U3
    public Text HealthLabel { get; private set; }
    public Image HealthFill { get; private set; }
    public Text MoneyText { get; private set; }
    public Text StolenLabel { get; private set; }
    public Image StolenFill { get; private set; }
    public Text StolenPercent { get; private set; }
    public Text RespawnText { get; private set; }
    public int FloatingCount { get { return floats.Count; } }
    public int FloatingSpawned { get; private set; }
    public readonly List<MissIndicator> Indicators = new List<MissIndicator>();
    public readonly List<HealthBarIndicator> HealthBars = new List<HealthBarIndicator>();

    private sealed class Floating { public Text text; public float born; public Vector2 start; }
    private readonly List<Floating> floats = new List<Floating>();
    private Vector2 floatOrigin;

    private string notice;
    private float noticeUntil = -1f;
    private int radioShownFor = -1;     // telsiz mesajı hangi varış için gösterildi
    private float radioUntil = -1f;
    private Font font;

    private void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        // Para simgesi: yazı tipinde ₺ yoksa "TL"
        if (font != null && !font.HasCharacter('₺')) LootRegistry.MoneySymbol = "TL";
        BuildScreenCanvas();
    }

    private void Start()
    {
        if (mission != null)
            foreach (var p in mission.patients)
                if (p != null) Indicators.Add(MissIndicator.Create(p, transform));
        if (health == null && interaction != null && interaction.player != null) health = interaction.player.GetComponent<PlayerHealth>();
        if (loot == null) loot = LootRegistry.Instance != null ? LootRegistry.Instance : FindAnyObjectByType<LootRegistry>();
        Subscribe();
    }

    private void OnEnable() { GameNotice.Raised += OnNotice; Subscribe(); }
    private void OnDisable()
    {
        GameNotice.Raised -= OnNotice;
        if (loot != null) { loot.Credited -= OnCredited; loot.Stolen -= OnStolen; }
    }

    private void Subscribe()
    {
        if (loot == null) return;
        loot.Credited -= OnCredited; loot.Credited += OnCredited;
        loot.Stolen -= OnStolen; loot.Stolen += OnStolen;
    }

    private void OnNotice(string message)
    {
        notice = message;
        noticeUntil = Time.time + noticeSeconds;
    }

    private void OnCredited(string label, int amount) { SpawnFloating(label, CreditColor); }
    private void OnStolen(string label, int amount) { SpawnFloating(label, StolenColor); }

    private void BuildScreenCanvas()
    {
        var go = new GameObject("HUD_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        ScreenCanvas = go.GetComponent<Canvas>();
        ScreenCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        ScreenCanvas.sortingOrder = 10;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        TimerText = MakeText("Timer", new Vector2(1f, 1f), new Vector2(-40f, -36f), new Vector2(560f, 64f), 40, TextAnchor.UpperRight);
        RadioLabel = MakeText("Radio", new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(1400f, 64f), 38, TextAnchor.MiddleCenter);
        RadioLabel.color = new Color(1f, 0.85f, 0.35f);
        NoticeText = MakeText("Notice", new Vector2(0.5f, 0f), new Vector2(0f, 190f), new Vector2(1200f, 60f), 38, TextAnchor.MiddleCenter);
        NoticeText.color = new Color(1f, 0.55f, 0.45f);
        HintText = MakeText("Hint", new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(1200f, 60f), 34, TextAnchor.MiddleCenter);
        ResultText = MakeText("Result", new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(1600f, 220f), 54, TextAnchor.MiddleCenter);
        ReviveText = MakeText("Revive", new Vector2(0.5f, 0f), new Vector2(0f, 260f), new Vector2(1200f, 60f), 38, TextAnchor.MiddleCenter);
        ReviveText.color = new Color(0.65f, 0.95f, 1f);

        // U3: sol üst can barı
        HealthLabel = MakeText("HealthLabel", new Vector2(0f, 1f), new Vector2(40f, -30f), new Vector2(400f, 40f), 28, TextAnchor.UpperLeft);
        HealthLabel.text = "Can";
        HealthFill = MakeBar("HealthBar", new Vector2(0f, 1f), new Vector2(40f, -72f), new Vector2(340f, 22f), new Color(0.3f, 0.9f, 0.4f));
        // U3: sağ üst, sayacın altında para ve çalınan çubuğu
        MoneyText = MakeText("Money", new Vector2(1f, 1f), new Vector2(-40f, -96f), new Vector2(560f, 56f), 40, TextAnchor.UpperRight);
        MoneyText.color = new Color(1f, 0.93f, 0.55f);
        StolenLabel = MakeText("StolenLabel", new Vector2(1f, 1f), new Vector2(-40f, -150f), new Vector2(560f, 36f), 26, TextAnchor.UpperRight);
        StolenLabel.text = StolenLabelText;
        StolenFill = MakeBar("StolenBar", new Vector2(1f, 1f), new Vector2(-40f, -186f), new Vector2(340f, 18f), StolenColor);
        StolenPercent = MakeText("StolenPercent", new Vector2(1f, 1f), new Vector2(-390f, -180f), new Vector2(160f, 32f), 24, TextAnchor.UpperRight);
        floatOrigin = new Vector2(-40f, -230f);
        // U3: yeniden doğma geri sayımı (orta, üstte)
        RespawnText = MakeText("Respawn", new Vector2(0.5f, 0.5f), new Vector2(0f, 180f), new Vector2(1200f, 70f), 48, TextAnchor.MiddleCenter);
        RespawnText.color = new Color(1f, 0.8f, 0.5f);
    }

    private Text MakeText(string name, Vector2 anchor, Vector2 position, Vector2 size, int fontSize, TextAnchor align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
        go.transform.SetParent(ScreenCanvas.transform, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(anchor.x, anchor.y);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.alignment = align;
        text.color = Color.white;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        var outline = go.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);
        return text;
    }

    // Çubuk: koyu arka plan + soldan dolan renkli dolgu (Image, sprite'sız beyaz dikdörtgen). Dönen Image dolgudur.
    private Image MakeBar(string name, Vector2 anchor, Vector2 position, Vector2 size, Color fillColor)
    {
        var bg = new GameObject(name, typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(ScreenCanvas.transform, false);
        var br = bg.GetComponent<RectTransform>();
        br.anchorMin = br.anchorMax = anchor;
        br.pivot = new Vector2(anchor.x, anchor.y);
        br.anchoredPosition = position;
        br.sizeDelta = size;
        var bgImg = bg.GetComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.6f);
        bgImg.raycastTarget = false;
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(bg.transform, false);
        var fr = fill.GetComponent<RectTransform>();
        fr.anchorMin = new Vector2(0f, 0f);
        fr.anchorMax = new Vector2(1f, 1f);
        fr.pivot = new Vector2(0f, 0.5f);
        fr.offsetMin = new Vector2(2f, 2f);
        fr.offsetMax = new Vector2(-2f, -2f);
        var img = fill.GetComponent<Image>();
        img.color = fillColor;
        img.raycastTarget = false;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.fillAmount = 1f;
        return img;
    }

    // Sprite'sız Image'da fillAmount uygulanmaz: dolgu genişliği anchor ile verilir (fillAmount da yazılır; testler okur).
    public static void SetFill(Image img, float f)
    {
        f = Mathf.Clamp01(f);
        img.fillAmount = f;
        img.rectTransform.anchorMax = new Vector2(f, 1f);
    }

    // U3: uçan para yazısı (paranın altından yukarı süzülür, solar). Kayıt 0.4 sn arayla yayınlar.
    private void SpawnFloating(string label, Color color)
    {
        // Yeni yazı, hâlâ görünen yazıların altından başlar (üst üste binmesin)
        Vector2 start = floatOrigin + Vector2.down * (38f * floats.Count);
        var t = MakeText("Float", new Vector2(1f, 1f), start, new Vector2(560f, 48f), 34, TextAnchor.UpperRight);
        t.text = label;
        t.color = color;
        floats.Add(new Floating { text = t, born = Time.time, start = start });
        FloatingSpawned++;
    }

    private void UpdateFloating()
    {
        for (int i = floats.Count - 1; i >= 0; i--)
        {
            var f = floats[i];
            float u = (Time.time - f.born) / Mathf.Max(0.1f, floatSeconds);
            if (u >= 1f || f.text == null)
            {
                if (f.text != null) Destroy(f.text.gameObject);
                floats.RemoveAt(i);
                continue;
            }
            f.text.rectTransform.anchoredPosition = f.start + Vector2.up * (floatRise * u);
            var c = f.text.color;
            c.a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - 0.5f) * 2f));
            f.text.color = c;
        }
    }

    // U3: başkalarının (ikinci oyuncu) can barı başlarının üstünde
    private void UpdateOtherHealthBars()
    {
        foreach (var h in PlayerHealth.All)
        {
            if (h == null || h == health) continue;
            bool have = false;
            foreach (var b in HealthBars) if (b != null && b.health == h) { have = true; break; }
            if (!have) HealthBars.Add(HealthBarIndicator.Create(h, transform));
        }
        for (int i = HealthBars.Count - 1; i >= 0; i--)
            if (HealthBars[i] == null || HealthBars[i].health == null) { if (HealthBars[i] != null) Destroy(HealthBars[i].gameObject); HealthBars.RemoveAt(i); }
    }

    // Kaldırma ilerlemesi: kendi oyuncumuz yerdeyse onun, arkadaşı kaldırıyorsak arkadaşın ilerlemesi.
    private string ReviveLine()
    {
        if (interaction == null || interaction.player == null) return "";
        var own = interaction.player.knockdown;
        PlayerKnockdown k = own != null && own.IsDown ? own : interaction.ReviveTarget;
        if (k == null || !k.IsDown) return "";
        if (k.ReviveProgress <= 0f) return k == own ? "Yerdesin: arkadaşın kaldırabilir" : "";
        int filled = Mathf.Clamp(Mathf.FloorToInt(k.ReviveProgress * 10f), 0, 10);
        return "Kaldırılıyor [" + new string('|', filled * 2) + new string('.', (10 - filled) * 2) + "] %" + Mathf.RoundToInt(k.ReviveProgress * 100f);
    }

    private void LateUpdate()
    {
        if (mission == null) return;

        // Ambulans sayacı
        var waiting = mission.WaitingAmbulance;
        if (mission.IsFinished)
            TimerText.text = "";
        else if (waiting != null)
            TimerText.text = "Ambulans bekliyor: " + Mathf.CeilToInt(waiting.WaitRemaining);
        else
        {
            float left = Mathf.Max(0f, mission.EstimatedNextArrival - mission.Elapsed); // gecikmede gerçek tahmin
            int secs = Mathf.CeilToInt(left);
            TimerText.text = "Ambulans: " + (secs / 60) + ":" + (secs % 60).ToString("00");
        }

        // Telsiz: varıştan 30 sn önce, 4 sn boyunca
        float toArrival = mission.EstimatedNextArrival - mission.Elapsed;
        if (!mission.IsFinished && waiting == null && toArrival <= radioLeadSeconds && toArrival > 0f && radioShownFor != mission.ArrivalCount)
        {
            radioShownFor = mission.ArrivalCount;
            radioUntil = Time.time + radioShowSeconds;
        }
        RadioLabel.text = Time.time < radioUntil ? RadioText : "";

        HintText.text = interaction != null && !mission.IsFinished ? interaction.CurrentHint : "";
        // U1: "Burada bırakılamaz" gibi uyarılar ipucunun yerinde, soluk uyarı renginde
        HintText.color = interaction != null && interaction.HintIsWarning ? WarningHintColor : Color.white;
        ReviveText.text = ReviveLine();
        NoticeText.text = Time.time < noticeUntil ? notice : "";
        ResultText.text = mission.IsFinished ? mission.ResultMessage + "\n" + RestartText : "";
        ResultText.color = mission.Succeeded ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.6f, 0.55f);

        // U3: can, para, çalınan, geri sayım
        if (health == null && interaction != null && interaction.player != null) health = interaction.player.GetComponent<PlayerHealth>();
        if (loot == null && LootRegistry.Instance != null) { loot = LootRegistry.Instance; Subscribe(); }
        float hf = health != null ? health.Fraction : 1f;
        SetFill(HealthFill, hf);
        HealthFill.color = Color.Lerp(new Color(0.95f, 0.3f, 0.25f), new Color(0.3f, 0.9f, 0.4f), hf);
        HealthLabel.text = "Can " + (health != null ? Mathf.CeilToInt(health.Current).ToString() : "-");
        MoneyText.text = LootRegistry.FormatMoney(loot != null ? loot.Money : 0);
        float sf = loot != null ? loot.StolenFraction : 0f;
        SetFill(StolenFill, sf);
        StolenPercent.text = "%" + Mathf.RoundToInt(sf * 100f);
        float remaining = health != null ? health.RespawnRemaining : -1f;
        RespawnText.text = remaining >= 0f && !mission.IsFinished ? RespawnPrefix + Mathf.CeilToInt(remaining) : "";
        UpdateFloating();
        UpdateOtherHealthBars();
    }
}
