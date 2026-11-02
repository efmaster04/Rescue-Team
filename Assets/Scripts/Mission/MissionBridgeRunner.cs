using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Görev köprüsü: sahneler arası yaşayan (DontDestroyOnLoad) yürütücü ve ekran katmanı.
// Kararma, yükleme ekranı (iş adı + ekipman) ve saha sonuç paneli IMGUI ile her şeyin üstüne çizilir;
// sahnelerin kendi arayüzlerine (ofis ComputerUI, Bölüm 1 MissionHud) dokunulmaz.
[DefaultExecutionOrder(-900)]
public sealed class MissionBridgeRunner : MonoBehaviour
{
    private static MissionBridgeRunner instance;
    public static bool Exists { get { return instance != null; } }
    public static MissionBridgeRunner Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("MissionBridge");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<MissionBridgeRunner>();
            }
            return instance;
        }
    }

    public static Coroutine Run(IEnumerator routine) { return Instance.StartCoroutine(routine); }

    // 0 = açık ekran, 1 = tamamen siyah
    public float FadeAlpha { get; private set; }
    public bool LoadingVisible { get; private set; }
    public bool EndPanelVisible { get; private set; }
    public string LoadingTitle { get; private set; } = "";
    public string LoadingBody { get; private set; } = "";
    public string EndTitle { get; private set; } = "";
    public readonly List<string> EndLines = new List<string>();

    private Font font;
    private GUIStyle titleStyle, bodyStyle, panelStyle;
    private bool hasLira;

    private void Awake()
    {
        font = Resources.Load<Font>("Fonts/SourceSans3-Semibold");
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        hasLira = font != null && font.HasCharacter('₺');
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    public IEnumerator Fade(float target, float seconds)
    {
        float from = FadeAlpha;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            FadeAlpha = Mathf.Lerp(from, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds)));
            yield return null;
        }
        FadeAlpha = target;
    }

    public void ShowLoading(string title, string body) { LoadingTitle = title; LoadingBody = body; LoadingVisible = true; }
    public void HideLoading() { LoadingVisible = false; }

    public void ShowEndPanel(string title, IEnumerable<string> lines)
    {
        EndTitle = title;
        EndLines.Clear();
        EndLines.AddRange(lines);
        EndPanelVisible = true;
    }
    public void HideEndPanel() { EndPanelVisible = false; }

    private string T(string s) { return hasLira || s == null ? s : s.Replace("₺", "TL "); }

    private void EnsureStyles(float scale)
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, wordWrap = true, font = font };
            titleStyle.normal.textColor = Color.white;
            bodyStyle = new GUIStyle(titleStyle);
            bodyStyle.normal.textColor = new Color(.86f, .88f, .86f);
            panelStyle = new GUIStyle(bodyStyle) { alignment = TextAnchor.UpperLeft };
        }
        titleStyle.fontSize = Mathf.RoundToInt(46 * scale);
        bodyStyle.fontSize = Mathf.RoundToInt(28 * scale);
        panelStyle.fontSize = Mathf.RoundToInt(24 * scale);
    }

    private void OnGUI()
    {
        GUI.depth = -1000;
        float w = Screen.width, h = Screen.height;
        float scale = h / 1080f;
        EnsureStyles(scale);
        if (FadeAlpha > 0.001f)
        {
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, FadeAlpha);
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            GUI.color = old;
        }
        if (LoadingVisible && FadeAlpha > 0.9f)
        {
            GUI.Label(new Rect(w * .1f, h * .36f, w * .8f, 70 * scale), T(LoadingTitle), titleStyle);
            GUI.Label(new Rect(w * .1f, h * .36f + 80 * scale, w * .8f, 120 * scale), T(LoadingBody), bodyStyle);
        }
        if (EndPanelVisible && FadeAlpha < 0.5f)
        {
            float pw = Mathf.Min(w * .8f, 1100 * scale), ph = (60 + EndLines.Count * 32 + 50) * scale;
            var r = new Rect((w - pw) * .5f, h * .97f - ph, pw, ph);
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, .62f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
            float y = r.y + 14 * scale;
            GUI.Label(new Rect(r.x + 24 * scale, y, pw - 48 * scale, 40 * scale), T(EndTitle), panelStyle);
            y += 44 * scale;
            foreach (var line in EndLines)
            {
                GUI.Label(new Rect(r.x + 24 * scale, y, pw - 48 * scale, 32 * scale), T(line), panelStyle);
                y += 32 * scale;
            }
            GUI.Label(new Rect(r.x + 24 * scale, y + 6 * scale, pw - 48 * scale, 36 * scale), MissionBridge.ReturnHint + "    ·    R: Yeniden başlat", panelStyle);
        }
    }
}
