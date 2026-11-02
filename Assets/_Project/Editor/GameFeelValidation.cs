using System;
using System.Collections;
using System.IO;
using System.Linq;
using CompanySim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CompanySimEditor
{
    public static class GameFeelValidation
    {
        public static bool Finished { get; private set; }
        public static Exception Error { get; private set; }
        static void Check(bool value,string message) { if(!value) throw new Exception("Presentation: "+message); }
        public static void Begin(PlayerInteractor player)
        {
            Finished=false; Error=null; player.StartCoroutine(Drive(Run(player)));
        }
        static IEnumerator Drive(IEnumerator test)
        {
            while(true)
            {
                object next=null; bool more=false;
                try { more=test.MoveNext(); if(more) next=test.Current; }
                catch(Exception error) { Error=error; }
                if(!more || Error!=null) break;
                yield return next;
            }
            Finished=true;
        }
        static void Teleport(PlayerInteractor player,Vector3 position,Quaternion rotation)
        {
            var controller=player.GetComponent<CharacterController>(); controller.enabled=false;
            player.transform.SetPositionAndRotation(position,rotation); controller.enabled=true;
            Physics.SyncTransforms();
        }
        static IEnumerator Run(PlayerInteractor player)
        {
            var data=player.viewCamera.GetUniversalAdditionalCameraData();
            Check(data.renderPostProcessing && data.antialiasing==AntialiasingMode.FastApproximateAntialiasing,"camera grade and lightweight antialiasing");
            var probes=UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None);
            Check(probes.Length>=2 && probes.All(x=>x.mode==ReflectionProbeMode.Custom && x.customBakedTexture),"room reflections are baked, with no per-frame capture");
            Check(!player.ui.audioFeedback.footstep && !player.ui.audioFeedback.doorOpen && !player.ui.audioFeedback.doorClose,"removed sounds remain unassigned");
            var position=player.transform.position; var rotation=player.transform.rotation;
            var view=player.viewCamera.transform.localRotation;
            player.InputLocked=true;
            player.viewCamera.transform.LookAt(new Vector3(-2.85f,1.10f,.84f));
            Check(player.FindTarget() is OfficeComputer,"monitor can be targeted with the interaction cast");
            player.ui.SetFocus(true); player.ui.SetPrompt("E — Şirket bilgisayarını kullan");
            yield return null;
            ComputerDesktopValidation.Capture(player.ui,"Documentation/OfficeLightingPreview.png",1600,900);
            var item=UnityEngine.Object.FindObjectsByType<CarryableItem>(FindObjectsSortMode.None).First(x=>x.definitionId=="toolbox");
            var itemPosition=item.transform.position; var itemRotation=item.transform.rotation;
            player.Hold(item);
            yield return new WaitForSeconds(.18f);
            Check(Vector3.Distance(item.transform.position+item.CenterOffset,player.HoldPoint.position)<.02f,"carry grip centers the actual collider");
            Teleport(player,new Vector3(-4.31f,.11f,-2.7f),Quaternion.Euler(0,270,0));
            player.viewCamera.transform.localRotation=Quaternion.identity;
            yield return new WaitForSecondsRealtime(.25f);
            int carryFrames=60;
            do { yield return null; } while(!item.IsHandOccluded && --carryFrames>0);
            Check(item.IsHandOccluded && item.GetComponentsInChildren<Renderer>().All(x=>x.shadowCastingMode==ShadowCastingMode.ShadowsOnly),
                "retracted gear cannot expose its clipped interior inside the camera; grip="+player.HoldPoint.localPosition+
                ", center="+player.viewCamera.transform.InverseTransformPoint(item.transform.position+item.CenterOffset)+", half="+item.HalfSize);
            ComputerDesktopValidation.Capture(player.ui,"Documentation/EquipmentNearWallSafe.png",1600,900);
            Check(!player.TryGetDropPosition(item,out _),"drop cannot pass through the office wall");
            player.DropHeld(); Check(player.HeldItem==item,"blocked drop keeps the equipment in hand");
            Teleport(player,new Vector3(7.35f,.01f,-4.1f),Quaternion.identity); player.viewCamera.transform.localRotation=Quaternion.identity;
            yield return new WaitForSecondsRealtime(.25f);
            carryFrames=60;
            do { yield return null; } while(item.IsHandOccluded && --carryFrames>0);
            Check(!item.IsHandOccluded && item.GetComponentsInChildren<Renderer>().All(x=>x.shadowCastingMode!=ShadowCastingMode.ShadowsOnly),
                "held gear returns to view when clear of the wall; hidden="+item.IsHandOccluded+", grip="+player.HoldPoint.localPosition+
                ", nearest="+item.NearestCameraDepth+", center="+player.viewCamera.transform.InverseTransformPoint(item.transform.position+item.CenterOffset)+", half="+item.HalfSize+
                ", shadows="+string.Join(",",item.GetComponentsInChildren<Renderer>().Select(x=>x.shadowCastingMode.ToString())));
            player.ui.SetPrompt(""); player.ui.SetFocus(false);
            // Renderer mode changes reach the culling data at the frame boundary.
            yield return null;
            ComputerDesktopValidation.Capture(player.ui,"Documentation/EquipmentCarryPreview.png",1600,900);
            Teleport(player,position,rotation); player.viewCamera.transform.localRotation=Quaternion.Euler(0,90,0);
            Check(player.TryGetDropPosition(item,out _),"drop finds open space in the room");
            player.DropHeld(); Check(!player.HeldItem && !item.Loaded,"clear drop releases real equipment");
            Check(!item.IsHandOccluded,"dropped gear always restores its world visibility");
            Check(Physics.GetIgnoreCollision(player.GetComponent<CharacterController>(),item.GetComponent<Collider>()),"drop preserves player collision filtering");
            item.transform.SetPositionAndRotation(itemPosition,itemRotation);
            item.GetComponent<Rigidbody>().linearVelocity=Vector3.zero;

            var door=GameObject.Find("OfficeDoor_A11").GetComponent<OfficeDoor>();
            var closed=door.transform.localRotation;
            door.Interact(player); yield return new WaitForSeconds(1.1f);
            Check(Quaternion.Angle(closed,door.transform.localRotation)>85,"door eases fully open");
            Teleport(player,new Vector3(0,.11f,-2.87f),Quaternion.identity);
            door.Interact(player); yield return new WaitForSeconds(1.1f);
            Check(Quaternion.Angle(closed,door.transform.localRotation)>5,"closing door waits at the player capsule");
            Teleport(player,position,rotation); yield return new WaitForSeconds(1.1f);
            Check(Quaternion.Angle(closed,door.transform.localRotation)<1,"door finishes closing after the player leaves");
            var keyboard=InputSystem.AddDevice<Keyboard>("PresentationTestKeyboard");
            var mouse=InputSystem.AddDevice<Mouse>("PresentationTestMouse");
            var background=InputSystem.settings.backgroundBehavior;
            var editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
            try
            {
                InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.EnableDevice(keyboard); InputSystem.EnableDevice(mouse);
                keyboard.MakeCurrent(); mouse.MakeCurrent();
                Teleport(player,new Vector3(-3,.11f,-3.7f),Quaternion.Euler(0,90,0));
                player.viewCamera.transform.localRotation=Quaternion.identity;
                player.SetCursor(false);
                var start=player.transform.position;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));
                InputSystem.Update();
                yield return new WaitForSeconds(.45f);
                float distance=Vector3.Distance(start,player.transform.position);
                Check(distance>.65f && distance<1.8f,"W input moves the player through the real movement controller; distance="+
                    distance+", W="+keyboard.wKey.isPressed+", keyboard="+(Keyboard.current==keyboard)+", mouse="+(Mouse.current==mouse)+
                    ", locked="+player.InputLocked+", position="+player.transform.position);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());
                InputSystem.Update();
                yield return new WaitForSeconds(.30f);
                var stopped=player.transform.position;
                yield return new WaitForSeconds(.18f);
                Check(Vector3.Distance(stopped,player.transform.position)<.025f,"releasing W settles without drift");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse);
                InputSystem.settings.backgroundBehavior=background;
                InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;
                player.SetCursor(false); Teleport(player,position,rotation);
                player.viewCamera.transform.localRotation=view;
            }
            player.ui.SetFocus(false); player.ui.SetPrompt("");
            File.AppendAllText("Logs/office_garage_validation.txt","PASS: baked room reflections, production lighting/AA, centered carry grip, wall-safe drops, collision filtering, eased doors with player clearance, W movement/release without drift, audio removed\n");
        }
    }
}
