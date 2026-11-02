using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanySim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace CompanySimEditor
{
    // Blender owns the geometry. This importer only binds materials, LODs and existing physics.
    public static class BlenderEquipmentBuilder
    {
        internal const string Folder="Assets/_Project/Art/BlenderEquipment";
        [Serializable] sealed class Manifest {public int version;public Model[] models;}
        [Serializable] sealed class Model {public string id,blend;public Level[] lods;}
        [Serializable] sealed class Level {public string name;public int triangles;public string[] materials;}
        [MenuItem("Company/Import Blender Equipment Models")]
        public static void Apply()=>PortableEquipmentBuilder.Apply();
        public static void ApplyAndValidate(){Apply();ProjectValidation.Run();}
        internal static void Configure()
        {
            string path=Folder+"/equipment_manifest.json";
            if(!File.Exists(path))throw new Exception("Generate the Blender equipment sources before importing: Art/Blender/Scripts/gen_refined_equipment.py");
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
            var ids=new[]{"toolbox","case","parts","bin","fuel","grease","absorbent","jack"};
            if(manifest.version!=1||manifest.models==null||manifest.models.Length!=8||ids.Any(id=>manifest.models.Count(m=>m.id==id)!=1))throw new Exception("Blender manifest must contain all eight equipment models.");
            Directory.CreateDirectory(Folder+"/Meshes");PortableEquipmentBuilder.PrepareMaterials();
            var report=new List<string>{"Blender 5.2 geometry imported with authored bevels/custom normals and three distance LODs. Original physical boxes, mass, IDs and prefab paths retained. One active renderer per equipment item; three stored LOD renderers."};
            foreach(var model in manifest.models)
            {
                if(model.lods==null||model.lods.Length!=3||!File.Exists(model.blend))throw new Exception("Incomplete editable Blender source/LOD set: "+model.id);
                string sourcePath=Folder+"/Models/"+model.id+".fbx";
                var source=AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                if(!source)throw new Exception("Blender FBX not imported: "+sourcePath);
                var definition=AssetDatabase.LoadAssetAtPath<EquipmentDefinition>("Assets/_Project/Data/Equipment/"+model.id+".asset");
                string prefabPath=AssetDatabase.GetAssetPath(definition.visualPrefab);var root=PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    var collision=root.GetComponent<BoxCollider>();string physical=PhysicsState(root);
                    if(root.GetComponentsInChildren<Collider>(true).Length!=1)throw new Exception("Unexpected physical children: "+model.id);
                    // Preserve imported source transform IDs; replace only their visual components.
                    foreach(string oldName in new[]{"CraftedEquipment","BlenderEquipment"})
                    {var old=root.transform.Find(oldName);if(old)Object.DestroyImmediate(old.gameObject);}
                    foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true))
                    {var f=r.GetComponent<MeshFilter>();if(f)Object.DestroyImmediate(f);Object.DestroyImmediate(r);}
                    var art=new GameObject("BlenderEquipment");art.transform.SetParent(root.transform,false);var group=art.AddComponent<LODGroup>();
                    var lods=new LOD[3];var counts=new int[3];var allowed=new Bounds(collision.center,collision.size+Vector3.one*.006f);
                    for(int level=0;level<3;level++)
                    {
                        var description=model.lods[level];
                        var filter=source.GetComponentsInChildren<MeshFilter>(true).SingleOrDefault(f=>f.name==description.name);
                        if(!filter)throw new Exception("Missing Blender mesh "+description.name+". Found: "+string.Join(",",source.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.name)));
                        var original=filter.sharedMesh;
                        if(original.subMeshCount!=description.materials.Length)throw new Exception("Blender material-slot mismatch: "+description.name);
                        // Blender's FBX node carries a 180-degree forward conversion.
                        // Normalize to the existing equipment convention: Unity +Z is the front.
                        var matrix=Matrix4x4.Rotate(Quaternion.Euler(0,180,0))*source.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;
                        // Bake the FBX transform once; combining one copy per submesh would
                        // unnecessarily duplicate the complete vertex buffer for every material.
                        var mesh=Object.Instantiate(original);mesh.name=description.name;
                        mesh.vertices=original.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
                        var normalMatrix=matrix.inverse.transpose;
                        mesh.normals=original.normals.Select(n=>normalMatrix.MultiplyVector(n).normalized).ToArray();
                        if(matrix.determinant<0)for(int sub=0;sub<mesh.subMeshCount;sub++)
                        {var indices=mesh.GetTriangles(sub);for(int i=0;i<indices.Length;i+=3){int swap=indices[i+1];indices[i+1]=indices[i+2];indices[i+2]=swap;}mesh.SetTriangles(indices,sub);}
                        mesh.RecalculateBounds();
                        if(!allowed.Contains(mesh.bounds.min)||!allowed.Contains(mesh.bounds.max))throw new Exception("Blender model exceeds the original physical box: "+description.name+" "+mesh.bounds.min.ToString("R")+".."+mesh.bounds.max.ToString("R"));
                        if(mesh.normals.Length!=mesh.vertexCount||mesh.vertices.Any(v=>!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z)))throw new Exception("Invalid Blender geometry: "+description.name);
                        counts[level]=mesh.triangles.Length/3;
                        // The manifest counts non-zero-area Blender triangles. FBX import
                        // removes degenerate cap slivers, so actual surfaces must match exactly.
                        if(counts[level]!=description.triangles)throw new Exception("Blender/Unity surface counts disagree: "+description.name+", Blender="+description.triangles+", Unity="+counts[level]);
                        mesh.RecalculateTangents();mesh=GeneratedMeshAsset.Store(mesh,Folder+"/Meshes/"+description.name+".asset");
                        var go=new GameObject("LOD"+level,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(art.transform,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;
                        go.GetComponent<MeshRenderer>().sharedMaterials=description.materials.Select(name=>
                        {
                            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/PortableEquipment/Materials/"+name+".mat");
                            if(!material)throw new Exception("Unbound Blender material: "+name);return material;
                        }).ToArray();
                        lods[level]=new LOD(level==0?.17f:level==1?.055f:.012f,new Renderer[]{go.GetComponent<MeshRenderer>()});
                    }
                    group.SetLODs(lods);group.fadeMode=LODFadeMode.CrossFade;group.animateCrossFading=true;group.RecalculateBounds();
                    if(physical!=PhysicsState(root))throw new Exception("Blender import changed equipment physics: "+model.id);
                    if(!(counts[0]>counts[1]&&counts[1]>counts[2]))throw new Exception("LOD triangle counts must decrease: "+model.id);
                    PrefabUtility.SaveAsPrefabAsset(root,prefabPath);
                    report.Add(model.id+": "+string.Join(" / ",counts.Select(c=>c.ToString()))+" Unity triangles (LOD0/1/2); Blender "+string.Join(" / ",model.lods.Select(l=>l.triangles.ToString()))+"; "+root.GetComponent<Rigidbody>().mass+" kg; collision "+collision.size+"; source "+model.blend);
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            File.WriteAllLines("Logs/blender_equipment_geometry.txt",report);
        }
        static string PhysicsState(GameObject root)
        {
            var box=root.GetComponent<BoxCollider>();var body=root.GetComponent<Rigidbody>();var item=root.GetComponent<CarryableItem>();
            return box.size.ToString("R")+box.center.ToString("R")+box.enabled+box.isTrigger+body.mass+"|"+body.linearDamping+"|"+body.angularDamping+"|"+body.useGravity+"|"+body.isKinematic+"|"+body.interpolation+"|"+body.constraints+"|"+item.definitionId+"|"+item.uid+"|"+item.durability;
        }
    }
    sealed class BlenderEquipmentModelImportSettings:AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if(!assetPath.StartsWith(BlenderEquipmentBuilder.Folder+"/Models/",StringComparison.Ordinal)||!assetPath.EndsWith(".fbx",StringComparison.OrdinalIgnoreCase))return;
            var importer=(ModelImporter)assetImporter;importer.globalScale=1;importer.importCameras=false;importer.importLights=false;importer.importAnimation=false;importer.addCollider=false;
            importer.importNormals=ModelImporterNormals.Import;importer.importTangents=ModelImporterTangents.CalculateMikk;importer.materialImportMode=ModelImporterMaterialImportMode.None;
            importer.meshCompression=ModelImporterMeshCompression.Off;importer.isReadable=true;
        }
    }
}
