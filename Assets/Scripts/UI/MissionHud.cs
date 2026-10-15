using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// HUD (kullanıcı kararı 16), uGUI + legacy Text (TMP Essential Resources projede yok). Tüm metinler Türkçe.
//  - Sağ üst: "Ambulans: 2:47" (bir sonraki varışa kalan süre) ya da durakta "Ambulans bekliyor: 12"
//  - Varıştan 30 sn önce 4 sn boyunca: "Telsiz: Ambulans rampa başına yaklaşıyor!"
//  - Alt orta: etkileşim ipucu ve kısa uyarılar ("İki kişi gerekli", "Ambulans dolu")
//  - Orta: görev sonu mesajı ve "R: Yeniden başlat"
//  - Her hastanın başının üstünde 3 kutucuklu kaçırma göstergesi (MissIndicator)
// Arayüz yalnız okur; oyun durumunu değiştirmez.
public class MissionHud : MonoBehaviour
{
    public MissionDirector mission;
    public PlayerInteraction interaction;
    [Tooltip("Kısa uyarıların ekranda kalma süresi (sn).")]
    public float noticeSeconds = 2.5f;
    public float radioLeadSeconds = 30f;
    public float radioShowSeconds = 4f;

    public const string RadioText = "Telsiz: Ambulans rampa başına yaklaşıyor!";
    public const string RestartText = "R: Yeniden başlat";

    public Canvas ScreenCanvas { get; private set; }
    public Text TimerText { get; private set; }
    public Text RadioLabel { get; private set; }
    public Text HintText { get; private set; }
    public Text NoticeText { get; private set; }
    public Text ResultText { get; private set; }
    public readonly List<MissIndicator> Indicators = new List<MissIndicator>();

    private string notice;
    private float noticeUntil = -1f;
    private int radioShownFor = -1;     // telsiz mesajı hangi varış için gösterildi
    private float radioUntil = -1f;
    private Font font;

    private void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildScreenCanvas();
    }

    private void Start()
    {
        if (mission != null)
            foreach (var p in mission.patients)
                if (p != null) Indicators.Add(MissIndicator.Create(p, transform));
    }

    private void OnEnable() { GameNotice.Raised += OnNotice; }
    private void OnDisable() { GameNotice.Raised -= OnNotice; }

    private void OnNotice(string message)
    {
        notice = message;
        noticeUntil = Time.time + noticeSeconds;
    }

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
            float left = Mathf.Max(0f, mission.NextArrivalTime - mission.Elapsed);
            int secs = Mathf.CeilToInt(left);
            TimerText.text = "Ambulans: " + (secs / 60) + ":" + (secs % 60).ToString("00");
        }

        // Telsiz: varıştan 30 sn önce, 4 sn boyunca
        float toArrival = mission.NextArrivalTime - mission.Elapsed;
        if (!mission.IsFinished && waiting == null && toArrival <= radioLeadSeconds && toArrival > 0f && radioShownFor != mission.ArrivalCount)
        {
            radioShownFor = mission.ArrivalCount;
            radioUntil = Time.time + radioShowSeconds;
        }
        RadioLabel.text = Time.time < radioUntil ? RadioText : "";

        HintText.text = interaction != null && !mission.IsFinished ? interaction.CurrentHint : "";
        NoticeText.text = Time.time < noticeUntil ? notice : "";
        ResultText.text = mission.IsFinished ? mission.ResultMessage + "\n" + RestartText : "";
        ResultText.color = mission.Succeeded ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.6f, 0.55f);
    }
}
