using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace RescueTeam.Bolum1
{
    public sealed class MissionHud : MonoBehaviour
    {
        public MissionDirector mission;
        public PlayerInteraction interaction;
        public Canvas canvas;
        Text timer, hint, radio, result;
        static string notice;
        static float noticeUntil;
        float radioUntil, announcedArrival = -1;
        readonly List<Text> patientLabels = new List<Text>();
        public static string LastNotice => notice;
        public static void Notify(string message) { notice = message; noticeUntil = Time.time + 1.8f; }
        void Start()
        {
            var go = new GameObject("AmbulanceHUD", typeof(Canvas), typeof(CanvasScaler)); go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
            timer = Label(canvas.transform, "Timer", new Vector2(1, 1), new Vector2(-270, -60), new Vector2(500, 70), 30);
            hint = Label(canvas.transform, "Hint", new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(900, 90), 30);
            radio = Label(canvas.transform, "Radio", new Vector2(0.5f, 1), new Vector2(0, -140), new Vector2(1200, 70), 30);
            result = Label(canvas.transform, "Result", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400, 240), 38);
            foreach (var patient in mission.patients)
            {
                var world = new GameObject(patient.displayName + "_Misses", typeof(Canvas)); world.transform.SetParent(transform, false);
                world.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                var rect = world.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(350, 100); rect.localScale = Vector3.one * 0.004f;
                patientLabels.Add(Label(world.transform, "Misses", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(350, 100), 25));
            }
        }
        static Text Label(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline)); go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = anchor; rect.anchoredPosition = position; rect.sizeDelta = size;
            var text = go.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter; text.color = Color.white; text.raycastTarget = false;
            go.GetComponent<Outline>().effectColor = new Color(0, 0, 0, 0.9f); return text;
        }
        void Update()
        {
            if (!timer) return;
            AmbulanceController waiting = mission.ambulances.Find(a => a && a.Phase == AmbulancePhase.Waiting);
            float seconds = Mathf.Max(0, mission.NextArrival - mission.Elapsed);
            timer.text = waiting ? "Ambulans bekliyor: " + Mathf.CeilToInt(waiting.WaitRemaining) : "Ambulans: " + ((int)seconds / 60) + ":" + ((int)seconds % 60).ToString("00");
            if (seconds <= 30 && announcedArrival != mission.NextArrival)
            { announcedArrival = mission.NextArrival; radioUntil = Time.time + 4; }
            radio.text = Time.time < radioUntil ? "Telsiz: Ambulans rampa başına yaklaşıyor!" : "";
            hint.text = Time.time < noticeUntil ? notice : interaction ? interaction.Hint : "";
            result.text = mission.IsFinished ? mission.ResultMessage + "\n\nR: Yeniden başlat" : "";
            for (int i = 0; i < patientLabels.Count; i++)
            {
                var p = mission.patients[i]; var label = patientLabels[i];
                bool visible = p.State != PatientState.InAmbulance && p.State != PatientState.Rescued;
                label.transform.parent.gameObject.SetActive(visible);
                if (!visible) continue;
                label.transform.parent.position = p.Position + Vector3.up * 0.8f + p.transform.forward * 0.55f;
                if (Camera.main) label.transform.parent.rotation = Camera.main.transform.rotation;
                label.text = p.displayName + "\n" + (p.MissedCount > 0 ? "■ " : "□ ") + (p.MissedCount > 1 ? "■ " : "□ ") + (p.MissedCount > 2 ? "■" : "□");
            }
        }
    }
}
