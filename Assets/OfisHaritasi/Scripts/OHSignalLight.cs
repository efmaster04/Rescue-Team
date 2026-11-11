using UnityEngine;

// Bir trafik ışığı direğinin mercekleri. OHTrafficNetwork faz durumunu verir, bu bileşen mercekleri yakar.
// Mercek malzemeleri emission açık kurulur (OHAssetPrep), parlaklık MaterialPropertyBlock ile değişir.
namespace OfisHaritasi
{
    public class OHSignalLight : MonoBehaviour
    {
        public int junction = -1;
        public int group = -1;
        public Renderer[] red = new Renderer[0];
        public Renderer[] yellow = new Renderer[0];
        public Renderer[] green = new Renderer[0];

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseId = Shader.PropertyToID("_BaseColor");
        static readonly Color RedOn = new Color(1f, .09f, .05f) * 6f, YellowOn = new Color(1f, .62f, .08f) * 5f, GreenOn = new Color(.15f, 1f, .45f) * 4.5f;
        static readonly Color RedBase = new Color(1f, .25f, .2f), YellowBase = new Color(1f, .8f, .35f), GreenBase = new Color(.45f, 1f, .7f);
        MaterialPropertyBlock mpb;
        int last = -1;

        public void Apply(OHSignalState s)
        {
            int code = (int)s;
            if (code == last) return;
            last = code;
            Set(red, s == OHSignalState.Red, RedOn, RedBase);
            Set(yellow, s == OHSignalState.Yellow, YellowOn, YellowBase);
            Set(green, s == OHSignalState.Green, GreenOn, GreenBase);
        }

        void Set(Renderer[] rs, bool on, Color emission, Color baseOn)
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            foreach (var r in rs)
            {
                if (!r) continue;
                if (!on) { r.SetPropertyBlock(null); continue; }
                mpb.Clear();
                mpb.SetColor(EmissionId, emission);
                mpb.SetColor(BaseId, baseOn);
                r.SetPropertyBlock(mpb);
            }
        }
    }
}
