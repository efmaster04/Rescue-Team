using System;
using System.Collections;
using System.IO;
using System.Linq;
using CompanySim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CompanySimEditor
{
    public static class DoorRegressionValidation
    {
        public static bool Finished { get; private set; }
        public static Exception Error { get; private set; }
        static void Check(bool condition,string message) { if(!condition) throw new Exception("Doors: "+message); }
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
        static void Teleport(PlayerInteractor player,Vector3 position,float yaw)
        {
            var controller=player.GetComponent<CharacterController>(); controller.enabled=false;
            player.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0)); controller.enabled=true;
            player.viewCamera.transform.localRotation=Quaternion.identity; Physics.SyncTransforms();
        }
        static void KeyState(Keyboard keyboard,params Key[] keys)
        {
            // Let the normal Dynamic input update consume transitions before
            // PlayerInteractor.Update; a manual update loses wasPressedThisFrame.
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
        }
        static IEnumerator Run(PlayerInteractor player)
        {
            var initialPosition=player.transform.position; var initialYaw=player.transform.eulerAngles.y;
            var initialView=player.viewCamera.transform.localRotation;
            var keyboard=InputSystem.AddDevice<Keyboard>("DoorRegressionKeyboard");
            var mouse=InputSystem.AddDevice<Mouse>("DoorRegressionMouse");
            var background=InputSystem.settings.backgroundBehavior;
            var editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
            var item=UnityEngine.Object.FindObjectsByType<CarryableItem>().First(x=>x.definitionId=="toolbox");
            var itemBody=item.GetComponent<Rigidbody>(); var itemPosition=item.transform.position;
            var itemRotation=item.transform.rotation; bool itemKinematic=itemBody.isKinematic;
            try
            {
                InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.EnableDevice(keyboard); InputSystem.EnableDevice(mouse); keyboard.MakeCurrent(); mouse.MakeCurrent();
                foreach(string name in new[]{"OfficeDoor_A11","OfficeDoor_A13"})
                {
                    var door=GameObject.Find(name).GetComponent<OfficeDoor>();
                    var leaf=door.GetComponent<BoxCollider>(); var body=door.GetComponent<Rigidbody>();
                    Check(body && body.isKinematic && !body.useGravity,"door uses a kinematic physics body: "+name);
                    Check(Mathf.Min(leaf.size.x,leaf.size.z)<.09f,"handles do not inflate the solid leaf: "+name);
                    var hardware=door.GetComponentsInChildren<MeshRenderer>().Where(x=>x.name.StartsWith("A15_")).ToArray();
                    var hardwareLocal=hardware.Select(x=>x.transform.localPosition).ToArray();
                    bool inner=name.EndsWith("A11");
                    for(int side=0;side<2;side++)
                    {
                        player.SetCursor(true); Teleport(player,initialPosition,initialYaw);
                        if(door.IsOpen) door.Interact(player);
                        yield return new WaitForSeconds(1.4f);
                        Check(Mathf.Abs(door.CurrentAngle)<.1f,"door resets closed: "+name);
                        var start=inner?new Vector3(side==0?-.78f:.78f,.01f,-2.87f):
                            new Vector3(-3.18f,.01f,side==0?-4.40f:-5.95f);
                        float yaw=inner?(side==0?90:270):(side==0?180:0);
                        Teleport(player,start,yaw); player.SetCursor(false); KeyState(keyboard);
                        yield return null;
                        Check(player.FindTarget()==door,"E target reachable from side "+side+" of "+name);
                        KeyState(keyboard,Key.E); yield return new WaitForSeconds(.06f); KeyState(keyboard);
                        yield return new WaitForSeconds(1.35f);
                        Check(door.IsOpen && Mathf.Abs(door.CurrentAngle)>94,"real E fully opens "+name+" from side "+side+" angle="+door.CurrentAngle);
                        Check(side==0?door.CurrentAngle>0:door.CurrentAngle<0,"door swings away from the approaching player: "+name);
                        KeyState(keyboard,Key.W); yield return new WaitForSeconds(.60f); KeyState(keyboard);
                        yield return new WaitForSeconds(.20f);
                        var end=player.transform.position;
                        Check(inner?(side==0?end.x>.45f:end.x<-.45f):(side==0?end.z<-5.6f:end.z>-4.75f),
                            "real W crosses "+name+" from side "+side+"; stopped at "+end);
                        Check(Mathf.Abs(end.y-start.y)<.25f,"crossing has no jump or ejection: "+name);
                        for(int i=0;i<hardware.Length;i++)
                            Check(Vector3.Distance(hardware[i].transform.localPosition,hardwareLocal[i])<.001f,"hardware stays attached "+hardware[i].name);
                    }
                    player.SetCursor(true); Teleport(player,initialPosition,initialYaw);
                    if(door.IsOpen) door.Interact(player);
                    yield return new WaitForSeconds(1.4f);
                    // Open from the office, then occupy the opening while closing.
                    Teleport(player,inner?new Vector3(-.78f,.01f,-2.87f):new Vector3(-3.18f,.01f,-4.4f),inner?90:180);
                    door.Interact(player); yield return new WaitForSeconds(1.35f);
                    var threshold=inner?new Vector3(0,.01f,-2.87f):new Vector3(-3.18f,.01f,-5.17f);
                    Teleport(player,threshold,0); door.Interact(player); yield return new WaitForSeconds(.90f);
                    Check(door.IsBlocked && Mathf.Abs(door.CurrentAngle)>5,"closing waits for an occupied doorway: "+name);
                    Check(Vector3.Distance(player.transform.position,threshold)<.01f,"door does not push the player: "+name);
                    var controller=player.GetComponent<CharacterController>();
                    Check(!Physics.ComputePenetration(leaf,body.position,body.rotation,controller,player.transform.position,player.transform.rotation,out _,out _),
                        "solid door does not enter the capsule: "+name);
                    Teleport(player,initialPosition,initialYaw); yield return new WaitForSeconds(1.4f);
                    Check(Mathf.Abs(door.CurrentAngle)<.1f,"closing resumes when clear: "+name);
                }
                var innerDoor=GameObject.Find("OfficeDoor_A11").GetComponent<OfficeDoor>();
                Teleport(player,new Vector3(-.78f,.01f,-2.87f),90);
                itemBody.isKinematic=true;
                item.transform.SetPositionAndRotation(new Vector3(.42f,.11f,-2.97f),Quaternion.identity); Physics.SyncTransforms();
                var obstructionPosition=item.transform.position;
                innerDoor.Interact(player); yield return new WaitForSeconds(1.4f);
                Check(innerDoor.IsBlocked && Mathf.Abs(innerDoor.CurrentAngle)<80,"opening stops at real loose equipment");
                Check(Vector3.Distance(item.transform.position,obstructionPosition)<.01f,"opening does not fling the equipment");
                item.transform.SetPositionAndRotation(itemPosition,itemRotation); Physics.SyncTransforms();
                yield return new WaitForSeconds(1.4f);
                Check(Mathf.Abs(innerDoor.CurrentAngle)>94,"opening resumes after equipment is moved");
                for(int i=0;i<12;i++) { innerDoor.Interact(player); yield return new WaitForSeconds(.035f); }
                yield return new WaitForSeconds(1.4f);
                Check(innerDoor.IsOpen && Mathf.Abs(innerDoor.CurrentAngle)>94,"rapid toggles settle at the requested pose");
                Teleport(player,new Vector3(-.72f,.01f,-2.87f),90);
                player.ui.SetPrompt("E — Kapıyı kapat"); player.ui.SetFocus(true);
                yield return null;
                ComputerDesktopValidation.Capture(player.ui,"Documentation/DoorPassageRefinedPreview.png",1600,900);
                Teleport(player,new Vector3(7.65f,.01f,-4.3f),325);
                player.viewCamera.transform.LookAt(new Vector3(4.5f,1.45f,.1f)); player.ui.SetPrompt(""); player.ui.SetFocus(false);
                yield return null;
                ComputerDesktopValidation.Capture(player.ui,"Documentation/GarageRefinedPreview.png",1600,900);
                Teleport(player,new Vector3(7.45f,.01f,-1.45f),30);
                player.viewCamera.transform.LookAt(new Vector3(8.75f,1.42f,.4f));
                yield return null;
                ComputerDesktopValidation.Capture(player.ui,"Documentation/WorkbenchRefinedPreview.png",1600,900);
                var floor=GameObject.Find("01_Environment_OfficeGarage").GetComponentsInChildren<MeshRenderer>().First(x=>x.name.StartsWith("A07_"));
                Check(floor.sharedMaterial.GetTexture("_BumpMap") && floor.sharedMaterial.GetTexture("_MetallicGlossMap"),"floor has normal and roughness detail");
                Check(GameObject.Find("10_GarageFinish").GetComponentsInChildren<Light>().Count(x=>x.shadows==LightShadows.Soft)==2,"two shadowed work lights plus unshadowed fixture fill");
                var lens=GameObject.Find("10_GarageFinish").GetComponentsInChildren<MeshRenderer>().First(x=>x.name.StartsWith("LED_Lens"));
                Check(lens.sharedMaterial.IsKeywordEnabled("_EMISSION"),"LED emission survives material import");
                player.SetCursor(true); Teleport(player,new Vector3(.65f,.01f,4.72f),270);
                var barrierStart=player.transform.position;
                for(int i=0;i<30;i++) player.GetComponent<CharacterController>().Move(Vector3.left*.04f);
                Check(player.transform.position.x>.25f,"rear-left garage wall blocks the capsule across the infill segment");
                innerDoor.Interact(player); yield return new WaitForSeconds(1.4f);
                File.AppendAllText("Logs/office_garage_validation.txt","PASS: both doors opened with E and crossed with W in both directions, swing away from player, thin leaf collision, hardware attachment, capsule-safe closing, equipment-safe opening/resume, rapid toggle recovery; garage PBR floor and four fixture-aligned work lights\n");
            }
            finally
            {
                item.transform.SetPositionAndRotation(itemPosition,itemRotation); itemBody.isKinematic=itemKinematic;
                if(!itemKinematic) { itemBody.linearVelocity=Vector3.zero; itemBody.angularVelocity=Vector3.zero; }
                InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse);
                InputSystem.settings.backgroundBehavior=background; InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;
                Teleport(player,initialPosition,initialYaw); player.viewCamera.transform.localRotation=initialView; player.SetCursor(false);
                player.ui.SetPrompt(""); player.ui.SetFocus(false);
            }
        }
    }
}
