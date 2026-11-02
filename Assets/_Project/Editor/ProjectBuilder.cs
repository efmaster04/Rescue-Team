using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CompanySim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CompanySimEditor
{
    public static class ProjectBuilder
    {
        const string Root = "Assets/_Project";
        static readonly Dictionary<string,Material> Palette = new Dictionary<string,Material>();

        [MenuItem("Company/Build Office and Garage")]
        public static void Build()
        {
            AssetDatabase.Refresh();
            Directory.CreateDirectory(Root+"/Art/Materials");
            Directory.CreateDirectory(Root+"/Data/Jobs");
            Directory.CreateDirectory(Root+"/Data/Modules");
            Directory.CreateDirectory(Root+"/Data/Equipment");
            Directory.CreateDirectory(Root+"/Prefabs/Equipment");
            Directory.CreateDirectory(Root+"/Prefabs/Modules");
            Directory.CreateDirectory(Root+"/Prefabs/Vehicles");
            Directory.CreateDirectory(Root+"/Prefabs/Station");
            Directory.CreateDirectory(Root+"/Scenes");
            BuildPalette();
            var modules = BuildModules();
            var items = BuildEquipment();
            var jobs = BuildJobs();
            var economy = Asset<EconomyConfig>(Root+"/Data/Economy.asset");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "OfficeGarage";
            var world = Model("Environment/Environment_OfficeGarage");
            var env = (GameObject)PrefabUtility.InstantiatePrefab(world);
            env.name = "01_Environment_OfficeGarage";
            env.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            PrefabUtility.UnpackPrefabInstance(env,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            // The environment export bakes each source mesh into its original world
            // coordinates before FBX conversion, like the original van export.
            ApplyPalette(env);
            BuildMonitorScreen(env);
            foreach (var r in env.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("COL_")) r.enabled=false;
            BuildCollision();
            BuildLighting();

            var catalogGo = new GameObject("02_CompanyCatalog");
            var catalog = catalogGo.AddComponent<CompanyCatalog>();
            catalog.economy=economy;
            catalog.jobs=jobs; catalog.modules=modules; catalog.equipment=items;
            var stateGo = new GameObject("03_CompanyRuntime");
            var runtime=stateGo.AddComponent<CompanyRuntime>(); runtime.catalog=catalog;

            var van=BuildVehiclePrefab();
            van.name="04_Vehicle_ServiceVan_Original";
            // V01–V22 in the supplied .blend is parked at (4.65, -0.18, 0).
            // Blender (x,y,z) maps to gameplay (x,z,y), including its front -Z.
            van.transform.SetPositionAndRotation(new Vector3(4.65f,0,-.18f),Quaternion.identity);
            var vehicle=van.GetComponent<ServiceVehicle>();
            vehicle.catalog=catalog; vehicle.runtime=runtime;
            runtime.vehicle=vehicle;

            // The original twin lift assemblies are part of the environment again.
            var stationRoot=new GameObject("05_Garage_ModuleStation");
            stationRoot.transform.position=new Vector3(4.65f,0,-.18f);
            var controls = new GameObject("ModuleStation_ControlPanel");
            controls.transform.SetParent(stationRoot.transform,false);
            controls.transform.localPosition=new Vector3(2.45f,1.20f,-1.95f);
            var controlCol=controls.AddComponent<BoxCollider>(); controlCol.size=new Vector3(.7f,1.7f,.7f);
            controlCol.isTrigger=true;
            var station=controls.AddComponent<ModuleStation>(); station.vehicle=vehicle;
            var stand=new GameObject("ModuleStorageStand").transform;
            stand.SetParent(stationRoot.transform,false); stand.localPosition=new Vector3(2.8f,1.15f,1.8f);
            station.storageStand=stand;

            var delivery=new GameObject("DeliveryPoint").transform;
            delivery.position=new Vector3(8.0f,.35f,-3.65f); runtime.deliveryPoint=delivery;
            var equipmentRoot=new GameObject("06_PhysicalEquipment").transform;
            PlaceInitialEquipment(items,equipmentRoot);

            var player=BuildPlayer();
            var uiGo=new GameObject("07_CompanyUI");
            var ui=uiGo.AddComponent<ComputerUI>();
            ui.Runtime=runtime; ui.catalog=catalog; ui.player=player; ui.station=station;
            player.ui=ui; station.ui=ui;
            var feedback=uiGo.AddComponent<AudioFeedback>();
            feedback.uiClick=Audio("ui_click"); feedback.notification=Audio("notification");
            feedback.garageMotor=Audio("garage_motor"); feedback.itemDrop=Audio("metal_drop");
            ui.audioFeedback=feedback;
            OfficePresentationBuilder.ConfigureAudio(feedback);
            AddInteractionTargets(env,ui,runtime);
            AddUpgrades(env);
            GarageRefinementBuilder.ConfigureDoors();
            GarageRefinementBuilder.ConfigureGarage();
            OfficePresentationBuilder.BakeReflections();

            EditorSceneManager.SaveScene(scene,Root+"/Scenes/OfficeGarage.unity");
            GarageSystemsBuilder.Apply();
            EditorBuildSettings.scenes=new[] {
                new EditorBuildSettingsScene(Root+"/Scenes/OfficeGarage.unity",true),
                new EditorBuildSettingsScene("Assets/Scenes/SampleScene.unity",true)
            };
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("Company OfficeGarage scene built: "+Root+"/Scenes/OfficeGarage.unity");
        }

        static GameObject Model(string rel) => AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Art/Models/"+rel+".fbx");
        static AudioClip Audio(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>(Root+"/Art/Audio/"+name+".wav");
        static string Safe(string name) => new string(name.Select(c=>char.IsLetterOrDigit(c)||c=='_'?c:'_').ToArray());
        static float Num(string text) => float.Parse(text,CultureInfo.InvariantCulture);
        static void BuildPalette()
        {
            Palette.Clear();
            var lines=File.ReadAllLines(Root+"/Art/material_palette.tsv");
            var shader=Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new Exception("URP Lit shader not found");
            foreach(var line in lines)
            {
                var c=line.Split('\t'); if(c.Length<8) continue;
                string path=Root+"/Art/Materials/"+Safe(c[0])+".mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material) { material=new Material(shader); AssetDatabase.CreateAsset(material,path); }
                material.shader=shader; material.name=c[0];
                material.SetColor("_BaseColor",new Color(Num(c[1]),Num(c[2]),Num(c[3]),Num(c[6])));
                material.SetFloat("_Smoothness",1-Num(c[4])); material.SetFloat("_Metallic",Num(c[5]));
                if (!string.IsNullOrEmpty(c[7]))
                {
                    var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Art/Textures/"+c[7]);
                    if(tex) material.SetTexture("_BaseMap",tex);
                }
                EditorUtility.SetDirty(material);
                Palette[c[0]]=material;
            }
        }
        static void ApplyPalette(GameObject go)
        {
            foreach(var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if(renderer.name.StartsWith("COL_")) { renderer.enabled=false; continue; }
                var mats=renderer.sharedMaterials;
                for(int i=0;i<mats.Length;i++)
                {
                    if(!mats[i]) continue;
                    string name=mats[i].name.Replace(" (Instance)","");
                    if(Palette.TryGetValue(name,out var replacement)) mats[i]=replacement;
                }
                renderer.sharedMaterials=mats;
            }
        }
        static void MakeVanMaterialsTwoSided(GameObject van)
        {
            var folder=Root+"/Art/Materials/Van";
            Directory.CreateDirectory(folder);
            foreach(var renderer in van.GetComponentsInChildren<Renderer>(true))
            {
                var mats=renderer.sharedMaterials;
                for(int i=0;i<mats.Length;i++)
                {
                    if(!mats[i]) continue;
                    var source=mats[i];
                    if(source.name.IndexOf("glass",StringComparison.OrdinalIgnoreCase)>=0 ||
                       source.name.IndexOf("glazing",StringComparison.OrdinalIgnoreCase)>=0) continue;
                    var path=folder+"/"+Safe(source.name)+".mat";
                    var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(!material)
                    {
                        material=new Material(source);
                        AssetDatabase.CreateAsset(material,path);
                    }
                    else material.CopyPropertiesFromMaterial(source);
                    // The supplied shell has single-surface panels; URP otherwise
                    // culls the side seen from the office doorway.
                    material.SetFloat("_Cull",0f);
                    material.doubleSidedGI=true;
                    EditorUtility.SetDirty(material);
                    mats[i]=material;
                }
                renderer.sharedMaterials=mats;
            }
        }
        static T Asset<T>(string path) where T:ScriptableObject
        {
            // Early generated definitions were written before each ScriptableObject had
            // its own MonoScript asset. Unity stored a null script reference in YAML.
            if (File.Exists(path) && File.ReadAllText(path).Contains("m_Script: {fileID: 0}"))
                AssetDatabase.DeleteAsset(path);
            var asset=AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) { asset=ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset,path); }
            return asset;
        }
        static ModuleDefinition[] BuildModules()
        {
            var specs=new[]{
                ("cargo","Nakliye Kasası","Cargo",1150f,420f,8,900,"Module_Cargo"),
                ("towing","Çekici Modülü","Towing",1450f,250f,4,1450,"Module_Towing"),
                ("fire","Yangın Modülü","Fire",1900f,310f,5,1900,"Module_Fire"),
                ("tech","Teknik Servis Modülü","Technical",1600f,350f,6,1550,"Module_TechService"),
                ("water","Su Tahliye Modülü","Water",1700f,290f,4,1750,"Module_WaterPump")
            };
            var result=new List<ModuleDefinition>();
            foreach(var (id,label,category,weight,payload,slots,price,model) in specs)
            {
                var prefab=VisualPrefab("Modules/"+model,"Modules/"+model);
                var data=Asset<ModuleDefinition>(Root+"/Data/Modules/"+id+".asset");
                data.id=id; data.displayName=label; data.category=category; data.weightKg=weight;
                data.payloadKg=payload; data.cargoSlots=slots; data.price=price;
                data.connections="ISO-style four-point lock; hydraulic/electric service";
                data.visualPrefab=prefab; EditorUtility.SetDirty(data); result.Add(data);
            }
            return result.ToArray();
        }
        static GameObject VisualPrefab(string source,string dest)
        {
            var fbx=Model(source);
            if(!fbx) throw new Exception("Missing model: "+source);
            var root=new GameObject(Path.GetFileName(dest));
            var art=(GameObject)PrefabUtility.InstantiatePrefab(fbx);
            art.transform.SetParent(root.transform,false);
            art.transform.localPosition=Vector3.zero; art.transform.localRotation=Quaternion.identity;
            ApplyPalette(root);
            var path=Root+"/Prefabs/"+dest+".prefab";
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }
        static EquipmentDefinition[] BuildEquipment()
        {
            var specs=new[]{
                ("toolbox","Alet çantası","Equipment_Toolbox",12f,120,.46f,.32f,.33f),
                ("case","Ekipman çantası","Equipment_Case",9f,100,.55f,.30f,.34f),
                ("parts","Parça kasası","Equipment_PartsCrate",19f,165,.52f,.48f,.52f),
                ("bin","Depo kutusu","Equipment_StorageBin",6f,70,.52f,.32f,.40f),
                ("fuel","Yakıt bidonu","Equipment_FuelCan",11f,95,.32f,.48f,.32f),
                ("grease","Gres kutusu","Equipment_GreaseTin",4f,55,.25f,.23f,.25f),
                ("absorbent","Emici malzeme kovası","Equipment_AbsorbentPail",7f,85,.38f,.42f,.38f),
                ("tire","Yedek lastik","Equipment_SpareTire",24f,210,.85f,.85f,.28f),
                ("jack","Hidrolik kriko","Equipment_FloorJack",35f,330,.66f,.41f,1.03f)
            };
            var result=new List<EquipmentDefinition>();
            foreach(var (id,label,model,weight,price,sx,sy,sz) in specs)
            {
                var fbx=Model("Equipment/"+model);
                if(!fbx) throw new Exception("Missing equipment model "+model);
                var root=new GameObject(model);
                var art=(GameObject)PrefabUtility.InstantiatePrefab(fbx); art.transform.SetParent(root.transform,false);
                art.transform.localPosition=Vector3.zero; art.transform.localRotation=Quaternion.identity;
                ApplyPalette(root);
                var col=root.AddComponent<BoxCollider>(); col.size=new Vector3(sx,sy,sz); col.center=new Vector3(0,sy*.5f,0);
                var rb=root.AddComponent<Rigidbody>(); rb.mass=weight; rb.interpolation=RigidbodyInterpolation.Interpolate;
                var item=root.AddComponent<CarryableItem>(); item.definitionId=id;
                var prefab=PrefabUtility.SaveAsPrefabAsset(root,Root+"/Prefabs/Equipment/"+model+".prefab");
                UnityEngine.Object.DestroyImmediate(root);
                var def=Asset<EquipmentDefinition>(Root+"/Data/Equipment/"+id+".asset");
                def.id=id; def.displayName=label; def.category="Field equipment";
                def.weightKg=weight; def.price=price; def.size=new Vector3(sx,sy,sz); def.visualPrefab=prefab;
                EditorUtility.SetDirty(def); result.Add(def);
            }
            return result.ToArray();
        }
        static JobDefinition[] BuildJobs()
        {
            var specs=new[]{
                ("recovery","Çamura Saplanan Araç","Kuzey Nakliyat","Dere kenarında mahsur kalan servis aracını çıkarın.","Kurtarma","Çamlıdere",18f,850,2,2,75,"towing",new[]{"toolbox","jack"},new[]{"tire"}),
                ("fire","Küçük Atölye Yangını","Mavi Sanayi","Atölyedeki küçük yangına güvenli müdahale için hazırlanın.","Yangın","Sanayi Sitesi",7f,920,3,4,50,"fire",new[]{"case","absorbent"},new[]{"toolbox"}),
                ("moving","Ofis Nakliyesi","Akarsu Mimarlık","Kırılabilir ekipmanı yeni ofise taşıyın.","Nakliye","Kent Merkezi",11f,680,2,1,90,"cargo",new[]{"bin","case"},new[]{"toolbox"}),
                ("generator","Jeneratör Arızası","Çınar Market","Kesilen elektriği tekrar sağlayın.","Teknik Servis","Doğu Mahallesi",12f,760,3,2,70,"tech",new[]{"toolbox","parts"},new[]{"fuel"}),
                ("flood","Bodrum Su Tahliyesi","Yıldız Apartmanı","Bodrum katındaki suyu boşaltmak için pompa hazırlayın.","Su Tahliye","Yıldız Sokak",9f,810,3,3,80,"water",new[]{"absorbent","fuel"},new[]{"case"}),
                ("heavy","Ağır Makine Nakliyesi","Özdemir Metal","Paketlenmiş makine parçasını emniyetle taşıyın.","Ağır Nakliye","Organize Sanayi",22f,1100,4,3,120,"cargo",new[]{"parts","jack"},new[]{"toolbox"})
            };
            var result=new List<JobDefinition>();
            foreach(var (id,title,client,desc,category,location,distance,pay,diff,risk,duration,module,required,recommended) in specs)
            {
                var job=Asset<JobDefinition>(Root+"/Data/Jobs/"+id+".asset");
                job.id=id; job.title=title; job.client=client; job.description=desc; job.category=category;
                job.location=location; job.distanceKm=distance; job.payment=pay; job.difficulty=diff;
                job.risk=risk; job.durationMinutes=duration; job.requiredModule=module;
                job.minimumReputation=id=="fire"?4:id=="heavy"?8:0;
                job.requiredEquipment=required; job.recommendedEquipment=recommended;
                job.specialTerms="Ekipmanın sabitlenmiş ve çalışır durumda olması istenir.";
                job.contractTitle="Özenli teslimat";
                job.contractBonus=Mathf.RoundToInt(pay*.15f);
                job.contractMaxEquipmentWear=10;
                job.contractMaxVehicleWear=10;
                EditorUtility.SetDirty(job); result.Add(job);
            }
            return result.ToArray();
        }
        static GameObject BuildVehiclePrefab()
        {
            var model=Model("Vehicles/Vehicle_ServiceVan_Complete");
            if(!model) throw new Exception("Missing original service van model");
            var root=new GameObject("Vehicle_ServiceVan_Original");
            var art=(GameObject)PrefabUtility.InstantiatePrefab(model);
            art.transform.SetParent(root.transform,false);
            art.transform.localPosition=Vector3.zero;
            art.transform.localRotation=Quaternion.identity;
            VanRestorationBuilder.ApplyVisuals(root);
            var service=root.AddComponent<ServiceVehicle>();
            var col=root.AddComponent<BoxCollider>(); col.center=new Vector3(0,1.35f,-.12f); col.size=new Vector3(2.45f,2.7f,5.83f);
            var socket=new GameObject("SOCKET_ModuleMount").transform;
            socket.SetParent(root.transform,false); socket.localPosition=new Vector3(0,2.79f,.12f);
            service.moduleSocket=socket;
            service.cargoSockets=new Transform[8];
            for(int i=0;i<8;i++)
            {
                var slot=new GameObject("SOCKET_Cargo_"+(i+1).ToString("00")).transform;
                slot.SetParent(root.transform,false);
                slot.localPosition=new Vector3(i%2==0?-.44f:.44f,1.05f,.36f+(i/2)*.53f);
                service.cargoSockets[i]=slot;
            }
            PrefabUtility.SaveAsPrefabAsset(root,Root+"/Prefabs/Vehicles/Vehicle_ServiceVan_Original.prefab");
            return root;
        }
        static Material VanLensMaterial()
        {
            var path=Root+"/Art/Materials/Van_HeadlampLens.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material=new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material,path);
            }
            material.SetColor("_BaseColor",new Color(.94f,.88f,.70f));
            material.SetColor("_EmissionColor",new Color(.38f,.34f,.24f));
            material.SetFloat("_Smoothness",.82f);
            material.SetFloat("_Metallic",.06f);
            material.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
            return material;
        }
        static void VanDetail(Transform parent,string name,Vector3 position,Vector3 size,Material material)
        {
            var detail=GameObject.CreatePrimitive(PrimitiveType.Cube);
            detail.name=name;
            detail.transform.SetParent(parent,false);
            detail.transform.localPosition=position;
            detail.transform.localScale=size;
            detail.GetComponent<Renderer>().sharedMaterial=material;
            UnityEngine.Object.DestroyImmediate(detail.GetComponent<Collider>());
        }
        static void AddVanExteriorDetails(GameObject van)
        {
            // The supplied lamp and rear-door meshes are present but their shallow
            // faces disappear into the body after import. Fit small visible faces to
            // their imported bounds without changing the source vehicle or office.
            var renderers=van.GetComponentsInChildren<Renderer>(true);
            var dark=Palette["09 Satin charcoal trim"];
            var lens=VanLensMaterial();
            foreach(var r in renderers.Where(x=>x.name.Contains("V11 |")))
            {
                var b=r.bounds;
                var face=b.min.z-.028f;
                var side=b.center.x<0?"Right":"Left";
                VanDetail(van.transform,"RestoredHeadlampBezel_"+side,
                    new Vector3(b.center.x,b.center.y,face),new Vector3(.37f,.23f,.022f),dark);
                VanDetail(van.transform,"RestoredHeadlampLens_"+side,
                    new Vector3(b.center.x,b.center.y,face-.017f),new Vector3(.30f,.15f,.018f),lens);
            }
            foreach(var r in renderers.Where(x=>x.name.Contains("V21 |")))
            {
                var x=Mathf.Sign(r.bounds.center.x)*.10f;
                var z=r.bounds.max.z+.012f;
                var side=x<0?"Right":"Left";
                VanDetail(van.transform,"RestoredRearLatchPlate_"+side,
                    new Vector3(x,1.44f,z),new Vector3(.09f,.18f,.014f),dark);
                VanDetail(van.transform,"RestoredRearLatchGrip_"+side,
                    new Vector3(x,1.47f,z+.032f),new Vector3(.12f,.034f,.045f),
                    Palette["11 Warm gray metal"]);
            }
        }
        static PlayerInteractor BuildPlayer()
        {
            var go=new GameObject("08_Player_Worker");
            go.transform.SetPositionAndRotation(new Vector3(-2.8f,.11f,-1.35f),Quaternion.identity);
            var cc=go.AddComponent<CharacterController>();
            cc.height=PlayerInteractor.BodyHeight; cc.radius=PlayerInteractor.BodyRadius;
            cc.center=new Vector3(0,PlayerInteractor.BodyHeight*.5f,0); cc.stepOffset=.22f; cc.skinWidth=.025f;
            var player=go.AddComponent<PlayerInteractor>();
            var camGo=new GameObject("FirstPersonCamera"); camGo.transform.SetParent(go.transform,false);
            camGo.transform.localPosition=new Vector3(0,PlayerInteractor.EyeHeight,0);
            var camera=camGo.AddComponent<Camera>(); camera.fieldOfView=62; camera.nearClipPlane=.045f;
            OfficePresentationBuilder.ConfigureCamera(camera);
            camGo.AddComponent<AudioListener>(); player.viewCamera=camera;
            var workerModel=Model("Characters/Character_ServiceWorker");
            if(workerModel)
            {
                var visual=(GameObject)PrefabUtility.InstantiatePrefab(workerModel);
                visual.name="Character_ServiceWorker_Visual";
                visual.transform.SetParent(go.transform,false);
                visual.transform.localPosition=Vector3.zero;
                ApplyPalette(visual);
                var renderers=visual.GetComponentsInChildren<Renderer>();
                var bounds=renderers[0].bounds;
                foreach(var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                visual.transform.localScale*=PlayerInteractor.BodyHeight/bounds.size.y;
                bounds=renderers[0].bounds;
                foreach(var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                visual.transform.localPosition+=Vector3.up*(go.transform.position.y-bounds.min.y);
                // The first-person camera sits inside this model. Keep its shadow,
                // but never draw its torso, face, or helmet across the player's view.
                foreach(var r in visual.GetComponentsInChildren<Renderer>())
                    r.shadowCastingMode=ShadowCastingMode.ShadowsOnly;
            }
            return player;
        }
        static void BuildMonitorScreen(GameObject environment)
        {
            const string path=Root+"/Art/Materials/Office_Monitor_Black.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material) { material=new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(material,path); }
            material.SetColor("_BaseColor",new Color(.002f,.003f,.004f,1));
            material.SetTexture("_BaseMap",null);
            foreach(var renderer in environment.GetComponentsInChildren<Renderer>())
                if(renderer.name.StartsWith("O08"))
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(_=>material).ToArray();
            EditorUtility.SetDirty(material);
        }
        static void PlaceInitialEquipment(EquipmentDefinition[] defs,Transform parent)
        {
            var positions=new[]{
                ("toolbox",new Vector3(1.25f,.40f,3.10f)),
                ("jack",new Vector3(7.35f,.08f,-2.0f)),
                ("case",new Vector3(2.20f,.40f,3.10f)),
                ("parts",new Vector3(3.05f,.42f,3.15f)),
                ("bin",new Vector3(2.10f,.04f,-4.20f)),
                ("fuel",new Vector3(2.10f,.04f,-3.55f)),
                ("absorbent",new Vector3(6.10f,.43f,3.14f)),
                ("tire",new Vector3(7.55f,.42f,3.18f))
            };
            int n=0;
            foreach(var (id,position) in positions)
            {
                var def=defs.FirstOrDefault(x=>x && x.id==id);
                if(!def || !def.visualPrefab) throw new Exception("Initial equipment unavailable: "+id);
                var go=(GameObject)PrefabUtility.InstantiatePrefab(def.visualPrefab);
                go.transform.SetParent(parent,true); go.transform.position=position;
                go.name="Equipment_"+id+"_01";
                go.GetComponent<CarryableItem>().uid="initial_"+(++n).ToString("00");
            }
        }
        static void AddInteractionTargets(GameObject env,ComputerUI ui,CompanyRuntime runtime)
        {
            var office=new GameObject("Office_Computer_Main");
            office.transform.position=new Vector3(-2.85f,1.10f,.84f);
            var pcCollider=office.AddComponent<BoxCollider>(); pcCollider.size=new Vector3(.55f,.52f,.25f);
            pcCollider.isTrigger=true;
            office.AddComponent<OfficeComputer>().ui=ui;
            var repair=new GameObject("Garage_RepairWorkbench_01");
            repair.transform.position=new Vector3(7.95f,1.0f,.20f);
            var repairTarget=repair.AddComponent<BoxCollider>(); repairTarget.size=new Vector3(.9f,1.5f,2.9f);
            repairTarget.isTrigger=true;
            repair.AddComponent<RepairWorkbench>().runtime=runtime;
            var dispatch=new GameObject("Garage_DispatchBoard");
            dispatch.transform.position=new Vector3(.28f,1.5f,-1.55f);
            var dispatchTarget=dispatch.AddComponent<BoxCollider>(); dispatchTarget.size=new Vector3(.42f,.9f,.95f);
            dispatchTarget.isTrigger=true;
            dispatch.AddComponent<DispatchBoard>().ui=ui;
            foreach(var (prefix, hinge, angle) in new[]{
                ("A11_", new Vector3(0,0,-3.33f), 95f),
                ("A13_", new Vector3(-3.64f,0,-5.17f), -95f)})
            {
                var parts=FindParts(env.transform,prefix)
                    .Where(x=>!x.name.Contains("hinge barrel") && !x.name.Contains("front door hinge"))
                    .ToList();
                if(parts.Count==0) { Debug.LogWarning("Door missing in FBX: "+prefix); continue; }
                // Source handles are A15 children, not A11/A13 meshes. The front
                // door notice is A34. Bind them to the same moving leaf.
                var hardwareCenter=prefix=="A11_" ? new Vector2(0f,-2.55f) :
                    new Vector2(-2.80f,-5.17f);
                parts.AddRange(FindParts(env.transform,"A15_").Where(x=>
                {
                    var center=x.GetComponent<Renderer>().bounds.center;
                    return Vector2.Distance(new Vector2(center.x,center.z),hardwareCenter)<.38f;
                }));
                if(prefix=="A13_") parts.AddRange(FindParts(env.transform,"A34_").Where(x=>
                {
                    var center=x.GetComponent<Renderer>().bounds.center;
                    return center.z < -5.05f && center.z > -5.32f;
                }));
                var pivot=new GameObject("OfficeDoor_"+prefix.Substring(0,3));
                pivot.transform.position=hinge;
                foreach(var part in parts) part.SetParent(pivot.transform,true);
                var renderers=pivot.GetComponentsInChildren<Renderer>();
                var bounds=renderers[0].bounds;
                foreach(var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                var col=pivot.AddComponent<BoxCollider>();
                col.center=pivot.transform.InverseTransformPoint(bounds.center);
                col.size=bounds.size;
                pivot.AddComponent<OfficeDoor>().openAngle=angle;
            }
        }
        static Transform FindDeep(Transform root,string prefix)
        {
            foreach(Transform child in root)
            {
                if(child.name.StartsWith(prefix)) return child;
                var found=FindDeep(child,prefix); if(found) return found;
            }
            return null;
        }
        static List<Transform> FindParts(Transform root,string prefix)
        {
            var parts=new List<Transform>();
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
                if(renderer.name.StartsWith(prefix)) parts.Add(renderer.transform);
            return parts;
        }
        static GameObject CloneParts(Transform source,string prefix,string name,Vector3 targetCenter)
        {
            var parts=FindParts(source,prefix);
            if(parts.Count==0) return null;
            var bounds=parts[0].GetComponent<Renderer>().bounds;
            foreach(var part in parts.Skip(1)) bounds.Encapsulate(part.GetComponent<Renderer>().bounds);
            var root=new GameObject(name);
            foreach(var part in parts) UnityEngine.Object.Instantiate(part.gameObject,root.transform);
            root.transform.position=targetCenter-new Vector3(bounds.center.x,0,bounds.center.z);
            return root;
        }
        static void AddUpgrades(GameObject env)
        {
            var shelf=CloneParts(env.transform,"G01_","Upgrade_ExtraStorageRack",
                new Vector3(.65f,0,-1.45f));
            if(shelf)
            {
                shelf.AddComponent<UpgradeVisual>().upgradeId="storage";
                shelf.GetComponent<UpgradeVisual>().PrepareLayout();
                shelf.SetActive(false);
            }
            var lamp=new GameObject("Upgrade_WarmWorkshopLight");
            lamp.transform.position=new Vector3(4.7f,4.45f,-1.2f);
            var light=lamp.AddComponent<Light>(); light.type=LightType.Point; light.range=11; light.intensity=8;
            light.color=new Color(1f,.81f,.59f);
            lamp.AddComponent<UpgradeVisual>().upgradeId="lighting"; lamp.SetActive(false);
            var board=CloneParts(env.transform,"O28_","Upgrade_OperationsBoard",
                new Vector3(-.48f,1.6f,-2.4f));
            if(!board) board=new GameObject("Upgrade_OperationsBoard");
            board.AddComponent<UpgradeVisual>().upgradeId="operations"; board.SetActive(false);
        }
        static void BuildCollision()
        {
            var root=new GameObject("00_WorldCollision").transform;
            Box(root,"Ground",new Vector3(0,-.16f,0),new Vector3(200,.30f,200));
            // Match the supplied A01/A02/A03/A39/A40 wall bounds. The earlier
            // approximate boxes stood up to four metres away from visible walls.
            Box(root,"Office_West",new Vector3(-4.80f,1.58f,-2.0f),new Vector3(.24f,3.16f,6.40f));
            Box(root,"Office_Rear",new Vector3(-2.40f,1.58f,1.205f),new Vector3(4.80f,3.16f,.23f));
            Box(root,"Office_Front_Left",new Vector3(-4.26f,1.58f,-5.18f),new Vector3(1.08f,3.16f,.22f));
            Box(root,"Office_Front_Right",new Vector3(-1.32f,1.58f,-5.18f),new Vector3(2.64f,3.16f,.22f));
            Box(root,"Partition_Front",new Vector3(0,1.63f,-4.30f),new Vector3(.16f,3.26f,1.80f));
            Box(root,"Partition_Rear",new Vector3(0,1.63f,-.58f),new Vector3(.16f,3.26f,3.56f));
            Box(root,"Garage_Left_Rear",new Vector3(0,2f,3.20f),new Vector3(.28f,4f,4.00f));
            Box(root,"Garage_East",new Vector3(9.25f,2f,0),new Vector3(.28f,4f,10.40f));
            Box(root,"Garage_Rear",new Vector3(4.50f,2f,5.20f),new Vector3(9.0f,4f,.28f));
            // Large furniture and machinery use a few solid volumes. Decorative
            // floor details stay non-colliding so the controller cannot snag on
            // bolts, lift plates, scattered tools or shelf trim.
            Box(root,"Office_Desk",new Vector3(-2.845f,.385f,.72f),new Vector3(1.79f,.77f,.74f));
            Box(root,"Office_Chair",new Vector3(-2.77f,.575f,-.185f),new Vector3(.76f,1.15f,.71f));
            Box(root,"Office_FilingCabinet",new Vector3(-.68f,1.055f,.40f),new Vector3(.68f,2.11f,.62f));
            Box(root,"Office_DocumentShelf",new Vector3(-.305f,1.245f,-1.39f),new Vector3(.45f,1.25f,1.40f));
            Box(root,"Garage_RearRack_01",new Vector3(1.42f,1.15f,4.39f),new Vector3(2.16f,2.30f,.68f));
            Box(root,"Garage_RearRack_02",new Vector3(4.08f,1.15f,4.39f),new Vector3(2.16f,2.30f,.68f));
            Box(root,"Garage_RearRack_03",new Vector3(6.74f,1.15f,4.39f),new Vector3(2.16f,2.30f,.68f));
            Box(root,"Garage_RightRack",new Vector3(8.75f,1.11f,-2.85f),new Vector3(.58f,2.22f,1.42f));
            Box(root,"Garage_Workbench",new Vector3(8.465f,.475f,.25f),new Vector3(.91f,.95f,4.34f));
            Box(root,"Garage_LiftTower_01",new Vector3(3.04f,1.65f,.46f),new Vector3(.40f,3.30f,.51f));
            Box(root,"Garage_LiftTower_02",new Vector3(6.26f,1.65f,.46f),new Vector3(.40f,3.30f,.51f));
            Box(root,"Garage_DepotTrunk",new Vector3(8.15f,.33f,3.425f),new Vector3(.76f,.66f,.61f));
            Box(root,"Garage_FrontBollard_01",new Vector3(.47f,.565f,-4.93f),new Vector3(.32f,1.13f,.32f));
            Box(root,"Garage_FrontBollard_02",new Vector3(8.73f,.565f,-4.93f),new Vector3(.32f,1.13f,.32f));
        }
        static void Box(Transform root,string name,Vector3 pos,Vector3 size)
        {
            var go=new GameObject(name); go.transform.SetParent(root,false); go.transform.position=pos;
            go.AddComponent<BoxCollider>().size=size;
        }
        static void BuildLighting()
        {
            OfficePresentationBuilder.ConfigureLighting();
        }
    }
}
