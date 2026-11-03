using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace CompanySim
{
    public sealed partial class ComputerUI
    {
        static readonly Color HudInk=new Color(.980f,.976f,.961f,.97f);
        static readonly Color HudPaper=Hex("#252B2C"),HudMuted=Hex("#62665F"),HudReady=Hex("#48664C"),HudWait=Hex("#986A2D");
        GameObject promptKey;
        Text promptKeyText,carriedDetails,carriedCondition,prepTitle,prepPhase,prepSummary;
        RectTransform conditionFill,prepFill,prepRows;
        CanvasGroup toastGroup;
        float toastStart;
        int lastCarriedCondition=-1;
        struct PreparationRow
        {
            public string name,state; public bool ready;
            public PreparationRow(string name,string state,bool ready) { this.name=name; this.state=state; this.ready=ready; }
        }
        void HudEdge(GameObject panel)
        {
            var edge=Block("TopEdge",panel.transform,Rule);
            Rect(edge,new Vector2(0,1),Vector2.one,new Vector2(0,-1),Vector2.zero);
        }
        GameObject KeyCap(Transform parent,string key,float x,float y,float width=30)
        {
            var cap=Block("Key_"+key,parent,Selection); Place(cap,x,y,width,27);
            var caption=Label("KeyLabel",cap.transform,key,13,HudPaper,TextAnchor.MiddleCenter); Fill(caption.gameObject);
            return cap;
        }
        void BuildGameHud(Transform root)
        {
            hud=Obj("HUD",root); Fill(hud);
            hud.AddComponent<CanvasGroup>().blocksRaycasts=false;
            promptPanel=Block("HUD_Interaction",hud.transform,HudInk);
            var rect=Rect(promptPanel,new Vector2(.5f,0),new Vector2(.5f,0),Vector2.zero,Vector2.zero);
            rect.pivot=new Vector2(.5f,0); rect.anchoredPosition=new Vector2(0,35); rect.sizeDelta=new Vector2(380,48);
            HudEdge(promptPanel); promptKey=KeyCap(promptPanel.transform,"E",10,10,28);
            promptKeyText=promptKey.GetComponentInChildren<Text>();
            promptText=Label("InteractionPrompt",promptPanel.transform,"",19,HudPaper);
            promptText.horizontalOverflow=HorizontalWrapMode.Overflow;
            Rect(promptText.gameObject,Vector2.zero,Vector2.one,new Vector2(50,5),new Vector2(-15,-5));
            promptPanel.SetActive(false);
            focusReticle=Icon(hud.transform,"Crosshair",new Color(.95f,.95f,.91f,.66f));
            Rect(focusReticle.gameObject,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(-3,-3),new Vector2(3,3));
            var hint=Block("StatusHint",hud.transform,new Color(.10f,.12f,.12f,.90f));
            Rect(hint,Vector2.one,Vector2.one,new Vector2(-190,-64),new Vector2(-26,-26));
            KeyCap(hint.transform,"TAB",6,5,42);
            Place(Label("PreparationHint",hint.transform,"İş fişi",16,HudInk).gameObject,58,5,94,27);
            var menu=Block("MenuHintPlate",hud.transform,new Color(.10f,.12f,.12f,.90f));
            Rect(menu,new Vector2(1,0),new Vector2(1,0),new Vector2(-164,26),new Vector2(-26,64));
            KeyCap(menu.transform,"ESC",6,5,42);
            Place(Label("MenuHint",menu.transform,"Menü",16,HudInk).gameObject,58,5,68,27);

            prepPanel=Block("PreparationBoard",hud.transform,HudInk);
            var board=Rect(prepPanel,Vector2.one,Vector2.one,Vector2.zero,Vector2.zero);
            board.pivot=Vector2.one; board.anchoredPosition=new Vector2(-26,-74); board.sizeDelta=new Vector2(330,340);
            HudEdge(prepPanel);
            Place(Label("PreparationHeading",prepPanel.transform,"İş fişi",16,HudMuted).gameObject,18,15,286,22);
            prepTitle=Label("PreparationJob",prepPanel.transform,"",20,HudPaper); prepTitle.font=headingFont;
            Place(prepTitle.gameObject,18,43,294,55);
            prepPhase=Label("PreparationPhase",prepPanel.transform,"",13,HudMuted); Place(prepPhase.gameObject,18,103,294,24);
            var rows=Obj("PreparationRows",prepPanel.transform); prepRows=rows.GetComponent<RectTransform>(); Place(rows,18,141,294,130);
            var progress=Block("PreparationTrack",prepPanel.transform,Rule);
            Rect(progress,Vector2.zero,new Vector2(1,0),new Vector2(18,50),new Vector2(-18,53));
            var fill=Block("PreparationFill",progress.transform,HudReady); Fill(fill); prepFill=fill.GetComponent<RectTransform>();
            prepSummary=Label("PreparationSummary",prepPanel.transform,"",14,HudPaper);
            Rect(prepSummary.gameObject,Vector2.zero,new Vector2(1,0),new Vector2(18,13),new Vector2(-18,42));
            prepPanel.SetActive(false);

            carriedPanel=Block("CarriedEquipment",hud.transform,HudInk);
            Rect(carriedPanel,Vector2.zero,Vector2.zero,new Vector2(26,27),new Vector2(326,145)); HudEdge(carriedPanel);
            carriedText=Label("CarriedEquipmentName",carriedPanel.transform,"",20,HudPaper);
            Place(carriedText.gameObject,15,10,270,28);
            carriedDetails=Label("CarriedEquipmentWeight",carriedPanel.transform,"",15,HudMuted); Place(carriedDetails.gameObject,15,39,145,20);
            carriedCondition=Label("CarriedEquipmentCondition",carriedPanel.transform,"",15,HudMuted,TextAnchor.MiddleRight);
            Place(carriedCondition.gameObject,170,39,115,20);
            var track=Block("ConditionTrack",carriedPanel.transform,Rule); Place(track,15,62,270,3);
            var meter=Block("ConditionFill",track.transform,HudReady); Fill(meter); conditionFill=meter.GetComponent<RectTransform>();
            KeyCap(carriedPanel.transform,"F",15,77,23); Place(Label("DropHint",carriedPanel.transform,"Bırak",12,HudMuted).gameObject,45,77,67,27);
            KeyCap(carriedPanel.transform,"R",136,77,23); Place(Label("RotateHint",carriedPanel.transform,"Döndür",12,HudMuted).gameObject,166,77,84,27);
            carriedPanel.SetActive(false);
        }
        void PresentPrompt(string value)
        {
            value=value??"";
            bool action=value.Length>3 && value[1]==' ' && value[2]=='—';
            bool hold=value.StartsWith("E basılı tut — ");
            promptKey.SetActive(action || hold);
            if(action || hold) promptKeyText.text=hold?"E":value.Substring(0,1);
            promptText.text=hold?"Basılı tut · "+value.Substring("E basılı tut — ".Length):action?value.Substring(3).TrimStart():value;
            action=action || hold;
            var rect=promptText.rectTransform; rect.offsetMin=new Vector2(action?50:16,5);
            float width=Mathf.Clamp(Mathf.Max(promptText.preferredWidth,promptText.text.Length*8.5f)+(action?66:32),190,700);
            var panel=promptPanel.GetComponent<RectTransform>(); panel.sizeDelta=new Vector2(width,48);
        }
        void UpdateCarriedInfo()
        {
            if(!carriedItem) return;
            int condition=Mathf.RoundToInt(Mathf.Clamp(carriedItem.durability,0,100));
            if(condition==lastCarriedCondition) return;
            lastCarriedCondition=condition; carriedText.text=carriedItem.DisplayName;
            var definition=catalog.Item(carriedItem.definitionId);
            carriedDetails.text=definition?definition.weightKg.ToString("0.#")+" kg":carriedItem.GetComponent<ManualModulePackage>()?"Servis modülü":"";
            carriedCondition.text="Durum %"+condition;
            conditionFill.anchorMax=new Vector2(condition*.01f,1);
            conditionFill.GetComponent<Image>().color=condition<20?new Color(.87f,.36f,.29f):condition<60?HudWait:HudReady;
        }
        void PrepRefresh()
        {
            if(!prepTitle) return;
            for(int i=prepRows.childCount-1;i>=0;i--) { var child=prepRows.GetChild(i).gameObject; child.SetActive(false); Destroy(child); }
            var job=catalog.Job(Runtime.Data.activeJobId);
            var rows=new List<PreparationRow>();
            if(job)
            {
                prepTitle.text=job.title; prepPhase.text=PhaseName();
                // Takılı ve sabit modül araçtan okunur (Data.installedModuleId yalnızca kayıtta güncellenir)
                bool module=Runtime.vehicle && Runtime.vehicle.InstalledModuleId==job.requiredModule && (!Runtime.manualModules || Runtime.manualModules.IsSecured);
                rows.Add(new PreparationRow(catalog.Module(job.requiredModule)?.displayName??job.requiredModule,module?"Takılı":"Takılmalı / sabitlenmeli",module));
                var available=Runtime.vehicle.LoadedItems.OrderByDescending(x=>x.durability).ToList();
                foreach(var id in job.requiredEquipment)
                {
                    var item=available.FirstOrDefault(x=>x.definitionId==id);
                    bool healthy=item && item.durability>=20;
                    rows.Add(new PreparationRow(catalog.Item(id)?.displayName??id,item?(healthy?"Yüklü":"Bakım gerekli"):"Eksik",healthy));
                    if(item) available.Remove(item);
                }
                if(Runtime.Data.vehicleCondition<catalog.economy.minimumVehicleCondition)
                    rows.Add(new PreparationRow("Araç bakımı","Gerekli",false));
                if(Runtime.manualModules && !Runtime.manualModules.IsSecured) rows.Add(new PreparationRow("Modül bağlantıları","Sabitleme gerekli",false));
                foreach(var item in available.Where(x=>x.durability<20)) rows.Add(new PreparationRow(item.DisplayName,"Bakım gerekli",false));
                var missing=Runtime.MissingPreparation();
                prepSummary.text=Runtime.Data.phase==JobPhase.AwaitingReport?"Rapor bilgisayarda bekliyor":
                    Runtime.Data.phase==JobPhase.Deployed?"Ekip sahada":missing.Count==0?"Çıkışa hazır":missing.Count+" kontrol bekliyor";
                prepSummary.color=missing.Count==0?HudReady:HudWait;
            }
            else
            {
                prepTitle.text="Henüz iş alınmadı"; prepPhase.text="Bilgisayardan bir iş seç."; prepSummary.text="İşler → İlanlar"; prepSummary.color=HudMuted;
            }
            for(int i=0;i<rows.Count;i++)
            {
                var row=rows[i]; float y=i*44;
                var check=Block("PreparationCheck_"+i,prepRows,new Color(1,1,1,.06f)); Place(check,0,y+10,18,18);
                if(row.ready) { var icon=Icon(check.transform,"Check",HudReady); Fill(icon.gameObject,2); }
                else { var marker=Block("Pending",check.transform,HudWait); Place(marker,5,8,8,2); }
                var name=Label("PreparationItem_"+i,prepRows,row.name,18,HudPaper); Place(name.gameObject,28,y,266,26);
                var state=Label("PreparationState_"+i,prepRows,row.state,15,row.ready?HudReady:HudWait); Place(state.gameObject,28,y+25,266,20);
            }
            prepRows.sizeDelta=new Vector2(294,rows.Count*44);
            prepPanel.GetComponent<RectTransform>().sizeDelta=new Vector2(330,rows.Count==0?210:212+rows.Count*44);
            prepFill.anchorMax=new Vector2(rows.Count==0?0:rows.Count(x=>x.ready)/(float)rows.Count,1);
        }
        void BuildNotification(Transform root)
        {
            toastPanel=Block("Notification",root,HudInk); HudEdge(toastPanel);
            var rect=Rect(toastPanel,new Vector2(.5f,1),new Vector2(.5f,1),Vector2.zero,Vector2.zero);
            rect.pivot=new Vector2(.5f,1); rect.anchoredPosition=new Vector2(0,-26); rect.sizeDelta=new Vector2(510,76);
            var edge=Block("NoticeAccent",toastPanel.transform,HudWait); Rect(edge,Vector2.zero,new Vector2(0,1),Vector2.zero,new Vector2(2,0));
            toastText=Label("NotificationText",toastPanel.transform,"",18,HudPaper); Place(toastText.gameObject,18,13,474,44);
            toastGroup=toastPanel.AddComponent<CanvasGroup>(); toastGroup.blocksRaycasts=false; toastPanel.SetActive(false);
        }
        void UpdateGameHud()
        {
            UpdateCarriedInfo();
            if(!toastPanel.activeSelf) return;
            float age=Time.unscaledTime-toastStart,remaining=toastUntil-Time.unscaledTime;
            if(remaining<=0) { toastPanel.SetActive(false); return; }
            toastGroup.alpha=Mathf.Min(Mathf.Clamp01(age/.16f),Mathf.Clamp01(remaining/.25f));
            toastPanel.GetComponent<RectTransform>().anchoredPosition=new Vector2(0,-26+8*(1-Mathf.Clamp01(age/.16f)));
        }
        void LayoutNotification()
        {
            float height=Mathf.Clamp(toastText.preferredHeight+26,56,166);
            toastPanel.GetComponent<RectTransform>().sizeDelta=new Vector2(510,height);
            toastText.rectTransform.sizeDelta=new Vector2(474,height-26);
        }
    }
}
