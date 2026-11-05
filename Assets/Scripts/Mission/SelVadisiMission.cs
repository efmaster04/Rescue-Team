using System.Collections.Generic;
using SelVadisi;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Sel Vadisi saha görevi (Bölüm 2): taşkın vadisi, çatılarda ve balkonlarda mahsur kalan köylüler.
//  1. Oyuncu kurtarma alanında (sel kıyısındaki plato) doğar.
//  2. Çatıdaki / balkondaki bir aileye bakıp E'ye basınca aile telsizle merkeze bildirilir; üstünde turuncu işaret yanar.
//  3. requiredReports aile bildirilince koordinasyon masasına dönülür, E ile rapor verilir → görev başarıyla biter.
//     FieldMissionTracker (ofisten gelindiyse) sonucu hesaplar ve "Enter: Ofise dön" gösterir.
// Derin sel suyuna giren oyuncu kurtarma alanında yeniden doğar. Haritada araba ve tekne yok (kullanıcı kararı).
// Arayüz: uGUI + legacy Text (projedeki MissionHud ile aynı yaklaşım), bütün metinler Türkçe.
[DefaultExecutionOrder(-40)]
public sealed class SelVadisiMission : MonoBehaviour, IFieldMission
{
    public static SelVadisiMission Instance { get; private set; }

    [Tooltip("Görevin bitmesi için bildirilmesi gereken aile sayısı (sahnedeki aile sayısından fazlaysa ona indirilir).")]
    public int requiredReports = 6;
    [Tooltip("Bildirme menzili (m): kameradan ailenin ortasına.")]
    public float reportRange = 75f;
    [Tooltip("Bakış konisi (°): aile, ekran ortasından en fazla bu kadar sapabilir (yakında biraz geniş).")]
    public float aimAngle = 6f;
    [Tooltip("Koordinasyon masası (kurtarma alanındaki çadırın altı).")]
    public Transform reportDesk;
    [Tooltip("Masaya en fazla yatay uzaklık (m).")]
    public float deskRange = 2.6f;
    public ThirdPersonPlayer player;
    public PlayerFallRespawn fallRespawn;
    [Tooltip("Ayak hizasında su derinliği bunu geçerse oyuncu kurtarma alanında yeniden doğar (m).")]
    public float maxWadeDepth = 0.75f;
    [Tooltip("Bildirilen ailenin üstünde yanan işaretin malzemesi (kurulum aracı atar).")]
    public Material beaconMaterial;

    public const string Title = "SEL VADİSİ · Mahsur kalanları bul";
    public const string HintReportPrefix = "E: Telsizle bildir (";
    public const string HintDesk = "E: Koordinasyon masasına raporu ver";
    public const string NoticeTooDeep = "Sel suyu çok derin ve akıntılı! Kurtarma alanına dönüldü.";
    // "Hızlı müdahale" sözleşmesinin hedef saha süresi (dk). İş tanımındaki araç aşınması sınırı bundan hesaplanır
    // (FieldPayout: gidiş-dönüş yol + sahada geçen her dakika); geçen süre görev panelinde gösterilir.
    public const float QuickResponseMinutes = 15f;

    public float Elapsed { get; private set; }
    public bool IsFinished { get; private set; }
    public bool Succeeded { get; private set; }
    public int Reported { get; private set; }
    public int PeopleReported { get; private set; }
    public int GroupCount { get { return groups.Count; } }
    public int Required { get { return Mathf.Max(1, Mathf.Min(requiredReports, groups.Count)); } }
    public string Hint { get; private set; } = "";
    public int WaterRespawns { get; private set; }
    public Vector3 LastGuardPosition { get; private set; }

    private sealed class Group
    {
        public int key, count;
        public Vector3 center, top;
        public string where, whereAt;
        public bool reported;
        public GameObject beacon;
    }

    private readonly List<Group> groups = new List<Group>();
    private Group aimed;
    private Text objectiveText, hintText, noticeText;
    private float noticeUntil;
    private float lastRespawn = -10f;

    private void Awake()
    {
        Instance = this;
        FieldMission.Register(this);
        GameNotice.Raised += OnNotice;
    }

    private void OnDestroy()
    {
        FieldMission.Unregister(this);
        GameNotice.Raised -= OnNotice;
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (player == null) player = FindAnyObjectByType<ThirdPersonPlayer>();
        if (fallRespawn == null && player != null) fallRespawn = player.GetComponent<PlayerFallRespawn>();
        CollectGroups();
        BuildHud();
    }

    // Çatı ve balkon gruplarını köylü listesinden çıkarır (kök adı: Arketip_Yer_No_Renk, grup kimliği PeopleAnimator'da).
    private void CollectGroups()
    {
        groups.Clear();
        var anim = FindAnyObjectByType<PeopleAnimator>();
        if (anim == null) return;
        var byKey = new Dictionary<int, Group>();
        for (int i = 0; i < anim.people.Count; i++)
        {
            var p = anim.people[i];
            if (p == null || p.root == null) continue;
            string[] parts = p.root.name.Split('_');
            if (parts.Length < 2) continue;
            string loc = parts[1];
            if (loc != "Roof" && loc != "Balcony") continue;
            int key = p.group >= 0 ? p.group : -1 - i;
            if (!byKey.TryGetValue(key, out var g))
            {
                g = new Group { key = key, where = loc == "Roof" ? "Çatı" : "Balkon", whereAt = loc == "Roof" ? "Çatıda" : "Balkonda", top = new Vector3(0f, float.NegativeInfinity, 0f) };
                byKey[key] = g; groups.Add(g);
            }
            g.center += p.root.position; g.count++;
            if (p.root.position.y > g.top.y) g.top = p.root.position;
        }
        foreach (var g in groups) { g.center /= Mathf.Max(1, g.count); g.center += Vector3.up * 1.1f; }
    }

    private void Update()
    {
        if (!IsFinished) Elapsed += Time.deltaTime;
        GuardWater();
        aimed = null;
        Hint = "";
        var kb = Keyboard.current;
        bool press = kb != null && kb.eKey.wasPressedThisFrame;
        if (!IsFinished)
        {
            if (Reported < Required)
            {
                aimed = FindAimed();
                if (aimed != null)
                {
                    Hint = HintReportPrefix + aimed.where + " · " + aimed.count + " kişi)";
                    if (press) Report(aimed);
                }
            }
            else if (NearDesk())
            {
                Hint = HintDesk;
                if (press) TryFinishAtDesk();
            }
        }
        UpdateHud();
    }

    private Group FindAimed()
    {
        var cam = Camera.main;
        return cam == null ? null : FindAimed(cam.transform.position, cam.transform.forward);
    }

    private Group FindAimed(Vector3 o, Vector3 f)
    {
        Group best = null; float bestScore = float.PositiveInfinity;
        foreach (var g in groups)
        {
            if (g.reported) continue;
            Vector3 d = g.center - o;
            float dist = d.magnitude;
            if (dist > reportRange || dist < 0.5f) continue;
            float limit = aimAngle + 12f * Mathf.Clamp01(1f - dist / 15f);     // close by, a looser cone
            float ang = Vector3.Angle(f, d);
            if (ang > limit) continue;
            float score = ang / limit + dist / reportRange * 0.3f;
            if (score < bestScore) { bestScore = score; best = g; }
        }
        return best;
    }

    private void Report(Group g)
    {
        g.reported = true;
        Reported++;
        PeopleReported += g.count;
        g.beacon = MakeBeacon(g.top + Vector3.up * 2.6f);
        string rest = Reported >= Required ? " Koordinasyon masasına dön." : " (" + Reported + "/" + Required + ")";
        GameNotice.Show("Telsiz: " + g.whereAt + " " + g.count + " kişi bildirildi." + rest);
    }

    // Turuncu işaret: ince direk ve üstünde parlayan küre (malzeme kurulumdan gelir; yoksa varsayılan).
    private GameObject MakeBeacon(Vector3 at)
    {
        var root = new GameObject("Bildirildi_Isaret");
        root.transform.position = at;
        var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(pole.GetComponent<Collider>());
        pole.transform.SetParent(root.transform, false);
        pole.transform.localScale = new Vector3(0.05f, 1.1f, 0.05f);
        pole.transform.localPosition = new Vector3(0f, -1.1f, 0f);
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(ball.GetComponent<Collider>());
        ball.transform.SetParent(root.transform, false);
        ball.transform.localScale = Vector3.one * 0.45f;
        if (beaconMaterial != null)
        {
            pole.GetComponent<MeshRenderer>().sharedMaterial = beaconMaterial;
            ball.GetComponent<MeshRenderer>().sharedMaterial = beaconMaterial;
        }
        root.AddComponent<SelVadisiBeacon>();
        return root;
    }

    // ---- doğrulama ve dış çağrılar (SelVadisiVerify) ----

    public Vector3 GroupCenter(int i) { return groups[i].center; }
    public bool GroupReported(int i) { return groups[i].reported; }
    public int GroupSize(int i) { return groups[i].count; }

    // Verilen bakış ışınının hedeflediği aileyi bildirir (E ile aynı kural). Bildirildiyse true.
    public bool ReportAlong(Vector3 eye, Vector3 dir)
    {
        if (IsFinished || Reported >= Required) return false;
        var g = FindAimed(eye, dir.normalized);
        if (g == null) return false;
        Report(g);
        return true;
    }

    // Yeterli aile bildirildiyse ve oyuncu masadaysa görevi başarıyla bitirir.
    public bool TryFinishAtDesk()
    {
        if (IsFinished || Reported < Required || !NearDesk()) return false;
        IsFinished = true; Succeeded = true;
        GameNotice.Show("Rapor verildi: " + Reported + " aile, " + PeopleReported + " kişi. Ekipler yönlendiriliyor.");
        return true;
    }

    private bool NearDesk()
    {
        if (reportDesk == null || player == null) return false;
        Vector3 d = player.transform.position - reportDesk.position; d.y = 0f;
        return d.magnitude <= deskRange;
    }

    // Derin suya giren oyuncu kurtarma alanında doğar (akıntı ölümcül; yüzme yok).
    private void GuardWater()
    {
        if (player == null || fallRespawn == null || Time.time - lastRespawn < 1.5f) return;
        Vector3 p = player.transform.position;
        float depth = Valley.WaterLevel(p.z) - p.y;
        if (depth > maxWadeDepth)
        {
            LastGuardPosition = p;
            lastRespawn = Time.time;
            WaterRespawns++;
            fallRespawn.Respawn();
            GameNotice.Show(NoticeTooDeep);
        }
    }

    // ---- HUD ----

    private void OnNotice(string message)
    {
        if (noticeText == null) return;
        noticeText.text = message;
        noticeUntil = Time.time + 3.5f;
    }

    private void BuildHud()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasGo = new GameObject("SelVadisi_GorevHUD", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        objectiveText = MakeText(canvasGo.transform, font, new Vector2(0f, 1f), new Vector2(28f, -26f), new Vector2(620f, 180f), 24, TextAnchor.UpperLeft, true);
        hintText = MakeText(canvasGo.transform, font, new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(900f, 44f), 26, TextAnchor.MiddleCenter, true);
        noticeText = MakeText(canvasGo.transform, font, new Vector2(0.5f, 0f), new Vector2(0f, 172f), new Vector2(1100f, 40f), 22, TextAnchor.MiddleCenter, false);
        noticeText.color = new Color(1f, 0.86f, 0.55f);
    }

    private static Text MakeText(Transform parent, Font font, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize, TextAnchor align, bool panel)
    {
        var go = new GameObject("Metin", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(anchor.x, anchor.y);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        if (panel)
        {
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.12f, 0.15f, 0.72f);
            var child = new GameObject("Yazi", typeof(RectTransform));
            child.transform.SetParent(go.transform, false);
            var crt = child.GetComponent<RectTransform>();
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = new Vector2(16f, 10f); crt.offsetMax = new Vector2(-16f, -10f);
            go = child;
        }
        var t = go.AddComponent<Text>();
        t.font = font; t.fontSize = fontSize; t.alignment = align; t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = true;
        return t;
    }

    private void UpdateHud()
    {
        if (objectiveText == null) return;
        string state;
        if (IsFinished) state = Succeeded ? "Görev tamamlandı. Ekipler yönlendiriliyor." : "Görev bitti.";
        else if (Reported < Required) state = "Çatılara ve balkonlara bak, mahsur kalanları E ile telsizle bildir.";
        else state = "Yeterli aile bildirildi. Kurtarma alanındaki koordinasyon masasına dön.";
        string clock = "Süre " + Clock(Elapsed);
        if (!IsFinished) clock += Elapsed <= QuickResponseMinutes * 60f
            ? "   ·   Hızlı müdahale hedefi: " + QuickResponseMinutes.ToString("0") + " dk"
            : "   ·   <color=#ffb080>Hızlı müdahale süresi doldu</color>";
        objectiveText.text = "<b>" + Title + "</b>\nBildirilen aile: " + Reported + " / " + Required + "   ·   Kişi: " + PeopleReported + "\n" + clock + "\n" + state;
        bool showHint = !string.IsNullOrEmpty(Hint);
        hintText.transform.parent.gameObject.SetActive(showHint);
        hintText.text = Hint;
        noticeText.gameObject.SetActive(Time.time < noticeUntil);
    }

    private static string Clock(float seconds)
    {
        int t = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return (t / 60).ToString("00") + ":" + (t % 60).ToString("00");
    }
}

// Bildirilen ailenin işareti: yavaşça yanıp söner.
public sealed class SelVadisiBeacon : MonoBehaviour
{
    private float phase;
    private void Start() { phase = Random.value * 6f; }
    private void Update()
    {
        float s = 1f + 0.18f * Mathf.Sin(Time.time * 4f + phase);
        if (transform.childCount > 1) transform.GetChild(1).localScale = Vector3.one * 0.45f * s;
    }
}
