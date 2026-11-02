using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
using Geometry=CompanySimEditor.WorkshopCraftBuilder.Geometry;

namespace CompanySimEditor
{
    public static class GarageStorageBuilder
    {
        const string Folder="Assets/_Project/Art/GarageStorage",RootName="17_GarageStorage";
        static Material[] shell,lid;
        static Material trim,steel,amber;
        [MenuItem("Company/Refine Garage Storage Cases")]
        public static void Apply()
        {
            if(EditorApplication.isPlaying)throw new Exception("Stop Play Mode before editing storage models.");
            EditorSceneManager.OpenScene(GarageSystemsBuilder.ScenePath);
            if(!File.Exists("Documentation/StorageDetailBefore_SideShelf.png"))Capture("Before");
            Configure();
            var probe=GameObject.Find("GarageReflection").GetComponent<ReflectionProbe>();var path=AssetDatabase.GetAssetPath(probe.customBakedTexture);
            if(!Lightmapping.BakeReflectionProbe(probe,path))throw new Exception("Garage reflection bake failed.");
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);probe.customBakedTexture=AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            AssetDatabase.SaveAssets();Capture("After");
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
        public static void ApplyAndValidate(){Apply();ProjectValidation.Run();}
        internal static void Configure()
        {
            Directory.CreateDirectory(Folder+"/Meshes");Directory.CreateDirectory(Folder+"/Materials");AssetDatabase.Refresh();
            var colors=new[]{new Color(.19f,.25f,.255f),new Color(.34f,.36f,.33f),new Color(.115f,.135f,.145f)};
            shell=colors.Select((c,i)=>Plastic("CaseShell_"+i,c)).ToArray();
            lid=colors.Select((c,i)=>Plastic("CaseLid_"+i,c*1.12f)).ToArray();
            trim=Plastic("CaseGasket",new Color(.045f,.052f,.05f));amber=Plastic("PartsBinOchre",new Color(.39f,.27f,.13f));
            steel=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/WorkshopCraft/Materials/MachinedSteel.mat");
            if(!steel)throw new Exception("Workshop steel material is missing.");
            var source=GameObject.Find("01_Environment_OfficeGarage").GetComponentsInChildren<MeshRenderer>();
            var bodies=source.Where(r=>r.name.StartsWith("G08_molded bin body")||r.name.StartsWith("G19_molded bin body")||
                r.name.StartsWith("G22_molded bin body")||r.name.StartsWith("G12_bin lower hull")).OrderBy(r=>r.name,StringComparer.Ordinal).ToArray();
            if(bodies.Length!=20||bodies.Any(r=>r.bounds.center.x<.4f))throw new Exception("Expected twenty original garage storage bodies, all outside the office.");
            var old=GameObject.Find(RootName);if(old)Object.DestroyImmediate(old);
            var root=new GameObject(RootName).transform;
            var groups=new Dictionary<string,Geometry>();var report=new List<string>();int closed=0,open=0;
            for(int index=0;index<bodies.Length;index++)
            {
                var body=bodies[index];var fit=Fit(body);bool caseWithLid=body.name.StartsWith("G08_")||body.name.StartsWith("G19_");
                string group=fit.center.x>7.7f&&fit.center.z>2.5f?"FloorStorageCase":fit.center.x>8.3f?"SideShelfStorage":fit.center.x<4.5f?"RearShelfLeftStorage":"RearShelfRightStorage";
                if(!groups.TryGetValue(group,out var geometry)){geometry=new Geometry();groups.Add(group,geometry);}
                Geometry model;
                if(caseWithLid){model=ClosedCase(fit.size,index);closed++;}
                else{model=PartsBin(fit.size,index,body.name.StartsWith("G12_"));open++;}
                geometry.Append(model,Matrix4x4.TRS(fit.center,fit.rotation,Vector3.one));
                report.Add(body.name+" | original base "+(fit.center.y-fit.size.y*.5f).ToString("F4")+" | original dimensions "+fit.size.ToString("F4"));
            }
            var retired=source.Where(r=>new[]{"G08_","G19_","G22_","G12_"}.Any(p=>r.name.StartsWith(p,StringComparison.Ordinal))).ToArray();
            foreach(var renderer in retired)renderer.enabled=false;
            foreach(var pair in groups)
            {
                var mesh=pair.Value.Build(out var materials);mesh.name=pair.Key;
                var go=new GameObject(pair.Key,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root,false);
                go.GetComponent<MeshFilter>().sharedMesh=GeneratedMeshAsset.Store(mesh,Folder+"/Meshes/"+pair.Key+".asset");go.GetComponent<MeshRenderer>().sharedMaterials=materials;
                GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.ReflectionProbeStatic);
            }
            if(root.GetComponentsInChildren<Collider>().Length!=0||root.GetComponentsInChildren<Light>().Length!=0)throw new Exception("Storage visuals must not introduce colliders or lights.");
            report.Insert(0,closed+" closed cases; "+open+" open parts bins; "+retired.Length+" original decorative renderers hidden; "+groups.Count+" spatially grouped renderers. Source transforms and physical collision untouched.");
            File.WriteAllLines("Logs/garage_storage_geometry.txt",report);
        }
        struct Fitting {public Vector3 center,size;public Quaternion rotation;}
        static Fitting Fit(MeshRenderer body)
        {
            var filter=body.GetComponent<MeshFilter>();var mesh=filter.sharedMesh;
            // Average the smoothed side-face corner normals. A single corner is
            // biased by its bevel and would rotate a source case a few degrees.
            var sides=mesh.normals.Select(n=>filter.transform.TransformDirection(n)).Where(n=>Mathf.Abs(n.y)<.12f&&n.x>.90f).ToArray();
            if(sides.Length==0)throw new Exception("No horizontal case-side reference on "+body.name);
            var side=sides.Aggregate(Vector3.zero,(sum,n)=>sum+n);
            var right=new Vector3(side.x,0,side.z).normalized;var forward=Vector3.Cross(right,Vector3.up).normalized;
            var minimum=Vector3.one*float.PositiveInfinity;var maximum=Vector3.one*float.NegativeInfinity;
            foreach(var point in mesh.vertices)
            {
                var world=filter.transform.TransformPoint(point);var p=new Vector3(Vector3.Dot(world,right),world.y,Vector3.Dot(world,forward));
                minimum=Vector3.Min(minimum,p);maximum=Vector3.Max(maximum,p);
            }
            var center=(minimum+maximum)*.5f;
            return new Fitting{center=right*center.x+Vector3.up*center.y+forward*center.z,size=maximum-minimum,rotation=Quaternion.LookRotation(forward,Vector3.up)};
        }
        static Material Plastic(string name,Color color)
        {
            color.a=1;string path=Folder+"/Materials/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
            material.SetColor("_BaseColor",color);material.SetFloat("_Metallic",0);material.SetFloat("_Smoothness",.23f);
            material.SetFloat("_Surface",0);material.SetFloat("_Cull",2);material.SetFloat("_ZWrite",1);
            const string texture="Assets/_Project/Art/Garage/Textures/MouldedPlastic_";
            material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texture+"Albedo.png"));
            material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texture+"Normal.png"));material.SetFloat("_BumpScale",.12f);
            BaseShaderGUI.SetMaterialKeywords(material,LitGUI.SetMaterialKeywords);EditorUtility.SetDirty(material);return material;
        }
        static Geometry ClosedCase(Vector3 size,int index)
        {
            var g=new Geometry();float w=size.x,h=size.y,d=size.z,s=Mathf.Clamp(Mathf.Min(w,d)/.30f,.75f,1.7f);int color=index%3;
            float top=h*.5f,bottom=-h*.5f;
            g.Box(new Vector3(0,.006f*s,0),new Vector3(w-.006f*s,h-.012f*s,d-.006f*s),shell[color],.014f*s);
            foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})g.Box(new Vector3(x*(w*.5f-.032f*s),bottom+.006f*s,z*(d*.5f-.032f*s)),new Vector3(.045f*s,.012f*s,.045f*s),trim,.004f*s);
            g.Box(new Vector3(0,top+.004f*s,0),new Vector3(w+.003f*s,.010f*s,d+.003f*s),trim,.006f*s);
            g.Box(new Vector3(0,top+.028f*s,0),new Vector3(w+.012f*s,.041f*s,d+.012f*s),lid[color],.012f*s);
            g.Box(new Vector3(0,top+.050f*s,0),new Vector3(w*.80f,.003f*s,d*.72f),shell[color],.007f*s);
            foreach(int side in new[]{-1,1})
            {
                float x=side*w*.285f,z=-d*.5f;
                g.Box(new Vector3(x,top-.006f*s,z-.006f*s),new Vector3(.034f*s,.074f*s,.009f*s),trim,.005f*s);
                g.Box(new Vector3(x,top-.004f*s,z-.012f*s),new Vector3(.024f*s,.053f*s,.004f*s),steel,.003f*s);
                g.Cylinder(new Vector3(x,top-.018f*s,z-.016f*s),.004f*s,.029f*s,steel,Vector3.right,20);
                foreach(float y in new[]{-.026f,.019f})g.Cylinder(new Vector3(x,top+y*s,z-.016f*s),.0024f*s,.003f*s,trim,Vector3.forward,12);
                // Recessed side pocket and a raised grip bridge.
                g.Box(new Vector3(side*(w*.5f-.001f),h*.12f,0),new Vector3(.003f,.045f*s,.133f*s),trim,.004f*s);
                g.Box(new Vector3(side*(w*.5f+.008f*s),h*.12f+.009f*s,0),new Vector3(.012f*s,.015f*s,.124f*s),lid[color],.005f*s);
                foreach(float end in new[]{-.060f,.060f})g.Box(new Vector3(side*(w*.5f+.003f*s),h*.12f,end*s),new Vector3(.016f*s,.035f*s,.013f*s),lid[color],.004f*s);
                foreach(float end in new[]{-.28f,.28f})g.Box(new Vector3(side*(w*.5f-.002f),-.015f*s,end*d),new Vector3(.009f*s,h*.55f,.013f*s),shell[color],.003f*s);
                var pivot=new Vector3(side*w*.22f,top+.058f*s,-d*.02f);
                g.Box(pivot,new Vector3(.029f*s,.019f*s,.028f*s),trim,.006f*s);
                g.Cylinder(pivot,.004f*s,.034f*s,steel,Vector3.right,16);
                Rod(g,pivot+Vector3.forward*(-.006f*s),new Vector3(pivot.x,top+.067f*s,-d*.16f),.006f*s,trim);
                g.Cylinder(new Vector3(side*w*.29f,top+.005f*s,d*.5f+.006f*s),.005f*s,.055f*s,steel,Vector3.right,20);
            }
            g.Box(new Vector3(0,top+.068f*s,-d*.16f),new Vector3(w*.47f,.017f*s,.026f*s),trim,.007f*s);
            g.Box(new Vector3(0,-h*.02f,-d*.5f-.002f*s),new Vector3(w*.32f,h*.20f,.004f*s),lid[color],.005f*s);
            return g;
        }
        static Geometry PartsBin(Vector3 size,int index,bool scoop)
        {
            var g=new Geometry();float w=size.x,h=size.y,d=size.z,t=.005f;var material=scoop?amber:shell[1];float floor=-h*.5f;
            g.Box(new Vector3(0,floor+t*.5f,0),new Vector3(w,t,d),material,.003f);
            g.Box(new Vector3(0,0,d*.5f-t*.5f),new Vector3(w,h,t),material,.003f);
            float frontHeight=h*(scoop?.40f:.70f);
            g.Box(new Vector3(0,floor+frontHeight*.5f,-d*.5f+t*.5f),new Vector3(w,frontHeight,t),material,.003f);
            foreach(int side in new[]{-1,1})
            {
                var polygon=scoop?new[]{new Vector2(floor,-d*.5f),new Vector2(floor,d*.5f),new Vector2(h*.5f,d*.5f),new Vector2(h*.5f,-d*.18f),new Vector2(floor+frontHeight,-d*.5f)}:
                    new[]{new Vector2(floor,-d*.5f),new Vector2(floor,d*.5f),new Vector2(h*.5f,d*.5f),new Vector2(h*.5f,-d*.5f)};
                var cheek=new Geometry();Extrude(cheek,polygon,t,material);g.Append(cheek,Matrix4x4.Translate(new Vector3(side*(w*.5f-t*.5f),0,0)));
                g.Box(new Vector3(side*(w*.5f-.004f),h*.5f-.002f,d*.15f),new Vector3(.011f,.010f,d*(scoop?.63f:.94f)),material,.003f);
            }
            g.Box(new Vector3(0,floor+frontHeight-.002f,-d*.5f),new Vector3(w+.005f,.011f,.012f),material,.003f);
            g.Box(new Vector3(0,floor+frontHeight*.49f,-d*.5f-.003f),new Vector3(w*.42f,frontHeight*.45f,.004f),trim,.003f);
            g.Box(new Vector3(0,floor+h*.23f,.01f),new Vector3(.004f,h*.42f,d*.83f),material,.002f);
            // A small, uneven stock of actual hollow nuts/washers rather than repeated opaque cubes.
            for(int i=0;i<5+index%3;i++)
            {
                float x=w*(i%2==0?-.23f:.23f)+Mathf.Sin(i*2.7f+index)*w*.06f;
                float z=d*(.12f+Mathf.Sin(i*1.6f+index)*.21f),y=floor+.009f+(i/4)*.006f;
                var p=new Vector3(x,y,z);var ring=new Geometry();
                ring.Lathe(Vector3.zero,new[]{new Vector2(-.003f,.0038f),new Vector2(.003f,.0038f),new Vector2(.003f,.008f),new Vector2(-.003f,.008f)},steel,i%2==0?6:24);
                g.Append(ring,Matrix4x4.TRS(p,Quaternion.FromToRotation(Vector3.right,Vector3.up)*Quaternion.Euler(i*7,0,0),Vector3.one));
                if(i%3==0)g.Cylinder(p+new Vector3(.008f,.003f,.005f),.003f,.026f,steel,new Vector3(.6f,0,.8f),20);
            }
            return g;
        }
        static void Rod(Geometry g,Vector3 a,Vector3 b,float radius,Material material)
            =>g.Cylinder((a+b)*.5f,radius,Vector3.Distance(a,b),material,(b-a).normalized,20);
        static void Extrude(Geometry g,Vector2[] polygon,float thickness,Material material)
        {
            float area=0;for(int i=0;i<polygon.Length;i++){var a=polygon[i];var b=polygon[(i+1)%polygon.Length];area+=a.x*b.y-b.x*a.y;}
            if(area<0)polygon=polygon.Reverse().ToArray();var vertices=new List<Vector3>();var normals=new List<Vector3>();var triangles=new List<int>();
            foreach(float side in new[]{-1f,1f})Face(polygon.Select(p=>new Vector3(side*thickness*.5f,p.x,p.y)).ToArray(),Vector3.right*side,vertices,normals,triangles);
            for(int i=0;i<polygon.Length;i++)
            {
                var a=polygon[i];var b=polygon[(i+1)%polygon.Length];var delta=b-a;
                Face(new[]{new Vector3(-thickness*.5f,a.x,a.y),new Vector3(thickness*.5f,a.x,a.y),new Vector3(thickness*.5f,b.x,b.y),new Vector3(-thickness*.5f,b.x,b.y)},new Vector3(0,delta.y,-delta.x).normalized,vertices,normals,triangles);
            }
            var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetTriangles(triangles,0);mesh.uv=vertices.Select(p=>new Vector2(p.z,p.y)*3).ToArray();mesh.RecalculateBounds();mesh.RecalculateTangents();g.AddMesh(mesh,material);
        }
        static void Face(Vector3[] points,Vector3 normal,List<Vector3> vertices,List<Vector3> normals,List<int> triangles)
        {
            int start=vertices.Count;vertices.AddRange(points);normals.AddRange(Enumerable.Repeat(normal,points.Length));
            bool reverse=Vector3.Dot(Vector3.Cross(points[1]-points[0],points[2]-points[0]),normal)<0;
            for(int i=1;i<points.Length-1;i++)triangles.AddRange(reverse?new[]{start,start+i+1,start+i}:new[]{start,start+i,start+i+1});
        }
        public static void Capture(string stage)
        {
            // Warm material buffers before keeping a snapshot after a cold Editor launch.
            for(int pass=0;pass<2;pass++)
            {
                GarageRefinementBuilder.SignagePreview(new Vector3(1.9f,2.1f,-4.2f),new Vector3(6.8f,1.38f,1),"Documentation/StorageDetail"+stage+"_Wide.png",74);
                GarageRefinementBuilder.SignagePreview(new Vector3(7.65f,1.63f,-4.1f),new Vector3(8.70f,1.38f,-2.95f),"Documentation/StorageDetail"+stage+"_SideShelf.png",60);
                GarageRefinementBuilder.SignagePreview(new Vector3(2.0f,1.85f,3.03f),new Vector3(3.12f,1.25f,4.28f),"Documentation/StorageDetail"+stage+"_RearShelf.png",66);
            }
        }
        [Serializable] sealed class Entry
        {
            public string name,parent;
            public Vector3 center,size;
            public string[] materials;
        }
        [Serializable] sealed class Snapshot {public Entry[] entries;}
        public static void Inspect()
        {
            EditorSceneManager.OpenScene(GarageSystemsBuilder.ScenePath);
            var environment=GameObject.Find("01_Environment_OfficeGarage");
            var entries=environment.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled&&r.bounds.center.x>0)
                .Select(r=>new Entry{name=r.name,parent=r.transform.parent.name,center=r.bounds.center,size=r.bounds.size,
                    materials=r.sharedMaterials.Select(m=>m?m.name:"").ToArray()}).ToArray();
            Directory.CreateDirectory("Logs");File.WriteAllText("Logs/garage_storage_inspection.json",JsonUtility.ToJson(new Snapshot{entries=entries},true));
            if(!File.Exists("Documentation/StorageDetailBefore_Wide.png"))Capture("Before");
        }
    }
}
