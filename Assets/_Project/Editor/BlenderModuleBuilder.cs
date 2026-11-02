using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanySim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace CompanySimEditor
{
    // Replaces the device only. The common skid, port and manual fitting stay intact.
    public static class BlenderModuleBuilder
    {
        internal const string Folder="Assets/_Project/Art/BlenderModules";
        internal static readonly string[] Ids={"towing","water"};
        [Serializable] sealed class Manifest {public int version;public Model[] models;}
        [Serializable] sealed class Model {public string id,blend;public Level[] lods;}
        [Serializable] sealed class Level {public string name;public int triangles;public string[] materials;}

        [MenuItem("Company/Import Blender Winch and Pump")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorSceneManager.GetActiveScene().isDirty)
                throw new Exception("Blender module import needs a saved scene outside Play Mode.");
            var vehicle=Object.FindFirstObjectByType<ServiceVehicle>();
            string before=vehicle?ModuleEquipmentBuilder.VehicleState(vehicle):"";
            var report=new List<string>{"Blender device meshes, authored normals, three LOD levels. Existing skid, port, 24 kg mass and physical envelope preserved."};
            foreach(string id in Ids)
            {
                AssetDatabase.ImportAsset(Folder+"/Models/"+id+".fbx",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
                string path="Assets/_Project/Prefabs/Modules/ServiceModule_"+id+".prefab";
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    string physical=PhysicsState(root),fixtures=FixtureState(root);
                    ConfigureDevice(id,root.transform.Find("PortableModuleDevice").gameObject);
                    if(physical!=PhysicsState(root)||fixtures!=FixtureState(root))throw new Exception("Blender import changed physical settings or shared fittings: "+id);
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                    report.Add(id+": "+string.Join(" / ",root.transform.Find("PortableModuleDevice").GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh.triangles.Length/3))+" triangles (LOD0/1/2)");
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                ModuleEquipmentBuilder.Render(prefab,"Documentation/BlenderModuleAfter_"+id+".png",false);
                ModuleEquipmentBuilder.Render(prefab,"Assets/_Project/Resources/OfficeThumbnails/"+id+".png",true);
                AssetDatabase.ImportAsset("Assets/_Project/Resources/OfficeThumbnails/"+id+".png",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            }
            if(vehicle&&before!=ModuleEquipmentBuilder.VehicleState(vehicle))throw new Exception("Module import changed protected vehicle state.");
            AssetDatabase.SaveAssets();File.WriteAllLines("Logs/blender_module_geometry.txt",report);
        }
        public static void ApplyAndValidate(){Apply();ProjectValidation.Run();}

        internal static void ConfigureDevice(string id,GameObject device)
        {
            if(!Ids.Contains(id))return;
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder+"/module_manifest.json"));
            if(manifest.version!=1||manifest.models==null||manifest.models.Length!=Ids.Length||Ids.Any(key=>manifest.models.Count(m=>m.id==key)!=1))
                throw new Exception("Generate both editable Blender modules before import.");
            var model=manifest.models.Single(m=>m.id==id);
            if(!File.Exists(model.blend)||model.lods==null||model.lods.Length!=3)throw new Exception("Missing Blender source/LOD set: "+id);
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Models/"+id+".fbx");
            if(!source)throw new Exception("Missing imported Blender FBX: "+id);
            Directory.CreateDirectory(Folder+"/Meshes");
            var oldGroup=device.GetComponent<LODGroup>();if(oldGroup)Object.DestroyImmediate(oldGroup);
            foreach(var child in device.transform.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
            var group=device.AddComponent<LODGroup>();var levels=new LOD[3];int previous=int.MaxValue;
            var allowed=new Bounds(new Vector3(0,.2565f,.0045f),new Vector3(.644f,.369f,.453f));
            for(int level=0;level<3;level++)
            {
                var description=model.lods[level];var filter=source.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name==description.name);
                var original=filter.sharedMesh;
                if(original.subMeshCount!=description.materials.Length)throw new Exception("Blender material slots disagree: "+description.name);
                var matrix=Matrix4x4.Rotate(Quaternion.Euler(0,180,0))*source.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;
                var mesh=Object.Instantiate(original);mesh.name=description.name;
                mesh.vertices=original.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
                var normalMatrix=matrix.inverse.transpose;mesh.normals=original.normals.Select(n=>normalMatrix.MultiplyVector(n).normalized).ToArray();
                if(matrix.determinant<0)for(int sub=0;sub<mesh.subMeshCount;sub++)
                {var indices=mesh.GetTriangles(sub);for(int i=0;i<indices.Length;i+=3){int swap=indices[i+1];indices[i+1]=indices[i+2];indices[i+2]=swap;}mesh.SetTriangles(indices,sub);}
                mesh.RecalculateBounds();int triangles=mesh.triangles.Length/3;
                if(triangles!=description.triangles||triangles>=previous)throw new Exception("Blender/Unity triangle parity or LOD reduction failed: "+description.name+" Blender="+description.triangles+", Unity="+triangles);
                previous=triangles;
                if(!allowed.Contains(mesh.bounds.min)||!allowed.Contains(mesh.bounds.max))throw new Exception("Blender device exceeds existing module collision: "+description.name+" "+mesh.bounds);
                if(mesh.normals.Length!=mesh.vertexCount||mesh.vertices.Any(v=>!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z)))throw new Exception("Invalid Blender device geometry: "+description.name);
                mesh.RecalculateTangents();mesh=GeneratedMeshAsset.Store(mesh,Folder+"/Meshes/"+description.name+".asset");
                GameObject go=device;
                if(level>0){go=new GameObject("LOD"+level);go.transform.SetParent(device.transform,false);}
                var mf=go.GetComponent<MeshFilter>();if(!mf)mf=go.AddComponent<MeshFilter>();mf.sharedMesh=mesh;
                var mr=go.GetComponent<MeshRenderer>();if(!mr)mr=go.AddComponent<MeshRenderer>();
                mr.sharedMaterials=description.materials.Select(name=>
                {
                    var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/ModuleEquipment/Materials/"+name+".mat");
                    if(!material)throw new Exception("Unbound Blender module material: "+name);return material;
                }).ToArray();
                levels[level]=new LOD(level==0?.17f:level==1?.055f:.012f,new Renderer[]{mr});
            }
            group.SetLODs(levels);group.fadeMode=LODFadeMode.CrossFade;group.animateCrossFading=true;group.RecalculateBounds();
        }
        static string PhysicsState(GameObject root)
        {
            var b=root.GetComponent<Rigidbody>();var c=root.GetComponent<BoxCollider>();var item=root.GetComponent<CarryableItem>();
            return c.center.ToString("R")+c.size.ToString("R")+c.enabled+c.isTrigger+"|"+root.GetComponentsInChildren<Collider>(true).Length+"|"+
                b.mass+"|"+b.linearDamping+"|"+b.angularDamping+"|"+b.useGravity+"|"+b.isKinematic+"|"+b.interpolation+"|"+b.constraints+"|"+item.definitionId+"|"+root.GetComponent<ManualModulePackage>().GetType().FullName;
        }
        static string FixtureState(GameObject root)=>string.Join("\n",new[]{"ModuleSkidAndBrackets","ServicePort"}.Select(name=>
        {
            var t=root.transform.Find(name);return name+t.localPosition.ToString("R")+t.localRotation.ToString("R")+t.localScale.ToString("R")+
                AssetDatabase.GetAssetPath(t.GetComponent<MeshFilter>().sharedMesh)+string.Join(",",t.GetComponent<MeshRenderer>().sharedMaterials.Select(AssetDatabase.GetAssetPath));
        }));
    }
    sealed class BlenderModuleImportSettings:AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if(!assetPath.StartsWith(BlenderModuleBuilder.Folder+"/Models/",StringComparison.Ordinal)||!assetPath.EndsWith(".fbx",StringComparison.OrdinalIgnoreCase))return;
            var importer=(ModelImporter)assetImporter;importer.globalScale=1;importer.importCameras=false;importer.importLights=false;importer.importAnimation=false;importer.addCollider=false;
            importer.importNormals=ModelImporterNormals.Import;importer.importTangents=ModelImporterTangents.CalculateMikk;importer.materialImportMode=ModelImporterMaterialImportMode.None;
            importer.meshCompression=ModelImporterMeshCompression.Off;importer.isReadable=true;
        }
    }
}
