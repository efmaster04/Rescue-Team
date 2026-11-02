using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanySim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

namespace CompanySimEditor
{
    public static class ComputerDesktopValidation
    {
        static void Require(bool value,string message)
        { if(!value) throw new Exception("Computer desktop: "+message); }
        public static bool Finished { get; private set; }
        public static Exception Error { get; private set; }
        public static void Begin(ComputerUI ui,PlayerInteractor player)
        {
            Finished=false; Error=null; ui.StartCoroutine(Drive(CheckRoutine(ui,player)));
        }
        static IEnumerator Drive(IEnumerator routine)
        {
            while(true)
            {
                object next=null; bool more=false;
                try { more=routine.MoveNext(); if(more) next=routine.Current; }
                catch(Exception error) { Error=error; }
                if(!more || Error!=null) break;
                yield return next;
            }
            Finished=true;
        }
        static IEnumerator CheckRoutine(ComputerUI ui,PlayerInteractor player)
        {
            var cc=player.GetComponent<CharacterController>();
            Require(Mathf.Abs(cc.height-PlayerInteractor.BodyHeight)<.01f && Mathf.Abs(cc.radius-PlayerInteractor.BodyRadius)<.01f,"human-scale capsule");
            Require(Mathf.Abs(player.viewCamera.transform.localPosition.y-PlayerInteractor.EyeHeight)<.01f &&
                Mathf.Abs(player.viewCamera.fieldOfView-62)<.1f,"eye height and perspective");
            var visual=player.transform.Find("Character_ServiceWorker_Visual");
            var renderers=visual.GetComponentsInChildren<Renderer>(); var bounds=renderers[0].bounds;
            foreach(var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            Require(Mathf.Abs(bounds.size.y-PlayerInteractor.BodyHeight)<.02f,"avatar matches the capsule height");
            var monitor=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r=>r.name.StartsWith("O08")).ToArray();
            Require(monitor.Length>0 && monitor.All(r=>r.sharedMaterials.All(m=>
                m.GetColor("_BaseColor").maxColorComponent<.011f && !m.GetTexture("_BaseMap"))),"physical monitor is black without the old texture");

            ui.Open("Desktop");
            yield return null;
            Canvas.ForceUpdateCanvases();
            var desktop=ui.GetComponentsInChildren<Transform>(true).First(t=>t.name=="DesktopApplications");
            var apps=desktop.GetComponentsInChildren<Button>();
            Require(apps.Length==7,"seven grouped desktop launchers");
            Require(ui.GetComponentsInChildren<DesktopGlyph>().All(x=>x.sprite),"all icons use imported upstream sprites");
            var bodyFont=ui.GetComponentsInChildren<Text>().First().font;
            Require("ÇĞİÖŞÜçğıöşü₺".All(bodyFont.HasCharacter),"Source Sans includes Turkish and lira glyphs");
            Capture(ui,"Documentation/ComputerDesktopPreview.png",1600,900);
            yield return null;
            foreach(var button in apps)
            {
                string target=button.name.Substring(4);
                Click(ui,button.name);
                yield return null;
                Require(ui.IsOpen && player.InputLocked && ui.ActiveApplication==target,"launcher opens "+target);
                Require(ui.GetComponentsInChildren<Transform>().Any(t=>t.name=="ApplicationWindow"),"application window is visible");
                if(target=="JobDesk")
                {
                    Click(ui,"Section_list"); yield return null;
                    Capture(ui,"Documentation/ComputerJobsPreview.png",1600,900);
                    Capture(ui,"Documentation/ComputerJobs720Preview.png",1280,720);
                    Capture(ui,"Documentation/ComputerJobs1080Preview.png",1920,1080);
                    Click(ui,"Job_fire"); yield return null;
                    Require(!ui.GetComponentsInChildren<Button>().First(x=>x.name=="AcceptJob").interactable,"locked jobs explain why acceptance is disabled");
                    Click(ui,"Job_recovery"); yield return null;
                    Click(ui,"Section_active"); yield return null;
                    Require(ui.GetComponentsInChildren<Text>().Any(x=>x.text=="Aktif iş yok."),"active-job section handles empty state");
                    Click(ui,"Section_reports"); yield return null;
                    Require(ui.GetComponentsInChildren<Text>().Any(x=>x.text=="Bekleyen rapor yok."),"reports section handles empty state");
                    Click(ui,"Section_list"); yield return null;
                }
                if(target=="EquipmentShop") Capture(ui,"Documentation/ComputerShopPreview.png",1600,900);
                if(target=="StockManager") Capture(ui,"Documentation/ComputerStockPreview.png",1600,900);
                if(target=="FleetManager") Capture(ui,"Documentation/ComputerFleetPreview.png",1600,900);
                if(target=="MailBox") Capture(ui,"Documentation/ComputerMailPreview.png",1600,900);
                if(target=="CompanyStats")
                {
                    Click(ui,"Section_upgrades"); yield return null;
                    Require(ui.GetComponentsInChildren<Transform>().Any(x=>x.name=="Table_Upgrades"),"company app groups upgrades");
                    Click(ui,"Section_inspection"); yield return null;
                    Require(ui.GetComponentsInChildren<Transform>().Any(x=>x.name=="Table_Inspection"),"company app groups inspection");
                }
                yield return null;
                Click(ui,"CloseApplication");
                yield return null;
                Require(ui.ActiveApplication=="Desktop" && player.InputLocked,"closing app returns to desktop");
            }
            ui.Open("EquipmentShop");
            yield return null;
            var titlebar=ui.GetComponentInChildren<DesktopWindowDrag>(); var window=titlebar.window;
            var originalPosition=window.anchoredPosition;
            DragTitlebar(titlebar,new Vector2(45,-25));
            yield return null;
            Require(Vector2.Distance(originalPosition,window.anchoredPosition)>20,"titlebar drag moves the application");
            var draggedPosition=window.anchoredPosition; var normalSize=window.rect.size;
            DoubleClickTitlebar(titlebar);
            yield return null;
            Require(window.rect.width>normalSize.x,"double-clicking titlebar maximizes");
            DoubleClickTitlebar(titlebar);
            yield return null;
            Require(Vector2.Distance(draggedPosition,window.anchoredPosition)<.5f && Vector2.Distance(normalSize,window.rect.size)<.5f,
                "restore keeps the user's dragged position");
            DragTitlebar(titlebar,new Vector2(-5000,5000));
            yield return null;
            var corners=new Vector3[4]; window.GetWorldCorners(corners); var area=(RectTransform)window.parent;
            var bottom=area.InverseTransformPoint(corners[0]); var top=area.InverseTransformPoint(corners[2]);
            Require(bottom.x>=area.rect.xMin+3 && top.y<=area.rect.yMax-3 && bottom.y>=area.rect.yMin+41,
                "dragging cannot lose the titlebar or cover taskbar controls");
            Capture(ui,"Documentation/ComputerDraggedWindow.png",1600,900);
            window.anchoredPosition=originalPosition;
            Click(ui,"MaximizeApplication");
            yield return null;
            Capture(ui,"Documentation/ComputerShopMaximizedPreview.png",1280,720);
            yield return null;
            Click(ui,"MaximizeApplication");
            yield return null;
            Click(ui,"MinimizeApplication");
            yield return null;
            Require(ui.ActiveApplication=="Desktop","minimize returns to desktop");
            Capture(ui,"Documentation/ComputerDesktopNarrowPreview.png",1024,768);
            yield return null;
            Click(ui,"TaskApplication");
            yield return null;
            Require(ui.ActiveApplication=="EquipmentShop","taskbar restores the minimized application");
            Click(ui,"TaskApplication");
            yield return null;
            Require(ui.ActiveApplication=="Desktop","taskbar can minimize the current application");
            Click(ui,"TaskApplication");
            yield return null;
            Click(ui,"CloseApplication");
            yield return null;
            Require(!ui.GetComponentsInChildren<Button>().Any(x=>x.name=="TaskApplication"),"closing removes the taskbar window button");
            Click(ui,"SaveCompany");
            yield return null;
            Click(ui,"ExitComputer");
            yield return null;
            Require(!ui.IsOpen && !player.InputLocked,"power button restores player control");
            CaptureWorld(player);
            ui.Open("JobDesk");
            yield return null;
            Click(ui,"Job_recovery");
            yield return null;
            var scroll=ui.GetComponentInChildren<ScrollRect>(); scroll.verticalNormalizedPosition=0;
            yield return null;
            var contract=ui.GetComponentsInChildren<Toggle>().First(x=>x.name=="JobContract");
            ExecuteEvents.Execute(contract.gameObject,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler);
            Require(contract.isOn,"contract checkbox can be selected");
            Click(ui,"AcceptJob");
            yield return null;
            Require(ui.Runtime.Data.activeJobId=="recovery" && ui.Runtime.Data.contractAccepted,"job acceptance button changes real company state");
            ui.Close();
            File.AppendAllText("Logs/office_garage_validation.txt",
                "PASS: seven grouped desktop launchers, work-order navigation and contract checkbox, clamped dragging, double-click maximize/restore, taskbar minimize/restore, close/save/exit, black monitor and human avatar scale\n");
        }
        static void DragTitlebar(DesktopWindowDrag titlebar,Vector2 delta)
        {
            Canvas.ForceUpdateCanvases(); var rect=titlebar.GetComponent<RectTransform>();
            var pointer=new PointerEventData(EventSystem.current) {button=PointerEventData.InputButton.Left,
                position=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(new Vector3(rect.rect.xMin+rect.rect.width*.3f,rect.rect.center.y,0)))};
            var hits=new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer,hits);
            Require(hits.Count>0 && hits[0].gameObject.transform.IsChildOf(titlebar.transform),"titlebar receives the pointer");
            ExecuteEvents.Execute(titlebar.gameObject,pointer,ExecuteEvents.beginDragHandler);
            pointer.position+=delta; pointer.delta=delta;
            ExecuteEvents.Execute(titlebar.gameObject,pointer,ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(titlebar.gameObject,pointer,ExecuteEvents.endDragHandler);
        }
        static void DoubleClickTitlebar(DesktopWindowDrag titlebar)
        {
            var pointer=new PointerEventData(EventSystem.current) {button=PointerEventData.InputButton.Left,clickCount=2};
            ExecuteEvents.Execute(titlebar.gameObject,pointer,ExecuteEvents.pointerClickHandler);
        }
        public static void Click(ComputerUI ui,string name)
        {
            Canvas.ForceUpdateCanvases();
            var button=ui.GetComponentsInChildren<Button>().FirstOrDefault(b=>b.name==name);
            Require(button && button.interactable,"clickable button "+name);
            var rect=button.GetComponent<RectTransform>();
            var canvas=button.GetComponentInParent<Canvas>();
            var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
            var pointer=new PointerEventData(EventSystem.current) { button=PointerEventData.InputButton.Left,
                position=RectTransformUtility.WorldToScreenPoint(camera,rect.TransformPoint(rect.rect.center)) };
            var hits=new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer,hits);
            Require(hits.Count>0 && hits[0].gameObject.GetComponentInParent<Button>()==button,
                "button is not covered: "+name+" at "+pointer.position+"; hits "+string.Join(",",hits.Take(4).Select(h=>h.gameObject.name)));
            ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler);
        }
        public static void Capture(ComputerUI ui,string path,int width,int height,bool includeNotification=false)
        {
            // Render an isolated canvas so changing capture resolution cannot leave
            // stale camera/overlay clipping state in the live computer UI.
            var liveCanvas=ui.GetComponentInChildren<Canvas>(); var camera=ui.player.viewCamera;
            var previousTarget=camera.targetTexture; var previousRect=camera.rect;
            var cameraData=camera.GetUniversalAdditionalCameraData(); bool post=cameraData.renderPostProcessing;
            var clone=UnityEngine.Object.Instantiate(liveCanvas.gameObject); clone.name="Desktop_CaptureCanvas"; clone.SetActive(false);
            var canvas=clone.GetComponent<Canvas>();
            var target=RenderTexture.GetTemporary(width,height,24); var previousActive=RenderTexture.active;
            Texture2D texture=null;
            try
            {
                camera.targetTexture=target; camera.rect=new Rect(0,0,1,1);
                if(ui.IsOpen) cameraData.renderPostProcessing=false;
                canvas.GetComponent<CanvasScaler>().enabled=false; canvas.scaleFactor=Mathf.Min(width/1600f,height/900f);
                canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=camera.nearClipPlane*1.001f;
                if(!includeNotification) clone.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Notification").gameObject.SetActive(false);
                clone.SetActive(true);
                foreach(var text in canvas.GetComponentsInChildren<Text>())
                    text.font.RequestCharactersInTexture(text.text,Mathf.RoundToInt(text.fontSize*canvas.scaleFactor),text.fontStyle);
                foreach(var text in canvas.GetComponentsInChildren<Text>()) text.SetAllDirty();
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active=target;
                texture=new Texture2D(width,height,TextureFormat.RGB24,false);
                texture.ReadPixels(new Rect(0,0,width,height),0,0); texture.Apply();
                Directory.CreateDirectory("Documentation"); File.WriteAllBytes(path,texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture=previousTarget; camera.rect=previousRect; RenderTexture.active=previousActive;
                cameraData.renderPostProcessing=post;
                clone.SetActive(false); UnityEngine.Object.Destroy(clone);
                if(texture) UnityEngine.Object.Destroy(texture); RenderTexture.ReleaseTemporary(target);
                Canvas.ForceUpdateCanvases();
            }
        }
        static void CaptureWorld(PlayerInteractor player)
        {
            var camera=player.viewCamera; var oldRotation=camera.transform.rotation;
            var canvas=player.ui.GetComponentInChildren<Canvas>();
            try
            {
                camera.transform.LookAt(new Vector3(-2.85f,1.10f,.84f));
                Capture(player.ui,"Documentation/OfficeHumanScalePreview.png",1600,900);
            }
            finally { camera.transform.rotation=oldRotation; }
        }
    }
}
