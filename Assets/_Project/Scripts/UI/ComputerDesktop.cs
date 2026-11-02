using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace CompanySim
{
    public sealed partial class ComputerUI
    {
        Canvas canvas;
        Font font,headingFont;
        Sprite roundSprite;
        GameObject computer,desktop,appWindow,hud,prepPanel,toastPanel,promptPanel,carriedPanel;
        RectTransform mainContent,windowRect,navigationPanel,jobFooter;
        ScrollRect appScroll;
        Text promptText,toastText,titleText,subtitleText,clockText,mailBadge,carriedText,taskWindowText;
        DesktopGlyph focusReticle,appIcon,taskWindowIcon;
        CarryableItem carriedItem;
        bool maximized;
        string lastWindowApp;
        float lastWindowScroll=1;
        Button taskWindowButton;
        Vector2 normalAnchorMin,normalAnchorMax,normalOffsetMin,normalOffsetMax;
        static Color Hex(string value) { ColorUtility.TryParseHtmlString(value,out var c); return c; }
        static readonly Color Navy=Hex("#202728"),Slate=Hex("#EDEBE4"),Surface=Hex("#FAF9F5");
        static readonly Color Paper=Hex("#202927"),Soft=Hex("#56615D"),Rule=Hex("#D7D8D1");
        static readonly Color Mint=Hex("#36596A"),Blue=Mint,Peach=Hex("#986A2D"),Ochre=Peach,Red=Hex("#9B4C43");
        static readonly Color Selection=Hex("#E0E7E9"),Ready=Hex("#48664C");
        sealed class AppDefinition
        {
            public string id,title; public Color tint;
            public AppDefinition(string id,string title,Color tint) { this.id=id; this.title=title; this.tint=tint; }
        }
        static readonly AppDefinition[] Apps={
            new AppDefinition("JobDesk","İşler",Mint),new AppDefinition("FleetManager","Araç",Mint),
            new AppDefinition("StockManager","Depo",Peach),new AppDefinition("EquipmentShop","Mağaza",Red),
            new AppDefinition("CompanyBank","Finans",Mint),new AppDefinition("CompanyStats","Şirket",Mint),new AppDefinition("MailBox","Posta",Mint)
        };
        GameObject Obj(string name,Transform parent)
        { var go=new GameObject(name,typeof(RectTransform)); go.transform.SetParent(parent,false); return go; }
        RectTransform Rect(GameObject go,Vector2 min,Vector2 max,Vector2 low,Vector2 high)
        { var rt=go.GetComponent<RectTransform>(); rt.anchorMin=min; rt.anchorMax=max; rt.offsetMin=low; rt.offsetMax=high; return rt; }
        void Fill(GameObject go,float pad=0) => Rect(go,Vector2.zero,Vector2.one,Vector2.one*pad,Vector2.one*-pad);
        void Place(GameObject go,float x,float y,float w,float h)
        {
            var rt=Rect(go,new Vector2(0,1),new Vector2(0,1),Vector2.zero,Vector2.zero);
            rt.pivot=new Vector2(0,1); rt.anchoredPosition=new Vector2(x,-y); rt.sizeDelta=new Vector2(w,h);
        }
        GameObject Block(string name,Transform parent,Color color,bool rounded=false)
        { var go=Obj(name,parent); go.AddComponent<Image>().color=color; return go; }
        void Border(GameObject go) => Bevel(go);
        // Existing edge callers now share flat rules; no embossed highlights.
        void Bevel(GameObject go,bool inset=false)
        {
            foreach(var edge in new[]{"Top","Left","Bottom","Right"})
            {
                var line=Block("Edge_"+edge,go.transform,Rule); line.GetComponent<Image>().raycastTarget=false;
                if(edge=="Top") Rect(line,new Vector2(0,1),Vector2.one,new Vector2(0,-1),Vector2.zero);
                if(edge=="Bottom") Rect(line,Vector2.zero,new Vector2(1,0),Vector2.zero,new Vector2(0,1));
                if(edge=="Left") Rect(line,Vector2.zero,new Vector2(0,1),Vector2.zero,new Vector2(1,0));
                if(edge=="Right") Rect(line,new Vector2(1,0),Vector2.one,new Vector2(-1,0),Vector2.zero);
            }
        }
        Text Label(string name,Transform parent,string value,int size,Color color,TextAnchor align=TextAnchor.MiddleLeft)
        {
            var go=Obj(name,parent); var t=go.AddComponent<Text>(); t.font=font; t.text=value; t.fontSize=size; t.color=color; t.alignment=align;
            t.raycastTarget=false; t.horizontalOverflow=HorizontalWrapMode.Wrap; t.verticalOverflow=VerticalWrapMode.Truncate; t.lineSpacing=1.08f; return t;
        }
        DesktopGlyph Icon(Transform parent,string kind,Color color)
        { var go=Obj("Icon_"+kind,parent); var icon=go.AddComponent<DesktopGlyph>(); icon.kind=kind; icon.color=color; icon.raycastTarget=false; return icon; }
        Button Bind(GameObject go,Action action,bool frame=true)
        {
            if(frame) Border(go); var b=go.AddComponent<Button>(); b.targetGraphic=go.GetComponent<Image>();
            var c=b.colors; c.normalColor=Color.white; c.highlightedColor=new Color(.93f,.94f,.94f); c.selectedColor=c.highlightedColor;
            c.pressedColor=new Color(.82f,.86f,.88f); c.disabledColor=new Color(.8f,.8f,.78f,.55f); c.fadeDuration=.12f; b.colors=c;
            b.onClick.AddListener(()=>{Click(); action();}); var n=b.navigation; n.mode=Navigation.Mode.None; b.navigation=n; return b;
        }
        Button SmallButton(string name,Transform parent,string kind,string caption,Action action,Color? tint=null)
        {
            var go=Block(name,parent,tint??Slate); var button=Bind(go,action,false); var icon=Icon(go.transform,kind,Paper);
            if(string.IsNullOrEmpty(caption)) Fill(icon.gameObject,11);
            else
            {
                Rect(icon.gameObject,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(12,-10),new Vector2(32,10));
                var label=Label("Caption",go.transform,caption,18,Paper); Rect(label.gameObject,Vector2.zero,Vector2.one,new Vector2(42,0),new Vector2(-8,0));
            }
            return button;
        }
        void Layout(Transform t,float spacing=10)
        {
            var l=t.gameObject.AddComponent<VerticalLayoutGroup>(); l.spacing=spacing; l.padding=new RectOffset(28,28,26,28);
            l.childControlHeight=true; l.childControlWidth=true; l.childForceExpandHeight=false; l.childForceExpandWidth=true;
            t.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        }
        void Height(GameObject go,float height)
        { var e=go.GetComponent<LayoutElement>()??go.AddComponent<LayoutElement>(); e.minHeight=height; e.preferredHeight=height; }
        void Row(string value,int size=20,Color? color=null,float height=48)
        { var t=Label("Row",mainContent,value,Mathf.Max(size,17),color??Paper); Height(t.gameObject,height); if(size>=22) t.font=headingFont; }
        void SectionHeading(string title,string detail=null)
        { Row(title,28,Paper,43); if(!string.IsNullOrEmpty(detail)) Row(detail,18,Soft,36); }
        void Divider()
        { var line=Block("SectionRule",mainContent,Rule); Height(line,1); line.GetComponent<Image>().raycastTarget=false; }
        Button ActionButton(string caption,Action click,Color? color=null)
        {
            var go=Block("Action_"+caption,mainContent,color??Mint); Height(go,44); var b=Bind(go,click,false);
            var t=Label("Caption",go.transform,caption,18,Color.white,TextAnchor.MiddleCenter); t.font=headingFont; Fill(t.gameObject,4); return b;
        }
        void Clear()
        { for(int i=mainContent.childCount-1;i>=0;i--) { var child=mainContent.GetChild(i).gameObject; child.SetActive(false); Destroy(child); } }
        void BuildCanvas()
        {
            var root=Obj("Company_UI",transform); canvas=root.AddComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            var s=root.AddComponent<CanvasScaler>(); s.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize; s.referenceResolution=new Vector2(1600,900); s.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            root.AddComponent<GraphicRaycaster>(); BuildGameHud(root.transform);
            BuildPerformanceOverlay(root.transform);
            computer=Block("OfficeComputerWindow",root.transform,Navy); Fill(computer);
            var workspace=Block("DesktopWorkArea",computer.transform,Navy); Fill(workspace);
            var af=workspace.AddComponent<AspectRatioFitter>(); af.aspectMode=AspectRatioFitter.AspectMode.FitInParent; af.aspectRatio=16f/9;
            workspace.AddComponent<RectMask2D>(); BuildDesktop(workspace.transform); BuildAppWindow(workspace.transform); BuildTaskbar(workspace.transform);
            computer.SetActive(false); BuildPauseMenu(root.transform); BuildNotification(root.transform);
        }
        void BuildDesktop(Transform parent)
        {
            desktop=Obj("Desktop",parent); Fill(desktop); var grid=Obj("DesktopApplications",desktop.transform); Place(grid,14,24,180,414);
            var l=grid.AddComponent<GridLayoutGroup>(); l.cellSize=new Vector2(82,88); l.spacing=new Vector2(6,18); l.startAxis=GridLayoutGroup.Axis.Horizontal;
            l.constraint=GridLayoutGroup.Constraint.FixedColumnCount; l.constraintCount=2;
            foreach(var definition in Apps)
            {
                var def=definition; var tile=Block("App_"+def.id,grid.transform,Color.white); var b=Bind(tile,()=>Show(def.id),false);
                var c=b.colors; c.normalColor=Color.clear; c.highlightedColor=new Color(1,1,1,.08f); c.selectedColor=c.highlightedColor; c.pressedColor=new Color(1,1,1,.16f); b.colors=c;
                Place(Icon(tile.transform,def.id,Surface).gameObject,21,6,40,40);
                Place(Label("AppName",tile.transform,def.title,18,Surface,TextAnchor.UpperCenter).gameObject,0,55,82,28);
                if(def.id=="MailBox") { mailBadge=Label("MailBadge",tile.transform,"",16,Surface,TextAnchor.UpperRight); Place(mailBadge.gameObject,60,0,15,20); }
            }
        }
        void BuildAppWindow(Transform parent)
        {
            appWindow=Block("ApplicationWindow",parent,Slate); windowRect=Rect(appWindow,new Vector2(.131f,.063f),new Vector2(.981f,.96f),Vector2.zero,Vector2.zero);
            var shadow=appWindow.AddComponent<Shadow>(); shadow.effectColor=new Color(0,0,0,.22f); shadow.effectDistance=new Vector2(0,-4); Border(appWindow);
            var header=Block("ApplicationHeader",appWindow.transform,Slate); Rect(header,new Vector2(0,1),Vector2.one,new Vector2(1,-52),new Vector2(-1,-1));
            var drag=header.AddComponent<DesktopWindowDrag>(); drag.window=windowRect; drag.canDrag=()=>!maximized; drag.doubleClick=ToggleMaximize;
            appIcon=Icon(header.transform,"JobDesk",Paper); Place(appIcon.gameObject,18,14,24,24);
            titleText=Label("WindowTitle",header.transform,"",21,Paper); titleText.font=headingFont; Rect(titleText.gameObject,Vector2.zero,Vector2.one,new Vector2(53,0),new Vector2(-145,0));
            var b=SmallButton("MinimizeApplication",header.transform,"Minimize","",()=>Show("Desktop")); Rect(b.gameObject,new Vector2(1,0),Vector2.one,new Vector2(-133,3),new Vector2(-91,-3));
            b=SmallButton("MaximizeApplication",header.transform,"Maximize","",ToggleMaximize); Rect(b.gameObject,new Vector2(1,0),Vector2.one,new Vector2(-89,3),new Vector2(-47,-3));
            b=SmallButton("CloseApplication",header.transform,"Close","",CloseApplicationWindow); Rect(b.gameObject,new Vector2(1,0),Vector2.one,new Vector2(-45,3),new Vector2(-3,-3));
            var nav=Block("AppNavigation",appWindow.transform,Surface); navigationPanel=Rect(nav,new Vector2(0,1),Vector2.one,new Vector2(1,-112),new Vector2(-1,-52));
            var scroll=Block("AppScroll",appWindow.transform,Surface); Rect(scroll,Vector2.zero,Vector2.one,new Vector2(1,1),new Vector2(-1,-112)); scroll.AddComponent<RectMask2D>();
            appScroll=scroll.AddComponent<ScrollRect>(); appScroll.horizontal=false; appScroll.movementType=ScrollRect.MovementType.Clamped; appScroll.scrollSensitivity=38;
            var content=Obj("AppContent",scroll.transform); mainContent=Rect(content,new Vector2(0,1),Vector2.one,Vector2.zero,Vector2.zero); mainContent.pivot=new Vector2(.5f,1);
            Layout(mainContent); appScroll.content=mainContent; appScroll.viewport=scroll.GetComponent<RectTransform>(); AddScrollbar(appScroll,scroll.transform); appWindow.SetActive(false);
        }
        void AddScrollbar(ScrollRect scroll,Transform parent)
        {
            var track=Block("ScrollTrack",parent,Color.clear); Rect(track,new Vector2(1,0),Vector2.one,new Vector2(-7,8),new Vector2(-2,-8));
            var handle=Block("ScrollHandle",track.transform,Rule); Fill(handle); var bar=track.AddComponent<Scrollbar>(); bar.direction=Scrollbar.Direction.BottomToTop;
            bar.handleRect=handle.GetComponent<RectTransform>(); bar.targetGraphic=handle.GetComponent<Image>(); scroll.verticalScrollbar=bar; scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
        }
        void RenderNavigation(params (string id,string title)[] entries)
        {
            foreach(Transform child in navigationPanel) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            float x=20;
            foreach(var entry in entries)
            {
                var item=entry; bool selected=appSection==entry.id; float width=Mathf.Clamp(entry.title.Length*11+42,132,240);
                var go=Block("Section_"+entry.id,navigationPanel,Surface); Place(go,x,4,width,52);
                Bind(go,()=>{appSection=item.id; Show(app);},false); var t=Label("Caption",go.transform,entry.title,21,selected?Mint:Soft,TextAnchor.MiddleCenter); Fill(t.gameObject,4);
                if(selected)
                {
                    t.font=headingFont; var underline=Block("SelectedTab",go.transform,Mint);
                    Rect(underline,Vector2.zero,new Vector2(1,0),new Vector2(12,0),new Vector2(-12,3)); underline.GetComponent<Image>().raycastTarget=false;
                }
                x+=width+8;
            }
        }
        void ToggleMaximize()
        {
            if(!maximized)
            { normalAnchorMin=windowRect.anchorMin; normalAnchorMax=windowRect.anchorMax; normalOffsetMin=windowRect.offsetMin; normalOffsetMax=windowRect.offsetMax;
                Rect(appWindow,new Vector2(.0075f,.065f),new Vector2(.9925f,.986f),Vector2.zero,Vector2.zero); }
            else { windowRect.anchorMin=normalAnchorMin; windowRect.anchorMax=normalAnchorMax; windowRect.offsetMin=normalOffsetMin; windowRect.offsetMax=normalOffsetMax; }
            maximized=!maximized; Show(app);
        }
        void CloseApplicationWindow() { lastWindowApp=null; Show("Desktop"); }
        void ToggleTaskWindow()
        { if(string.IsNullOrEmpty(lastWindowApp)) return; if(app==lastWindowApp) Show("Desktop"); else { float p=lastWindowScroll; Show(lastWindowApp); appScroll.verticalNormalizedPosition=p; } }
        void UpdateTaskWindow()
        {
            if(!taskWindowButton) return; var d=Apps.FirstOrDefault(x=>x.id==lastWindowApp); taskWindowButton.gameObject.SetActive(d!=null); if(d==null) return;
            taskWindowText.text=d.title; taskWindowIcon.kind=d.id; taskWindowButton.GetComponent<Image>().color=app==lastWindowApp?Selection:Slate;
        }
        void BuildTaskbar(Transform parent)
        {
            var dock=Block("Taskbar",parent,Slate); Rect(dock,Vector2.zero,new Vector2(1,0),Vector2.zero,new Vector2(0,44));
            var rule=Block("TaskbarRule",dock.transform,Rule); Rect(rule,new Vector2(0,1),Vector2.one,new Vector2(0,-1),Vector2.zero); rule.GetComponent<Image>().raycastTarget=false;
            Place(SmallButton("DesktopHome",dock.transform,"Home","Masaüstü",()=>Show("Desktop")).gameObject,4,3,138,38);
            Place(SmallButton("SaveCompany",dock.transform,"Save","Kaydet",()=>{if(Runtime.Save()) Toast("Oyun kaydedildi.");}).gameObject,145,3,118,38);
            Place(SmallButton("ExitComputer",dock.transform,"Power","Çıkış",Close).gameObject,266,3,108,38);
            taskWindowButton=SmallButton("TaskApplication",dock.transform,"JobDesk","İşler",ToggleTaskWindow); Place(taskWindowButton.gameObject,384,3,196,38);
            taskWindowText=taskWindowButton.GetComponentInChildren<Text>(); taskWindowIcon=taskWindowButton.GetComponentInChildren<DesktopGlyph>(); taskWindowButton.gameObject.SetActive(false);
            clockText=Label("Clock",dock.transform,"",16,Soft,TextAnchor.MiddleRight); Rect(clockText.gameObject,new Vector2(1,0),Vector2.one,new Vector2(-240,0),new Vector2(-18,0));
        }
        void RefreshDesktop() { mailBadge.text=Runtime.Data.inspectionPending?"•":""; }
        void StatStrip(params (string label,string value)[] stats)
        {
            var row=Obj("StatusLine",mainContent); Height(row,46); var l=row.AddComponent<HorizontalLayoutGroup>(); l.spacing=28; l.childForceExpandWidth=true;
            foreach(var stat in stats) { var t=Label("Status",row.transform,stat.label+"   "+stat.value,18,Soft); var e=t.gameObject.AddComponent<LayoutElement>(); e.flexibleWidth=1; e.minWidth=0; e.preferredWidth=0; }
        }
        void JobColumns(out RectTransform list,out RectTransform detail)
        {
            var row=Obj("JobWorkspace",mainContent); Height(row,appScroll.viewport.rect.height); var h=row.AddComponent<HorizontalLayoutGroup>(); h.spacing=0;
            h.childControlWidth=true; h.childControlHeight=true; h.childForceExpandWidth=false; h.childForceExpandHeight=true;
            var left=Block("JobList",row.transform,Surface); var right=Block("JobDetail",row.transform,Surface);
            var ls=left.AddComponent<LayoutElement>(); ls.preferredWidth=350; ls.minWidth=300; ls.flexibleWidth=0;
            var rs=right.AddComponent<LayoutElement>(); rs.minWidth=0; rs.preferredWidth=0; rs.flexibleWidth=1;
            list=ScrollColumn("JobListScroll",left.transform,0,16); list.GetComponent<VerticalLayoutGroup>().spacing=0;
            detail=ScrollColumn("JobDetailScroll",right.transform,160,32);
            var edge=Block("DocumentEdge",right.transform,Rule); Rect(edge,Vector2.zero,new Vector2(0,1),Vector2.zero,new Vector2(1,0)); edge.GetComponent<Image>().raycastTarget=false;
            var footer=Obj("JobActions",right.transform); jobFooter=Rect(footer,Vector2.zero,new Vector2(1,0),new Vector2(32,18),new Vector2(-32,155));
            Canvas.ForceUpdateCanvases();
            detail.GetComponent<VerticalLayoutGroup>().spacing=2;
            detail.GetComponent<VerticalLayoutGroup>().padding=new RectOffset(32,32,24,0);
        }
        RectTransform ScrollColumn(string name,Transform parent,float bottom,int padding)
        {
            var viewport=Block(name,parent,Color.clear); Rect(viewport,Vector2.zero,Vector2.one,new Vector2(0,bottom),Vector2.zero); viewport.AddComponent<RectMask2D>();
            var scroll=viewport.AddComponent<ScrollRect>(); scroll.horizontal=false; scroll.movementType=ScrollRect.MovementType.Clamped; scroll.scrollSensitivity=32;
            var content=Obj("Content",viewport.transform); var r=Rect(content,new Vector2(0,1),Vector2.one,Vector2.zero,Vector2.zero); r.pivot=new Vector2(.5f,1); Layout(r,8);
            r.GetComponent<VerticalLayoutGroup>().padding=new RectOffset(padding,padding,padding,padding); scroll.content=r; scroll.viewport=viewport.GetComponent<RectTransform>(); AddScrollbar(scroll,viewport.transform); return r;
        }
        void JobCard(JobDefinition job)
        {
            bool locked=Runtime.Data.reputation<job.minimumReputation,selected=selectedJob==job.id;
            var card=Block("Job_"+job.id,mainContent,selected?Mint:Surface); Height(card,84); Bind(card,()=>{selectedJob=job.id; selectedContract=false; Show("JobDesk");},false);
            var t=Label("JobTitle",card.transform,job.title,20,selected?Surface:Paper); t.font=headingFont; Rect(t.gameObject,new Vector2(0,1),Vector2.one,new Vector2(14,-43),new Vector2(-14,-8));
            var meta=Label("JobMeta",card.transform,locked?job.minimumReputation+" itibar gerekli":job.client,17,selected?Surface:locked?Peach:Soft);
            Rect(meta.gameObject,Vector2.zero,new Vector2(1,0),new Vector2(14,11),new Vector2(-90,38));
            if(!locked) { var fee=Label("JobPayment",card.transform,Money(job.payment),18,selected?Surface:Paper,TextAnchor.MiddleRight); Rect(fee.gameObject,new Vector2(1,0),new Vector2(1,0),new Vector2(-90,11),new Vector2(-14,38)); }
            var line=Block("JobRule",card.transform,Rule); Rect(line,Vector2.zero,new Vector2(1,0),Vector2.zero,new Vector2(0,1)); line.GetComponent<Image>().raycastTarget=false;
        }
    }
}
