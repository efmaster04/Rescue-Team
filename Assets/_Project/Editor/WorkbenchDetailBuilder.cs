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
using Geometry=CompanySimEditor.WorkshopCraftBuilder.Geometry;

namespace CompanySimEditor
{
    public static class WorkbenchDetailBuilder
    {
        const string Folder="Assets/_Project/Art/WorkbenchDetail";
        const string RootName="16_WorkbenchRefinement";
        static Material steel,dark,rubber,grip,paint,cloth,casterRubber;
        static readonly string[] LowerToolNames={"Blue grip","Cast adjustable wrench body","Chrome shank","Color size marker",
            "Comfort handle","Cross point","End cap","Ferrule","Flat driver blade","Forged tapered jaw","Grip end plug","Hinge center",
            "Hook upright","Jaw teeth","Knurled grip tooth","Lanyard eye","Moulded handle grip groove","Moving jaw",
            "Peg shaft","Pivot arm","Riveted hinge","Square mounting boss","Ten-facet ergonomic handle","Worm screw"};

        [MenuItem("Company/Refine Workbench Tools and Service Cart")]
        public static void Apply()
        {
            if(EditorApplication.isPlaying) throw new Exception("Stop Play Mode before refining workshop visuals.");
            EditorSceneManager.OpenScene(GarageSystemsBuilder.ScenePath);
            if(!File.Exists("Documentation/WorkbenchDetailBefore_Bench.png")) Capture("Before");
            Configure();
            var probe=GameObject.Find("GarageReflection").GetComponent<ReflectionProbe>();string path=AssetDatabase.GetAssetPath(probe.customBakedTexture);
            if(!Lightmapping.BakeReflectionProbe(probe,path)) throw new Exception("Garage reflection bake failed.");
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);probe.customBakedTexture=AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            AssetDatabase.SaveAssets();Capture("After");
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
        public static void ApplyAndValidate() {Apply();ProjectValidation.Run();}
        internal static void Configure()
        {
            Directory.CreateDirectory(Folder+"/Meshes");Directory.CreateDirectory(Folder+"/Materials");AssetDatabase.Refresh();
            steel=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/WorkshopCraft/Materials/MachinedSteel.mat");
            dark=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/WorkshopCraft/Materials/RecessMetal.mat");
            rubber=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/WorkshopCraft/Materials/TyreRubber.mat");
            cloth=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/WorkshopCraft/Materials/ShopCloth.mat");
            paint=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Garage/Materials/Workshop_CabinetRed.mat");
            if(!steel||!dark||!rubber||!paint||!cloth) throw new Exception("Apply workshop craft materials first.");
            grip=GripMaterial();casterRubber=CasterMaterial();
            var previous=GameObject.Find(RootName);if(previous) Object.DestroyImmediate(previous);
            var root=new GameObject(RootName).transform;
            var source=GameObject.Find("01_Environment_OfficeGarage").GetComponentsInChildren<MeshRenderer>().Where(IsSupersededTool).ToArray();
            if(source.Length<100) throw new Exception("Expected the original lower tool-board assemblies.");
            foreach(var renderer in source)renderer.enabled=false;
            var tools=new Geometry();
            Place(tools,Pliers(false),new Vector3(8.765f,1.47f,-1.40f),-6);
            Place(tools,Pliers(true),new Vector3(8.765f,1.46f,-.91f),5);
            Cradle(tools,new Vector3(8.765f,1.47f,-1.40f),.034f);
            Cradle(tools,new Vector3(8.765f,1.46f,-.91f),.029f);
            ScrewdriverRack(tools);
            Place(tools,Ratchet(),new Vector3(8.755f,1.40f,.91f),-4);
            Cradle(tools,new Vector3(8.755f,1.505f,.91f),.025f);
            Place(tools,Hammer(),new Vector3(8.755f,1.435f,1.49f),3);
            Cradle(tools,new Vector3(8.755f,1.548f,1.49f),.046f);
            Peg(tools,1.97f,1.62f);Peg(tools,.49f,1.42f);
            PartsDish(tools);
            Create(root,"ToolBoardHandTools",tools);
            var cart=GameObject.Find("11_WorkshopInterior").transform.Find("ServicePartsCart");
            var collision=cart.GetComponent<BoxCollider>();var size=collision.size;var center=collision.center;
            var old=cart.Find("CartAssembly");if(old)Object.DestroyImmediate(old.gameObject);
            var oldCartRenderers=cart.GetComponentsInChildren<MeshRenderer>();
            foreach(var renderer in oldCartRenderers)renderer.enabled=false;
            Create(cart,"CartAssembly",Cart());
            if(collision.size!=size||collision.center!=center)throw new Exception("Service cart collision changed.");
            var renderers=new[]{root.GetComponentInChildren<MeshRenderer>(),cart.Find("CartAssembly").GetComponent<MeshRenderer>()};
            foreach(var renderer in renderers)GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,StaticEditorFlags.ReflectionProbeStatic);
            var cartBounds=cart.Find("CartAssembly").GetComponent<MeshFilter>().sharedMesh.bounds;
            if(cartBounds.min.y<0||cartBounds.min.y>.012f)throw new Exception("Cart tyres must meet the floor: "+cartBounds.min.y);
            if(root.GetComponentsInChildren<Collider>().Length!=0||root.GetComponentsInChildren<Light>().Length!=0||cart.Find("CartAssembly").GetComponentsInChildren<Collider>().Length!=0)
                throw new Exception("Workshop detail meshes must not introduce collision or lights.");
            File.WriteAllText("Logs/workbench_detail_geometry.txt",source.Length+" original tool-board / bench decorative renderers retired; their source transforms are unchanged.\n"+
                "One combined board renderer: combination pliers, long-nose pliers, four different screwdrivers in a neck rack, ratchet, ball-peen hammer and supporting pegs.\n"+
                "The repeated bench bolts are replaced by a shallow parts dish with loose fasteners.\n"+
                oldCartRenderers.Length+" service-cart renderers replaced by one combined renderer; original collision retained. Tyre bottom="+cartBounds.min.y+" metres.\n"+
                "No new colliders, realtime lights, audio sources or gameplay interactions.\n");
        }
        internal static bool IsSupersededTool(MeshRenderer r)
        {
            var b=r.bounds;
            if(b.center.x>8.65f&&b.center.x<8.92f&&b.center.z>-1.8f&&b.center.z<2.2f&&r.name.StartsWith("Forged center ridge",StringComparison.Ordinal))return true;
            if(b.center.x>8.60f&&b.center.x<8.90f&&b.center.z>-.80f&&b.center.z<.02f&&b.center.y>.94f&&b.max.y<1.09f&&
                (r.name.StartsWith("Hex head",StringComparison.Ordinal)||r.name.StartsWith("Hex nut",StringComparison.Ordinal)||r.name.StartsWith("M8 bolt shaft",StringComparison.Ordinal)))return true;
            return b.center.x>8.65f&&b.center.x<8.92f&&b.center.z>-1.8f&&b.center.z<2.2f&&b.min.y>1.08f&&b.max.y<1.82f&&
                LowerToolNames.Any(n=>r.name.StartsWith(n,StringComparison.Ordinal));
        }
        static Material GripMaterial()
        {
            string path=Folder+"/Materials/HandToolGrip.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
            material.SetColor("_BaseColor",new Color(.42f,.17f,.085f));material.SetFloat("_Metallic",0);material.SetFloat("_Smoothness",.20f);
            material.SetFloat("_Surface",0);material.SetFloat("_Cull",2);material.SetFloat("_ZWrite",1);
            const string texture="Assets/_Project/Art/Garage/Textures/GripRubber_";
            material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texture+"Albedo.png"));
            material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texture+"Normal.png"));material.SetFloat("_BumpScale",.16f);
            BaseShaderGUI.SetMaterialKeywords(material,LitGUI.SetMaterialKeywords);EditorUtility.SetDirty(material);return material;
        }
        static Material CasterMaterial()
        {
            string path=Folder+"/Materials/CasterRubber.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
            material.SetColor("_BaseColor",new Color(.06f,.064f,.06f));material.SetFloat("_Metallic",0);material.SetFloat("_Smoothness",.22f);
            material.SetFloat("_Surface",0);material.SetFloat("_Cull",2);material.SetFloat("_ZWrite",1);
            BaseShaderGUI.SetMaterialKeywords(material,LitGUI.SetMaterialKeywords);EditorUtility.SetDirty(material);return material;
        }
        static void PartsDish(Geometry g)
        {
            var center=new Vector3(8.66f,.992f,-.34f);
            Axial(g,center,new[]{new Vector2(-.005f,0),new Vector2(-.005f,.14f),new Vector2(0,.17f),new Vector2(.018f,.174f),
                new Vector2(.018f,.166f),new Vector2(.006f,.156f),new Vector2(.002f,0)},steel,Vector3.up,64);
            var positions=new[]{new Vector3(-.055f,.011f,-.04f),new Vector3(.065f,.011f,-.025f),new Vector3(.014f,.011f,.075f)};
            for(int i=0;i<positions.Length;i++)
            {
                var p=center+positions[i];var axis=Quaternion.Euler(0,24+i*55,0)*Vector3.forward;
                g.Cylinder(p,.003f,.030f,steel,axis,20);g.Cylinder(p+axis*.018f,.006f,.005f,dark,axis,6);
                Axial(g,center+new Vector3(-.070f+i*.058f,.004f,.035f-i*.044f),new[]{new Vector2(-.001f,.0035f),new Vector2(.001f,.0035f),
                    new Vector2(.001f,.0075f),new Vector2(-.001f,.0075f)},steel,Vector3.up,24);
            }
        }
        static void Place(Geometry target,Geometry tool,Vector3 position,float angle)
            =>target.Append(tool,Matrix4x4.TRS(position,Quaternion.AngleAxis(angle,Vector3.right),Vector3.one));
        static void Axial(Geometry g,Vector3 center,Vector2[] profile,Material mat,Vector3 axis,int segments=32)
        {var part=new Geometry();part.Lathe(Vector3.zero,profile,mat,segments);g.Append(part,Matrix4x4.TRS(center,Quaternion.FromToRotation(Vector3.right,axis),Vector3.one));}
        static void Rod(Geometry g,Vector3 a,Vector3 b,float radius,Material mat,int segments=20)
            =>g.Cylinder((a+b)*.5f,radius,Vector3.Distance(a,b),mat,(b-a).normalized,segments);
        static void Cradle(Geometry g,Vector3 pivot,float span)
        {
            foreach(int side in new[]{-1,1})
            {
                var p=pivot+Vector3.forward*(side*span);
                Rod(g,new Vector3(8.913f,p.y,p.z),new Vector3(p.x-.018f,p.y,p.z),.0035f,steel,16);
                Rod(g,new Vector3(p.x-.018f,p.y,p.z),new Vector3(p.x-.020f,p.y+.014f,p.z),.0035f,steel,16);
                g.Box(new Vector3(8.913f,p.y+.007f,p.z),new Vector3(.008f,.030f,.018f),dark,.002f);
            }
        }
        static void Peg(Geometry g,float z,float y)
        {
            Rod(g,new Vector3(8.913f,y,z),new Vector3(8.80f,y,z),.0035f,steel,16);
            Rod(g,new Vector3(8.80f,y,z),new Vector3(8.79f,y+.018f,z),.0035f,steel,16);
            g.Box(new Vector3(8.913f,y+.009f,z),new Vector3(.008f,.030f,.018f),dark,.002f);
        }
        static Geometry Pliers(bool longNose)
        {
            var g=new Geometry();
            foreach(int side in new[]{-1,1})
            {
                float top=longNose?.170f:.122f;
                var polygon=new[]{new Vector2(.006f,side*.004f),new Vector2(top,side*(longNose?.002f:.004f)),
                    new Vector2(top,side*(longNose?.009f:.030f)),new Vector2(.015f,side*.037f)};
                Extrude(g,polygon,.016f,steel);
                Rod(g,new Vector3(0,-.005f,side*.018f),new Vector3(0,-.074f,side*.039f),.0085f,steel);
                Tube(g,new[]{new Vector3(0,-.048f,side*.033f),new Vector3(0,-.080f,side*.043f),
                    new Vector3(0,-.130f,side*.057f),new Vector3(0,-.184f,side*.060f)},new[]{.010f,.0135f,.013f,.008f},longNose?rubber:grip);
                if(!longNose)for(int i=0;i<5;i++)g.Box(new Vector3(-.0085f,.070f+i*.009f,side*.007f),new Vector3(.001f,.0018f,.009f),dark,.0003f);
            }
            g.Cylinder(Vector3.zero,.025f,.022f,steel,Vector3.right,40);
            g.Cylinder(new Vector3(-.012f,0,0),.011f,.003f,dark,Vector3.right,32);
            g.Box(new Vector3(-.014f,0,0),new Vector3(.002f,.002f,.014f),steel,.0003f);
            return g;
        }
        static void ScrewdriverRack(Geometry g)
        {
            float[] z={-.41f,-.23f,-.05f,.13f},length={.87f,1.06f,.98f,.76f};
            g.Box(new Vector3(8.89f,1.49f,-.14f),new Vector3(.030f,.041f,.78f),dark,.005f);
            foreach(float edge in new[]{-.49f,.21f})g.Cylinder(new Vector3(8.870f,1.49f,edge),.006f,.008f,steel,Vector3.right,12);
            for(int i=0;i<4;i++)
            {
                var tool=new Geometry();var handle=i<2?grip:rubber;
                Axial(tool,new Vector3(0,.064f,0),new[]{new Vector2(-.065f,0),new Vector2(-.065f,.010f),new Vector2(-.045f,.020f),
                    new Vector2(.034f,.020f),new Vector2(.060f,.014f),new Vector2(.065f,0)},handle,Vector3.up);
                tool.Cylinder(new Vector3(0,-.006f,0),.011f,.012f,steel,Vector3.up,24);
                tool.Cylinder(new Vector3(0,-.083f,0),.0034f,.15f,steel,Vector3.up,24);
                if(i%2==0) tool.Box(new Vector3(0,-.168f,0),new Vector3(.0028f,.022f,.010f),steel,.0008f);
                else
                {
                    tool.Cylinder(new Vector3(0,-.165f,0),.003f,.021f,steel,Vector3.up,16);
                    tool.Box(new Vector3(0,-.170f,0),new Vector3(.002f,.014f,.008f),steel,.0005f);
                    tool.Box(new Vector3(0,-.170f,0),new Vector3(.008f,.014f,.002f),steel,.0005f);
                }
                g.Append(tool,Matrix4x4.TRS(new Vector3(8.765f,1.51f,z[i]),Quaternion.identity,new Vector3(1,length[i],1)));
                Rod(g,new Vector3(8.89f,1.511f,z[i]),new Vector3(8.785f,1.511f,z[i]),.005f,dark,16);
                Axial(g,new Vector3(8.765f,1.511f,z[i]),new[]{new Vector2(-.003f,.0135f),new Vector2(.003f,.0135f),
                    new Vector2(.003f,.023f),new Vector2(-.003f,.023f)},dark,Vector3.up,32);
            }
        }
        static Geometry Ratchet()
        {
            var g=new Geometry();
            g.Box(new Vector3(0,-.005f,0),new Vector3(.014f,.245f,.025f),steel,.006f);
            Axial(g,new Vector3(0,-.184f,0),new[]{new Vector2(-.069f,0),new Vector2(-.069f,.009f),new Vector2(-.055f,.016f),
                new Vector2(.045f,.016f),new Vector2(.062f,.011f),new Vector2(.065f,0)},rubber,Vector3.up);
            Axial(g,new Vector3(0,.128f,0),new[]{new Vector2(-.015f,0),new Vector2(-.015f,.032f),new Vector2(-.010f,.036f),
                new Vector2(.010f,.036f),new Vector2(.015f,.029f),new Vector2(.015f,0)},steel,Vector3.right,48);
            g.Cylinder(new Vector3(-.016f,.128f,0),.026f,.003f,dark,Vector3.right,40);
            g.Box(new Vector3(-.025f,.128f,0),new Vector3(.020f,.018f,.018f),steel,.002f);
            g.Box(new Vector3(-.018f,.148f,.012f),new Vector3(.004f,.005f,.014f),steel,.001f);
            return g;
        }
        static Geometry Hammer()
        {
            var g=new Geometry();
            Axial(g,new Vector3(0,-.03f,0),new[]{new Vector2(-.18f,0),new Vector2(-.18f,.009f),new Vector2(-.16f,.018f),
                new Vector2(-.045f,.017f),new Vector2(.09f,.012f),new Vector2(.16f,.016f),new Vector2(.16f,0)},rubber,Vector3.up);
            Axial(g,new Vector3(0,.133f,0),new[]{new Vector2(-.087f,0),new Vector2(-.087f,.026f),new Vector2(-.076f,.028f),
                new Vector2(-.057f,.025f),new Vector2(-.042f,.019f),new Vector2(.010f,.023f),new Vector2(.030f,.020f),
                new Vector2(.045f,.015f),new Vector2(.062f,.014f),new Vector2(.074f,.007f),new Vector2(.077f,0)},steel,Vector3.forward,40);
            g.Box(new Vector3(-.023f,.134f,0),new Vector3(.007f,.029f,.028f),dark,.003f);
            return g;
        }
        static Geometry Cart()
        {
            var g=new Geometry();
            foreach(float x in new[]{-.255f,.255f})foreach(float z in new[]{-.335f,.335f})
            {
                var p=new Vector3(x,.082f,z);
                Axial(g,p,new[]{new Vector2(-.025f,.018f),new Vector2(.025f,.018f),new Vector2(.025f,.060f),
                    new Vector2(.017f,.078f),new Vector2(-.017f,.078f),new Vector2(-.025f,.060f)},casterRubber,Vector3.forward,40);
                g.Cylinder(p,.025f,.053f,steel,Vector3.forward,32);
                g.Cylinder(p,.009f,.080f,dark,Vector3.forward,12);
                foreach(int side in new[]{-1,1})g.Box(new Vector3(x,.149f,z+side*.035f),new Vector3(.058f,.135f,.012f),steel,.006f);
                g.Cylinder(new Vector3(x,.219f,z),.037f,.019f,steel,Vector3.up,32);
                g.Box(new Vector3(x,.233f,z),new Vector3(.095f,.014f,.090f),dark,.005f);
            }
            foreach(float x in new[]{-.274f,.274f})foreach(float z in new[]{-.38f,.38f})
                g.Box(new Vector3(x,.520f,z),new Vector3(.028f,.55f,.028f),dark,.005f);
            foreach(float y in new[]{.245f,.775f})
            {
                g.Box(new Vector3(0,y,0),new Vector3(.61f,.020f,.86f),paint,.009f);
                foreach(float x in new[]{-.296f,.296f})g.Box(new Vector3(x,y+.040f,0),new Vector3(.018f,.080f,.85f),paint,.005f);
                foreach(float z in new[]{-.416f,.416f})g.Box(new Vector3(0,y+.040f,z),new Vector3(.59f,.080f,.018f),paint,.005f);
                g.Box(new Vector3(0,y+.013f,0),new Vector3(.56f,.005f,.80f),rubber,.002f);
            }
            foreach(float x in new[]{-.25f,.25f})
                Tube(g,new[]{new Vector3(x,.805f,-.39f),new Vector3(x,.92f,-.415f),new Vector3(x,.961f,-.427f)},new[]{.012f,.012f,.012f},steel);
            g.Cylinder(new Vector3(0,.962f,-.427f),.017f,.52f,rubber,Vector3.right,40);
            foreach(float z in new[]{-.19f,.19f})
            {
                g.Box(new Vector3(0,.801f,z),new Vector3(.40f,.012f,.31f),steel,.006f);
                foreach(float x in new[]{-.195f,.195f})g.Box(new Vector3(x,.821f,z),new Vector3(.010f,.040f,.30f),steel,.003f);
                foreach(float end in new[]{-.15f,.15f})g.Box(new Vector3(0,.821f,z+end),new Vector3(.39f,.040f,.010f),steel,.003f);
            }
            foreach(var p in new[]{new Vector3(-.09f,.812f,-.24f),new Vector3(.08f,.812f,-.16f),new Vector3(.02f,.812f,-.27f)})
            {
                Axial(g,p,new[]{new Vector2(-.002f,.0045f),new Vector2(.002f,.0045f),new Vector2(.002f,.010f),new Vector2(-.002f,.010f)},steel,Vector3.up,24);
                g.Cylinder(p+Vector3.up*.007f,.006f,.012f,dark,Vector3.up,6);
            }
            for(int i=0;i<3;i++)g.Box(new Vector3(-.04f+i*.011f,.815f+i*.005f,.19f),new Vector3(.19f,.006f,.20f),cloth,.002f);
            return g;
        }
        static void Extrude(Geometry g,Vector2[] polygon,float thickness,Material material)
        {
            float area=0;for(int i=0;i<polygon.Length;i++){var a=polygon[i];var b=polygon[(i+1)%polygon.Length];area+=a.x*b.y-b.x*a.y;}
            if(area<0)polygon=polygon.Reverse().ToArray();
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var triangles=new List<int>();
            foreach(float side in new[]{-1f,1f})Face(polygon.Select(p=>new Vector3(side*thickness*.5f,p.x,p.y)).ToArray(),Vector3.right*side,vertices,normals,triangles);
            for(int i=0;i<polygon.Length;i++)
            {
                var a=polygon[i];var b=polygon[(i+1)%polygon.Length];var d=b-a;
                Face(new[]{new Vector3(-thickness*.5f,a.x,a.y),new Vector3(thickness*.5f,a.x,a.y),new Vector3(thickness*.5f,b.x,b.y),new Vector3(-thickness*.5f,b.x,b.y)},
                    new Vector3(0,d.y,-d.x).normalized,vertices,normals,triangles);
            }
            var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetTriangles(triangles,0);mesh.uv=vertices.Select(p=>new Vector2(p.z,p.y)*3).ToArray();mesh.RecalculateBounds();mesh.RecalculateTangents();g.AddMesh(mesh,material);
        }
        static void Face(Vector3[] points,Vector3 normal,List<Vector3> vertices,List<Vector3> normals,List<int> triangles)
        {
            int start=vertices.Count;vertices.AddRange(points);normals.AddRange(Enumerable.Repeat(normal,points.Length));
            bool reverse=Vector3.Dot(Vector3.Cross(points[1]-points[0],points[2]-points[0]),normal)<0;
            for(int i=1;i<points.Length-1;i++)triangles.AddRange(reverse?new[]{start,start+i+1,start+i}:new[]{start,start+i,start+i+1});
        }
        static void Tube(Geometry g,Vector3[] path,float[] radii,Material material)
        {
            const int segments=20;var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var indices=new List<int>();
            for(int i=0;i<path.Length;i++)
            {
                var tangent=(path[Math.Min(i+1,path.Length-1)]-path[Math.Max(0,i-1)]).normalized;
                var across=Vector3.Cross(tangent,Mathf.Abs(tangent.x)<.8f?Vector3.right:Vector3.up).normalized;
                var second=Vector3.Cross(tangent,across).normalized;
                for(int j=0;j<segments;j++)
                {
                    float a=j*Mathf.PI*2/segments;var n=across*Mathf.Cos(a)+second*Mathf.Sin(a);
                    vertices.Add(path[i]+n*radii[i]);normals.Add(n);uv.Add(new Vector2(j/(float)segments,i/(float)(path.Length-1)));
                }
            }
            for(int i=0;i<path.Length-1;i++)for(int j=0;j<segments;j++)
            {
                int a=i*segments+j,b=i*segments+(j+1)%segments,c=b+segments,d=a+segments;
                indices.AddRange(new[]{a,b,c,a,c,d});
            }
            foreach(int end in new[]{0,path.Length-1})
            {
                var n=(end==0?path[0]-path[1]:path[end]-path[end-1]).normalized;int c=vertices.Count;vertices.Add(path[end]);normals.Add(n);uv.Add(Vector2.zero);
                for(int j=0;j<segments;j++){vertices.Add(vertices[end*segments+j]);normals.Add(n);uv.Add(Vector2.zero);}
                for(int j=0;j<segments;j++)indices.AddRange(new[]{c,c+1+j,c+1+(j+1)%segments});
            }
            for(int i=0;i<indices.Count;i+=3)
            {
                int a=indices[i],b=indices[i+1],c=indices[i+2];
                if(Vector3.Dot(Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]),normals[a]+normals[b]+normals[c])<0)
                {indices[i+1]=c;indices[i+2]=b;}
            }
            var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();mesh.RecalculateTangents();g.AddMesh(mesh,material);
        }
        static void Create(Transform root,string name,Geometry g)
        {
            var mesh=g.Build(out var materials);mesh.name=name;var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root,false);
            go.GetComponent<MeshFilter>().sharedMesh=GeneratedMeshAsset.Store(mesh,Folder+"/Meshes/"+name+".asset");go.GetComponent<MeshRenderer>().sharedMaterials=materials;
        }
        public static void Capture(string stage)
        {
            GarageRefinementBuilder.SignagePreview(new Vector3(6.98f,1.70f,-3.65f),new Vector3(8.65f,1.55f,.15f),"Documentation/WorkbenchDetail"+stage+"_Bench.png",64);
            GarageRefinementBuilder.SignagePreview(new Vector3(7.12f,1.72f,.17f),new Vector3(8.80f,1.70f,.17f),"Documentation/WorkbenchDetail"+stage+"_Board.png",72);
            GarageRefinementBuilder.SignagePreview(new Vector3(7.45f,1.14f,-4.76f),new Vector3(8.56f,.51f,-4.17f),"Documentation/WorkbenchDetail"+stage+"_Cart.png",58);
        }
    }
}
