using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace CompanySimEditor
{
    // Applies the Blender service module art made by Art/Blender/Scripts/gen_service_modules.py:
    // all five device LOD sets go through BlenderModuleBuilder.ConfigureDevice, and the shared
    // skid and service port meshes are replaced in place. Prefab paths, physics, the 24 kg body,
    // the collision box, the port position, the label and the manual fitting stay as they are.
    public static class ServiceModuleArtImporter
    {
        const string Folder=BlenderModuleBuilder.Folder;
        const string Fixtures=Folder+"/Models/service_module_fixtures.fbx";
        const string MaterialFolder="Assets/_Project/Art/ModuleEquipment/Materials";
        internal const string SkidName="ServiceModuleSkid",PortName="ServiceModulePort";
        [Serializable] sealed class Palette {public Swatch[] materials;}
        [Serializable] sealed class Swatch {public string name;public float[] color;public float metallic,smoothness;}
        [Serializable] sealed class Manifest {public int version;public Fixture[] fixtures;}
        [Serializable] sealed class Fixture {public string name;public int triangles;public string[] materials;}

        [MenuItem("Company/Import Service Module Art")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Stop Play Mode before importing service module art.");
            EnsureMaterials();
            foreach(string id in BlenderModuleBuilder.Ids)
                AssetDatabase.ImportAsset(Folder+"/Models/"+id+".fbx",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(Fixtures,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            var report=new List<string>{"Service module art: Blender device LOD0/1/2, shared skid and service port. Physics, port position, label and manual fitting unchanged."};
            foreach(string id in BlenderModuleBuilder.Ids)
            {
                string path="Assets/_Project/Prefabs/Modules/ServiceModule_"+id+".prefab";
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    string physical=Physics(root);
                    var port=root.transform.Find("ServicePort");var portPose=port.localPosition.ToString("R")+port.localRotation.ToString("R");
                    BlenderModuleBuilder.ConfigureDevice(id,root.transform.Find("PortableModuleDevice").gameObject);
                    ConfigureFixtures(root.transform.Find("ModuleSkidAndBrackets").gameObject,port.gameObject);
                    if(physical!=Physics(root)||portPose!=port.localPosition.ToString("R")+port.localRotation.ToString("R"))
                        throw new Exception("Service module art changed physics or the service port pose: "+id);
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                    report.Add(id+": "+string.Join(" / ",root.transform.Find("PortableModuleDevice").GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh.triangles.Length/3))+" device triangles (LOD0/1/2)");
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                ModuleEquipmentBuilder.Render(prefab,"Documentation/ServiceModuleArt_"+id+".png",false);
                ModuleEquipmentBuilder.Render(prefab,"Assets/_Project/Resources/OfficeThumbnails/"+id+".png",true);
                AssetDatabase.ImportAsset("Assets/_Project/Resources/OfficeThumbnails/"+id+".png",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            }
            var skid=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/Meshes/"+SkidName+".asset");var portMesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/Meshes/"+PortName+".asset");
            report.Add("shared skid: "+skid.triangles.Length/3+" triangles, bounds "+skid.bounds);
            report.Add("service port: "+portMesh.triangles.Length/3+" triangles");
            AssetDatabase.SaveAssets();Directory.CreateDirectory("Logs");File.WriteAllLines("Logs/service_module_art.txt",report);
        }
        public static void ApplyAndValidate(){Apply();ProjectValidation.RunManualOnly();}
        // Batch entry: Unity -batchmode -executeMethod CompanySimEditor.ServiceModuleArtImporter.ApplyBatch -quit
        public static void ApplyBatch()
        {
            try{Apply();}
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }

        // Docked on the real van carrier in the saved garage scene, lit by the scene itself.
        // Nothing is saved: the preview module and camera are removed afterwards.
        [MenuItem("Company/Render Service Modules In Garage")]
        public static void RenderGarageViews()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Stop Play Mode before rendering.");
            EditorSceneManager.OpenScene(GarageSystemsBuilder.ScenePath);
            var system=Object.FindAnyObjectByType<CompanySim.ManualModuleSystem>();
            if(!system||!system.mount) throw new Exception("Garage scene has no module carrier mount.");
            var go=new GameObject("ServiceModulePreviewCamera");var camera=go.AddComponent<Camera>();
            camera.fieldOfView=40;camera.nearClipPlane=.02f;camera.farClipPlane=80;
            var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;data.renderShadows=true;
            var target=RenderTexture.GetTemporary(1600,900,24,RenderTextureFormat.ARGB32);target.antiAliasing=4;camera.targetTexture=target;
            var pixels=new Texture2D(1600,900,TextureFormat.RGBA32,false);var old=RenderTexture.active;
            var views=new[]{("yakin",new Vector3(.34f,.36f,1.05f),true),("genis",new Vector3(.62f,.62f,1.55f),false)};
            try
            {
                foreach(string id in BlenderModuleBuilder.Ids)
                {
                    var module=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Modules/ServiceModule_"+id+".prefab"));
                    try
                    {
                        foreach(var script in module.GetComponentsInChildren<MonoBehaviour>())script.enabled=false;
                        module.GetComponent<Rigidbody>().isKinematic=true;module.transform.SetParent(system.mount,false);
                        module.transform.localPosition=Vector3.zero;module.transform.localRotation=Quaternion.identity;
                        foreach(var (name,offset,close) in views)
                        {
                            foreach(var lod in module.GetComponentsInChildren<LODGroup>())lod.ForceLOD(close?0:-1);
                            camera.transform.position=system.mount.TransformPoint(offset);camera.transform.LookAt(system.mount.TransformPoint(new Vector3(0,close?.21f:.05f,0)));
                            Canvas.ForceUpdateCanvases();camera.Render();camera.Render();
                            RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,900),0,0);pixels.Apply();
                            File.WriteAllBytes("Documentation/ServiceModuleGarage_"+id+"_"+name+".png",pixels.EncodeToPNG());
                        }
                    }
                    finally{Object.DestroyImmediate(module);}
                }
            }
            finally{RenderTexture.active=old;camera.targetTexture=null;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(pixels);Object.DestroyImmediate(go);}
        }
        public static void RenderGarageViewsBatch()
        {
            try{RenderGarageViews();}
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }

        internal static bool Available=>File.Exists(Fixtures)&&File.Exists(Folder+"/service_module_palette.json");

        internal static void EnsureMaterials()
        {
            var palette=JsonUtility.FromJson<Palette>(File.ReadAllText(Folder+"/service_module_palette.json"));
            foreach(var swatch in palette.materials)
            {
                string path=MaterialFolder+"/"+swatch.name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
                m.SetColor("_BaseColor",new Color(swatch.color[0],swatch.color[1],swatch.color[2]));
                m.SetFloat("_Metallic",swatch.metallic);m.SetFloat("_Smoothness",swatch.smoothness);
                m.SetFloat("_Surface",0);m.SetFloat("_Cull",2);m.SetFloat("_ZWrite",1);
                m.SetTexture("_BaseMap",null);m.SetTexture("_BumpMap",null);m.SetTexture("_MetallicGlossMap",null);
                BaseShaderGUI.SetMaterialKeywords(m,LitGUI.SetMaterialKeywords);EditorUtility.SetDirty(m);
            }
        }

        // Replaces the skid and port meshes in place; their transforms are not touched.
        internal static void ConfigureFixtures(GameObject skid,GameObject port)
        {
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder+"/module_manifest.json"));
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(Fixtures);
            if(!source||manifest.fixtures==null||manifest.fixtures.Length!=2)throw new Exception("Generate the service module fixtures in Blender before import.");
            Directory.CreateDirectory(Folder+"/Meshes");
            Assign(skid,Convert(source,manifest,SkidName),manifest.fixtures.Single(f=>f.name==SkidName));
            Assign(port,Convert(source,manifest,PortName),manifest.fixtures.Single(f=>f.name==PortName));
        }
        static Mesh Convert(GameObject source,Manifest manifest,string name)
        {
            string asset=Folder+"/Meshes/"+name+".asset";
            var description=manifest.fixtures.Single(f=>f.name==name);
            var filter=source.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name==name);var original=filter.sharedMesh;
            if(original.subMeshCount!=description.materials.Length)throw new Exception("Blender material slots disagree: "+name);
            // Same conversion as BlenderModuleBuilder.ConfigureDevice, so device and fixtures share one frame.
            var matrix=Matrix4x4.Rotate(Quaternion.Euler(0,180,0))*source.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;
            var mesh=Object.Instantiate(original);mesh.name=name;
            mesh.vertices=original.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
            var normalMatrix=matrix.inverse.transpose;mesh.normals=original.normals.Select(n=>normalMatrix.MultiplyVector(n).normalized).ToArray();
            if(matrix.determinant<0)for(int sub=0;sub<mesh.subMeshCount;sub++)
            {var indices=mesh.GetTriangles(sub);for(int i=0;i<indices.Length;i+=3){int swap=indices[i+1];indices[i+1]=indices[i+2];indices[i+2]=swap;}mesh.SetTriangles(indices,sub);}
            mesh.RecalculateBounds();mesh.RecalculateTangents();
            if(mesh.triangles.Length/3!=description.triangles)throw new Exception("Blender/Unity triangle parity failed: "+name);
            var b=mesh.bounds;
            if(name==SkidName&&!(b.min.x<-.30f&&b.max.x<.305f&&b.max.z>.178f&&b.max.z<.19f&&b.min.y>-.001f&&b.max.y<.31f))
                throw new Exception("Skid is not in the module frame (socket tower must sit at -X, label rail at +Z): "+b);
            if(name==PortName&&!(b.max.z>.02f&&b.min.z<-.02f&&b.extents.x<.026f))throw new Exception("Service port is not facing +Z: "+b);
            return GeneratedMeshAsset.Store(mesh,asset);
        }
        static void Assign(GameObject go,Mesh mesh,Fixture description)
        {
            go.GetComponent<MeshFilter>().sharedMesh=mesh;
            go.GetComponent<MeshRenderer>().sharedMaterials=description.materials.Select(name=>
            {
                var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder+"/"+name+".mat");
                if(!material)throw new Exception("Unbound service module material: "+name);return material;
            }).ToArray();
        }
        static string Physics(GameObject root)
        {
            var b=root.GetComponent<Rigidbody>();var c=root.GetComponent<BoxCollider>();
            return c.center.ToString("R")+c.size.ToString("R")+c.enabled+c.isTrigger+"|"+root.GetComponentsInChildren<Collider>(true).Length+"|"+b.mass+"|"+b.isKinematic+"|"+b.interpolation+"|"+b.constraints;
        }
    }
}
