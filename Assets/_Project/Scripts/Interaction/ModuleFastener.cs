using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CompanySim
{
    public sealed class ModuleFastener : InteractableBehaviour
    {
        public ManualModuleSystem system;
        public int index;
        public Transform boltVisual;
        public Renderer indicator;
        bool working;
        float progress;
        public bool Working => working;
        public float WorkProgress => progress;
        Vector3 restPosition;
        Quaternion restRotation;
        MaterialPropertyBlock indicatorColor;
        void Awake() {if(boltVisual) {restPosition=boltVisual.localPosition; restRotation=boltVisual.localRotation;}}
        void Start() {system.Changed+=Refresh; Refresh();}
        void OnDestroy() {if(system) system.Changed-=Refresh;}
        void Refresh()
        {
            bool tight=system.Current && (index<0?system.Current.record.connected:(system.Current.record.bolts&(1<<index))!=0);
            SetPose(tight?0:1,tight?0:90);
            if(indicator)
            {
                if(indicatorColor==null) indicatorColor=new MaterialPropertyBlock();
                indicator.GetPropertyBlock(indicatorColor); indicatorColor.SetColor("_BaseColor",tight?new Color(.20f,.42f,.24f):new Color(.64f,.36f,.13f)); indicator.SetPropertyBlock(indicatorColor);
            }
        }
        void SetPose(float loosened,float angle)
        {
            if(!boltVisual) return;
            boltVisual.localPosition=restPosition+Vector3.forward*(loosened*.025f);
            boltVisual.localRotation=restRotation*Quaternion.AngleAxis(angle,Vector3.forward);
        }
        public override string Prompt(PlayerInteractor player)
        {
            if(!system.Current) return "Önce modülü yuvaya yerleştirin";
            if(!system.CanWork(player,out var reason)) return reason;
            bool tight=index<0?system.Current.record.connected:(system.Current.record.bolts&(1<<index))!=0;
            if(index<0) return "E — Modül bağlantısını "+(tight?"ayır":"bağla");
            if(tight && system.Current.record.connected) return "Önce modül bağlantısını ayırın";
            if(!player.HeldItem || player.HeldItem.definitionId!="toolbox") return "Cıvatalar için alet çantasını alın";
            return "E basılı tut — Cıvata "+(index+1)+(tight?" sök":" sık")+(working?"  %"+Mathf.RoundToInt(progress*100):"");
        }
        public override void Interact(PlayerInteractor player)
        {
            if(working || !system.Current || !system.CanWork(player,out _)) return;
            if(index<0) {system.ToggleConnection(player); return;}
            if(system.Current.record.connected && (system.Current.record.bolts&(1<<index))!=0) {player.ui.Toast("Önce modül bağlantısını ayırın."); return;}
            if(!player.HeldItem || player.HeldItem.definitionId!="toolbox") {player.ui.Toast("Cıvatalar için alet çantası gerekiyor."); return;}
            StartCoroutine(Turn(player));
        }
        IEnumerator Turn(PlayerInteractor player)
        {
            working=true; progress=0; bool loosening=(system.Current.record.bolts&(1<<index))!=0;
            try
            {
                while(progress<1)
                {
                    // E basılı bilgisi oyuncudan (bizim karakterde PlayerInputReader; UI açıkken false)
                    if(!player.InteractHeld || player.InputLocked || player.FindTarget()!=this || !system.CanWork(player,out _)) break;
                    progress=Mathf.Clamp01(progress+Time.deltaTime/.65f);
                    SetPose(loosening?progress:1-progress,(loosening?1:-1)*progress*810+(loosening?0:90));
                    yield return null;
                }
                if(progress>=1) system.ToggleBolt(index,player);
            }
            finally {working=false; progress=0; Refresh();}
        }
    }
}
