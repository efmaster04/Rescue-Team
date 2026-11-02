using UnityEngine;
using UnityEngine.UI;

namespace CompanySim
{
    public sealed class PerformanceCounter : MonoBehaviour
    {
        public Text label;
        public ComputerUI ui;
        public float FramesPerSecond { get; private set; }
        public float FrameMilliseconds { get; private set; }
        public int SampleCount { get; private set; }
        float elapsed;
        int frames;
        bool wasComputer;
        void Update()
        {
            bool computer=ui && ui.IsOpen;
            if(computer!=wasComputer)
            {
                var rect=(RectTransform)transform;
                rect.anchorMin=rect.anchorMax=computer?Vector2.one:new Vector2(0,1);
                rect.pivot=new Vector2(computer?1:0,1); rect.anchoredPosition=new Vector2(computer?-26:26,-8);
                wasComputer=computer;
            }
            float delta=Time.unscaledDeltaTime;
            if(delta<=0) return;
            elapsed+=delta; frames++;
            if(elapsed<.5f) return;
            FramesPerSecond=frames/elapsed; FrameMilliseconds=elapsed/frames*1000; SampleCount++;
            // A batch validation loop can tick without presenting frames. Do not
            // present those simulation-only rates as gameplay FPS in captures.
            label.text=Application.isBatchMode?"FPS —":FramesPerSecond.ToString("0")+" FPS   "+FrameMilliseconds.ToString("0.0")+" ms";
            elapsed=0; frames=0;
        }
    }
    public sealed partial class ComputerUI
    {
        void BuildPerformanceOverlay(Transform root)
        {
            var go=Block("PerformanceOverlay",root,new Color(.10f,.13f,.14f,.82f));
            var rect=Rect(go,new Vector2(0,1),new Vector2(0,1),Vector2.zero,Vector2.zero);
            rect.pivot=new Vector2(0,1); rect.anchoredPosition=new Vector2(26,-8); rect.sizeDelta=new Vector2(192,27);
            go.GetComponent<Image>().raycastTarget=false;
            var overlay=go.AddComponent<Canvas>(); overlay.overrideSorting=true; overlay.sortingOrder=100;
            var text=Label("FrameRate",go.transform,"FPS —",17,Color.white,TextAnchor.MiddleCenter); Fill(text.gameObject);
            var counter=go.AddComponent<PerformanceCounter>(); counter.label=text; counter.ui=this;
        }
    }
}
