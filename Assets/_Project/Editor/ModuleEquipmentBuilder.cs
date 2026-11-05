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
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;
using Geometry=CompanySimEditor.WorkshopCraftBuilder.Geometry;

namespace CompanySimEditor
{
    public static class ModuleEquipmentBuilder
    {
        const string Folder="Assets/_Project/Art/ModuleEquipment";
        const string Prefabs="Assets/_Project/Prefabs/Modules";
        static Material metal,dark,paint,rubber,cable,red,brass,ivory,green,webbing,cast,orange;
        [MenuItem("Company/Rebuild Portable Module Models")]
        public static void Apply()
        {
            if(EditorApplication.isPlaying)throw new Exception("Stop Play Mode before editing equipment assets.");
            EditorSceneManager.OpenScene(GarageSystemsBuilder.ScenePath);
            var vehicle=Object.FindFirstObjectByType<ServiceVehicle>();string before=VehicleState(vehicle);
            var runtime=Object.FindFirstObjectByType<CompanyRuntime>();
            runtime.manualModules.packageVisuals=Build(runtime.catalog);
            foreach(var variant in runtime.manualModules.packageVisuals)
            {
                Render(variant.prefab,"Documentation/ModuleEquipmentAfter_"+variant.moduleId+".png",false);
                Render(variant.prefab,"Assets/_Project/Resources/OfficeThumbnails/"+variant.moduleId+".png",true);
            }
            if(VehicleState(vehicle)!=before)throw new Exception("Equipment art must not change the van's geometry, materials or transforms.");
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            File.WriteAllText("Logs/module_equipment_vehicle_parity.txt","PASS: van child transforms, mesh references, material references and parking pose unchanged.\n");
        }
        public static void ApplyAndValidate(){Apply();ProjectValidation.Run();}
        internal static string VehicleState(ServiceVehicle vehicle)
        {
            string PathOf(Transform t)=>t==vehicle.transform?t.name:PathOf(t.parent)+"/"+t.name;
            return string.Join("\n",vehicle.GetComponentsInChildren<Transform>(true).OrderBy(PathOf).Select(t=>PathOf(t)+"|"+t.localPosition.ToString("R")+"|"+t.localRotation.ToString("R")+"|"+t.localScale.ToString("R")))+
                string.Join("\n",vehicle.GetComponentsInChildren<MeshFilter>(true).OrderBy(f=>PathOf(f.transform)).Select(f=>PathOf(f.transform)+"|"+AssetDatabase.GetAssetPath(f.sharedMesh)))+
                string.Join("\n",vehicle.GetComponentsInChildren<MeshRenderer>(true).OrderBy(r=>PathOf(r.transform)).Select(r=>PathOf(r.transform)+"|"+string.Join(",",r.sharedMaterials.Select(AssetDatabase.GetAssetPath))));
        }
        internal static void RefineFireOnly()
        {
            PrepareMaterials();const string path=Prefabs+"/ServiceModule_fire.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var mesh=FireReel().Build(out var materials);mesh.name="fire_Device";var b=mesh.bounds;
                if(b.min.y<.072f||b.max.y>.441f||Mathf.Abs(b.min.x)>.322f||Mathf.Abs(b.max.x)>.322f||b.min.z<-.222f||b.max.z>.231f)throw new Exception("Fire equipment exceeds its existing collision: "+b);
                var art=root.transform.Find("PortableModuleDevice");art.GetComponent<MeshFilter>().sharedMesh=GeneratedMeshAsset.Store(mesh,Folder+"/Meshes/fire_Device.asset");art.GetComponent<MeshRenderer>().sharedMaterials=materials;
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Render(prefab,"Documentation/ModuleEquipmentAfter_fire.png",false);Render(prefab,"Assets/_Project/Resources/OfficeThumbnails/fire.png",true);
        }
        internal static ModulePackageVisual[] Build(CompanyCatalog catalog)
        {
            Directory.CreateDirectory(Folder+"/Meshes");Directory.CreateDirectory(Folder+"/Materials");PrepareMaterials();
            if(ServiceModuleArtImporter.Available)ServiceModuleArtImporter.EnsureMaterials();
            var result=new List<ModulePackageVisual>();var summary=new List<string>();
            foreach(var definition in catalog.modules)
            {
                var root=new GameObject("ServiceModule_"+definition.id);
                try
                {
                    var collision=root.AddComponent<BoxCollider>();collision.center=new Vector3(0,.22f,0);collision.size=new Vector3(.66f,.44f,.46f);
                    var body=root.AddComponent<Rigidbody>();body.mass=24;body.interpolation=RigidbodyInterpolation.Interpolate;body.useGravity=true;
                    root.AddComponent<CarryableItem>().definitionId="module:"+definition.id;root.AddComponent<ManualModulePackage>();
                    Geometry mechanism;
                    switch(definition.id)
                    {
                        case "towing":mechanism=Winch();break;
                        case "fire":mechanism=FireReel();break;
                        case "water":mechanism=WaterPump();break;
                        case "tech":mechanism=ServiceUnit();break;
                        case "cargo":mechanism=TransitCase();break;
                        default:throw new Exception("Unknown equipment type: "+definition.id);
                    }
                    var device=Attach(root.transform,"PortableModuleDevice",mechanism,definition.id+"_Device");
                    BlenderModuleBuilder.ConfigureDevice(definition.id,device);
                    var bounds=device.GetComponent<MeshFilter>().sharedMesh.bounds;
                    if(bounds.min.y<.072f||bounds.max.y>.441f||Mathf.Abs(bounds.min.x)>.322f||Mathf.Abs(bounds.max.x)>.322f||bounds.min.z<-.222f||bounds.max.z>.231f)
                        throw new Exception("Equipment must fit the existing portable collision, "+definition.id+": "+bounds);
                    var skid=Attach(root.transform,"ModuleSkidAndBrackets",Skid(),definition.id+"_Skid");
                    var port=Attach(root.transform,"ServicePort",ServicePort(),definition.id+"_Port");port.transform.localPosition=new Vector3(-.29f,.27f,.20f);
                    // The Blender service module art supplies the shared skid and port when present.
                    if(ServiceModuleArtImporter.Available)ServiceModuleArtImporter.ConfigureFixtures(skid,port);
                    Label(root.transform,definition.displayName);
                    var prefab=PrefabUtility.SaveAsPrefabAsset(root,Prefabs+"/ServiceModule_"+definition.id+".prefab");
                    result.Add(new ModulePackageVisual{moduleId=definition.id,prefab=prefab});
                    summary.Add(definition.id+": "+device.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3+" mechanism triangles; "+bounds);
                }
                finally{Object.DestroyImmediate(root);}
            }
            File.WriteAllLines("Logs/module_equipment_geometry.txt",new[]{"Five complete mechanisms; Blender winch/pump use three device LODs, other devices one renderer. Shared skid and service port retained. Original physical box, mass, item IDs, port position and manual fitting interface retained."}.Concat(summary));
            return result.ToArray();
        }
        static void PrepareMaterials()
        {
            foreach(string suffix in new[]{"Normal","MetalSmooth"})
            {
                var importer=AssetImporter.GetAtPath(Folder+"/Textures/Cast_"+suffix+".png") as TextureImporter;
                if(!importer)continue;
                var type=suffix=="Normal"?TextureImporterType.NormalMap:TextureImporterType.Default;
                if(importer.textureType!=type||importer.sRGBTexture)
                {importer.textureType=type;importer.sRGBTexture=false;importer.SaveAndReimport();}
            }
            metal=Surface("SatinSteel",new Color(.48f,.51f,.50f),.80f,.50f,"Steel");
            dark=Surface("MachinedRecess",new Color(.075f,.092f,.095f),.45f,.29f,"Steel");
            paint=Surface("GraphiteEnamel",new Color(.080f,.102f,.104f),.23f,.38f,"Enamel");
            rubber=Surface("Rubber",new Color(.043f,.050f,.049f),0,.18f,"Rubber");
            cable=Surface("WireRope",new Color(.24f,.28f,.29f),.62f,.39f,"Steel");
            red=Surface("ReelRed",new Color(.43f,.070f,.043f),.20f,.32f,"Enamel");
            brass=Surface("ValveBrass",new Color(.48f,.35f,.16f),.73f,.40f,"Steel");
            ivory=Surface("InstrumentIvory",new Color(.77f,.77f,.69f),0,.24f,null);
            green=Surface("EquipmentEnamel",new Color(.23f,.29f,.255f),.15f,.29f,"Enamel");
            webbing=Surface("Webbing",new Color(.25f,.27f,.20f),0,.12f,"Cloth");
            cast=Surface("CastAlloy",new Color(.23f,.26f,.25f),.67f,.24f,"Cast");
            orange=Surface("SafetyOrange",new Color(.60f,.19f,.048f),.10f,.34f,"Enamel");
        }
        static Material Surface(string name,Color color,float metallic,float smoothness,string map)
        {
            string path=Folder+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
            m.SetColor("_BaseColor",color);m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",smoothness);
            m.SetFloat("_Surface",0);m.SetFloat("_Cull",2);m.SetFloat("_ZWrite",1);
            if(map!=null)
            {
                string textures=map=="Cast"?Folder+"/Textures/":map=="Cloth"?"Assets/_Project/Art/WorkshopCraft/Textures/":"Assets/_Project/Art/WorkshopFinish/Textures/";
                m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(textures+map+"_Albedo.png"));
                m.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(textures+map+"_Normal.png"));m.SetFloat("_BumpScale",map=="Cast"?.32f:.14f);
                if(map=="Cast") {m.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(textures+map+"_MetalSmooth.png"));m.SetFloat("_Smoothness",.48f);}
            }
            BaseShaderGUI.SetMaterialKeywords(m,LitGUI.SetMaterialKeywords);EditorUtility.SetDirty(m);return m;
        }
        static GameObject Attach(Transform root,string name,Geometry geometry,string key)
        {
            var mesh=geometry.Build(out var materials);mesh.name=key;
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root,false);
            go.GetComponent<MeshFilter>().sharedMesh=GeneratedMeshAsset.Store(mesh,Folder+"/Meshes/"+key+".asset");go.GetComponent<MeshRenderer>().sharedMaterials=materials;return go;
        }
        internal static void Axial(Geometry g,Vector3 center,Vector2[] profile,Material material,Vector3 axis,int segments=48)
        {var shape=new Geometry();shape.Lathe(Vector3.zero,profile,material,segments);g.Append(shape,Matrix4x4.TRS(center,Quaternion.FromToRotation(Vector3.right,axis),Vector3.one));}
        internal static void Rod(Geometry g,Vector3 a,Vector3 b,float radius,Material material,int sides=24)
            =>g.Cylinder((a+b)*.5f,radius,Vector3.Distance(a,b),material,(b-a).normalized,sides);
        internal static void Ring(Geometry g,Vector3 center,float radius,float thickness,Material material,Vector3 axis,int segments=48)
        {
            var profile=Enumerable.Range(0,10).Select(i=>{float a=i*Mathf.PI/5;return new Vector2(Mathf.Cos(a)*thickness,radius+Mathf.Sin(a)*thickness);}).ToArray();
            Axial(g,center,profile,material,axis,segments);
        }
        static void Bolt(Geometry g,Vector3 center,Vector3 axis,float radius=.004f)
        {g.Cylinder(center,radius*1.35f,.0015f,metal,axis,24);g.Cylinder(center+axis*.0023f,radius,.0038f,dark,axis,6);}
        // Authoring helpers for folded/cast profiles and genuine through-openings. All faces
        // are closed, with bevelled returns; dark rectangles do not stand in for holes.
        sealed class ProfileMesh
        {
            readonly List<Vector3> vertices=new List<Vector3>(),normals=new List<Vector3>();
            readonly List<Vector2> uv=new List<Vector2>();readonly List<int> triangles=new List<int>();
            readonly Matrix4x4 transform;
            public ProfileMesh(Vector3 center,Quaternion rotation){transform=Matrix4x4.TRS(center,rotation,Vector3.one);}
            public void Triangle(Vector3 a,Vector3 b,Vector3 c)
            {
                var n=transform.MultiplyVector(Vector3.Cross(b-a,c-a).normalized);int start=vertices.Count;
                foreach(var p in new[]{a,b,c}){vertices.Add(transform.MultiplyPoint3x4(p));normals.Add(n);uv.Add(new Vector2(p.x*3,p.y*3+p.z));}
                triangles.AddRange(new[]{start,start+1,start+2});
            }
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d){Triangle(a,b,c);Triangle(a,c,d);}
            public void Add(Geometry g,Material material)
            {var m=new Mesh{indexFormat=IndexFormat.UInt32};m.SetVertices(vertices);m.SetNormals(normals);m.SetUVs(0,uv);m.SetTriangles(triangles,0);m.RecalculateBounds();m.RecalculateTangents();g.AddMesh(m,material);}
        }
        static Vector2[] Inset(Vector2[] points,float amount)
        {
            var result=new Vector2[points.Length];
            for(int i=0;i<points.Length;i++)
            {
                var a=(points[i]-points[(i+points.Length-1)%points.Length]).normalized;var b=(points[(i+1)%points.Length]-points[i]).normalized;
                var n0=new Vector2(-a.y,a.x);var n1=new Vector2(-b.y,b.x);
                result[i]=points[i]+(n0+n1)*(amount/Mathf.Max(.05f,1+Vector2.Dot(n0,n1)));
            }
            return result;
        }
        internal static void Plate(Geometry g,Vector3 center,Quaternion rotation,Vector2[] outline,float thickness,Material material)
        {
            float bevel=Mathf.Min(.0018f,thickness*.24f);var inset=Inset(outline,bevel);var mesh=new ProfileMesh(center,rotation);
            var loops=new[]{inset,outline,outline,inset};var depths=new[]{-thickness*.5f,-thickness*.5f+bevel,thickness*.5f-bevel,thickness*.5f};
            for(int k=0;k<3;k++)for(int i=0;i<outline.Length;i++)
            {int j=(i+1)%outline.Length;mesh.Quad(new Vector3(loops[k][i].x,loops[k][i].y,depths[k]),new Vector3(loops[k][j].x,loops[k][j].y,depths[k]),new Vector3(loops[k+1][j].x,loops[k+1][j].y,depths[k+1]),new Vector3(loops[k+1][i].x,loops[k+1][i].y,depths[k+1]));}
            for(int i=1;i<outline.Length-1;i++)
            {
                mesh.Triangle(new Vector3(inset[0].x,inset[0].y,depths[3]),new Vector3(inset[i].x,inset[i].y,depths[3]),new Vector3(inset[i+1].x,inset[i+1].y,depths[3]));
                mesh.Triangle(new Vector3(inset[0].x,inset[0].y,depths[0]),new Vector3(inset[i+1].x,inset[i+1].y,depths[0]),new Vector3(inset[i].x,inset[i].y,depths[0]));
            }
            mesh.Add(g,material);
        }
        internal static Vector2[] RoundedOutline(float width,float height,float radius)
        {
            var result=new List<Vector2>();
            for(int corner=0;corner<4;corner++)for(int step=0;step<8;step++)
            {
                float angle=(corner*90+step*90f/7)*Mathf.Deg2Rad;
                var center=new Vector2((corner==0||corner==3?1:-1)*(width*.5f-radius),(corner<2?1:-1)*(height*.5f-radius));
                result.Add(center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius);
            }
            return result.ToArray();
        }
        internal static void Aperture(Geometry g,Vector3 center,Quaternion rotation,float width,float height,float innerWidth,float innerHeight,float thickness,float radius,Material material)
        {
            float bevel=Mathf.Min(.0025f,thickness*.24f);var outside=RoundedOutline(width,height,Mathf.Min(radius,height*.49f));
            var inside=RoundedOutline(innerWidth,innerHeight,Mathf.Min(radius,innerHeight*.49f));
            var outerFace=Inset(outside,bevel);var innerFace=Inset(inside,-bevel);var mesh=new ProfileMesh(center,rotation);
            var depths=new[]{-thickness*.5f,-thickness*.5f+bevel,thickness*.5f-bevel,thickness*.5f};
            for(int i=0;i<outside.Length;i++)
            {
                int j=(i+1)%outside.Length;
                Vector3 P(Vector2[] shape,int index,float depth)=>new Vector3(shape[index].x,shape[index].y,depth);
                mesh.Quad(P(outerFace,i,depths[3]),P(outerFace,j,depths[3]),P(innerFace,j,depths[3]),P(innerFace,i,depths[3]));
                mesh.Quad(P(outerFace,j,depths[0]),P(outerFace,i,depths[0]),P(innerFace,i,depths[0]),P(innerFace,j,depths[0]));
                for(int k=0;k<3;k++)
                {
                    var outer0=k==0?outerFace:outside;var outer1=k==2?outerFace:outside;
                    mesh.Quad(P(outer0,i,depths[k]),P(outer0,j,depths[k]),P(outer1,j,depths[k+1]),P(outer1,i,depths[k+1]));
                    var inner0=k==0?innerFace:inside;var inner1=k==2?innerFace:inside;
                    mesh.Quad(P(inner0,j,depths[k]),P(inner0,i,depths[k]),P(inner1,i,depths[k+1]),P(inner1,j,depths[k+1]));
                }
            }
            mesh.Add(g,material);
        }
        static Geometry Skid()
        {
            var g=new Geometry();
            // Thin folded tray, with real hand openings and edge returns instead of a solid plinth.
            Plate(g,new Vector3(0,.069f,-.004f),Quaternion.Euler(90,0,0),
                new[]{new Vector2(-.272f,-.164f),new Vector2(.272f,-.164f),new Vector2(.298f,-.138f),new Vector2(.298f,.144f),new Vector2(.272f,.170f),new Vector2(-.272f,.170f),new Vector2(-.298f,.144f),new Vector2(-.298f,-.138f)},.008f,paint);
            foreach(float z in new[]{-.181f,.181f})
            {
                g.Box(new Vector3(0,.052f,z),new Vector3(.560f,.039f,.008f),paint,.002f);
                g.Box(new Vector3(0,.031f,z-.004f*Mathf.Sign(z)),new Vector3(.550f,.006f,.018f),paint,.002f);
            }
            foreach(int side in new[]{-1,1})
            {
                foreach(float z in new[]{-.128f,.128f})
                {
                    g.Box(new Vector3(side*.243f,.023f,z),new Vector3(.075f,.034f,.070f),rubber,.006f);
                    Bolt(g,new Vector3(side*.243f,.077f,z),Vector3.up,.004f);
                }
                Aperture(g,new Vector3(side*.302f,.078f,-.009f),Quaternion.Euler(0,90,0),.300f,.069f,.105f,.026f,.012f,.012f,paint);
                // Small die-cast corner bumpers protect the folded sheet, rather than thick block feet.
                foreach(float z in new[]{-.143f,.143f})g.Box(new Vector3(side*.294f,.056f,z),new Vector3(.028f,.030f,.037f),rubber,.005f);
            }
            // A braced side rail supports the interface without a solid, featureless upright.
            Curve(g,new[]{new Vector3(-.292f,.078f,-.102f),new Vector3(-.292f,.170f,.043f),new Vector3(-.292f,.242f,.150f)},.007f,paint);
            Rod(g,new Vector3(-.292f,.078f,.151f),new Vector3(-.292f,.241f,.151f),.007f,paint);
            g.Box(new Vector3(-.29f,.270f,.160f),new Vector3(.055f,.072f,.068f),rubber,.009f);
            g.Box(new Vector3(-.29f,.270f,.195f),new Vector3(.051f,.066f,.004f),cast,.004f);
            foreach(float y in new[]{.245f,.295f})Bolt(g,new Vector3(-.29f,y,.200f),Vector3.forward,.0024f);
            return g;
        }
        static Geometry ServicePort()
        {
            var g=new Geometry();
            Axial(g,Vector3.zero,new[]{new Vector2(-.024f,.010f),new Vector2(-.024f,.020f),new Vector2(-.017f,.023f),
                new Vector2(.017f,.023f),new Vector2(.023f,.019f),new Vector2(.023f,.011f)},metal,Vector3.forward,40);
            g.Cylinder(new Vector3(0,0,.005f),.011f,.012f,rubber,Vector3.forward,32);
            Ring(g,new Vector3(0,0,.019f),.018f,.002f,rubber,Vector3.forward,40);
            return g;
        }
        static Geometry Winch()
        {
            var g=new Geometry();var c=new Vector3(.006f,.181f,-.040f);
            // Low horizontal recovery winch: cast bearing saddles share the real mounting plane.
            foreach(float x in new[]{-.113f,.135f})
            {
                Plate(g,new Vector3(x,0,c.z),Quaternion.Euler(0,90,0),new[]{new Vector2(-.075f,.077f),new Vector2(.075f,.077f),new Vector2(.069f,.102f),new Vector2(.048f,.202f),new Vector2(-.048f,.202f),new Vector2(-.069f,.102f)},.020f,cast);
                g.Box(new Vector3(x,.081f,c.z),new Vector3(.054f,.012f,.164f),cast,.004f);
                foreach(float z in new[]{-.106f,.026f})Bolt(g,new Vector3(x,.090f,z),Vector3.up,.0047f);
            }
            // Tie bars make the two end castings a single assembly without a large rear panel.
            foreach(float z in new[]{-.116f,.031f})
            {
                Rod(g,new Vector3(-.113f,.244f,z),new Vector3(.135f,.244f,z),.0065f,metal,32);
                foreach(float x in new[]{-.113f,.135f})Bolt(g,new Vector3(x,.252f,z),Vector3.up,.0035f);
            }
            g.Cylinder(c,.047f,.234f,dark,Vector3.right,64);
            foreach(float x in new[]{-.104f,.116f})
                Axial(g,new Vector3(x,c.y,c.z),new[]{new Vector2(-.005f,.030f),new Vector2(-.005f,.069f),new Vector2(-.003f,.076f),new Vector2(.003f,.076f),new Vector2(.005f,.069f),new Vector2(.005f,.030f)},paint,Vector3.right,64);
            // Several slender wire strands form the visible crown, rather than a solid screw thread.
            var rope=new List<Vector3>();
            for(int i=0;i<=896;i++){float t=i/896f,a=t*28*Mathf.PI*2;rope.Add(new Vector3(Mathf.Lerp(-.094f,.106f,t),c.y+Mathf.Sin(a)*.060f,c.z+Mathf.Cos(a)*.060f));}
            Tube(g,rope,.00335f,cable,8);
            // Motor has a rolled body, cast end bell, long tie bolts and a separate rear dust cap.
            Axial(g,new Vector3(-.202f,c.y,c.z),new[]{new Vector2(-.076f,0),new Vector2(-.076f,.042f),new Vector2(-.067f,.055f),new Vector2(-.052f,.058f),new Vector2(.047f,.058f),new Vector2(.060f,.051f),new Vector2(.071f,.045f),new Vector2(.071f,0)},paint,Vector3.right,64);
            foreach(float x in new[]{-.261f,-.148f})Ring(g,new Vector3(x,c.y,c.z),.056f,.0022f,cast,Vector3.right,48);
            Axial(g,new Vector3(-.281f,c.y,c.z),new[]{new Vector2(-.007f,0),new Vector2(-.007f,.038f),new Vector2(0,.047f),new Vector2(.007f,.044f),new Vector2(.007f,0)},rubber,Vector3.right,48);
            for(int i=0;i<4;i++){float a=(i*90+45)*Mathf.Deg2Rad;var offset=new Vector3(0,Mathf.Sin(a)*.058f,Mathf.Cos(a)*.058f);Rod(g,new Vector3(-.262f,c.y,c.z)+offset,new Vector3(-.142f,c.y,c.z)+offset,.0025f,metal,16);Bolt(g,new Vector3(-.270f,c.y,c.z)+offset,Vector3.left,.003f);}
            // Compact solenoid bridge follows the drum's back edge; no oversized box on the motor.
            g.Box(new Vector3(.003f,.269f,-.078f),new Vector3(.185f,.038f,.069f),paint,.009f);
            g.Box(new Vector3(.003f,.290f,-.078f),new Vector3(.158f,.006f,.054f),rubber,.005f);
            foreach(float x in new[]{-.065f,.070f})Bolt(g,new Vector3(x,.294f,-.078f),Vector3.up,.003f);
            g.Box(new Vector3(-.210f,.238f,-.040f),new Vector3(.055f,.016f,.040f),paint,.005f);
            foreach(float x in new[]{-.196f,-.225f})g.Cylinder(new Vector3(x,.250f,-.040f),.008f,.017f,rubber,Vector3.up,24);
            Curve(g,new[]{new Vector3(-.080f,.269f,-.087f),new Vector3(-.144f,.259f,-.098f),new Vector3(-.189f,.264f,-.070f),new Vector3(-.196f,.254f,-.040f)},.004f,rubber);
            Curve(g,new[]{new Vector3(-.265f,.270f,.152f),new Vector3(-.254f,.240f,.115f),new Vector3(-.232f,.260f,.026f),new Vector3(-.225f,.254f,-.040f)},.004f,rubber);
            // Stepped planetary reduction casing with a recessed cover and radial cast ribs.
            var gear=new Vector3(.208f,c.y,c.z);
            Axial(g,gear,new[]{new Vector2(-.071f,0),new Vector2(-.071f,.054f),new Vector2(-.062f,.075f),new Vector2(-.045f,.081f),new Vector2(.031f,.081f),new Vector2(.043f,.072f),new Vector2(.052f,.069f),new Vector2(.052f,0)},cast,Vector3.right,64);
            Axial(g,gear,new[]{new Vector2(.051f,0),new Vector2(.051f,.059f),new Vector2(.055f,.063f),new Vector2(.061f,.060f),new Vector2(.064f,.048f),new Vector2(.064f,0)},paint,Vector3.right,64);
            Ring(g,new Vector3(.266f,c.y,c.z),.054f,.0017f,metal,Vector3.right,64);
            for(int i=0;i<8;i++)
            {
                float a=(i*45+22.5f)*Mathf.Deg2Rad;var radial=new Vector3(0,Mathf.Cos(a),Mathf.Sin(a));
                Bolt(g,gear+new Vector3(.048f,0,0)+radial*.069f,Vector3.right,.0035f);
                Rod(g,gear+new Vector3(-.037f,0,0)+radial*.080f,gear+new Vector3(.027f,0,0)+radial*.080f,.0035f,cast,16);
            }
            g.Cylinder(new Vector3(.201f,.270f,c.z),.012f,.020f,cast,Vector3.up,32);
            Rod(g,new Vector3(.201f,.281f,c.z),new Vector3(.253f,.287f,c.z),.006f,metal,24);
            g.Box(new Vector3(.255f,.287f,c.z),new Vector3(.027f,.017f,.025f),orange,.006f);
            // Full-height folded fairlead bracket, with a genuine chamfered opening.
            Aperture(g,new Vector3(.004f,.158f,.129f),Quaternion.identity,.350f,.161f,.227f,.058f,.024f,.023f,paint);
            Aperture(g,new Vector3(.004f,.177f,.151f),Quaternion.identity,.290f,.091f,.224f,.052f,.018f,.023f,metal);
            foreach(float x in new[]{-.153f,.161f})Bolt(g,new Vector3(x,.159f,.145f),Vector3.forward,.005f);
            foreach(float x in new[]{-.151f,.159f})
            {
                Plate(g,new Vector3(x,0,0),Quaternion.Euler(0,90,0),new[]{new Vector2(-.124f,.078f),new Vector2(-.063f,.078f),new Vector2(-.122f,.203f)},.007f,paint);
                Bolt(g,new Vector3(x,.078f,.067f),Vector3.up,.004f);
            }
            Curve(g,new[]{rope.Last(),new Vector3(.106f,.188f,.055f),new Vector3(.048f,.180f,.115f),new Vector3(.020f,.175f,.174f)},.00335f,cable);
            // A forged hook rests at the fairlead; the pull strap is a thin flexible loop.
            Ring(g,new Vector3(.020f,.172f,.188f),.010f,.0037f,metal,Vector3.forward,40);
            var hook=new List<Vector3>();var radii=new List<float>();
            for(int i=0;i<=40;i++){float t=i/40f,a=Mathf.Lerp(80,338,t)*Mathf.Deg2Rad;hook.Add(new Vector3(.020f+Mathf.Cos(a)*.022f,.130f+Mathf.Sin(a)*.022f,.191f));radii.Add(Mathf.Lerp(.009f,.003f,t));}
            Rod(g,new Vector3(.020f,.160f,.191f),hook[0],.008f,orange);Tube(g,hook,.008f,orange,12,radii.ToArray());
            Rod(g,new Vector3(.022f,.151f,.198f),new Vector3(.039f,.121f,.198f),.0018f,metal,12);
            Bolt(g,new Vector3(.021f,.150f,.201f),Vector3.forward,.0025f);
            Curve(g,new[]{new Vector3(.010f,.112f,.194f),new Vector3(-.019f,.096f,.197f),new Vector3(-.055f,.093f,.196f),new Vector3(-.069f,.104f,.195f),new Vector3(-.056f,.110f,.195f),new Vector3(.009f,.117f,.194f)},.004f,webbing);
            return g;
        }
        static Geometry FireReel()
        {
            var g=new Geometry();var c=new Vector3(.041f,.250f,-.020f);
            g.Cylinder(c,.048f,.114f,dark,Vector3.forward,48);
            foreach(float z in new[]{-.081f,.046f})
            {
                Axial(g,new Vector3(c.x,c.y,z),new[]{new Vector2(-.005f,.106f),new Vector2(.005f,.106f),new Vector2(.007f,.121f),
                    new Vector2(.003f,.128f),new Vector2(-.003f,.128f),new Vector2(-.007f,.121f)},red,Vector3.forward,64);
                g.Cylinder(new Vector3(c.x,c.y,z),.039f,.013f,red,Vector3.forward,48);
                for(int i=0;i<4;i++)Plate(g,new Vector3(c.x,c.y,z),Quaternion.Euler(0,0,i*90+15),new[]{new Vector2(-.014f,.025f),new Vector2(.014f,.025f),new Vector2(.010f,.111f),new Vector2(-.010f,.111f)},.007f,red);
                Ring(g,new Vector3(c.x,c.y,z+.002f),.123f,.0017f,metal,Vector3.forward,64);
            }
            g.Cylinder(new Vector3(c.x,c.y,.057f),.026f,.012f,paint,Vector3.forward,48);
            for(int i=0;i<4;i++){float a=i*Mathf.PI*.5f;Bolt(g,new Vector3(c.x+Mathf.Sin(a)*.030f,c.y+Mathf.Cos(a)*.030f,.057f),Vector3.forward,.0025f);}
            var hose=new List<Vector3>();
            for(int i=0;i<=400;i++){float t=i/400f,a=t*10*Mathf.PI*2+Mathf.PI*.5f;hose.Add(new Vector3(c.x+Mathf.Sin(a)*.088f,c.y+Mathf.Cos(a)*.088f,Mathf.Lerp(-.071f,.036f,t)));}
            Tube(g,hose,.0054f,rubber,10);
            // The reel has its own winding crank, rather than an unsupported decorative wheel.
            g.Cylinder(new Vector3(c.x,c.y,.081f),.008f,.043f,metal,Vector3.forward,32);
            Rod(g,new Vector3(c.x,c.y,.103f),new Vector3(c.x+.073f,c.y-.024f,.103f),.006f,metal,24);
            g.Cylinder(new Vector3(c.x+.073f,c.y-.024f,.122f),.008f,.034f,rubber,Vector3.forward,32);
            foreach(float x in new[]{-.065f,.147f})
            {Rod(g,new Vector3(x,.084f,-.081f),new Vector3(.041f,.250f,-.081f),.011f,paint,32);g.Box(new Vector3(x,.080f,-.081f),new Vector3(.050f,.012f,.062f),paint,.003f);Bolt(g,new Vector3(x,.089f,-.100f),Vector3.up,.0035f);}
            Rod(g,new Vector3(-.065f,.086f,-.081f),new Vector3(.147f,.086f,-.081f),.008f,paint,24);
            Rod(g,new Vector3(-.192f,.112f,.09f),new Vector3(.122f,.112f,.09f),.012f,brass,32);
            Axial(g,new Vector3(-.215f,.112f,.09f),new[]{new Vector2(-.023f,.008f),new Vector2(-.023f,.018f),new Vector2(.016f,.018f),new Vector2(.022f,.012f)},brass,Vector3.right,40);
            Curve(g,new[]{new Vector3(.122f,.112f,.09f),new Vector3(.164f,.112f,.055f),new Vector3(.164f,.134f,-.091f),
                new Vector3(.164f,.240f,-.096f),new Vector3(.041f,.250f,-.096f)},.007f,brass);
            foreach(float x in new[]{-.116f,.085f})
            {Plate(g,new Vector3(x,0,.09f),Quaternion.Euler(0,90,0),new[]{new Vector2(-.027f,.075f),new Vector2(.027f,.075f),new Vector2(.013f,.112f),new Vector2(-.013f,.112f)},.018f,paint);Bolt(g,new Vector3(x,.080f,.070f),Vector3.up,.003f);}
            foreach(float x in new[]{-.202f,-.138f,.105f})
            {g.Cylinder(new Vector3(x,.112f,.09f),.019f,.023f,brass,Vector3.right,6);Ring(g,new Vector3(x+.013f,.112f,.09f),.013f,.002f,rubber,Vector3.right,32);}
            Rod(g,new Vector3(-.177f,.112f,.09f),new Vector3(-.177f,.228f,.09f),.007f,brass);
            Gauge(g,new Vector3(-.177f,.251f,.103f),.028f);Ring(g,new Vector3(-.177f,.251f,.104f),.029f,.002f,rubber,Vector3.forward,48);
            g.Cylinder(new Vector3(-.177f,.145f,.09f),.006f,.039f,metal,Vector3.up,24);
            Ring(g,new Vector3(-.177f,.166f,.09f),.022f,.003f,red,Vector3.up,40);
            for(int i=0;i<4;i++){float a=i*Mathf.PI*.5f;Rod(g,new Vector3(-.177f,.166f,.09f),new Vector3(-.177f+Mathf.Cos(a)*.021f,.166f,.09f+Mathf.Sin(a)*.021f),.0025f,red,16);}
            Curve(g,new[]{hose.Last(),new Vector3(.207f,.240f,.09f),new Vector3(.244f,.16f,.165f),new Vector3(.230f,.091f,.173f),new Vector3(.207f,.124f,.172f)},.0054f,rubber);
            Axial(g,new Vector3(.207f,.182f,.172f),new[]{new Vector2(-.057f,.008f),new Vector2(-.057f,.014f),new Vector2(-.036f,.018f),
                new Vector2(.021f,.018f),new Vector2(.033f,.012f),new Vector2(.052f,.011f),new Vector2(.056f,.007f)},paint,Vector3.up,48);
            foreach(float y in new[]{.152f,.163f,.174f,.185f,.196f})Ring(g,new Vector3(.207f,y,.172f),.018f,.0018f,rubber,Vector3.up,40);
            Axial(g,new Vector3(.207f,.233f,.172f),new[]{new Vector2(-.006f,.008f),new Vector2(-.006f,.012f),new Vector2(.008f,.012f),new Vector2(.010f,.007f)},metal,Vector3.up,40);
            g.Cylinder(new Vector3(.207f,.133f,.172f),.019f,.013f,brass,Vector3.up,6);
            Rod(g,new Vector3(.207f,.181f,.192f),new Vector3(.225f,.203f,.195f),.0035f,red,16);
            g.Box(new Vector3(.207f,.149f,.151f),new Vector3(.033f,.019f,.018f),paint,.004f);
            Plate(g,new Vector3(.207f,0,.142f),Quaternion.identity,new[]{new Vector2(-.018f,.075f),new Vector2(.018f,.075f),new Vector2(.012f,.181f),new Vector2(-.012f,.181f)},.006f,paint);
            return g;
        }
        static Geometry WaterPump()
        {
            var g=new Geometry();var pump=new Vector3(.105f,.181f,-.020f);var motor=new Vector3(-.107f,.181f,-.020f);
            // Coaxial electric pump. The motor, guarded coupling and impeller bowl form one drive train.
            Axial(g,motor,new[]{new Vector2(-.112f,0),new Vector2(-.112f,.049f),new Vector2(-.099f,.061f),new Vector2(.085f,.061f),new Vector2(.101f,.050f),new Vector2(.105f,0)},green,Vector3.right,64);
            for(int i=0;i<12;i++)
            {
                float a=i*Mathf.PI/6;var offset=new Vector3(0,Mathf.Sin(a)*.064f,Mathf.Cos(a)*.064f);
                Rod(g,motor+new Vector3(-.087f,0,0)+offset,motor+new Vector3(.073f,0,0)+offset,.0035f,cast,16);
            }
            foreach(float x in new[]{-.201f,-.017f})Ring(g,new Vector3(x,.181f,-.020f),.062f,.003f,cast,Vector3.right,48);
            Axial(g,new Vector3(-.229f,.181f,-.020f),new[]{new Vector2(-.010f,0),new Vector2(-.010f,.046f),new Vector2(-.004f,.057f),new Vector2(.009f,.057f),new Vector2(.014f,.049f),new Vector2(.014f,0)},paint,Vector3.right,48);
            for(int i=0;i<8;i++)
            {float a=i*Mathf.PI*.25f;var n=new Vector3(0,Mathf.Sin(a),Mathf.Cos(a));Rod(g,new Vector3(-.240f,.181f,-.020f)+n*.022f,new Vector3(-.240f,.181f,-.020f)+n*.043f,.003f,cast,16);}
            g.Cylinder(new Vector3(.042f,.181f,-.020f),.034f,.092f,paint,Vector3.right,48);
            foreach(float x in new[]{.012f,.069f})Ring(g,new Vector3(x,.181f,-.020f),.034f,.002f,metal,Vector3.right,40);
            Axial(g,pump,new[]{new Vector2(-.057f,0),new Vector2(-.057f,.047f),new Vector2(-.042f,.079f),new Vector2(-.020f,.089f),new Vector2(.017f,.090f),new Vector2(.038f,.075f),new Vector2(.047f,.045f),new Vector2(.047f,0)},metal,Vector3.right,64);
            Ring(g,pump+Vector3.right*.025f,.080f,.002f,cast,Vector3.right,64);
            for(int i=0;i<6;i++)
            {float a=i*Mathf.PI/3;var n=new Vector3(0,Mathf.Cos(a),Mathf.Sin(a));Bolt(g,pump+Vector3.right*.035f+n*.074f,Vector3.right,.004f);Rod(g,pump-Vector3.right*.032f+n*.080f,pump+Vector3.right*.024f+n*.075f,.003f,cast,16);}
            // Short intake elbow and two hollow cam-lock style couplings.
            Rod(g,pump+Vector3.right*.043f,new Vector3(.173f,.181f,-.020f),.026f,metal,40);
            Curve(g,new[]{new Vector3(.169f,.181f,-.020f),new Vector3(.191f,.181f,-.013f),new Vector3(.198f,.181f,.016f),new Vector3(.198f,.181f,.080f)},.026f,metal);
            Coupling(g,new Vector3(.198f,.181f,.111f),Vector3.forward,.029f);
            Coupling(g,new Vector3(.105f,.296f,-.020f),Vector3.up,.025f);
            g.Box(new Vector3(-.103f,.260f,-.020f),new Vector3(.095f,.038f,.075f),paint,.009f);
            g.Box(new Vector3(-.103f,.281f,-.020f),new Vector3(.081f,.005f,.061f),rubber,.005f);
            foreach(float x in new[]{-.133f,-.073f})Bolt(g,new Vector3(x,.285f,-.020f),Vector3.up,.0025f);
            Curve(g,new[]{new Vector3(-.265f,.270f,.152f),new Vector3(-.222f,.266f,.118f),new Vector3(-.171f,.265f,.067f),new Vector3(-.145f,.260f,-.020f)},.004f,rubber);
            foreach(float x in new[]{-.151f,.104f})
            {
                g.Box(new Vector3(x,.081f,-.020f),new Vector3(.077f,.015f,.158f),paint,.004f);
                Plate(g,new Vector3(x,0,-.020f),Quaternion.Euler(0,90,0),new[]{new Vector2(-.073f,.085f),new Vector2(.073f,.085f),new Vector2(.049f,x<0?.135f:.119f),new Vector2(-.049f,x<0?.135f:.119f)},.034f,cast);
                foreach(float z in new[]{-.084f,.044f})Bolt(g,new Vector3(x,.092f,z),Vector3.up,.004f);
            }
            foreach(float x in new[]{-.248f,.248f})
            {
                Curve(g,new[]{new Vector3(x,.087f,-.122f),new Vector3(x,.320f,-.122f),new Vector3(x,.343f,-.092f),new Vector3(x,.343f,.093f),new Vector3(x,.320f,.123f),new Vector3(x,.087f,.123f)},.009f,paint);
                Rod(g,new Vector3(x,.344f,-.067f),new Vector3(x,.344f,.067f),.012f,rubber,32);
            }
            foreach(float z in new[]{-.114f,.114f})Rod(g,new Vector3(-.248f,.084f,z),new Vector3(.248f,.084f,z),.009f,paint,32);
            return g;
        }
        static void Coupling(Geometry g,Vector3 center,Vector3 axis,float radius)
        {
            Axial(g,center,new[]{new Vector2(-.034f,radius*.65f),new Vector2(-.034f,radius),new Vector2(.009f,radius),new Vector2(.012f,radius*1.15f),new Vector2(.027f,radius*1.15f),new Vector2(.031f,radius),new Vector2(.031f,radius*.70f)},metal,axis,48);
            Ring(g,center+axis*.008f,radius,.002f,rubber,axis,48);
            var transverse=Mathf.Abs(axis.y)>.5f?Vector3.right:Vector3.up;
            foreach(int side in new[]{-1,1})
            {Rod(g,center+transverse*side*radius,center+transverse*side*(radius+.013f)-axis*.014f,.004f,cast,16);}
        }
        static Geometry ServiceUnit()
        {
            var g=new Geometry();
            // Field-service instrument: sloping control panel, deep pressed sides and a lower tool drawer.
            var outline=new[]{new Vector2(-.152f,.100f),new Vector2(.144f,.100f),new Vector2(.144f,.347f),new Vector2(.125f,.362f),new Vector2(-.094f,.362f),new Vector2(-.152f,.190f)};
            Plate(g,new Vector3(.015f,0,0),Quaternion.Euler(0,90,0),outline,.478f,green);
            foreach(float x in new[]{-.227f,.257f})
            {
                Plate(g,new Vector3(x,0,0),Quaternion.Euler(0,90,0),outline,.012f,paint);
                g.Box(new Vector3(x,.238f,-.018f),new Vector3(.014f,.099f,.140f),dark,.008f);
                for(int i=0;i<7;i++)g.Box(new Vector3(x+Mathf.Sign(x)*.009f,.238f,-.074f+i*.019f),new Vector3(.006f,.079f,.008f),cast,.002f);
            }
            foreach(float x in new[]{-.178f,.208f})foreach(float z in new[]{-.099f,.108f})
            {g.Box(new Vector3(x,.090f,z),new Vector3(.052f,.031f,.044f),rubber,.006f);Bolt(g,new Vector3(x,.110f,z),Vector3.up,.0035f);}
            var panel=new Geometry();panel.Box(Vector3.zero,new Vector3(.431f,.155f,.010f),paint,.007f);
            panel.Box(new Vector3(-.108f,.023f,.006f),new Vector3(.148f,.072f,.004f),metal,.004f);
            panel.Box(new Vector3(-.108f,.023f,.009f),new Vector3(.128f,.052f,.004f),rubber,.003f);
            foreach(float x in new[]{.024f,.086f})
            {
                Axial(panel,new Vector3(x,.024f,.013f),new[]{new Vector2(-.007f,.008f),new Vector2(-.007f,.019f),new Vector2(.006f,.019f),new Vector2(.008f,.013f),new Vector2(.008f,.008f)},metal,Vector3.forward,32);
                panel.Cylinder(new Vector3(x,.024f,.016f),.010f,.011f,x<.05f?red:rubber,Vector3.forward,32);
            }
            panel.Cylinder(new Vector3(.164f,.024f,.015f),.020f,.020f,rubber,Vector3.forward,40);
            panel.Box(new Vector3(.164f,.030f,.026f),new Vector3(.003f,.018f,.002f),ivory,.0005f);
            foreach(float x in new[]{-.131f,-.087f,-.043f})
            {panel.Cylinder(new Vector3(x,-.042f,.010f),.007f,.008f,metal,Vector3.forward,24);Rod(panel,new Vector3(x,-.042f,.014f),new Vector3(x,-.037f,.027f),.0025f,metal,16);}
            foreach(float x in new[]{-.197f,.197f})foreach(float y in new[]{-.063f,.063f})Bolt(panel,new Vector3(x,y,.006f),Vector3.forward,.0027f);
            g.Append(panel,Matrix4x4.TRS(new Vector3(.015f,.274f,.127f),Quaternion.Euler(-18,0,0),Vector3.one));
            g.Box(new Vector3(.015f,.145f,.155f),new Vector3(.431f,.072f,.012f),rubber,.006f);
            g.Box(new Vector3(.015f,.145f,.162f),new Vector3(.418f,.062f,.009f),green,.004f);
            foreach(float x in new[]{-.050f,.080f})g.Box(new Vector3(x,.149f,.174f),new Vector3(.015f,.014f,.023f),metal,.003f);
            g.Cylinder(new Vector3(.015f,.149f,.185f),.006f,.143f,rubber,Vector3.right,32);
            foreach(float x in new[]{-.128f,.158f})g.Box(new Vector3(x,.380f,-.010f),new Vector3(.026f,.036f,.036f),cast,.005f);
            Curve(g,new[]{new Vector3(-.128f,.383f,-.010f),new Vector3(-.128f,.410f,-.010f),new Vector3(.158f,.410f,-.010f),new Vector3(.158f,.383f,-.010f)},.007f,metal);
            g.Cylinder(new Vector3(.015f,.412f,-.010f),.010f,.205f,rubber,Vector3.right,40);
            var lead=new List<Vector3>();
            for(int i=0;i<=200;i++){float t=i/200f,a=t*4*Mathf.PI*2;lead.Add(new Vector3(-.242f-t*.020f,.233f+Mathf.Sin(a)*.063f,-.025f+Mathf.Cos(a)*.063f));}
            Tube(g,lead,.0035f,rubber,8);
            Curve(g,new[]{lead.Last(),new Vector3(-.266f,.149f,.04f),new Vector3(-.266f,.123f,.074f)},.0035f,rubber);
            g.Cylinder(new Vector3(-.266f,.155f,.074f),.006f,.063f,red,Vector3.up,24);
            g.Cylinder(new Vector3(-.266f,.194f,.074f),.002f,.019f,metal,Vector3.up,16);
            return g;
        }
        static Geometry TransitCase()
        {
            var g=new Geometry();
            g.Box(new Vector3(0,.225f,-.003f),new Vector3(.490f,.254f,.302f),green,.016f);
            g.Box(new Vector3(0,.354f,-.003f),new Vector3(.496f,.009f,.307f),rubber,.005f);
            g.Box(new Vector3(0,.379f,-.003f),new Vector3(.502f,.042f,.314f),paint,.016f);
            g.Box(new Vector3(0,.402f,-.003f),new Vector3(.425f,.004f,.239f),green,.009f);
            foreach(float x in new[]{-.19f,-.095f,.095f,.19f})
            {
                // Moulded ribs continue from the wall onto the lid, with a separate gasket reveal.
                g.Box(new Vector3(x,.228f,.150f),new Vector3(.017f,.211f,.013f),green,.004f);
                g.Box(new Vector3(x,.381f,.156f),new Vector3(.018f,.027f,.010f),cast,.003f);
                g.Box(new Vector3(x,.405f,-.003f),new Vector3(.013f,.010f,.223f),green,.004f);
            }
            foreach(int side in new[]{-1,1})
            {
                foreach(float z in new[]{-.105f,.099f})g.Box(new Vector3(side*.239f,.225f,z),new Vector3(.022f,.215f,.021f),paint,.006f);
                foreach(float z in new[]{-.128f,.122f})
                {
                    g.Box(new Vector3(side*.229f,.117f,z),new Vector3(.045f,.060f,.041f),rubber,.007f);
                    g.Box(new Vector3(side*.232f,.374f,z),new Vector3(.045f,.045f,.040f),rubber,.006f);
                }
                float x=side*.145f;
                g.Box(new Vector3(x,.344f,.158f),new Vector3(.049f,.062f,.012f),rubber,.005f);
                g.Box(new Vector3(x,.341f,.166f),new Vector3(.035f,.050f,.008f),metal,.004f);
                g.Box(new Vector3(x,.334f,.171f),new Vector3(.022f,.024f,.004f),dark,.002f);
                g.Cylinder(new Vector3(x,.365f,.167f),.005f,.045f,metal,Vector3.right,32);
                g.Cylinder(new Vector3(x,.377f,-.166f),.007f,.079f,cast,Vector3.right,32);
                for(int i=0;i<3;i++)Ring(g,new Vector3(x-.027f+i*.027f,.377f,-.166f),.007f,.001f,metal,Vector3.right,24);
                foreach(float y in new[]{.337f,.388f})g.Box(new Vector3(x,y,-.159f),new Vector3(.064f,.025f,.009f),cast,.003f);
                g.Box(new Vector3(side*.109f,.410f,-.003f),new Vector3(.025f,.015f,.029f),rubber,.004f);
            }
            g.Box(new Vector3(0,.418f,-.003f),new Vector3(.232f,.015f,.023f),rubber,.006f);
            g.Cylinder(new Vector3(.200f,.277f,.160f),.014f,.010f,dark,Vector3.forward,32);
            g.Cylinder(new Vector3(.200f,.277f,.166f),.009f,.003f,cast,Vector3.forward,32);
            foreach(int side in new[]{-1,1})
            {
                for(int i=0;i<4;i++)g.Box(new Vector3(side*.249f,.225f,-.071f+i*.046f),new Vector3(.010f,.151f,.010f),green,.003f);
                Aperture(g,new Vector3(side*.258f,.265f,-.003f),Quaternion.Euler(0,90,0),.110f,.051f,.077f,.024f,.011f,.011f,rubber);
            }
            return g;
        }
        static void Gauge(Geometry g,Vector3 face,float radius)
        {
            g.Cylinder(face,radius,.014f,metal,Vector3.forward,48);g.Cylinder(face+Vector3.forward*.008f,radius*.86f,.003f,ivory,Vector3.forward,48);
            for(int i=0;i<11;i++)RotatedBox(g,face+Vector3.forward*.0105f,Quaternion.Euler(0,0,-120+i*24),new Vector3(0,radius*.68f,0),new Vector3(.0013f,.0045f,.0008f),dark,.0002f);
            RotatedBox(g,face+Vector3.forward*.012f,Quaternion.Euler(0,0,42),new Vector3(0,radius*.25f,0),new Vector3(.0015f,radius*.90f,.001f),red,.0002f);
            g.Cylinder(face+Vector3.forward*.014f,.003f,.002f,dark,Vector3.forward,20);
        }
        internal static void RotatedBox(Geometry g,Vector3 origin,Quaternion rotation,Vector3 offset,Vector3 size,Material material,float bevel)
        {var box=new Geometry();box.Box(offset,size,material,bevel);g.Append(box,Matrix4x4.TRS(origin,rotation,Vector3.one));}
        internal static void Curve(Geometry g,Vector3[] anchors,float radius,Material material)
        {
            var points=new List<Vector3>();
            for(int i=0;i<anchors.Length-1;i++)for(int j=0;j<8;j++)
            {
                float t=j/8f;var a=anchors[Math.Max(0,i-1)];var b=anchors[i];var c=anchors[i+1];var d=anchors[Math.Min(anchors.Length-1,i+2)];
                points.Add(.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t));
            }
            points.Add(anchors.Last());Tube(g,points,radius,material,12);
        }
        internal static void Tube(Geometry g,List<Vector3> points,float radius,Material material,int sides=10,float[] radii=null)
        {
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            var previous=(points[1]-points[0]).normalized;var normal=Vector3.Cross(previous,Mathf.Abs(previous.y)>.85f?Vector3.right:Vector3.up).normalized;float length=0;
            for(int i=0;i<points.Count;i++)
            {
                var tangent=(points[Math.Min(i+1,points.Count-1)]-points[Math.Max(0,i-1)]).normalized;
                normal=Vector3.ProjectOnPlane(Quaternion.FromToRotation(previous,tangent)*normal,tangent).normalized;var binormal=Vector3.Cross(tangent,normal).normalized;
                if(i>0)length+=Vector3.Distance(points[i],points[i-1]);
                for(int j=0;j<sides;j++){float a=j*Mathf.PI*2/sides;var n=normal*Mathf.Cos(a)+binormal*Mathf.Sin(a);vertices.Add(points[i]+n*(radii==null?radius:radii[i]));normals.Add(n);uv.Add(new Vector2(length*8,j/(float)sides));}
                previous=tangent;
            }
            for(int i=0;i<points.Count-1;i++)for(int j=0;j<sides;j++){int a=i*sides+j,b=i*sides+(j+1)%sides,c=b+sides,d=a+sides;triangles.AddRange(new[]{a,b,c,a,c,d});}
            foreach(int end in new[]{0,points.Count-1})
            {
                var n=(end==0?points[0]-points[1]:points[end]-points[end-1]).normalized;int start=vertices.Count;vertices.Add(points[end]);normals.Add(n);uv.Add(Vector2.zero);
                for(int j=0;j<sides;j++){vertices.Add(vertices[end*sides+j]);normals.Add(n);uv.Add(Vector2.zero);}
                for(int j=0;j<sides;j++)triangles.AddRange(new[]{start,start+1+j,start+1+(j+1)%sides});
            }
            for(int i=0;i<triangles.Count;i+=3){int a=triangles[i],b=triangles[i+1],c=triangles[i+2];if(Vector3.Dot(Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]),normals[a]+normals[b]+normals[c])<0){triangles[i+1]=c;triangles[i+2]=b;}}
            var mesh=new Mesh{indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();mesh.RecalculateTangents();g.AddMesh(mesh,material);
        }
        static void Label(Transform root,string caption)
        {
            var label=new GameObject("ModuleLabel",typeof(RectTransform),typeof(Canvas));label.transform.SetParent(root,false);
            label.transform.localPosition=new Vector3(0,.054f,.186f);label.transform.localRotation=Quaternion.Euler(0,180,0);label.transform.localScale=Vector3.one*.001f;
            label.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;label.GetComponent<RectTransform>().sizeDelta=new Vector2(160,18);
            var paper=new GameObject("PrintedLabel",typeof(RectTransform),typeof(Image));paper.transform.SetParent(label.transform,false);Fill(paper.GetComponent<RectTransform>());
            paper.GetComponent<Image>().color=new Color(.08f,.10f,.10f);paper.GetComponent<Image>().raycastTarget=false;
            var go=new GameObject("ModuleName",typeof(RectTransform),typeof(Text));go.transform.SetParent(paper.transform,false);Fill(go.GetComponent<RectTransform>());
            var text=go.GetComponent<Text>();text.font=Resources.Load<Font>("Fonts/SourceSans3-Semibold");text.text=caption;text.fontSize=14;text.resizeTextForBestFit=true;text.resizeTextMinSize=11;text.resizeTextMaxSize=14;
            text.color=new Color(.65f,.68f,.65f);text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;
        }
        static void Fill(RectTransform rect){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
        internal static void Render(GameObject prefab,string path,bool thumbnail)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));var scene=EditorSceneManager.NewPreviewScene();var old=RenderTexture.active;RenderTexture target=null;Texture2D pixels=null;Material floorMaterial=null;
            try
            {
                var model=Object.Instantiate(prefab);SceneManager.MoveGameObjectToScene(model,scene);model.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                foreach(var script in model.GetComponentsInChildren<MonoBehaviour>())script.enabled=false;
                foreach(var lod in model.GetComponentsInChildren<LODGroup>())lod.ForceLOD(0);
                var renderers=model.GetComponentsInChildren<MeshRenderer>();var bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);
                var go=new GameObject("ModulePreviewCamera");SceneManager.MoveGameObjectToScene(go,scene);var camera=go.AddComponent<Camera>();camera.scene=scene;
                camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=thumbnail?new Color(0,0,0,0):new Color(.125f,.14f,.145f,1);
                camera.allowHDR=false;camera.nearClipPlane=.01f;camera.farClipPlane=20;
                camera.transform.position=bounds.center+new Vector3(.95f,.62f,1.45f).normalized*3;camera.transform.LookAt(bounds.center);
                int width=thumbnail?512:1400,height=thumbnail?512:1000;camera.aspect=width/(float)height;float extent=0;
                foreach(float x in new[]{-1f,1f})foreach(float y in new[]{-1f,1f})foreach(float z in new[]{-1f,1f})
                {var p=camera.transform.InverseTransformPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z)));extent=Mathf.Max(extent,Mathf.Abs(p.y),Mathf.Abs(p.x)/camera.aspect);}
                camera.orthographicSize=extent*1.12f;var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;data.renderShadows=true;
                Light(scene,new Vector3(45,-145,0),2.1f,new Color(1,.96f,.90f),true);Light(scene,new Vector3(25,40,0),.45f,new Color(.86f,.92f,1));Light(scene,new Vector3(30,120,0),.7f,Color.white);
                if(!thumbnail)
                {
                    var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,scene);floor.transform.position=new Vector3(0,-.012f,0);floor.transform.localScale=new Vector3(200,.02f,200);
                    floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMaterial.SetColor("_BaseColor",new Color(.125f,.14f,.145f));floorMaterial.SetFloat("_Smoothness",.14f);floor.GetComponent<MeshRenderer>().sharedMaterial=floorMaterial;
                }
                target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);target.antiAliasing=4;camera.targetTexture=target;Canvas.ForceUpdateCanvases();camera.Render();camera.Render();
                RenderTexture.active=target;pixels=new Texture2D(width,height,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());
            }
            finally{RenderTexture.active=old;if(target)RenderTexture.ReleaseTemporary(target);if(pixels)Object.DestroyImmediate(pixels);EditorSceneManager.ClosePreviewScene(scene);if(floorMaterial)Object.DestroyImmediate(floorMaterial);}
        }
        static void Light(Scene scene,Vector3 angles,float intensity,Color color,bool shadows=false)
        {var go=new GameObject("ModulePreviewLight");SceneManager.MoveGameObjectToScene(go,scene);go.transform.rotation=Quaternion.Euler(angles);var light=go.AddComponent<Light>();light.type=LightType.Directional;light.intensity=intensity;light.color=color;light.shadows=shadows?LightShadows.Soft:LightShadows.None;light.shadowBias=.006f;light.shadowNormalBias=.008f;}
    }
}
