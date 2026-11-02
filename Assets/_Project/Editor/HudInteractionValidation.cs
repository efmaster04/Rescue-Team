using System;
using System.Collections;
using System.IO;
using System.Linq;
using CompanySim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace CompanySimEditor
{
    public static class HudInteractionValidation
    {
        public static bool Finished { get; private set; }
        public static Exception Error { get; private set; }
        static void Check(bool value,string message) { if(!value) throw new Exception("HUD and pause: "+message); }
        public static void Begin(PlayerInteractor player)
        {
            Finished=false; Error=null; player.StartCoroutine(Drive(Run(player)));
        }
        static IEnumerator Drive(IEnumerator routine)
        {
            while(true)
            {
                bool more=false; object next=null;
                try { more=routine.MoveNext(); if(more) next=routine.Current; }
                catch(Exception error) { Error=error; }
                if(!more || Error!=null) break;
                yield return next;
            }
            Finished=true;
        }
        static Transform Find(ComputerUI ui,string name)=>ui.GetComponentsInChildren<Transform>(true).Where(x=>x.name==name)
            .OrderByDescending(x=>x.gameObject.activeInHierarchy).First();
        static string Text(ComputerUI ui,string name)=>Find(ui,name).GetComponent<Text>().text;
        static void KeyState(Keyboard keyboard,params Key[] keys)=>InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
        static void Teleport(PlayerInteractor player,Vector3 position,Quaternion rotation)
        {
            var controller=player.GetComponent<CharacterController>(); controller.enabled=false;
            player.transform.SetPositionAndRotation(position,rotation); controller.enabled=true; Physics.SyncTransforms();
        }
        static void Slide(Slider slider,float fraction)
        {
            var rect=slider.GetComponent<RectTransform>();
            var screen=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(new Vector3(Mathf.Lerp(rect.rect.xMin,rect.rect.xMax,fraction),0,0)));
            var pointer=new PointerEventData(EventSystem.current){position=screen,pressPosition=screen,button=PointerEventData.InputButton.Left};
            var hits=new System.Collections.Generic.List<RaycastResult>(); EventSystem.current.RaycastAll(pointer,hits);
            Check(hits.Count>0 && (hits[0].gameObject.transform==slider.transform || hits[0].gameObject.transform.IsChildOf(slider.transform)),"slider is reachable through the pause sheet");
            pointer.pointerPressRaycast=hits[0]; pointer.pointerCurrentRaycast=hits[0];
            ExecuteEvents.Execute(slider.gameObject,pointer,ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(slider.gameObject,pointer,ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.Execute(slider.gameObject,pointer,ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(slider.gameObject,pointer,ExecuteEvents.pointerUpHandler);
        }
        static IEnumerator Run(PlayerInteractor player)
        {
            var ui=player.ui; var runtime=ui.Runtime; var vehicle=runtime.vehicle;
            var position=player.transform.position; var rotation=player.transform.rotation;
            var cameraLocal=player.viewCamera.transform.localRotation;
            var keyboard=InputSystem.AddDevice<Keyboard>("HudRegressionKeyboard"); var mouse=InputSystem.AddDevice<Mouse>("HudRegressionMouse");
            var background=InputSystem.settings.backgroundBehavior; var editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
            float oldVolume=AudioListener.volume,oldSensitivity=player.lookSensitivity,oldScale=Time.timeScale;
            bool oldSway=player.carrySway,oldAudioPause=AudioListener.pause;
            string[] keys={ComputerUI.VolumePreference,ComputerUI.SensitivityPreference,ComputerUI.SwayPreference};
            bool[] had=keys.Select(PlayerPrefs.HasKey).ToArray();
            float[] values={PlayerPrefs.GetFloat(keys[0]),PlayerPrefs.GetFloat(keys[1]),PlayerPrefs.GetInt(keys[2])};
            int balance=runtime.Data.balance; string oldJob=runtime.Data.activeJobId; var oldPhase=runtime.Data.phase;
            string oldModule=vehicle.InstalledModuleId;
            var items=UnityEngine.Object.FindObjectsByType<CarryableItem>().Where(x=>x.definitionId=="toolbox" || x.definitionId=="jack").ToArray();
            var itemPositions=items.Select(x=>x.transform.position).ToArray(); var itemRotations=items.Select(x=>x.transform.rotation).ToArray();
            var condition=items.Select(x=>x.durability).ToArray();
            try
            {
                InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                keyboard.MakeCurrent(); mouse.MakeCurrent(); InputSystem.EnableDevice(keyboard); InputSystem.EnableDevice(mouse);
                Teleport(player,new Vector3(7.45f,.01f,-4.23f),Quaternion.Euler(0,325,0));
                player.viewCamera.transform.LookAt(new Vector3(4.65f,1.35f,.1f)); player.SetCursor(false);
                Check(runtime.AcceptJob("recovery"),"test job accepted through normal runtime"); ui.TogglePreparation();
                yield return null;
                Check(Text(ui,"PreparationState_0")=="Gerekli" && Text(ui,"PreparationState_1")=="Eksik" && Text(ui,"PreparationState_2")=="Eksik","missing module and both real cargo items are separate rows");
                ComputerDesktopValidation.Capture(ui,"Documentation/HudPreparationMissing.png",1600,900);

                KeyState(keyboard,Key.Escape); yield return new WaitForSecondsRealtime(.08f); KeyState(keyboard);
                Check(ui.IsPaused && player.InputLocked && Time.timeScale==0 && AudioListener.pause,"real Escape freezes gameplay and exposes the cursor");
                float stoppedTime=Time.time; var stoppedPosition=player.transform.position;
                KeyState(keyboard,Key.W); yield return new WaitForSecondsRealtime(.25f); KeyState(keyboard);
                Check(Time.time==stoppedTime && Vector3.Distance(stoppedPosition,player.transform.position)<.001f,"W cannot move the player while paused");
                Canvas.ForceUpdateCanvases();
                var volume=Find(ui,"MasterVolume").GetComponentInChildren<Slider>(); Slide(volume,.34f);
                Check(AudioListener.volume>.30f && AudioListener.volume<.39f && Mathf.Abs(PlayerPrefs.GetFloat(keys[0])-AudioListener.volume)<.001f,"pointer slider changes master volume and preference");
                var sensitivity=Find(ui,"LookSensitivity").GetComponentInChildren<Slider>(); sensitivity.value=.17f;
                Check(Mathf.Abs(player.lookSensitivity-.17f)<.001f && Mathf.Abs(PlayerPrefs.GetFloat(keys[1])-.17f)<.001f,"mouse setting changes the real controller");
                var toggle=Find(ui,"EquipmentSway").GetComponent<Toggle>(); toggle.isOn=!oldSway;
                Check(player.carrySway!=oldSway && PlayerPrefs.GetInt(keys[2])==(player.carrySway?1:0),"equipment sway uses the persisted player option");
                runtime.Data.balance=balance+7;
                ComputerDesktopValidation.Click(ui,"PauseSave");
                var saved=JsonUtility.FromJson<CompanySave>(File.ReadAllText(Path.Combine(Application.persistentDataPath,"office_garage_v1.json")));
                Check(saved.balance==balance+7 && saved.activeJobId=="recovery","pause save writes the actual current company state");
                ComputerDesktopValidation.Capture(ui,"Documentation/PauseMenuPreview.png",1600,900);
                ComputerDesktopValidation.Capture(ui,"Documentation/PauseMenuNarrowPreview.png",1024,768);
                KeyState(keyboard,Key.Escape); yield return new WaitForSecondsRealtime(.08f); KeyState(keyboard);
                Check(!ui.IsPaused && !player.InputLocked && Time.timeScale==oldScale && AudioListener.pause==oldAudioPause,"Escape resumes gameplay and audio");
                var before=player.transform.position;
                KeyState(keyboard,Key.W); yield return new WaitForSecondsRealtime(.20f); KeyState(keyboard); yield return new WaitForSecondsRealtime(.15f);
                Check(Vector3.Distance(before,player.transform.position)>.20f,"movement resumes after closing the menu");
                Teleport(player,stoppedPosition,Quaternion.Euler(0,325,0)); player.SetCursor(true);
                player.viewCamera.transform.LookAt(new Vector3(4.65f,1.35f,.1f));
                ui.SetPrompt(""); ui.SetFocus(false);
                vehicle.SetModuleImmediate("towing"); runtime.WorldChanged();
                foreach(var item in items) { player.Hold(item); Check(vehicle.TryLoad(player),"load real "+item.definitionId); }
                Check(Text(ui,"PreparationState_0")=="Takılı" && Text(ui,"PreparationState_1")=="Yüklü" && Text(ui,"PreparationState_2")=="Yüklü" && Text(ui,"PreparationSummary")=="Çıkışa hazır",
                    "checklist updates from actual module and vehicle cargo: "+Text(ui,"PreparationState_0")+" / "+Text(ui,"PreparationState_1")+" / "+Text(ui,"PreparationState_2")+" / "+Text(ui,"PreparationSummary"));
                ComputerDesktopValidation.Capture(ui,"Documentation/HudPreparationReady.png",1600,900);
                var toolbox=items.First(x=>x.definitionId=="toolbox"); toolbox.durability=10; runtime.WorldChanged();
                Check(Text(ui,"PreparationState_1")=="Bakım gerekli" && Text(ui,"PreparationSummary")!="Çıkışa hazır","loaded broken gear cannot appear ready");
                player.Hold(toolbox); yield return new WaitForSecondsRealtime(.18f);
                Check(Text(ui,"CarriedEquipmentName")=="Alet çantası" && Text(ui,"CarriedEquipmentCondition")=="Durum %10","held equipment shows real condition");
                ComputerDesktopValidation.Capture(ui,"Documentation/HudCarriedCondition.png",1600,900);
                ui.Toast("Alet çantası için bakım gerekiyor. Tezgâhta tamir ettikten sonra ekipmanı araca yükleyip hazırlık listesini yeniden kontrol et.");
                yield return new WaitForSecondsRealtime(.20f); Canvas.ForceUpdateCanvases();
                var notice=Find(ui,"NotificationText").GetComponent<Text>();
                Check(notice.preferredHeight<=notice.rectTransform.rect.height+1,"multi-line notice fits its visible area");
                ComputerDesktopValidation.Capture(ui,"Documentation/HudNotificationPreview.png",1600,900,true);
                player.TakeHeld(); toolbox.Drop(itemPositions[Array.IndexOf(items,toolbox)]);
                ui.Open("Desktop"); ui.SetPaused(true);
                Check(!ui.IsPaused && Time.timeScale==oldScale,"computer input does not open a second pause overlay"); ui.Close();
                ui.SetPaused(true); ui.enabled=false;
                Check(!ui.IsPaused && Time.timeScale==oldScale,"disabling the UI releases its pause state"); ui.enabled=true;
                File.AppendAllText("Logs/office_garage_validation.txt","PASS: compact HUD reflects actual module/cargo/maintenance and held condition; real Escape pauses/resumes time, input and audio, W stays still while paused; pointer volume slider, saved mouse/sway options, pause save readback, computer exclusion and pause cleanup\n");
            }
            finally
            {
                if(ui.IsPaused) ui.SetPaused(false); if(ui.IsOpen) ui.Close(); ui.enabled=true;
                if(player.HeldItem) player.TakeHeld();
                for(int i=0;i<items.Length;i++)
                {
                    items[i].Drop(itemPositions[i]); items[i].transform.rotation=itemRotations[i]; items[i].durability=condition[i];
                    var body=items[i].GetComponent<Rigidbody>(); body.linearVelocity=Vector3.zero; body.angularVelocity=Vector3.zero;
                }
                runtime.Data.balance=balance; runtime.Data.activeJobId=oldJob; runtime.Data.phase=oldPhase; vehicle.SetModuleImmediate(oldModule); runtime.WorldChanged();
                if(Find(ui,"PreparationBoard").gameObject.activeSelf) ui.TogglePreparation();
                for(int i=0;i<keys.Length;i++)
                    if(!had[i]) PlayerPrefs.DeleteKey(keys[i]); else if(i==2) PlayerPrefs.SetInt(keys[i],(int)values[i]); else PlayerPrefs.SetFloat(keys[i],values[i]);
                PlayerPrefs.Save(); AudioListener.volume=oldVolume; AudioListener.pause=oldAudioPause; Time.timeScale=oldScale;
                player.lookSensitivity=oldSensitivity; player.carrySway=oldSway;
                Teleport(player,position,rotation); player.viewCamera.transform.localRotation=cameraLocal; player.SetCursor(false);
                InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse);
                InputSystem.settings.backgroundBehavior=background; InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;
            }
        }
    }
}
