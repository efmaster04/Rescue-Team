using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanySim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace CompanySimEditor
{
    public static class WorkshopVisualFinishBuilder
    {
        const string Folder="Assets/_Project/Art/WorkshopFinish";
        const string Garage="Assets/_Project/Art/Garage/Materials/";
        [MenuItem("Company/Finish Workshop Surfaces and Vehicle")]
        public static void Apply()
        {
            EditorSceneManager.OpenScene(GarageSystemsBuilder.ScenePath);
            Directory.CreateDirectory(Folder+"/Meshes"); Directory.CreateDirectory(Folder+"/Materials");
            if(!File.Exists("Documentation/WorkshopFinishBefore.png")) Capture("Before");
            ImportTextures();
            GarageSystemsBuilder.Apply();
            FinishGarage();
            var vehicle=Object.FindAnyObjectByType<ServiceVehicle>();
            FinishVan(vehicle);
            FinishLights();
            WorkshopCraftBuilder.Configure();
            BakeGarageReflection();
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
            Capture("After");
        }
        public static void ApplyAndValidate() {Apply(); ProjectValidation.Run();}
        static void ImportTextures()
        {
            AssetDatabase.Refresh();
            foreach(string path in Directory.GetFiles(Folder+"/Textures","*.png"))
            {
                var importer=AssetImporter.GetAtPath(path) as TextureImporter;
                bool normal=path.Contains("_Normal"),linear=path.Contains("_MetalSmooth");
                importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
                importer.sRGBTexture=!normal && !linear; importer.mipmapEnabled=true; importer.anisoLevel=4;
                importer.maxTextureSize=512; importer.alphaSource=TextureImporterAlphaSource.FromInput;
                importer.textureCompression=TextureImporterCompression.Compressed;
                importer.wrapMode=TextureWrapMode.Repeat;
                if(path.Contains("VanPaint") || path.Contains("Glass")) importer.wrapModeV=TextureWrapMode.Clamp;
                if(path.Contains("ServiceBayWear")) {importer.wrapMode=TextureWrapMode.Clamp; importer.alphaIsTransparency=true;}
                importer.SaveAndReimport();
            }
        }
        static void FinishGarage()
        {
            Set(Garage+"Workshop_BayFloor.mat","Epoxy",new Color(.22f,.31f,.325f),.30f);
            Set(Garage+"Workshop_Walkway.mat","Epoxy",new Color(.45f,.46f,.435f),.30f);
            Set(Garage+"Garage_Epoxy.mat","Epoxy",new Color(.34f,.37f,.365f),.30f);
            Set(Garage+"Garage_Plaster.mat","Wall",new Color(.74f,.73f,.695f),.28f);
            Set(Garage+"Garage_Ceiling.mat","Enamel",new Color(.43f,.46f,.46f),.15f);
            Set(Garage+"Garage_LiftEnamel.mat","Enamel",new Color(.085f,.16f,.205f),.18f);
            Set(Garage+"Garage_ProtectionBand.mat","Enamel",new Color(.12f,.205f,.23f),.18f);
            Set(Garage+"Workshop_CabinetRed.mat","Enamel",new Color(.37f,.075f,.058f),.18f);
            Set(Garage+"Workshop_BrushedMetal.mat","Steel",new Color(.58f,.61f,.61f),.22f);
            Set(Garage+"Workshop_Graphite.mat","Enamel",new Color(.105f,.125f,.135f),.18f);
            Set(Garage+"Workshop_Rubber.mat","Rubber",new Color(.075f,.08f,.08f),.28f);
            Set("Assets/_Project/Art/VehicleMechanics/Module_MountSteel.mat","Steel",new Color(.235f,.27f,.28f),.18f);
            Set("Assets/_Project/Art/VehicleMechanics/Module_BoltSteel.mat","Steel",new Color(.54f,.57f,.57f),.14f);
            Set("Assets/_Project/Art/VehicleMechanics/Module_ConnectorRubber.mat","Rubber",new Color(.065f,.075f,.075f),.20f);
            var previous=GameObject.Find("14_WorkshopVisualFinish"); if(previous) Object.DestroyImmediate(previous);
            var root=new GameObject("14_WorkshopVisualFinish");
            var go=new GameObject("ServiceBayUsage",typeof(MeshFilter),typeof(MeshRenderer)); go.transform.SetParent(root.transform,false);
            var mesh=new Mesh {name="ServiceBayUsage"};
            mesh.vertices=new[]{new Vector3(2.41f,.0125f,-4.25f),new Vector3(2.41f,.0125f,3.41f),new Vector3(6.89f,.0125f,3.41f),new Vector3(6.89f,.0125f,-4.25f)};
            mesh.uv=new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right}; mesh.triangles=new[]{0,1,2,0,2,3}; mesh.RecalculateNormals();
            go.GetComponent<MeshFilter>().sharedMesh=Store(mesh,Folder+"/Meshes/ServiceBayUsage.asset");
            string path=Folder+"/Materials/ServiceBayUsage.mat"; var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material) {material=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material,path);}
            material.SetTexture("_BaseMap",Texture("ServiceBayWear")); material.SetColor("_BaseColor",Color.white);
            material.SetFloat("_Surface",1); material.SetFloat("_Blend",0); material.SetFloat("_Smoothness",.18f);
            material.SetFloat("_Cull",2); material.SetFloat("_ZWrite",0); material.renderQueue=3000;
            BaseShaderGUI.SetMaterialKeywords(material,LitGUI.SetMaterialKeywords); EditorUtility.SetDirty(material);
            var renderer=go.GetComponent<MeshRenderer>(); renderer.sharedMaterial=material; renderer.shadowCastingMode=ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.ReflectionProbeStatic);
        }
        static void FinishVan(ServiceVehicle vehicle)
        {
            string materialFolder="Assets/_Project/Art/Materials/Van/Complete";
            foreach(var guid in AssetDatabase.FindAssets("t:Material",new[]{materialFolder}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid); var material=AssetDatabase.LoadAssetAtPath<Material>(path); string name=material.name.ToLowerInvariant();
                string texture="VanPaint"; float bump=.18f;
                if(name.Contains("glass")) {texture="Glass"; bump=0;}
                else if(name.StartsWith("07") || name.StartsWith("08") || name.StartsWith("09") || name.StartsWith("10") || name.StartsWith("18") || name.StartsWith("20")) {texture="Rubber"; bump=.25f;}
                else if(name.StartsWith("11") || name.StartsWith("12") || name.StartsWith("19") || name.StartsWith("21")) texture="Steel";
                else if(name.StartsWith("15") || name.StartsWith("16") || name.StartsWith("17")) texture="Lens";
                Set(path,texture,material.GetColor("_BaseColor"),bump);
            }
            int count=0,wheels=0; float maxDelta=0;
            foreach(var filter in vehicle.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.StartsWith("Complete_") || f.name.StartsWith("DoorSurface_") || f.name.StartsWith("CabinInnerFace_")).OrderBy(f=>Hierarchy(f.transform)))
            {
                var source=filter.sharedMesh; if(!source || source.vertexCount==0) continue;
                var mesh=Object.Instantiate(source); mesh.name=source.name+"_Finish";
                var vertices=mesh.vertices; var normals=mesh.normals; var uv=new Vector2[vertices.Length];
                bool wheel=filter.name.StartsWith("Complete_V14");
                if(wheel && !AssetDatabase.GetAssetPath(source).StartsWith(Folder)) {normals=SmoothWheel(vertices,normals); mesh.normals=normals;}
                var center=vehicle.transform.InverseTransformPoint(filter.GetComponent<MeshRenderer>().bounds.center);
                for(int i=0;i<vertices.Length;i++)
                {
                    var p=vehicle.transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[i]));
                    var n=vehicle.transform.InverseTransformDirection(filter.transform.TransformDirection(normals[i]));
                    if(wheel) uv[i]=new Vector2(Mathf.Atan2(p.y-center.y,p.z-center.z)/(2*Mathf.PI)+.5f,(p.x-center.x)*3+.5f);
                    else if(Mathf.Abs(n.y)>.6f) uv[i]=new Vector2(p.x*.5f,.78f+p.z*.04f);
                    else uv[i]=new Vector2((Mathf.Abs(n.x)>Mathf.Abs(n.z)?p.z:p.x)*.5f,p.y/3f);
                }
                mesh.uv=uv; mesh.RecalculateTangents();
                var oldPositions=source.vertices; var newPositions=mesh.vertices;
                for(int i=0;i<vertices.Length;i++) maxDelta=Mathf.Max(maxDelta,Vector3.Distance(oldPositions[i],newPositions[i]));
                string key=Hash128.Compute(Hierarchy(filter.transform)).ToString().Substring(0,12);
                filter.sharedMesh=Store(mesh,Folder+"/Meshes/Van_"+key+".asset"); count++; if(wheel) wheels++;
            }
            if(maxDelta>.000001f || wheels!=4) throw new Exception("Van finish must preserve geometry and all four tires: delta="+maxDelta+", wheels="+wheels);
            File.WriteAllText("Logs/workshop_visual_geometry.txt",count+" van surface meshes; four tire normal groups smoothed; maximum source vertex movement "+maxDelta+"; original silhouette, doors and colliders preserved.\nOne floor overlay renderer; no new collider or light.\n");
        }
        static Vector3[] SmoothWheel(Vector3[] positions,Vector3[] normals)
        {
            var groups=new Dictionary<Vector3Int,List<int>>();
            for(int i=0;i<positions.Length;i++)
            {
                var key=Vector3Int.RoundToInt(positions[i]*100000);
                if(!groups.TryGetValue(key,out var list)) {list=new List<int>(); groups.Add(key,list);} list.Add(i);
            }
            var result=new Vector3[normals.Length];
            foreach(var group in groups.Values) foreach(int index in group)
            {
                var sum=Vector3.zero; foreach(int other in group) if(Vector3.Dot(normals[index],normals[other])>.5f) sum+=normals[other];
                result[index]=sum.normalized;
            }
            return result;
        }
        static string Hierarchy(Transform transform) => transform.parent?Hierarchy(transform.parent)+"/"+transform.name:transform.name;
        static Mesh Store(Mesh mesh,string path)
        {
            return GeneratedMeshAsset.Store(mesh,path);
        }
        static Texture2D Texture(string name)=>AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/"+name+".png");
        static void Set(string path,string kind,Color color,float bump)
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(path); if(!material) throw new Exception("Missing finish material "+path);
            material.SetColor("_BaseColor",color); material.SetTexture("_BaseMap",Texture(kind+"_Albedo")); material.SetTextureScale("_BaseMap",Vector2.one);
            material.SetTexture("_BumpMap",Texture(kind+"_Normal")); material.SetFloat("_BumpScale",bump);
            material.SetTexture("_MetallicGlossMap",Texture(kind+"_MetalSmooth")); material.SetFloat("_Smoothness",1);
            material.SetFloat("_Surface",0); material.SetFloat("_Cull",2); material.SetFloat("_ZWrite",1);
            BaseShaderGUI.SetMaterialKeywords(material,LitGUI.SetMaterialKeywords); EditorUtility.SetDirty(material);
        }
        static void FinishLights()
        {
            foreach(var light in Object.FindObjectsByType<Light>())
            {
                if(light.name.StartsWith("Garage_LED_Light")) {light.intensity=9.5f; light.color=new Color(1,.97f,.92f);}
                if(light.name=="Garage_PortalDaylight") {light.intensity=4.0f; light.color=new Color(.84f,.92f,1);}
            }
        }
        static void BakeGarageReflection()
        {
            var probe=GameObject.Find("GarageReflection").GetComponent<ReflectionProbe>();
            foreach(var renderer in Object.FindAnyObjectByType<ServiceVehicle>().GetComponentsInChildren<MeshRenderer>())
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,GameObjectUtility.GetStaticEditorFlags(renderer.gameObject)|StaticEditorFlags.ReflectionProbeStatic);
            string path="Assets/_Project/Art/Presentation/GarageReflection.exr";
            // Keep the configured cubemap asset path; office reflections are not rebaked.
            if(probe.customBakedTexture) path=AssetDatabase.GetAssetPath(probe.customBakedTexture);
            if(!Lightmapping.BakeReflectionProbe(probe,path)) throw new Exception("Garage reflection capture failed");
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            probe.mode=ReflectionProbeMode.Custom; probe.customBakedTexture=AssetDatabase.LoadAssetAtPath<Cubemap>(path);
        }
        static void Capture(string phase)
        {
            GarageRefinementBuilder.SignagePreview(new Vector3(1.9f,2.1f,-4.2f),new Vector3(6.8f,1.38f,1.0f),"Documentation/WorkshopFinish"+phase+".png",74);
            GarageRefinementBuilder.SignagePreview(new Vector3(7.45f,1.75f,-4.23f),new Vector3(4.65f,1.35f,.10f),"Documentation/VehicleFinish"+phase+".png",64);
        }
    }
}
