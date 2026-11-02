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
    // Replaces selected garage visuals, without moving the supplied room or its collision.
    public static class WorkshopCraftBuilder
    {
        const string Folder="Assets/_Project/Art/WorkshopCraft";
        static Material rubber,steel,dark,paint,paper;
        [MenuItem("Company/Refine Workshop Models and Atmosphere")]
        public static void Apply()
        {
            if(EditorApplication.isPlaying) throw new Exception("Stop Play Mode before refining workshop assets.");
            EditorSceneManager.OpenScene(GarageSystemsBuilder.ScenePath);
            Configure(); BakeReflection();
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets(); Capture();
        }
        internal static void Configure()
        {
            Directory.CreateDirectory(Folder+"/Meshes"); Directory.CreateDirectory(Folder+"/Materials");
            ImportTextures();
            rubber=Surface("TyreRubber",new Color(.055f,.060f,.058f),0,.17f,"Tyre");
            steel=Surface("MachinedSteel",new Color(.48f,.51f,.50f),.78f,.40f,"Steel");
            dark=Surface("RecessMetal",new Color(.075f,.085f,.083f),.60f,.25f,"Steel");
            paint=Surface("ViseEnamel",new Color(.14f,.245f,.25f),.30f,.32f,"Enamel");
            paper=Surface("ShopCloth",new Color(.39f,.415f,.35f),0,.08f,"Cloth");
            var old=GameObject.Find("15_WorkshopCraft"); if(old) Object.DestroyImmediate(old);
            var root=new GameObject("15_WorkshopCraft").transform;
            RefineWheels(Object.FindFirstObjectByType<ServiceVehicle>());
            BenchTools(root);
            BenchVise(root);
            WorkingDetails(root);
            ShelfPackaging(root);
            CounterWear(root);
            RefineSurfaces();
            RefineLighting();
            VehicleDetailBuilder.Configure();
            WorkbenchDetailBuilder.Configure();
            GarageStorageBuilder.Configure();
            foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>())
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,StaticEditorFlags.ReflectionProbeStatic);
        }
        public static void ApplyAndValidate() {Apply(); ProjectValidation.Run();}
        static void ImportTextures()
        {
            AssetDatabase.Refresh();
            foreach(string path in Directory.GetFiles(Folder+"/Textures","*.png"))
            {
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType=path.Contains("_Normal")?TextureImporterType.NormalMap:TextureImporterType.Default;
                importer.sRGBTexture=!path.Contains("_Normal") && !path.Contains("_MetalSmooth");
                importer.mipmapEnabled=true; importer.wrapMode=path.Contains("CounterUse")?TextureWrapMode.Clamp:TextureWrapMode.Repeat;
                importer.maxTextureSize=path.Contains("CounterUse")?1024:512; importer.anisoLevel=4;
                importer.textureCompression=TextureImporterCompression.CompressedHQ; importer.SaveAndReimport();
            }
        }
        static Material Surface(string name,Color color,float metallic,float smooth,string texture)
        {
            string path=Folder+"/Materials/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material) {material=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material,path);}
            material.SetColor("_BaseColor",color); material.SetFloat("_Metallic",metallic); material.SetFloat("_Smoothness",smooth);
            string textures=Folder+"/Textures/";
            var albedo=AssetDatabase.LoadAssetAtPath<Texture2D>(textures+texture+"_Albedo.png");
            if(!albedo) textures="Assets/_Project/Art/WorkshopFinish/Textures/";
            material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(textures+texture+"_Albedo.png"));
            material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(textures+texture+"_Normal.png"));
            material.SetFloat("_BumpScale",texture=="Tyre"?.65f:.22f);
            var map=AssetDatabase.LoadAssetAtPath<Texture2D>(textures+texture+"_MetalSmooth.png");
            material.SetTexture("_MetallicGlossMap",map); if(map) material.SetFloat("_Smoothness",1);
            BaseShaderGUI.SetMaterialKeywords(material,LitGUI.SetMaterialKeywords); EditorUtility.SetDirty(material); return material;
        }
        static void RefineWheels(ServiceVehicle vehicle)
        {
            int tires=0,rims=0; float radiusError=0;
            foreach(var filter in vehicle.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.StartsWith("Complete_V14")||f.name.StartsWith("Complete_V15")))
            {
                var renderer=filter.GetComponent<MeshRenderer>(); bool tire=filter.name.StartsWith("Complete_V14");
                var original=renderer.bounds; var previousCenter=vehicle.transform.InverseTransformPoint(original.center);
                // Calibrated source-wheel centers; repeated application must not accumulate
                // the asymmetric valve/hub bounds as a new wheel position.
                var center=new Vector3(Mathf.Sign(previousCenter.x)*(tire?.94f:1.14f),.42f,filter.name.Contains("Arka")?1.78f:-1.96f);
                var geometry=new Geometry();
                if(tire)
                {
                    var profile=new[]{new Vector2(-.15f,.265f),new Vector2(.15f,.265f),new Vector2(.15f,.335f),
                        new Vector2(.139f,.365f),new Vector2(.117f,.392f),new Vector2(.084f,.410f),
                        new Vector2(.067f,.415f),new Vector2(.052f,.415f),new Vector2(.047f,.407f),
                        new Vector2(.036f,.407f),new Vector2(.031f,.415f),new Vector2(-.031f,.415f),
                        new Vector2(-.036f,.407f),new Vector2(-.047f,.407f),new Vector2(-.052f,.415f),
                        new Vector2(-.067f,.415f),new Vector2(-.084f,.410f),new Vector2(-.117f,.392f),
                        new Vector2(-.139f,.365f),new Vector2(-.15f,.335f)};
                    geometry.Lathe(center,profile,rubber,72); tires++;
                }
                else
                {
                    var orient=center.x<0?Quaternion.Euler(0,180,0):Quaternion.identity;
                    var rim=new Geometry();
                    rim.Lathe(Vector3.zero,new[]{new Vector2(-.035f,.190f),new Vector2(-.035f,.262f),
                        new Vector2(-.022f,.275f),new Vector2(.020f,.275f),new Vector2(.034f,.263f),
                        new Vector2(.024f,.250f),new Vector2(.006f,.238f),new Vector2(-.006f,.190f)},steel,64);
                    rim.Cylinder(new Vector3(-.030f,0,0),.217f,.020f,dark,Vector3.right,48);
                    rim.Lathe(Vector3.zero,new[]{new Vector2(-.025f,0),new Vector2(-.025f,.146f),
                        new Vector2(-.010f,.154f),new Vector2(.012f,.135f),new Vector2(.023f,.104f),new Vector2(.023f,0)},steel,48);
                    for(int i=0;i<8;i++)
                    {
                        float angle=i*45;
                        rim.Lathe(Vector3.zero,new[]{new Vector2(-.014f,.140f),new Vector2(-.014f,.205f),
                            new Vector2(.001f,.205f),new Vector2(.009f,.145f)},steel,8,angle-14,28);
                    }
                    rim.Lathe(Vector3.zero,new[]{new Vector2(.022f,0),new Vector2(.022f,.065f),
                        new Vector2(.038f,.065f),new Vector2(.047f,.054f),new Vector2(.047f,0)},dark,48);
                    for(int i=0;i<6;i++)
                    {
                        float angle=i*Mathf.PI/3;
                        var p=new Vector3(.031f,Mathf.Cos(angle)*.094f,Mathf.Sin(angle)*.094f);
                        rim.Cylinder(p,.016f,.020f,steel,Vector3.right,6);
                    }
                    rim.Cylinder(new Vector3(.003f,.196f,.089f),.007f,.027f,dark,new Vector3(.2f,1,.4f),12);
                    geometry.Append(rim,Matrix4x4.TRS(center,orient,Vector3.one)); rims++;
                }
                var mesh=geometry.Build(out var materials);
                var points=mesh.vertices; var normals=mesh.normals;
                for(int i=0;i<points.Length;i++)
                {
                    points[i]=filter.transform.InverseTransformPoint(vehicle.transform.TransformPoint(points[i]));
                    normals[i]=filter.transform.InverseTransformDirection(vehicle.transform.TransformDirection(normals[i]));
                }
                mesh.vertices=points; mesh.normals=normals; mesh.RecalculateBounds(); mesh.RecalculateTangents();
                string key=Hash128.Compute(filter.name).ToString().Substring(0,12);
                mesh.name="Wheel_"+key;
                filter.sharedMesh=GeneratedMeshAsset.Store(mesh,Folder+"/Meshes/Wheel_"+key+".asset"); renderer.sharedMaterials=materials;
                if(tire) radiusError=Mathf.Max(radiusError,Mathf.Abs(renderer.bounds.size.y-original.size.y));
            }
            if(tires!=4||rims!=4||radiusError>.004f) throw new Exception("Wheel refinement changed the original fit: "+tires+" tires, "+rims+" rims, diameter delta "+radiusError);
            File.WriteAllText("Logs/workshop_craft_geometry.txt","Four 72-segment tyres and four vented steel rims, six lug nuts per rim; original centers and diameter retained (delta "+radiusError+").\n");
        }
        static void BenchTools(Transform root)
        {
            var env=GameObject.Find("01_Environment_OfficeGarage");
            foreach(var renderer in env.GetComponentsInChildren<MeshRenderer>().Where(r=>r.bounds.center.x>7.9f && r.bounds.center.x<9 &&
                (r.name.Contains("Forged open-ended wrench")||r.name.StartsWith("Forged center ridge")))) renderer.enabled=false;
            foreach(var renderer in env.GetComponentsInChildren<MeshRenderer>().Where(r=>r.bounds.center.x>8.7f && r.bounds.center.x<9 && r.bounds.center.y>1.96f && r.bounds.center.y<2.22f &&
                (r.name.StartsWith("Hook upright")||r.name.StartsWith("Peg shaft")||r.name.StartsWith("Square mounting boss")))) renderer.enabled=false;
            var geometry=new Geometry();
            for(int i=0;i<5;i++)
            {
                float size=.72f+i*.10f,z=-1.28f+i*.65f;
                var tool=new Geometry();
                tool.Box(new Vector3(0,0,0),new Vector3(.008f,.218f,.027f),steel);
                tool.Lathe(new Vector3(0,.139f,0),new[]{new Vector2(-.004f,.021f),new Vector2(-.004f,.038f),new Vector2(.004f,.038f),new Vector2(.004f,.021f)},steel,40);
                tool.Lathe(new Vector3(0,-.136f,0),new[]{new Vector2(-.005f,.020f),new Vector2(-.005f,.039f),new Vector2(.005f,.039f),new Vector2(.005f,.020f)},steel,32,-140,280);
                tool.Box(new Vector3(0,-.106f,0),new Vector3(.009f,.047f,.034f),steel);
                var rotation=Quaternion.AngleAxis(-3+i*1.5f,Vector3.right);
                var position=new Vector3(8.78f,1.91f,z);
                geometry.Append(tool,Matrix4x4.TRS(position,rotation,Vector3.one*size));
                var hook=position+rotation*(new Vector3(0,.139f+.014f,0)*size);
                geometry.Cylinder(new Vector3(8.846f,hook.y,hook.z),.004f,.144f,steel,Vector3.right,16);
                geometry.Cylinder(new Vector3(8.773f,hook.y,hook.z),.007f,.010f,dark,Vector3.right,16);
                geometry.Box(new Vector3(8.914f,hook.y,hook.z),new Vector3(.014f,.035f,.023f),dark,.003f);
            }
            Create(root,"GraduatedCombinationSpanners",geometry);
        }
        static void BenchVise(Transform root)
        {
            var env=GameObject.Find("01_Environment_OfficeGarage");
            foreach(var renderer in env.GetComponentsInChildren<MeshRenderer>())
            {
                var p=renderer.bounds.center;
                if(p.x>8.30f && p.x<8.72f && p.z> -2.05f && p.z<-.99f && p.y>.99f && renderer.bounds.max.y<1.31f) renderer.enabled=false;
            }
            var g=new Geometry(); Vector3 c=new Vector3(8.38f,.988f,-1.42f);
            g.Box(c+new Vector3(.02f,.028f,0),new Vector3(.34f,.056f,.27f),paint,.013f);
            g.Cylinder(c+new Vector3(.02f,.067f,0),.105f,.037f,dark,Vector3.up,48);
            g.Box(c+new Vector3(.005f,.123f,0),new Vector3(.20f,.10f,.13f),paint,.018f);
            g.Box(c+new Vector3(.118f,.184f,0),new Vector3(.075f,.16f,.21f),paint,.013f);
            g.Box(c+new Vector3(-.070f,.184f,0),new Vector3(.070f,.15f,.21f),paint,.013f);
            foreach(float x in new[]{.073f,-.027f}) g.Box(c+new Vector3(x,.238f,0),new Vector3(.018f,.045f,.22f),steel,.003f);
            g.Cylinder(c+new Vector3(-.06f,.127f,0),.017f,.34f,steel,Vector3.right,32);
            g.Cylinder(c+new Vector3(-.234f,.127f,0),.026f,.035f,dark,Vector3.right,32);
            g.Cylinder(c+new Vector3(-.254f,.127f,0),.007f,.22f,steel,new Vector3(0,.93f,.34f),16);
            foreach(int sign in new[]{-1,1})
            {
                g.Cylinder(c+new Vector3(-.254f,.127f+sign*.102f,sign*.037f),.012f,.014f,dark,new Vector3(0,.93f,.34f),20);
                foreach(float x in new[]{-.11f,.15f}) g.Cylinder(c+new Vector3(x,.062f,sign*.104f),.012f,.014f,steel,Vector3.up,6);
            }
            Create(root,"CastBenchVise",g);
        }
        static void WorkingDetails(Transform root)
        {
            var g=new Geometry();
            // Small work items sit on existing counters; they introduce no floor obstruction.
            for(int i=0;i<8;i++)
            {
                float z=-.37f+i*.046f; float radius=.009f+i*.0008f;
                g.Lathe(new Vector3(8.18f,1.013f,z),new[]{new Vector2(-.020f,.004f),new Vector2(-.020f,radius),new Vector2(.020f,radius),new Vector2(.020f,.004f)},steel,16);
            }
            g.Box(new Vector3(8.17f,1.003f,-.20f),new Vector3(.105f,.018f,.42f),dark,.004f);
            for(int i=0;i<4;i++)
            {
                var p=new Vector3(8.31f,1.001f,.46f);
                g.Box(p+new Vector3(i*.023f,i*.0035f,0),new Vector3(.25f,.007f,.32f),paper,.002f);
            }
            Create(root,"BenchSocketsAndFoldedCloth",g);
        }
        static void RefineSurfaces()
        {
            foreach(string name in new[]{"Garage_Plaster","Garage_Ceiling","Workshop_EnamelWhite"})
            {
                string path="Assets/_Project/Art/Garage/Materials/"+name+".mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(path); if(!material) continue;
                if(name=="Garage_Plaster") {material.SetFloat("_BumpScale",.16f); material.SetColor("_BaseColor",new Color(.73f,.72f,.68f));}
                else {material.SetColor("_BaseColor",name=="Garage_Ceiling"?new Color(.40f,.44f,.44f):new Color(.61f,.65f,.64f));}
                EditorUtility.SetDirty(material);
            }
        }
        static void ShelfPackaging(Transform root)
        {
            var env=GameObject.Find("01_Environment_OfficeGarage");
            var cartons=env.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled && r.gameObject.activeInHierarchy && r.name.Contains("folded carton body") &&
                r.bounds.size.y>.1f && r.bounds.center.x>.5f && r.bounds.center.x<9).OrderBy(r=>r.name).ToArray();
            var kraft=new[]{Surface("KraftLight",new Color(.57f,.44f,.29f),0,.16f,"Cardboard"),
                Surface("KraftNatural",new Color(.49f,.375f,.245f),0,.16f,"Cardboard"),
                Surface("KraftGrey",new Color(.51f,.475f,.39f),0,.16f,"Cardboard")};
            var tape=Surface("PackingTape",new Color(.62f,.53f,.37f),0,.30f,"Cardboard");
            var label=Surface("PackingPaper",new Color(.77f,.75f,.68f),0,.12f,"Cardboard");
            var details=new Geometry(); int count=0;
            foreach(var renderer in cartons)
            {
                var b=renderer.bounds; renderer.sharedMaterials=renderer.sharedMaterials.Select(_=>kraft[count%3]).ToArray();
                if(!AssetDatabase.GetAssetPath(renderer.GetComponent<MeshFilter>().sharedMesh).StartsWith(Folder))
                {
                    var mesh=Object.Instantiate(renderer.GetComponent<MeshFilter>().sharedMesh);
                    var vertices=mesh.vertices; var normals=mesh.normals; var uv=new Vector2[vertices.Length];
                    for(int i=0;i<vertices.Length;i++) {var n=normals[i]; var p=vertices[i];uv[i]=Mathf.Abs(n.y)>.6f?new Vector2(p.x,p.z)*3:Mathf.Abs(n.x)>.6f?new Vector2(p.z,p.y)*3:new Vector2(p.x,p.y)*3;}
                    mesh.uv=uv; mesh.RecalculateTangents(); string key=Hash128.Compute(renderer.name).ToString().Substring(0,12);
                    mesh.name="Carton_"+key; renderer.GetComponent<MeshFilter>().sharedMesh=GeneratedMeshAsset.Store(mesh,Folder+"/Meshes/Carton_"+key+".asset");
                }
                // The original cartons remain in their exact shelf positions.
                if(count%3==0)
                {
                    details.Box(new Vector3(b.center.x,b.max.y+.0015f,b.center.z),new Vector3(.035f,.002f,b.size.z*.80f),tape,.0002f);
                    details.Box(new Vector3(b.center.x,b.center.y,b.min.z-.0015f),new Vector3(.12f,.065f,.002f),label,.0002f);
                }
                count++;
            }
            Create(root,"ShelfPackingDetails",details);
            File.AppendAllText("Logs/workshop_craft_geometry.txt",count+" garage cartons refinished; original office materials and all source transforms untouched.\n");
        }
        static void CounterWear(Transform root)
        {
            var mesh=new Mesh{name="CounterUse"};
            mesh.vertices=new[]{new Vector3(7.99f,.986f,-1.85f),new Vector3(7.99f,.986f,2.40f),new Vector3(8.91f,.986f,2.40f),new Vector3(8.91f,.986f,-1.85f)};
            mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up}; mesh.triangles=new[]{0,1,2,0,2,3}; mesh.RecalculateNormals();
            string path=Folder+"/Materials/CounterUse.mat"; var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material) {material=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material,path);}
            material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/CounterUse.png")); material.SetColor("_BaseColor",Color.white);
            material.SetFloat("_Surface",1); material.SetFloat("_Blend",0); material.SetFloat("_ZWrite",0); material.SetFloat("_Smoothness",.18f); material.renderQueue=3000;
            BaseShaderGUI.SetMaterialKeywords(material,LitGUI.SetMaterialKeywords); EditorUtility.SetDirty(material);
            var go=new GameObject("CounterUse",typeof(MeshFilter),typeof(MeshRenderer)); go.transform.SetParent(root,false);
            go.GetComponent<MeshFilter>().sharedMesh=GeneratedMeshAsset.Store(mesh,Folder+"/Meshes/CounterUse.asset");
            go.GetComponent<MeshRenderer>().sharedMaterial=material; go.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
        }
        static void RefineLighting()
        {
            foreach(var light in Object.FindObjectsByType<Light>())
            {
                if(light.name.StartsWith("Garage_LED_Light"))
                {
                    bool rear=light.name.EndsWith("1"),left=light.name.Contains("_0");
                    light.intensity=rear?7.1f:9.0f; if(left) light.intensity*=.93f;
                    light.spotAngle=116; light.innerSpotAngle=82; light.color=new Color(1,.975f,.935f);
                    light.shadowNormalBias=.035f; light.shadowBias=.012f; light.shadowNearPlane=.05f;
                }
                if(light.name=="Garage_PortalDaylight") {light.intensity=5.4f; light.color=new Color(.80f,.89f,1);}
                if(light.name=="Garage_WorkbenchTaskLight") {light.intensity=3.0f; light.color=new Color(1,.92f,.80f);}
            }
        }
        static void BakeReflection()
        {
            var probe=GameObject.Find("GarageReflection").GetComponent<ReflectionProbe>();
            foreach(var renderer in GameObject.Find("15_WorkshopCraft").GetComponentsInChildren<MeshRenderer>())
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,StaticEditorFlags.ReflectionProbeStatic);
            var path=AssetDatabase.GetAssetPath(probe.customBakedTexture);
            if(!Lightmapping.BakeReflectionProbe(probe,path)) throw new Exception("Garage reflection bake failed.");
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            probe.mode=ReflectionProbeMode.Custom; probe.customBakedTexture=AssetDatabase.LoadAssetAtPath<Cubemap>(path);
        }
        public static void Capture()
        {
            GarageRefinementBuilder.SignagePreview(new Vector3(1.9f,2.1f,-4.2f),new Vector3(6.8f,1.38f,1),"Documentation/VisualQualityAfter_Wide.png",74);
            GarageRefinementBuilder.SignagePreview(new Vector3(6.98f,1.70f,-3.65f),new Vector3(8.65f,1.55f,.15f),"Documentation/VisualQualityAfter_Bench.png",64);
            GarageRefinementBuilder.SignagePreview(new Vector3(2.95f,.79f,-2.55f),new Vector3(3.56f,.43f,-2.14f),"Documentation/VisualQualityAfter_Wheel.png",48);
            GarageRefinementBuilder.SignagePreview(new Vector3(7.57f,1.55f,-1.96f),new Vector3(8.38f,1.14f,-1.42f),"Documentation/VisualQualityAfter_Vise.png",48);
            GarageRefinementBuilder.SignagePreview(new Vector3(.6f,1.57f,-2.88f),new Vector3(4.65f,1.3f,-.60f),"Documentation/VisualQualityAfter_Entry.png",62);
        }
        static void Create(Transform root,string name,Geometry g)
        {
            var mesh=g.Build(out var materials); mesh.name=name;
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer)); go.transform.SetParent(root,false);
            go.GetComponent<MeshFilter>().sharedMesh=GeneratedMeshAsset.Store(mesh,Folder+"/Meshes/"+name+".asset");
            go.GetComponent<MeshRenderer>().sharedMaterials=materials;
        }
        internal sealed class Geometry
        {
            readonly Dictionary<Material,List<CombineInstance>> parts=new Dictionary<Material,List<CombineInstance>>();
            readonly List<Mesh> temporary=new List<Mesh>();
            void Add(Mesh mesh,Material material,Matrix4x4 matrix)
            {if(!parts.TryGetValue(material,out var list)) {list=new List<CombineInstance>();parts.Add(material,list);} list.Add(new CombineInstance{mesh=mesh,transform=matrix});}
            public void AddMesh(Mesh mesh,Material material)
            {temporary.Add(mesh);Add(mesh,material,Matrix4x4.identity);}
            public void Box(Vector3 center,Vector3 size,Material material,float bevel=.003f)
            {Add(WorkshopOverhaulBuilder.BoxMesh(size,bevel),material,Matrix4x4.Translate(center));}
            public void Append(Geometry other,Matrix4x4 transform)
            {
                foreach(var group in other.parts) foreach(var part in group.Value) Add(part.mesh,group.Key,transform*part.transform);
                temporary.AddRange(other.temporary); other.temporary.Clear();
            }
            public void Cylinder(Vector3 center,float radius,float length,Material material,Vector3 axis,int segments)
            {
                var g=new Geometry();
                g.Lathe(Vector3.zero,new[]{new Vector2(-length*.5f,0),new Vector2(-length*.5f,radius),new Vector2(length*.5f,radius),new Vector2(length*.5f,0)},material,segments);
                Append(g,Matrix4x4.TRS(center,Quaternion.FromToRotation(Vector3.right,axis.normalized),Vector3.one));
            }
            public void Lathe(Vector3 center,Vector2[] profile,Material material,int segments,float start=0,float arc=360)
            {
                float area=0;
                for(int i=0;i<profile.Length;i++) {var p=profile[i];var q=profile[(i+1)%profile.Length];area+=p.x*q.y-q.x*p.y;}
                if(area<0) profile=profile.Reverse().ToArray();
                var vertices=new List<Vector3>(); var normals=new List<Vector3>(); var uv=new List<Vector2>(); var triangles=new List<int>();
                for(int i=0;i<profile.Length;i++)
                {
                    var p=profile[i];var q=profile[(i+1)%profile.Length];
                    var delta=q-p;var previous=p-profile[(i+profile.Length-1)%profile.Length];var next=profile[(i+2)%profile.Length]-q;
                    var edge=new Vector2(delta.y,-delta.x).normalized;
                    var before=new Vector2(previous.y,-previous.x).normalized;var after=new Vector2(next.y,-next.x).normalized;
                    var n0=Vector2.Dot(edge,before)>.65f?(edge+before).normalized:edge;
                    var n1=Vector2.Dot(edge,after)>.65f?(edge+after).normalized:edge;
                    for(int j=0;j<segments;j++)
                    {
                        int offset=vertices.Count;
                        for(int k=0;k<4;k++)
                        {
                            bool atEnd=k==1||k==2,atNext=k>=2;
                            var point=atEnd?q:p; var normal=atEnd?n1:n0;
                            float fraction=(j+(atNext?1:0))/(float)segments;
                            float a=(start+arc*fraction)*Mathf.Deg2Rad;
                            float na=segments<=8?(start+arc*(j+.5f)/segments)*Mathf.Deg2Rad:a;
                            vertices.Add(center+new Vector3(point.x,Mathf.Cos(a)*point.y,Mathf.Sin(a)*point.y));
                            normals.Add(new Vector3(normal.x,normal.y*Mathf.Cos(na),normal.y*Mathf.Sin(na)));
                            uv.Add(new Vector2(fraction,point.x/.30f+.5f));
                        }
                        triangles.AddRange(new[]{offset,offset+1,offset+2,offset,offset+2,offset+3});
                    }
                }
                if(arc<359.99f)
                {
                    // Seal the two radial cut faces, so slots and wrench jaws have thickness.
                    foreach(bool last in new[]{false,true})
                    {
                        float a=(start+(last?arc:0))*Mathf.Deg2Rad;
                        var normal=new Vector3(0,-Mathf.Sin(a),Mathf.Cos(a))*(last?1:-1);
                        int offset=vertices.Count;
                        var radial=new Vector3(0,Mathf.Cos(a),Mathf.Sin(a)); var middle=Vector2.zero;
                        foreach(var point in profile) middle+=point;
                        middle/=profile.Length;
                        vertices.Add(center+Vector3.right*middle.x+radial*middle.y); normals.Add(normal); uv.Add(Vector2.zero);
                        foreach(var point in profile) {vertices.Add(center+Vector3.right*point.x+radial*point.y);normals.Add(normal);uv.Add(point);}
                        for(int i=0;i<profile.Length;i++)
                        {
                            int b=offset+1+i,c=offset+1+(i+1)%profile.Length;
                            triangles.AddRange(last?new[]{offset,b,c}:new[]{offset,c,b});
                        }
                    }
                }
                for(int i=0;i<triangles.Count;i+=3)
                {
                    int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                    var normal=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);
                    if(normal.sqrMagnitude>1e-14f && Vector3.Dot(normal.normalized,(normals[a]+normals[b]+normals[c]).normalized)<-.05f)
                        throw new Exception("Craft lathe face winding disagrees with its visible normal.");
                }
                var mesh=new Mesh{indexFormat=IndexFormat.UInt32}; mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0);
                mesh.RecalculateBounds(); mesh.RecalculateTangents(); temporary.Add(mesh); Add(mesh,material,Matrix4x4.identity);
            }
            public Mesh Build(out Material[] materials)
            {
                var meshes=new List<Mesh>();
                foreach(var group in parts)
                {var mesh=new Mesh{indexFormat=IndexFormat.UInt32}; mesh.CombineMeshes(group.Value.ToArray(),true,true); meshes.Add(mesh);}
                var result=new Mesh{indexFormat=IndexFormat.UInt32}; result.CombineMeshes(meshes.Select(m=>new CombineInstance{mesh=m,transform=Matrix4x4.identity}).ToArray(),false,true);
                result.RecalculateBounds(); materials=parts.Keys.ToArray(); foreach(var mesh in meshes) Object.DestroyImmediate(mesh); foreach(var mesh in temporary) Object.DestroyImmediate(mesh);
                return result;
            }
        }
    }
}
