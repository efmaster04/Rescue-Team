using System;
using System.IO;
using System.Linq;
using CompanySim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
using Geometry=CompanySimEditor.WorkshopCraftBuilder.Geometry;

namespace CompanySimEditor
{
    public static class VehicleDetailBuilder
    {
        const string Folder="Assets/_Project/Art/VehicleDetail";
        const string VanModel="Assets/_Project/Art/Models/Vehicles/Vehicle_ServiceVan_Complete.fbx";
        const string VanPrefab="Assets/_Project/Prefabs/Vehicles/Vehicle_ServiceVan_Original.prefab";
        const string TyrePrefab="Assets/_Project/Prefabs/Equipment/Equipment_SpareTire.prefab";
        static Material seal,lens,flute,metal,recess;
        [MenuItem("Company/Refine Vehicle Front and Equipment")]
        public static void Apply()
        {
            if(EditorApplication.isPlaying) throw new Exception("Stop Play Mode before editing vehicle assets.");
            EditorSceneManager.OpenScene(GarageSystemsBuilder.ScenePath);
            Configure();
            var probe=GameObject.Find("GarageReflection").GetComponent<ReflectionProbe>();
            string path=AssetDatabase.GetAssetPath(probe.customBakedTexture);
            if(!Lightmapping.BakeReflectionProbe(probe,path)) throw new Exception("Garage reflection bake failed.");
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            probe.customBakedTexture=AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets(); Capture();
            // Preview-scene instantiation can mark the active scene dirty even
            // after the temporary object is removed. Save after capture cleanup.
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
        public static void ApplyAndValidate() {Apply(); ProjectValidation.Run();}
        internal static void Configure()
        {
            Directory.CreateDirectory(Folder+"/Meshes"); Directory.CreateDirectory(Folder+"/Materials"); AssetDatabase.Refresh();
            seal=Material("LampGasket",new Color(.045f,.053f,.055f),0,.19f);
            lens=Material("HeadlampGlass",new Color(.77f,.79f,.75f),.08f,.82f);
            flute=Material("MouldedLensRibs",new Color(.81f,.83f,.79f),.08f,.81f);
            metal=Material("FastenerSteel",new Color(.40f,.45f,.45f),.76f,.43f);
            recess=Material("GrilleGraphite",new Color(.085f,.10f,.105f),.28f,.32f);
            var live=Object.FindFirstObjectByType<ServiceVehicle>();
            RefineFront(live.gameObject); VehicleInteriorBuilder.Configure(live);
            var vehicle=PrefabUtility.LoadPrefabContents(VanPrefab);
            try {RefineFront(vehicle); VehicleInteriorBuilder.Configure(vehicle.GetComponent<ServiceVehicle>()); PrefabUtility.SaveAsPrefabAsset(vehicle,VanPrefab);}
            finally {PrefabUtility.UnloadPrefabContents(vehicle);}
            var tyre=PrefabUtility.LoadPrefabContents(TyrePrefab);
            try {RefineSpare(tyre); PrefabUtility.SaveAsPrefabAsset(tyre,TyrePrefab);}
            finally {PrefabUtility.UnloadPrefabContents(tyre);}
            foreach(var item in Object.FindObjectsByType<CarryableItem>().Where(i=>i.definitionId=="tire")) RefineSpare(item.gameObject);
            WorkOrderAssets.Render(AssetDatabase.LoadAssetAtPath<GameObject>(TyrePrefab),"Assets/_Project/Resources/OfficeThumbnails/tire.png");
            WorkOrderAssets.Render(Object.FindFirstObjectByType<ServiceVehicle>().gameObject,"Assets/_Project/Resources/OfficeThumbnails/vehicle.png");
            var runtime=Object.FindFirstObjectByType<CompanyRuntime>();
            runtime.manualModules.packageVisuals=ModulePackagePresentationBuilder.Build(runtime.catalog);
            File.WriteAllText("Logs/vehicle_detail_geometry.txt","Two original headlamp renderers and the original grille refined inside their source mounting bounds. Opaque depth-writing materials; no new lamp light or collider.\nSpare tyre: one combined visual renderer, original physical collider/mass preserved.\n");
        }
        static Material Material(string name,Color color,float metallic,float smoothness)
        {
            string path=Folder+"/Materials/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material) {material=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material,path);}
            material.SetColor("_BaseColor",color); material.SetFloat("_Metallic",metallic); material.SetFloat("_Smoothness",smoothness);
            material.SetFloat("_Surface",0); material.SetFloat("_Cull",2); material.SetFloat("_ZWrite",1);
            BaseShaderGUI.SetMaterialKeywords(material,LitGUI.SetMaterialKeywords); EditorUtility.SetDirty(material); return material;
        }
        static void RefineFront(GameObject vehicle)
        {
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(VanModel);
            var sources=model.GetComponentsInChildren<MeshFilter>(); int lamps=0,grilles=0;
            foreach(var filter in vehicle.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.StartsWith("Complete_V10")||f.name.StartsWith("Complete_V11")))
            {
                // Read dimensions from the immutable FBX, never the previous refinement.
                var original=sources.Single(s=>s.name==filter.name).sharedMesh.bounds;
                var g=new Geometry(); var c=original.center;
                if(filter.name.StartsWith("Complete_V11"))
                {
                    float w=original.size.x,h=original.size.y,d=original.size.z,z=original.min.z;
                    g.Box(c+new Vector3(0,0,d*.5f-.006f),new Vector3(w*.98f,h*.98f,.012f),seal,.004f);
                    foreach(int side in new[]{-1,1})
                    {
                        g.Box(c+new Vector3(side*(w*.5f-.012f),0,0),new Vector3(.024f,h,d),seal,.007f);
                        g.Box(c+new Vector3(0,side*(h*.5f-.012f),0),new Vector3(w-.04f,.024f,d),seal,.007f);
                    }
                    var face=new Vector3(c.x,c.y,z+.014f);
                    g.Box(face,new Vector3(w-.048f,h-.048f,.014f),lens,.006f);
                    for(int i=0;i<19;i++)
                    {
                        float x=c.x+Mathf.Lerp(-w*.5f+.031f,w*.5f-.031f,i/18f);
                        g.Box(new Vector3(x,c.y,z+.006f),new Vector3(.0014f,h-.057f,.001f),flute,.0003f);
                    }
                    foreach(float y in new[]{-.062f,.062f})
                        g.Box(new Vector3(c.x,c.y+y,z+.004f),new Vector3(w-.055f,.0015f,.002f),lens,.0003f);
                    foreach(int sx in new[]{-1,1}) foreach(int sy in new[]{-1,1})
                        g.Cylinder(new Vector3(c.x+sx*(w*.5f-.011f),c.y+sy*(h*.5f-.011f),z+.001f),.004f,.003f,metal,Vector3.forward,12);
                    lamps++;
                }
                else
                {
                    g.Box(c+new Vector3(0,0,.026f),new Vector3(1.16f,.33f,.035f),seal,.008f);
                    foreach(int side in new[]{-1,1})
                    {
                        g.Box(c+new Vector3(side*.573f,0,-.011f),new Vector3(.026f,.35f,.083f),recess,.007f);
                        g.Box(c+new Vector3(0,side*.160f,-.011f),new Vector3(1.15f,.029f,.083f),recess,.007f);
                    }
                    for(int i=0;i<5;i++) g.Box(c+new Vector3(0,-.123f+i*.0615f,-.049f),new Vector3(1.105f,.018f,.028f),recess,.004f);
                    foreach(float x in new[]{-.38f,0,.38f}) g.Box(c+new Vector3(x,0,-.025f),new Vector3(.014f,.29f,.019f),seal,.003f);
                    g.Cylinder(c+new Vector3(0,0,-.068f),.042f,.014f,metal,Vector3.forward,48);
                    g.Cylinder(c+new Vector3(0,0,-.076f),.027f,.003f,recess,Vector3.forward,48);
                    foreach(int sx in new[]{-1,1}) foreach(int sy in new[]{-1,1})
                        g.Cylinder(c+new Vector3(sx*.545f,sy*.143f,-.055f),.004f,.004f,metal,Vector3.forward,12);
                    grilles++;
                }
                var mesh=g.Build(out var materials); string key=Hash128.Compute(filter.name).ToString().Substring(0,12); mesh.name="Front_"+key;
                filter.sharedMesh=GeneratedMeshAsset.Store(mesh,Folder+"/Meshes/Front_"+key+".asset"); filter.GetComponent<MeshRenderer>().sharedMaterials=materials;
            }
            if(lamps!=2 || grilles!=1) throw new Exception("Original front assemblies are missing: "+lamps+" lamps / "+grilles+" grille");
        }
        static void RefineSpare(GameObject root)
        {
            var collision=root.GetComponent<BoxCollider>(); var size=collision.size; var center=collision.center; float mass=root.GetComponent<Rigidbody>().mass;
            foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>()) {Object.DestroyImmediate(renderer.GetComponent<MeshFilter>()); Object.DestroyImmediate(renderer);}
            var old=root.transform.Find("RefinedSpareTyre"); if(old) Object.DestroyImmediate(old.gameObject);
            var rubber=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/WorkshopCraft/Materials/TyreRubber.mat");
            if(!rubber) throw new Exception("Workshop tyre material missing.");
            var tyre=new Geometry();
            tyre.Lathe(Vector3.zero,new[]{new Vector2(-.095f,.205f),new Vector2(.095f,.205f),new Vector2(.095f,.285f),
                new Vector2(.084f,.327f),new Vector2(.062f,.367f),new Vector2(.047f,.380f),new Vector2(.025f,.380f),
                new Vector2(.020f,.374f),new Vector2(.010f,.374f),new Vector2(.005f,.380f),new Vector2(-.005f,.380f),
                new Vector2(-.010f,.374f),new Vector2(-.020f,.374f),new Vector2(-.025f,.380f),new Vector2(-.047f,.380f),
                new Vector2(-.062f,.367f),new Vector2(-.084f,.327f),new Vector2(-.095f,.285f)},rubber,64);
            var geometry=new Geometry(); geometry.Append(tyre,Matrix4x4.TRS(new Vector3(0,.11f,0),Quaternion.FromToRotation(Vector3.right,Vector3.up),Vector3.one));
            var mesh=geometry.Build(out var materials); mesh.name="SpareTyre";
            var go=new GameObject("RefinedSpareTyre",typeof(MeshFilter),typeof(MeshRenderer)); go.transform.SetParent(root.transform,false);
            go.GetComponent<MeshFilter>().sharedMesh=GeneratedMeshAsset.Store(mesh,Folder+"/Meshes/SpareTyre.asset"); go.GetComponent<MeshRenderer>().sharedMaterials=materials;
            if(collision.size!=size || collision.center!=center || root.GetComponent<Rigidbody>().mass!=mass) throw new Exception("Spare tyre physics changed.");
        }
        public static void Capture()
        {
            GarageRefinementBuilder.SignagePreview(new Vector3(4.65f,1.18f,-4.20f),new Vector3(4.65f,1.02f,-2.85f),"Documentation/VehicleDetailAfter_Front.png",55);
            GarageRefinementBuilder.SignagePreview(new Vector3(1.9f,2.1f,-4.2f),new Vector3(6.8f,1.38f,1),"Documentation/VehicleDetailAfter_Garage.png",74);
            WorkOrderAssets.Render(AssetDatabase.LoadAssetAtPath<GameObject>(TyrePrefab),"Documentation/SpareTyreRefined.png");
        }
    }
}
