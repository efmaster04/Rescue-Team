using System;
using System.IO;
using System.Linq;
using CompanySim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CompanySimEditor
{
    public static class GarageSystemsBuilder
    {
        public const string ScenePath="Assets/_Project/Scenes/OfficeGarage.unity";
        public static void ApplyAndValidate() {Apply(); ProjectValidation.RunGarageOnly();}
        public static void ApplyAndValidateManual() {Apply(); ProjectValidation.RunManualOnly();}
        public static void ApplyAndValidateFull() {Apply(); ProjectValidation.Run();}
        [MenuItem("Company/Install Lift, Vehicle Doors and Performance")]
        public static void Apply()
        {
            if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=ScenePath) EditorSceneManager.OpenScene(ScenePath);
            var vehicle=UnityEngine.Object.FindAnyObjectByType<ServiceVehicle>();
            var old=GameObject.Find("13_GarageSystems"); var root=old?old:new GameObject("13_GarageSystems");
            var lift=root.GetComponent<GarageLift>()??root.AddComponent<GarageLift>(); lift.vehicle=vehicle; vehicle.lift=lift;
            lift.moduleStation=UnityEngine.Object.FindAnyObjectByType<ModuleStation>();
            lift.movingParts=GameObject.Find("01_Environment_OfficeGarage").GetComponentsInChildren<MeshRenderer>()
                .Where(r=>r.name.StartsWith("T29_") || r.name.StartsWith("T28C_")).Select(r=>r.transform).ToArray();
            var doors=vehicle.GetComponentsInChildren<VehicleDoor>();
            if(doors.Length==0) doors=BuildDoors(vehicle,lift);
            lift.doors=doors; vehicle.doors=doors;
            foreach(var door in doors) if(door.name.StartsWith("Rear")) door.openAngle=door.name.EndsWith("Right")?-180:180;
            foreach(var socket in vehicle.cargoSockets) {var position=socket.localPosition; position.y=.67f; socket.localPosition=position;}
            EnsureRearSurfaces(vehicle,doors);
            var body=vehicle.GetComponent<BoxCollider>(); body.center=new Vector3(0,.95f,2.63f); body.size=new Vector3(1.3f,.65f,.10f); body.isTrigger=true;
            Solid(vehicle.transform,"CargoLeftWall",new Vector3(-.97f,1.60f,.95f),new Vector3(.10f,2.2f,3.5f));
            Solid(vehicle.transform,"CargoRightWall",new Vector3(.97f,1.60f,.95f),new Vector3(.10f,2.2f,3.5f));
            Solid(vehicle.transform,"CargoFloor",new Vector3(0,.58f,.95f),new Vector3(1.94f,.16f,3.5f));
            Solid(vehicle.transform,"CargoRoof",new Vector3(0,2.64f,.95f),new Vector3(1.94f,.12f,3.5f));
            Solid(vehicle.transform,"CargoBulkhead",new Vector3(0,1.60f,-.73f),new Vector3(1.94f,2.2f,.12f));
            Solid(vehicle.transform,"CabCollision",new Vector3(0,1.2f,-1.1f),new Vector3(1.65f,1.5f,1.4f));
            Solid(vehicle.transform,"EngineCollision",new Vector3(0,.85f,-2.2f),new Vector3(1.95f,1.3f,1.45f));
            Solid(vehicle.transform,"FrontBumperCollision",new Vector3(0,.50f,-2.98f),new Vector3(2.08f,.50f,.20f));
            var control=GameObject.Find("LiftController").transform;
            foreach(int direction in new[]{1,-1})
            {
                string name=direction>0?"LiftRaiseTarget":"LiftLowerTarget";
                var target=control.Find(name); if(!target) {target=new GameObject(name).transform; target.SetParent(control,false);}
                target.localPosition=new Vector3(0,direction>0?.075f:-.060f,-.12f);
                var collider=target.GetComponent<BoxCollider>(); if(!collider) collider=target.gameObject.AddComponent<BoxCollider>(); collider.size=new Vector3(.18f,.13f,.06f); collider.isTrigger=true;
                var button=target.GetComponent<LiftButton>()??target.gameObject.AddComponent<LiftButton>(); button.lift=lift; button.direction=direction;
            }
            // The lift panel must no longer intercept E and launch the module desktop.
            lift.moduleStation.GetComponent<BoxCollider>().enabled=false;
            OfficePresentationBuilder.ConfigureAudio(UnityEngine.Object.FindAnyObjectByType<AudioFeedback>());
            var optimization=root.GetComponent<GarageRuntimeOptimization>()??root.AddComponent<GarageRuntimeOptimization>(); optimization.lift=lift;
            ManualModuleBuilder.Configure(root,vehicle,lift);
            ConfigureRearSteps(vehicle);
            VehicleBodyRepairBuilder.Configure(vehicle,doors);
            ConfigurePerformance();
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
            File.WriteAllText("Logs/garage_systems_installed.txt",doors.Length+" vehicle doors; "+lift.movingParts.Length+" original moving lift parts; audio removed; performance settings applied.");
        }
        static void ConfigureRearSteps(ServiceVehicle vehicle)
        {
            var previous=vehicle.transform.Find("RearAccessSteps"); if(previous) UnityEngine.Object.DestroyImmediate(previous.gameObject);
            var steps=new GameObject("RearAccessSteps").transform; steps.SetParent(vehicle.transform,false);
            var metal=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/VehicleMechanics/Module_MountSteel.mat");
            var rubber=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/VehicleMechanics/Module_ConnectorRubber.mat");
            for(int i=0;i<2;i++)
            {
                float top=i==0?.26f:.50f,z=i==0?3.18f:2.86f,depth=i==0?.32f:.44f;
                var tread=new GameObject(i==0?"RearLowerTread":"RearUpperTread",typeof(MeshFilter),typeof(MeshRenderer)); tread.transform.SetParent(steps,false);
                tread.transform.localPosition=new Vector3(0,top-.025f,z);
                tread.GetComponent<MeshFilter>().sharedMesh=WorkshopOverhaulBuilder.BoxMesh(new Vector3(1.40f,.05f,depth),.008f);
                tread.GetComponent<MeshRenderer>().sharedMaterial=metal;
                var hit=tread.AddComponent<BoxCollider>(); hit.size=new Vector3(1.40f,.05f,depth);
                for(int rib=0;rib<6;rib++)
                {
                    var strip=new GameObject("TreadGrip",typeof(MeshFilter),typeof(MeshRenderer)); strip.transform.SetParent(steps,false);
                    strip.transform.localPosition=new Vector3(0,top+.001f,z-depth*.35f+rib*depth*.14f);
                    strip.GetComponent<MeshFilter>().sharedMesh=WorkshopOverhaulBuilder.BoxMesh(new Vector3(1.30f,.002f,.014f),.0003f);
                    strip.GetComponent<MeshRenderer>().sharedMaterial=rubber;
                }
            }
            foreach(float side in new[]{-.52f,.52f})
            {
                var support=new GameObject("RearStepBracket",typeof(MeshFilter),typeof(MeshRenderer)); support.transform.SetParent(steps,false);
                support.transform.localPosition=new Vector3(side,.35f,3.0f); support.GetComponent<MeshFilter>().sharedMesh=WorkshopOverhaulBuilder.BoxMesh(new Vector3(.045f,.28f,.53f),.004f);
                support.GetComponent<MeshRenderer>().sharedMaterial=metal;
            }
            var surfaces=steps.GetComponentsInChildren<MeshRenderer>();
            var parts=new System.Collections.Generic.List<Mesh>(); var materials=new System.Collections.Generic.List<Material>();
            foreach(var group in surfaces.GroupBy(r=>r.sharedMaterial))
            {
                var part=new Mesh(); part.CombineMeshes(group.Select(r=>new CombineInstance {mesh=r.GetComponent<MeshFilter>().sharedMesh,transform=steps.worldToLocalMatrix*r.transform.localToWorldMatrix}).ToArray(),true,true);
                parts.Add(part); materials.Add(group.Key);
            }
            var mesh=new Mesh {name="RearAccessTreads"}; mesh.CombineMeshes(parts.Select(m=>new CombineInstance {mesh=m,transform=Matrix4x4.identity}).ToArray(),false,true);
            const string path="Assets/_Project/Art/VehicleMechanics/RearAccessTreads.asset"; var stored=GeneratedMeshAsset.Store(mesh,path);
            foreach(var part in parts) UnityEngine.Object.DestroyImmediate(part);
            foreach(var surface in surfaces) {UnityEngine.Object.DestroyImmediate(surface.GetComponent<MeshFilter>()); UnityEngine.Object.DestroyImmediate(surface);}
            steps.gameObject.AddComponent<MeshFilter>().sharedMesh=stored; steps.gameObject.AddComponent<MeshRenderer>().sharedMaterials=materials.ToArray();
            var guard=steps.gameObject.AddComponent<BoxCollider>(); guard.center=new Vector3(0,.26f,3.0f); guard.size=new Vector3(1.42f,.52f,.78f); guard.isTrigger=true; vehicle.rearStepCollision=guard;
            UnityEngine.Object.FindAnyObjectByType<PlayerInteractor>().GetComponent<CharacterController>().stepOffset=.28f;
            foreach(var item in UnityEngine.Object.FindObjectsByType<CarryableItem>())
            {
                var local=vehicle.transform.InverseTransformPoint(item.transform.position);
                if(item.Loaded || Mathf.Abs(local.x)>.95f || local.z<2.50f || local.z>3.75f || local.y>.65f) continue;
                if(item.definitionId=="bin") item.transform.position=new Vector3(2.10f,.04f,-4.20f);
                if(item.definitionId=="fuel") item.transform.position=new Vector3(2.10f,.04f,-3.55f);
            }
        }
        static void EnsureRearSurfaces(ServiceVehicle vehicle,VehicleDoor[] doors)
        {
            var art=vehicle.transform.Find("OriginalVan_CompleteShell");
            foreach(int side in new[]{-1,1})
            {
                string suffix=side<0?"Right":"Left";
                var door=doors.First(d=>d.name=="RearDoor_"+suffix);
                if(door.transform.Find("DoorSurface_RearInterior"+suffix)) continue;
                var region=new Bounds(new Vector3(side*.47f,1.56f,2.70f),new Vector3(.94f,1.96f,.46f));
                foreach(var renderer in art.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("Complete_V22") || r.name.StartsWith("Complete_V01 |")))
                    DoorMeshPartition.Extract(renderer.GetComponent<MeshFilter>(),region,vehicle.transform,door.transform,(renderer.name.StartsWith("Complete_V22")?"RearInterior":"RearBody")+suffix,true);
            }
        }
        static void Solid(Transform parent,string name,Vector3 center,Vector3 size)
        {
            var child=parent.Find(name); if(!child) {child=new GameObject(name).transform; child.SetParent(parent,false);}
            child.localPosition=center; var collider=child.GetComponent<BoxCollider>(); if(!collider) collider=child.gameObject.AddComponent<BoxCollider>(); collider.size=size;
        }
        static VehicleDoor[] BuildDoors(ServiceVehicle vehicle,GarageLift lift)
        {
            var art=vehicle.transform.Find("OriginalVan_CompleteShell");
            if(PrefabUtility.IsPartOfPrefabInstance(art)) PrefabUtility.UnpackPrefabInstance(art.gameObject,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            var originals=art.GetComponentsInChildren<MeshRenderer>(); var result=new System.Collections.Generic.List<VehicleDoor>();
            foreach(bool rear in new[]{false,true}) foreach(int side in new[]{-1,1})
            {
                var panel=originals.First(r=>r.name.StartsWith(rear?"Complete_V21":"Complete_V03") && Mathf.Sign(r.bounds.center.x-vehicle.transform.position.x)==side);
                var hinge=new GameObject((rear?"RearDoor_":"FrontDoor_")+(side<0?"Right":"Left")); hinge.transform.SetParent(vehicle.transform,false);
                hinge.transform.localPosition=new Vector3(side*(rear?1.0f:1.025f),0,rear?2.73f:-1.54f);
                var collider=hinge.AddComponent<BoxCollider>(); collider.center=hinge.transform.InverseTransformPoint(panel.bounds.center); collider.size=panel.bounds.size;
                var rigidbody=hinge.AddComponent<Rigidbody>(); rigidbody.isKinematic=true; rigidbody.useGravity=false;
                var door=hinge.AddComponent<VehicleDoor>(); door.vehicle=vehicle; door.lift=lift; door.openAngle=side*(rear?108:74);
                door.displayName=(rear?"Arka ":"Ön ")+(side<0?"sağ kapıyı":"sol kapıyı");
                foreach(var renderer in originals.Where(r=>rear?r==panel:
                    (r.name.StartsWith("Complete_V03") || r.name.StartsWith("Complete_V05") || r.name.StartsWith("Complete_V06") || r.name.StartsWith("Complete_V07")) && Mathf.Sign(r.bounds.center.x-vehicle.transform.position.x)==side))
                    renderer.transform.SetParent(hinge.transform,true);
                if(!rear)
                {
                    var region=new Bounds(new Vector3(side*.94f,1.47f,-1.065f),new Vector3(.32f,1.76f,.955f));
                    foreach(var renderer in originals.Where(r=>r.name.StartsWith("Complete_V01 |") || r.name.StartsWith("Complete_V20") || (r.name.StartsWith("Complete_V08") && Mathf.Sign(r.bounds.center.x-vehicle.transform.position.x)==side)))
                        DoorMeshPartition.Extract(renderer.GetComponent<MeshFilter>(),region,vehicle.transform,hinge.transform,(renderer.name.StartsWith("Complete_V01")?"Body":renderer.name.StartsWith("Complete_V20")?"Stripe":"Trim")+(side<0?"Right":"Left"));
                }
                result.Add(door);
            }
            return result.ToArray();
        }
        static void ConfigurePerformance()
        {
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            pipeline.shadowDistance=22; pipeline.shadowCascadeCount=2; pipeline.supportsCameraOpaqueTexture=false;
            var data=new SerializedObject(pipeline); data.FindProperty("m_SoftShadowQuality").intValue=2; data.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(pipeline);
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            foreach(var feature in renderer.rendererFeatures)
            {
                var settings=new SerializedObject(feature); var downsample=settings.FindProperty("m_Settings.Downsample");
                if(downsample!=null) {downsample.boolValue=true; settings.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(feature);}
            }
            var camera=UnityEngine.Object.FindAnyObjectByType<PlayerInteractor>().viewCamera;
            camera.farClipPlane=45; var cameraData=camera.GetUniversalAdditionalCameraData(); cameraData.antialiasing=AntialiasingMode.FastApproximateAntialiasing;
            foreach(var light in UnityEngine.Object.FindObjectsByType<Light>())
                if(light.name=="Garage_LED_Light_01" || light.name=="Garage_LED_Light_10") light.shadows=LightShadows.None;
            int tiny=0;
            foreach(var mesh in UnityEngine.Object.FindObjectsByType<MeshRenderer>())
            {
                if(mesh.bounds.center.x>.3f && mesh.bounds.size.magnitude<.11f && !mesh.GetComponentInParent<InteractableBehaviour>()) {mesh.shadowCastingMode=ShadowCastingMode.Off; tiny++;}
            }
            foreach(var guid in AssetDatabase.FindAssets("t:Model",new[]{"Assets/_Project/Art/Models"}))
            {
                var importer=AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as ModelImporter;
                if(importer && !importer.isReadable) {importer.isReadable=true; importer.SaveAndReimport();}
            }
            File.WriteAllText("Logs/garage_optimization_settings.txt","Shadow cascades: 4 -> 2\nShadow distance: 50 -> 22 m\nSSAO: half resolution\nOpaque scene copy: disabled\nGarage shadowed spots: 4 -> 2\nTiny garage shadow casters disabled: "+tiny+"\nRender scale: unchanged, 1.0\nAA: FXAA\nStatic geometry batching: runtime, moving parts excluded\n");
        }
        public static void Inspect()
        {
            var rows=UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Where(r=>r.name.StartsWith("T29") || r.name.StartsWith("T28C") || r.GetComponentInParent<ServiceVehicle>())
                .Select(r=>r.name+"\t"+r.bounds.center.ToString("F4")+"\t"+r.bounds.size.ToString("F4")+"\t"+r.transform.position.ToString("F4"));
            Directory.CreateDirectory("Logs"); File.WriteAllLines("Logs/garage_systems_geometry.tsv",rows);
            File.WriteAllText("Logs/garage_systems_baseline.txt",
                "Renderers: "+UnityEngine.Object.FindObjectsByType<MeshRenderer>().Length+"\n"+
                "Active lights: "+UnityEngine.Object.FindObjectsByType<Light>().Count(x=>x.enabled)+"\n"+
                "Shadowed lights: "+UnityEngine.Object.FindObjectsByType<Light>().Count(x=>x.enabled && x.shadows!=LightShadows.None)+"\n"+
                "Pipeline: "+GraphicsSettings.currentRenderPipeline.name+"\n"+
                string.Join("\n",UnityEngine.Object.FindObjectsByType<Light>().Where(x=>x.enabled).Select(x=>x.name+" / "+x.type+" / "+x.shadows)));
        }
    }
}
