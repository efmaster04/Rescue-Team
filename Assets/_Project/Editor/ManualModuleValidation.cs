using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CompanySim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CompanySimEditor
{
    public static class ManualModuleValidation
    {
        public static bool Finished { get; private set; }
        public static Exception Error { get; private set; }
        static void Check(bool value,string message) {if(!value) throw new Exception("Manual modules: "+message);}
        public static void Begin(PlayerInteractor player) {Finished=false; Error=null; player.StartCoroutine(Drive(Run(player)));}
        static IEnumerator Drive(IEnumerator test)
        {
            yield return Guard(test); Finished=true;
        }
        static IEnumerator Guard(IEnumerator test)
        {
            while(Error==null)
            {
                object next=null; bool more=false;
                try {more=test.MoveNext(); if(more) next=test.Current;} catch(Exception error) {Error=error;}
                if(Error!=null) break;
                if(!more) break;
                if(next is IEnumerator nested) yield return Guard(nested); else yield return next;
            }
        }
        static IEnumerator PressKey(Keyboard keyboard,Key key,float seconds=.09f)
        {InputSystem.QueueStateEvent(keyboard,new KeyboardState(key)); yield return null; yield return new WaitForSeconds(seconds); InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null; yield return new WaitForSeconds(.06f);}
        static IEnumerator AimAt(PlayerInteractor player,InteractableBehaviour target)
        {
            var vehicle=player.ui.Runtime.vehicle;
            GarageSystemsValidation.Aim(player,vehicle.transform.TransformPoint(new Vector3(.25f,.01f,3.88f)),target.GetComponent<Collider>().bounds.center); player.SetCursor(false); yield return null;
            var controller=player.GetComponent<CharacterController>();
            foreach(var obstacle in Physics.OverlapSphere(controller.bounds.center,controller.radius,~0,QueryTriggerInteraction.Ignore))
                if(!obstacle.transform.IsChildOf(player.transform) && !obstacle.GetComponentInParent<CarryableItem>())
                    Check(Vector3.Distance(obstacle.ClosestPoint(controller.bounds.center),controller.bounds.center)>.02f,"manual work position must not be inside "+obstacle.name);
            Check(player.FindTarget()==target,"rear exterior E target "+target.name+", got "+(player.FindTarget()?player.FindTarget().name:"none"));
        }
        static IEnumerator Pickup(PlayerInteractor player,CarryableItem item,Keyboard keyboard)
        {
            // Prior tests can leave loose items side by side. Approach from a clear
            // side instead of aiming through a different physical package.
            bool found=false;
            foreach(var direction in new[]{Vector3.forward,Vector3.back,Vector3.right,Vector3.left,
                new Vector3(1,0,1).normalized,new Vector3(-1,0,1).normalized,new Vector3(1,0,-1).normalized,new Vector3(-1,0,-1).normalized})
            {
                var center=item.GetComponent<BoxCollider>().bounds.center;
                var position=center+direction*.95f; position.y=.01f;
                GarageSystemsValidation.Aim(player,position,center); player.SetCursor(false); yield return null;
                var controller=player.GetComponent<CharacterController>();
                bool insideSolid=Physics.OverlapSphere(controller.bounds.center,controller.radius,~0,QueryTriggerInteraction.Ignore)
                    .Any(c=>!c.transform.IsChildOf(player.transform) && !c.GetComponentInParent<CarryableItem>() &&
                        Vector3.Distance(c.ClosestPoint(controller.bounds.center),controller.bounds.center)<.02f);
                if(!insideSolid && player.FindTarget()==item) {found=true; break;}
            }
            Check(found,"physical pickup target "+item.DisplayName+" at "+item.transform.position+", got "+(player.FindTarget()?player.FindTarget().name:"none"));
            yield return PressKey(keyboard,Key.E); Check(player.HeldItem==item,"E picks up "+item.DisplayName);
        }
        static IEnumerator Drop(PlayerInteractor player,Keyboard keyboard)
        {
            float x=6.30f+(dropSpot++%3)*.65f;
            GarageSystemsValidation.Aim(player,new Vector3(x,.01f,-3.15f),new Vector3(x,.50f,-4.3f)); player.SetCursor(false); yield return null;
            yield return PressKey(keyboard,Key.F); Check(!player.HeldItem,"F drops the carried object in clear garage space");
        }
        static IEnumerator Run(PlayerInteractor player)
        {
            dropSpot=0;
            var runtime=player.ui.Runtime; var system=runtime.manualModules;
            var initialPosition=player.transform.position; var initialRotation=player.transform.rotation; var initialView=player.viewCamera.transform.localRotation;
            var pitch=typeof(PlayerInteractor).GetField("pitch",BindingFlags.Instance|BindingFlags.NonPublic); float initialPitch=(float)pitch.GetValue(player);
            var keyboard=InputSystem.AddDevice<Keyboard>("ManualModuleKeyboard"); var mouse=InputSystem.AddDevice<Mouse>("ManualModuleMouse");
            var background=InputSystem.settings.backgroundBehavior; var editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
            try
            {
                Check(system && system.Initialized && system.Current && system.IsSecured,"physical starting module is installed and secured");
                Check(system.packageVisuals.Length==5 && system.packageVisuals.Select(v=>v.moduleId).Distinct().Count()==5,"five physical module variants are configured");
                Check(system.packageVisuals.All(v=>v.prefab && v.prefab.transform.Find("PortableModuleDevice") && v.prefab.transform.Find("ServicePort")),"every variant has its own equipment model and common service connection");
                foreach(var variant in system.packageVisuals)
                {
                    var device=variant.prefab.transform.Find("PortableModuleDevice");var group=device.GetComponent<LODGroup>();
                    bool blender=BlenderModuleBuilder.Ids.Contains(variant.moduleId);
                    Check(variant.prefab.GetComponentsInChildren<MeshRenderer>().Length==(blender?5:3) && variant.prefab.transform.Find("ModuleSkidAndBrackets"),"module fixture renderer budget: "+variant.moduleId);
                    if(blender)
                    {
                        var levels=group?group.GetLODs():new LOD[0];
                        Check(levels.Length==3 && levels.All(l=>l.renderers.Length==1 && l.renderers[0]) && levels.Select(l=>l.renderers[0]).Distinct().Count()==3,"three separate Blender device LODs: "+variant.moduleId);
                        var counts=levels.Select(l=>l.renderers[0].GetComponent<MeshFilter>().sharedMesh.triangles.Length/3).ToArray();
                        Check(counts[0]>counts[1] && counts[1]>counts[2] && counts[2]<counts[0]*.15f,"distance LOD geometry decreases: "+variant.moduleId);
                    }
                }
                Check(system.packageVisuals.Select(v=>v.prefab.transform.Find("PortableModuleDevice").GetComponent<MeshFilter>().sharedMesh).Distinct().Count()==5,"all five modules have different actual mechanism meshes");
                var cable=runtime.vehicle.GetComponentInChildren<ModuleConnectionVisual>();
                Check(cable && cable.Connected && Vector3.Distance(cable.EndPoint,system.Current.transform.Find("ServicePort").position)<.002f,"service cable plugs into the actual module port");
                var line=cable.GetComponentInChildren<LineRenderer>();
                for(int i=0;i<line.positionCount;i++)
                {
                    var point=runtime.vehicle.transform.InverseTransformPoint(line.transform.TransformPoint(line.GetPosition(i)));
                    Check(point.x>-.05f && point.x<1.05f && point.z>2.78f && point.z<3.60f,"actual cable geometry remains behind the rear door: "+point);
                }
                InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                keyboard.MakeCurrent(); mouse.MakeCurrent(); InputSystem.EnableDevice(keyboard); InputSystem.EnableDevice(mouse);
                if(player.ui.IsOpen) player.ui.Close();
                if(runtime.Data.phase==JobPhase.None) Check(runtime.AcceptJob("recovery"),"test recovery job accepted");
                var legacy=JsonUtility.FromJson<CompanySave>(JsonUtility.ToJson(runtime.Data)); legacy.version=4; legacy.modulePackages=null;
                legacy.rearAccessLayoutReady=false;
                legacy.equipment.First(r=>r.typeId=="fuel").position=new Vector3(5.06f,.43f,3.14f);
                File.WriteAllText(Path.Combine(Application.persistentDataPath,"office_garage_v1.json"),JsonUtility.ToJson(legacy));
                Check(runtime.Load() && runtime.Data.version==5 && runtime.Data.balance==legacy.balance && runtime.EquipmentItems.Length==legacy.equipment.Count && system.Packages.Count==legacy.ownedModules.Count && system.IsSecured,
                    "version-4 save migrates balance, equipment and owned modules into physical version-5 packages");
                Check(runtime.Data.rearAccessLayoutReady && runtime.Data.equipment.First(r=>r.typeId=="fuel").position.z<0,"legacy loose fuel is relocated out of the new rear step/door space without losing stock");
                // Real purchase/delivery/persistence, then restore this isolated economy fixture.
                int oldBalance=runtime.Data.balance,oldTransactions=runtime.Data.transactions.Count;
                var water=runtime.catalog.Module("water"); runtime.Data.balance=water.price+500;
                string active=runtime.vehicle.InstalledModuleId;
                Check(runtime.BuyModule("water") && runtime.Data.balance==500,"module purchase charges the actual price");
                var order=runtime.Data.modulePackages.Single(x=>x.moduleId=="water");
                Check(!order.delivered && runtime.vehicle.InstalledModuleId==active,"purchase creates a delivery and does not install a module");
                order.arrivalUnix=0; yield return new WaitForSeconds(1.25f);
                Check(order.delivered && system.Packages.Any(x=>x.record.moduleId=="water"),"purchased module arrives as a physical package");
                Check(runtime.Save() && runtime.Load(),"physical module delivery save/load succeeds");
                Check(system.Packages.Any(x=>x.record.moduleId=="water") && runtime.vehicle.InstalledModuleId==active,"delivered spare module persists without auto installation");
                runtime.Data.ownedModules.Remove("water"); runtime.Data.modulePackages.RemoveAll(x=>x.moduleId=="water");
                runtime.Data.balance=oldBalance; runtime.Data.transactions.RemoveRange(oldTransactions,runtime.Data.transactions.Count-oldTransactions);
                system.Initialize(runtime); runtime.WorldChanged();
                Check(runtime.vehicle.doors.Where(d=>d.name.StartsWith("Rear")).All(d=>d.IsClosed),"rear doors stay closed throughout exterior fitting");
                Check(system.CanWork(player,out _),"external module fitting needs no rear-door access");
                foreach(var renderer in system.Current.GetComponentsInChildren<MeshRenderer>())
                    foreach(var point in renderer.GetComponent<MeshFilter>().sharedMesh.vertices)
                        Check(runtime.vehicle.transform.InverseTransformPoint(renderer.transform.TransformPoint(point)).z>2.80f,"installed module is wholly outside the rear body");
                var connector=GameObject.Find("ModuleConnector").GetComponent<ModuleFastener>();
                yield return AimAt(player,connector); yield return PressKey(keyboard,Key.E);
                Check(!system.Current.record.connected && !system.IsSecured && runtime.vehicle.InstalledModuleId=="","disconnecting disables module capability; work="+system.CanWork(player,out var workReason)+" "+workReason+", locked="+player.InputLocked+", keyboard="+(Keyboard.current==keyboard)+", target="+player.FindTarget()?.name);
                Check(!cable.Connected && Vector3.Distance(cable.EndPoint,system.Current.transform.Find("ServicePort").position)>.10f,"disconnected cable parks clear of the module");
                Check(!runtime.Depart(),"an unsecured module blocks departure");
                var toolbox=runtime.EquipmentItems.First(x=>x.definitionId=="toolbox");
                yield return Pickup(player,toolbox,keyboard);
                for(int i=0;i<4;i++)
                {
                    var bolt=GameObject.Find("ModuleBolt_"+(i+1)).GetComponent<ModuleFastener>(); yield return AimAt(player,bolt);
                    if(i==0)
                    {
                        int before=system.Current.record.bolts; var pose=bolt.boltVisual.localRotation;
                        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.E)); yield return new WaitForSeconds(.16f);
                        Check(bolt.Working && bolt.WorkProgress>0 && bolt.WorkProgress<1 && Quaternion.Angle(pose,bolt.boltVisual.localRotation)>2,"hold E visibly rotates the bolt before changing the saved state");
                        ComputerDesktopValidation.Capture(player.ui,"Documentation/ModuleFastenerWorking.png",1600,900);
                        InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return new WaitForSeconds(.1f);
                        Check(system.Current.record.bolts==before && !bolt.Working && Quaternion.Angle(pose,bolt.boltVisual.localRotation)<.1f,"releasing E early restores the bolt pose and cancels the state change");
                    }
                    yield return PressKey(keyboard,Key.E,.85f); Check((system.Current.record.bolts&(1<<i))==0,"hold E removes fastener "+(i+1));
                }
                yield return Drop(player,keyboard);
                var mount=GameObject.Find("ModuleDockTarget").GetComponent<ModuleMountPoint>(); yield return AimAt(player,mount); yield return PressKey(keyboard,Key.E);
                Check(!system.Current && player.HeldItem && player.HeldItem.GetComponent<ManualModulePackage>(),"old module is physically removed into the player's hand");
                var heldModule=player.HeldItem;
                GarageSystemsValidation.Aim(player,new Vector3(-4.31f,.11f,-2.70f),new Vector3(-6,1.68f,-2.70f));
                yield return new WaitForSeconds(.5f);
                Check(heldModule.IsHandOccluded && heldModule.GetComponentsInChildren<Canvas>().All(c=>!c.enabled),"module label disappears with the retracted model at a close wall");
                GarageSystemsValidation.Aim(player,new Vector3(7.35f,.01f,-4.10f),new Vector3(7.35f,1.58f,-2.0f));
                yield return new WaitForSeconds(.5f);
                Check(!heldModule.IsHandOccluded && heldModule.GetComponentsInChildren<Canvas>().All(c=>c.enabled),"module model and label return together in clear space");
                yield return Drop(player,keyboard);
                var towing=system.Packages.First(x=>x.record.moduleId=="towing"); yield return Pickup(player,towing.Item,keyboard);
                yield return AimAt(player,mount); yield return PressKey(keyboard,Key.E);
                Check(system.Current==towing && !player.HeldItem && towing.record.bolts==0 && !towing.record.connected,"new module is manually staged with no automatic fastening");
                Check(!system.ToggleConnection(player),"connector cannot latch until all four fasteners are tight");
                yield return Pickup(player,toolbox,keyboard);
                for(int i=0;i<2;i++) {yield return AimAt(player,GameObject.Find("ModuleBolt_"+(i+1)).GetComponent<ModuleFastener>()); yield return PressKey(keyboard,Key.E,.85f);}
                Check(system.Current.record.bolts==3 && !system.IsSecured,"partially fitted module remains unusable");
                yield return Drop(player,keyboard); Check(runtime.Save() && runtime.Load(),"partial fitting save/load succeeds");
                Check(system.Current.record.moduleId=="towing" && system.Current.record.bolts==3 && !system.Current.record.connected && runtime.vehicle.InstalledModuleId=="","two tightened fasteners and disconnected state survive reload");
                toolbox=runtime.EquipmentItems.First(x=>x.definitionId=="toolbox"); yield return Pickup(player,toolbox,keyboard);
                for(int i=2;i<4;i++) {yield return AimAt(player,GameObject.Find("ModuleBolt_"+(i+1)).GetComponent<ModuleFastener>()); yield return PressKey(keyboard,Key.E,.85f);}
                yield return AimAt(player,connector); yield return PressKey(keyboard,Key.E);
                Check(system.IsSecured && runtime.vehicle.InstalledModuleId=="towing" && runtime.Data.installedModuleId=="towing","capability activates only after four fasteners plus connector");
                Check(cable.Connected && Vector3.Distance(cable.EndPoint,system.Current.transform.Find("ServicePort").position)<.002f,"reloaded module cable reconnects to the new unit");
                var signal=runtime.vehicle.moduleSocket.GetChild(0); runtime.vehicle.SetModuleImmediate("towing");
                Check(runtime.vehicle.moduleSocket.GetChild(0)==signal,"unchanged module state preserves the existing roof signal instead of rebuilding it");
                Check(system.Current.transform.parent==system.mount && runtime.vehicle.transform.InverseTransformPoint(system.mount.position).z>2.90f,"partially fitted save migrates/restores to the rear exterior mount");
                ComputerDesktopValidation.Capture(player.ui,"Documentation/ManualModuleInstalled.png",1600,900);
                yield return Drop(player,keyboard);
                yield return AimAt(player,mount);
                yield return new WaitForSeconds(.15f);
                ComputerDesktopValidation.Capture(player.ui,"Documentation/ManualModuleBay.png",1600,900);
                Check(runtime.vehicle.doors.Where(d=>d.name.StartsWith("Rear")).All(d=>d.IsClosed),"manual fitting finishes with the rear doors still closed");
                VehicleInteriorValidation.Capture(runtime.vehicle,"ExteriorModuleMounted",new Vector3(2.30f,2.15f,3.95f),new Vector3(.50f,1.80f,2.90f));
                VehicleInteriorValidation.Capture(runtime.vehicle,"ExteriorModuleDetail",new Vector3(.52f,2.27f,4.15f),new Vector3(.52f,2.16f,3.12f));
                yield return CaptureVariants(system);
                var spare=runtime.EquipmentItems.First(x=>x.definitionId=="tire");
                yield return Pickup(player,spare,keyboard);
                Check(player.HeldItem==spare && spare.GetComponentsInChildren<MeshRenderer>().Length==1,"refined spare tyre remains a real carryable item with one combined visual renderer");
                GarageSystemsValidation.Aim(player,new Vector3(7.10f,.01f,-.50f),new Vector3(7.10f,1.0f,-1.80f)); player.SetCursor(false);
                yield return new WaitForSeconds(.35f);
                ComputerDesktopValidation.Capture(player.ui,"Documentation/SpareTyreCarry.png",1600,900);
                yield return PressKey(keyboard,Key.F);
                Check(!player.HeldItem && !spare.Loaded,"real F drops the refined spare tyre in clear garage space");
                var jack=runtime.EquipmentItems.First(x=>x.definitionId=="jack"); yield return Pickup(player,jack,keyboard);
                foreach(var rear in runtime.vehicle.doors.Where(d=>d.name.StartsWith("Rear")))
                {
                    yield return AimRear(player,rear); yield return PressKey(keyboard,Key.E); yield return new WaitForSeconds(2.2f);
                    Check(Mathf.Abs(rear.CurrentAngle)>100,"rear doors open with real E while carrying the jack");
                }
                yield return AimRear(player,runtime.vehicle); yield return PressKey(keyboard,Key.E);
                Check(!player.HeldItem && runtime.vehicle.LoadedItems.Contains(jack),"real E loads physical equipment through the rear opening with an exterior module fitted");
                yield return AimRear(player,runtime.vehicle); yield return PressKey(keyboard,Key.E);
                Check(player.HeldItem==jack && !runtime.vehicle.LoadedItems.Contains(jack),"real E unloads the same equipment into the player's hand");
                yield return Drop(player,keyboard);
                foreach(var rear in runtime.vehicle.doors.Where(d=>d.name.StartsWith("Rear")))
                {
                    yield return AimRear(player,rear); yield return PressKey(keyboard,Key.E); yield return new WaitForSeconds(2.2f);
                    Check(rear.IsClosed,"rear doors close without hitting the exterior module");
                }
                File.AppendAllText("Logs/office_garage_validation.txt","PASS: rear exterior module delivery, real E/F pickup/drop/staging with carrier door closed, hold-E fasteners/cancellation, connector interlock, manual removal/refitting and partial save/load; module outside the rear body; real E rear cargo load/unload and door clearance\n");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse); InputSystem.settings.backgroundBehavior=background; InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;
                var controller=player.GetComponent<CharacterController>(); controller.enabled=false; player.transform.SetPositionAndRotation(initialPosition,initialRotation); controller.enabled=true;
                pitch.SetValue(player,initialPitch); player.viewCamera.transform.localRotation=initialView; player.SetCursor(false);
            }
        }
        static int dropSpot;
        static IEnumerator AimRear(PlayerInteractor player,InteractableBehaviour target)
        {
            GarageSystemsValidation.Aim(player,new Vector3(4.65f,.01f,3.70f),target.GetComponent<Collider>().bounds.center); player.SetCursor(false); yield return null;
            Check(player.FindTarget()==target,"rear E reaches "+target.name+", got "+(player.FindTarget()?player.FindTarget().name:"none"));
        }
        static IEnumerator CaptureVariants(ManualModuleSystem system)
        {
            foreach(var variant in system.packageVisuals)
            {
                var model=UnityEngine.Object.Instantiate(variant.prefab,new Vector3(0,80,0),Quaternion.identity);
                var body=model.GetComponent<Rigidbody>(); body.isKinematic=true;
                yield return null;
                var cameraGo=new GameObject("Temporary_ModuleVariantCamera"); var camera=cameraGo.AddComponent<Camera>();
                camera.transform.position=new Vector3(.62f,80.48f,.90f); camera.transform.LookAt(new Vector3(0,80.23f,0)); camera.fieldOfView=42;
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.28f,.30f,.29f);
                ProjectValidation.CaptureCamera(camera,"Documentation/ModuleVariant_"+variant.moduleId+".png");
                model.SetActive(false); UnityEngine.Object.Destroy(model); UnityEngine.Object.Destroy(cameraGo);
                yield return null;
            }
            File.AppendAllText("Logs/office_garage_validation.txt","PASS: five distinct complete equipment models, combined physical skid/brackets, visible bolt turning/cancellation, connected/parked service cable, wall-safe labels and stable roof signal identity\n");
        }
    }
}
