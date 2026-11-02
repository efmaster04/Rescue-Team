using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanySim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using Object=UnityEngine.Object;
using Geometry=CompanySimEditor.WorkshopCraftBuilder.Geometry;
using Shape=CompanySimEditor.ModuleEquipmentBuilder;

namespace CompanySimEditor
{
    public static class PortableEquipmentBuilder
    {
        const string Folder="Assets/_Project/Art/PortableEquipment";
        static readonly string[] Ids={"toolbox","case","parts","bin","fuel","grease","absorbent","jack"};
        static Material steel,dark,rubber,red,olive,plastic,cream,blue,brass;
        [MenuItem("Company/Rebuild Portable Workshop Equipment")]
        public static void Apply()
        {
            if(EditorApplication.isPlaying)throw new Exception("Stop Play Mode before changing equipment models.");
            EditorSceneManager.OpenScene(GarageSystemsBuilder.ScenePath);
            var vehicle=Object.FindAnyObjectByType<ServiceVehicle>();string before=ModuleEquipmentBuilder.VehicleState(vehicle);
            Configure();
            if(before!=ModuleEquipmentBuilder.VehicleState(vehicle))throw new Exception("Portable equipment must not change the vehicle.");
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            File.WriteAllText("Logs/portable_equipment_vehicle_parity.txt","PASS: vehicle transforms, mesh/material references and parking pose unchanged.\n");
            Capture("After");
        }
        public static void ApplyAndValidate(){Apply();ProjectValidation.Run();}
        public static void Capture(string stage)
        {
            foreach(string id in Ids)
            {
                var definition=AssetDatabase.LoadAssetAtPath<EquipmentDefinition>("Assets/_Project/Data/Equipment/"+id+".asset");
                Shape.Render(definition.visualPrefab,"Documentation/PortableEquipment"+stage+"_"+id+".png",false);
                if(stage=="After")Shape.Render(definition.visualPrefab,"Assets/_Project/Resources/OfficeThumbnails/"+id+".png",true);
            }
        }
        internal static void Configure()=>BlenderEquipmentBuilder.Configure();
        // Previous C# geometry is kept for inspecting/reproducing the backed-up pass.
        static void ConfigureLegacyGeometry()
        {
            Directory.CreateDirectory(Folder+"/Meshes");Directory.CreateDirectory(Folder+"/Materials");PrepareMaterials();
            var report=new List<string>{"Eight complete equipment models. Original item IDs, root transforms, collider boxes and rigidbody settings retained. One combined MeshRenderer per item; no additional colliders, lights or audio."};
            foreach(string id in Ids)
            {
                var definition=AssetDatabase.LoadAssetAtPath<EquipmentDefinition>("Assets/_Project/Data/Equipment/"+id+".asset");
                string path=AssetDatabase.GetAssetPath(definition.visualPrefab);var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var box=root.GetComponent<BoxCollider>();var body=root.GetComponent<Rigidbody>();
                    string physical=PhysicsState(root);int oldCount=root.GetComponentsInChildren<MeshRenderer>(true).Length;
                    if(root.GetComponentsInChildren<Collider>(true).Length!=1)throw new Exception("Unexpected equipment collision: "+id);
                    Geometry geometry;
                    switch(id)
                    {
                        case "toolbox":geometry=Toolbox();break;
                        case "case":geometry=Case();break;
                        case "parts":geometry=PartsCrate();break;
                        case "bin":geometry=StorageBin();break;
                        case "fuel":geometry=FuelCan();break;
                        case "grease":geometry=GreaseTin();break;
                        case "absorbent":geometry=AbsorbentPail();break;
                        case "jack":geometry=FloorJack();break;
                        default:throw new Exception(id);
                    }
                    var mesh=geometry.Build(out var materials);mesh.name=id+"_CraftedEquipment";
                    var b=mesh.bounds;var allowed=new Bounds(box.center,box.size+Vector3.one*.006f);
                    if(!allowed.Contains(b.min)||!allowed.Contains(b.max))throw new Exception(id+" art exceeds existing physics. Art="+b.min.ToString("R")+".."+b.max.ToString("R")+"; collision="+allowed.min.ToString("R")+".."+allowed.max.ToString("R"));
                    if(mesh.vertices.Any(v=>!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z)))throw new Exception("Non-finite equipment vertex: "+id);
                    // Keep source hierarchy/file IDs. Imported equipment renderers are removed as
                    // prefab overrides; the source FBX and its transforms remain available.
                    var previous=root.transform.Find("CraftedEquipment");if(previous)Object.DestroyImmediate(previous.gameObject);
                    foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                    {var filter=renderer.GetComponent<MeshFilter>();if(filter)Object.DestroyImmediate(filter);Object.DestroyImmediate(renderer);}
                    var art=new GameObject("CraftedEquipment",typeof(MeshFilter),typeof(MeshRenderer));art.transform.SetParent(root.transform,false);
                    mesh=GeneratedMeshAsset.Store(mesh,Folder+"/Meshes/"+id+".asset");art.GetComponent<MeshFilter>().sharedMesh=mesh;
                    art.GetComponent<MeshRenderer>().sharedMaterials=materials;
                    if(physical!=PhysicsState(root)||art.GetComponentsInChildren<Collider>().Length!=0)throw new Exception("Equipment physics changed: "+id);
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                    report.Add(id+": "+oldCount+" -> 1 MeshRenderer; "+mesh.triangles.Length/3+" triangles; "+b+"; "+body.mass+" kg; collision "+box.size);
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            File.WriteAllLines("Logs/portable_equipment_geometry.txt",report);
        }
        static string PhysicsState(GameObject root)
        {
            var b=root.GetComponent<BoxCollider>();var r=root.GetComponent<Rigidbody>();var item=root.GetComponent<CarryableItem>();
            return b.size.ToString("R")+b.center.ToString("R")+b.enabled+b.isTrigger+r.mass+"|"+r.linearDamping+"|"+r.angularDamping+"|"+r.useGravity+"|"+r.isKinematic+"|"+r.interpolation+"|"+r.constraints+"|"+item.definitionId+"|"+item.uid+"|"+item.durability;
        }
        internal static void PrepareMaterials()
        {
            steel=Surface("BrushedAlloy",new Color(.49f,.52f,.51f),.78f,.47f,"Steel");
            dark=Surface("CastGraphite",new Color(.080f,.095f,.095f),.57f,.28f,"Steel");
            rubber=Surface("Rubber",new Color(.032f,.037f,.036f),0,.17f,"Rubber");
            red=Surface("PressedRed",new Color(.40f,.075f,.046f),.22f,.38f,"Enamel");
            olive=Surface("ServiceOlive",new Color(.25f,.29f,.20f),.16f,.33f,"Enamel");
            plastic=Surface("MouldedGraphite",new Color(.11f,.14f,.145f),.04f,.25f,"Rubber");
            cream=Surface("BucketIvory",new Color(.67f,.64f,.53f),0,.27f,"Enamel");
            blue=Surface("SlateBlue",new Color(.13f,.20f,.235f),.06f,.29f,"Enamel");
            brass=Surface("BrassFitting",new Color(.46f,.33f,.14f),.74f,.40f,"Steel");
        }
        static Material Surface(string name,Color color,float metallic,float smooth,string map)
        {
            string path=Folder+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
            m.SetColor("_BaseColor",color);m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",smooth);m.SetFloat("_Surface",0);m.SetFloat("_Cull",2);m.SetFloat("_ZWrite",1);
            const string textures="Assets/_Project/Art/WorkshopFinish/Textures/";
            m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(textures+map+"_Albedo.png"));
            m.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(textures+map+"_Normal.png"));m.SetFloat("_BumpScale",.20f);
            BaseShaderGUI.SetMaterialKeywords(m,LitGUI.SetMaterialKeywords);EditorUtility.SetDirty(m);return m;
        }
        static void Bolt(Geometry g,Vector3 at,Vector3 axis,float radius=.003f)
        {g.Cylinder(at,radius*1.35f,.0015f,steel,axis,24);g.Cylinder(at+axis*.002f,radius,.003f,dark,axis,6);}
        static void Latch(Geometry g,float x,float y,float z,float scale=1)
        {
            g.Box(new Vector3(x,y,z),new Vector3(.029f,.043f,.006f)*scale,dark,.002f);
            g.Box(new Vector3(x,y-.003f*scale,z+.005f*scale),new Vector3(.022f,.031f,.005f)*scale,steel,.002f);
            g.Cylinder(new Vector3(x,y+.016f*scale,z+.005f*scale),.0035f*scale,.027f*scale,steel,Vector3.right,24);
            g.Box(new Vector3(x,y-.009f*scale,z+.008f*scale),new Vector3(.013f,.012f,.003f)*scale,rubber,.001f);
        }
        static void Handle(Geometry g,Vector3 center,float width,float rise,Material material)
        {
            foreach(int sign in new[]{-1,1})g.Box(center+new Vector3(sign*width*.5f,0,0),new Vector3(.022f,.014f,.032f),dark,.003f);
            Shape.Curve(g,new[]{center+new Vector3(-width*.5f,0,0),center+new Vector3(-width*.5f,rise,0),center+new Vector3(width*.5f,rise,0),center+new Vector3(width*.5f,0,0)},.005f,steel);
            g.Cylinder(center+Vector3.up*(rise+.001f),.008f,width*.65f,material,Vector3.right,32);
        }
        static Geometry Toolbox()
        {
            var g=new Geometry();
            g.Box(new Vector3(0,.102f,0),new Vector3(.435f,.172f,.288f),red,.008f);
            g.Box(new Vector3(0,.025f,0),new Vector3(.444f,.026f,.296f),dark,.005f);
            g.Box(new Vector3(0,.190f,0),new Vector3(.443f,.007f,.298f),rubber,.003f);
            Shape.Plate(g,Vector3.zero,Quaternion.Euler(0,90,0),new[]{new Vector2(-.149f,.194f),new Vector2(.149f,.194f),new Vector2(.149f,.218f),new Vector2(.092f,.267f),new Vector2(-.092f,.267f),new Vector2(-.149f,.218f)},.442f,red);
            foreach(float x in new[]{-.137f,.137f})
            {
                Latch(g,x,.184f,.153f);
                g.Cylinder(new Vector3(x,.194f,-.152f),.004f,.056f,steel,Vector3.right,24);
                g.Box(new Vector3(x,.172f,-.149f),new Vector3(.048f,.027f,.005f),dark,.002f);
            }
            foreach(float x in new[]{-.180f,.180f})foreach(float z in new[]{-.115f,.115f})g.Box(new Vector3(x,.010f,z),new Vector3(.039f,.020f,.039f),rubber,.004f);
            foreach(int side in new[]{-1,1})
            {Shape.Aperture(g,new Vector3(side*.224f,.131f,0),Quaternion.Euler(0,90,0),.114f,.038f,.077f,.017f,.007f,.008f,dark);}
            Handle(g,new Vector3(0,.274f,0),.170f,.024f,rubber);
            foreach(float x in new[]{-.191f,.191f})g.Box(new Vector3(x,.104f,.146f),new Vector3(.013f,.133f,.004f),red,.002f);
            return g;
        }
        static Geometry Case()
        {
            var g=new Geometry();
            g.Box(new Vector3(0,.112f,0),new Vector3(.515f,.202f,.306f),plastic,.018f);
            g.Box(new Vector3(0,.217f,0),new Vector3(.524f,.009f,.313f),rubber,.004f);
            g.Box(new Vector3(0,.246f,0),new Vector3(.530f,.050f,.318f),plastic,.013f);
            foreach(float x in new[]{-.20f,-.105f,.105f,.20f})
            {
                g.Box(new Vector3(x,.118f,.155f),new Vector3(.013f,.154f,.010f),plastic,.004f);
                g.Box(new Vector3(x,.274f,0),new Vector3(.015f,.009f,.237f),plastic,.003f);
            }
            foreach(float x in new[]{-.159f,.159f})
            {Latch(g,x,.216f,.160f,1.12f);g.Cylinder(new Vector3(x,.221f,-.163f),.005f,.070f,dark,Vector3.right,24);}
            foreach(int side in new[]{-1,1})foreach(float z in new[]{-.121f,.121f})
            {g.Box(new Vector3(side*.247f,.057f,z),new Vector3(.041f,.094f,.051f),rubber,.008f);g.Box(new Vector3(side*.248f,.245f,z),new Vector3(.042f,.045f,.049f),rubber,.006f);}
            // A low recessed handle fits the original 30 cm carry envelope.
            foreach(float x in new[]{-.071f,.071f})g.Box(new Vector3(x,.280f,0),new Vector3(.026f,.016f,.032f),dark,.004f);
            g.Box(new Vector3(0,.290f,0),new Vector3(.157f,.016f,.026f),rubber,.006f);
            g.Cylinder(new Vector3(.214f,.157f,.158f),.010f,.008f,dark,Vector3.forward,32);
            return g;
        }
        static Geometry PartsCrate()
        {
            var g=new Geometry();
            g.Box(new Vector3(0,.025f,0),new Vector3(.478f,.035f,.478f),plastic,.005f);
            foreach(int side in new[]{-1,1})
            {
                foreach(float x in new[]{-.220f,-.110f,0,.110f,.220f})
                {g.Box(new Vector3(x,.218f,side*.235f),new Vector3(.022f,.350f,.016f),blue,.003f);g.Box(new Vector3(side*.235f,.218f,x),new Vector3(.016f,.350f,.022f),blue,.003f);}
                foreach(float y in new[]{.078f,.186f,.293f})
                {g.Box(new Vector3(0,y,side*.236f),new Vector3(.474f,.018f,.019f),blue,.003f);g.Box(new Vector3(side*.236f,y,0),new Vector3(.019f,.018f,.474f),blue,.003f);}
                Shape.Aperture(g,new Vector3(0,.391f,side*.238f),Quaternion.identity,.488f,.075f,.159f,.032f,.015f,.010f,plastic);
                Shape.Aperture(g,new Vector3(side*.238f,.391f,0),Quaternion.Euler(0,90,0),.459f,.075f,.159f,.032f,.015f,.010f,plastic);
                foreach(float z in new[]{-.231f,.231f})g.Box(new Vector3(side*.231f,.391f,z),new Vector3(.030f,.077f,.030f),plastic,.003f);
                foreach(float z in new[]{-.213f,.213f})g.Box(new Vector3(side*.213f,.011f,z),new Vector3(.050f,.022f,.050f),rubber,.003f);
            }
            // Distinct, readable spare parts are contained below the crate rim.
            foreach(float x in new[]{-.111f,.094f})
            {
                Shape.Axial(g,new Vector3(x,.111f,-.045f),new[]{new Vector2(-.043f,0),new Vector2(-.043f,.049f),new Vector2(-.020f,.062f),new Vector2(.025f,.062f),new Vector2(.044f,.043f),new Vector2(.044f,0)},steel,Vector3.up,40);
                g.Cylinder(new Vector3(x,.170f,-.045f),.018f,.045f,dark,Vector3.up,24);
            }
            g.Box(new Vector3(.015f,.088f,.132f),new Vector3(.272f,.080f,.094f),cream,.009f);
            g.Box(new Vector3(.015f,.131f,.132f),new Vector3(.044f,.005f,.095f),dark,.001f);
            Shape.Ring(g,new Vector3(-.017f,.213f,-.040f),.085f,.008f,rubber,Vector3.up,48);
            return g;
        }
        static Geometry StorageBin()
        {
            var g=new Geometry();
            Loft(g,new[]{new Vector3(.438f,.010f,.326f),new Vector3(.445f,.045f,.333f),new Vector3(.498f,.262f,.377f)},blue);
            g.Box(new Vector3(0,.264f,0),new Vector3(.506f,.017f,.385f),plastic,.006f);
            g.Box(new Vector3(0,.286f,0),new Vector3(.512f,.032f,.390f),plastic,.008f);
            foreach(int side in new[]{-1,1})
            {
                Shape.Aperture(g,new Vector3(side*.253f,.229f,0),Quaternion.Euler(0,90,0),.171f,.059f,.120f,.026f,.009f,.010f,rubber);
                foreach(float x in new[]{-.170f,-.085f,0,.085f,.170f})Shape.Rod(g,new Vector3(x,.063f,side*.168f),new Vector3(x,.250f,side*.188f),.004f,blue,16);
                g.Box(new Vector3(side*.244f,.285f,0),new Vector3(.020f,.018f,.131f),olive,.004f);
            }
            for(int i=0;i<5;i++)g.Box(new Vector3(-.180f+i*.090f,.306f,0),new Vector3(.028f,.008f,.304f),plastic,.003f);
            return g;
        }
        // Closed tapered container, with separate face normals and metric UVs.
        static void Loft(Geometry g,Vector3[] rings,Material material)
        {
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            var outlines=rings.Select(r=>Shape.RoundedOutline(r.x,r.z,.021f)).ToArray();int count=outlines[0].Length;
            Vector3 Corner(int ring,int corner)=>new Vector3(outlines[ring][corner].x,rings[ring].y,outlines[ring][corner].y);
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
            {int start=vertices.Count;vertices.AddRange(new[]{a,b,c,d});uv.AddRange(new[]{Vector2.zero,new Vector2(Vector3.Distance(a,b)*3,0),new Vector2(Vector3.Distance(a,b)*3,Vector3.Distance(b,c)*3),new Vector2(0,Vector3.Distance(b,c)*3)});triangles.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});}
            void Triangle(Vector3 a,Vector3 b,Vector3 c)
            {int start=vertices.Count;vertices.AddRange(new[]{a,b,c});uv.AddRange(new[]{new Vector2(a.x,a.z)*3,new Vector2(b.x,b.z)*3,new Vector2(c.x,c.z)*3});triangles.AddRange(new[]{start,start+1,start+2});}
            for(int ring=0;ring<rings.Length-1;ring++)for(int i=0;i<count;i++)Quad(Corner(ring,i),Corner(ring,(i+1)%count),Corner(ring+1,(i+1)%count),Corner(ring+1,i));
            for(int i=0;i<count;i++)
            {Triangle(new Vector3(0,rings[0].y,0),Corner(0,(i+1)%count),Corner(0,i));Triangle(new Vector3(0,rings[rings.Length-1].y,0),Corner(rings.Length-1,i),Corner(rings.Length-1,(i+1)%count));}
            for(int i=0;i<triangles.Count;i+=3){int swap=triangles[i+1];triangles[i+1]=triangles[i+2];triangles[i+2]=swap;}
            var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.RecalculateTangents();g.AddMesh(mesh,material);
        }
        static Geometry FuelCan()
        {
            var g=new Geometry();
            Shape.Plate(g,Vector3.zero,Quaternion.identity,new[]{new Vector2(-.141f,.021f),new Vector2(.141f,.021f),new Vector2(.149f,.048f),new Vector2(.149f,.342f),new Vector2(.104f,.397f),new Vector2(-.110f,.397f),new Vector2(-.149f,.342f),new Vector2(-.149f,.048f)},.215f,olive);
            g.Box(new Vector3(0,.020f,0),new Vector3(.303f,.025f,.226f),olive,.006f);
            foreach(int side in new[]{-1,1})
            {
                foreach(float angle in new[]{-37f,37f})Shape.RotatedBox(g,new Vector3(0,.203f,side*.110f),Quaternion.Euler(0,0,angle),Vector3.zero,new Vector3(.014f,.282f,.006f),olive,.003f);
                g.Box(new Vector3(0,.203f,side*.114f),new Vector3(.053f,.059f,.008f),olive,.005f);
            }
            foreach(float z in new[]{-.060f,0,.060f})
            {
                Shape.Aperture(g,new Vector3(.035f,.438f,z),Quaternion.identity,.152f,.060f,.110f,.027f,.013f,.012f,olive);
                foreach(float x in new[]{-.029f,.100f})g.Box(new Vector3(x,.402f,z),new Vector3(.023f,.028f,.014f),olive,.003f);
            }
            var axis=new Vector3(-.22f,.975f,0).normalized;var cap=new Vector3(-.100f,.416f,0);
            Shape.Axial(g,cap,new[]{new Vector2(-.020f,0),new Vector2(-.020f,.024f),new Vector2(.010f,.029f),new Vector2(.017f,.032f),new Vector2(.027f,.032f),new Vector2(.029f,0)},dark,axis,48);
            g.Box(new Vector3(-.100f,.450f,0),new Vector3(.054f,.010f,.018f),steel,.003f);
            foreach(float x in new[]{-.118f,-.082f})Bolt(g,new Vector3(x,.455f,0),Vector3.up,.0027f);
            Shape.Curve(g,new[]{new Vector3(-.124f,.438f,.018f),new Vector3(-.148f,.407f,.038f),new Vector3(-.143f,.373f,.054f),new Vector3(-.118f,.391f,.035f)},.0017f,steel);
            return g;
        }
        static Geometry GreaseTin()
        {
            var g=new Geometry();
            Shape.Axial(g,new Vector3(0,.111f,0),new[]{new Vector2(-.105f,0),new Vector2(-.105f,.101f),new Vector2(-.099f,.111f),new Vector2(-.090f,.111f),new Vector2(-.087f,.107f),new Vector2(.086f,.107f),new Vector2(.090f,.112f),new Vector2(.100f,.112f),new Vector2(.104f,.103f),new Vector2(.104f,0)},cream,Vector3.up,64);
            Shape.Axial(g,new Vector3(0,.107f,0),new[]{new Vector2(-.046f,.107f),new Vector2(-.046f,.108f),new Vector2(.046f,.108f),new Vector2(.046f,.107f)},blue,Vector3.up,64);
            foreach(float y in new[]{.014f,.207f})Shape.Ring(g,new Vector3(0,y,0),.109f,.003f,steel,Vector3.up,64);
            g.Cylinder(new Vector3(0,.216f,0),.102f,.004f,steel,Vector3.up,64);
            Shape.Ring(g,new Vector3(0,.2195f,0),.086f,.0014f,dark,Vector3.up,48);
            Shape.Aperture(g,new Vector3(0,.2195f,.010f),Quaternion.Euler(90,0,0),.056f,.029f,.032f,.013f,.003f,.006f,dark);
            return g;
        }
        static Geometry AbsorbentPail()
        {
            var g=new Geometry();
            Shape.Axial(g,new Vector3(0,.185f,0),new[]{new Vector2(-.177f,0),new Vector2(-.177f,.138f),new Vector2(-.168f,.145f),new Vector2(.142f,.169f),new Vector2(.151f,.175f),new Vector2(.168f,.175f),new Vector2(.174f,.166f),new Vector2(.174f,0)},cream,Vector3.up,64);
            foreach(float y in new[]{.318f,.339f,.357f})Shape.Ring(g,new Vector3(0,y,0),.173f,.003f,cream,Vector3.up,64);
            g.Cylinder(new Vector3(0,.367f,0),.178f,.022f,olive,Vector3.up,64);
            g.Cylinder(new Vector3(0,.379f,0),.163f,.005f,olive,Vector3.up,64);
            Shape.Ring(g,new Vector3(0,.378f,0),.174f,.003f,dark,Vector3.up,64);
            foreach(int side in new[]{-1,1})
            {g.Box(new Vector3(side*.168f,.284f,0),new Vector3(.014f,.033f,.033f),cream,.004f);Bolt(g,new Vector3(side*.178f,.284f,0),Vector3.right*side,.004f);}
            Shape.Curve(g,new[]{new Vector3(-.177f,.284f,0),new Vector3(-.169f,.259f,.089f),new Vector3(-.100f,.238f,.156f),new Vector3(.100f,.238f,.156f),new Vector3(.169f,.259f,.089f),new Vector3(.177f,.284f,0)},.0026f,steel);
            g.Cylinder(new Vector3(0,.235f,.164f),.006f,.112f,rubber,Vector3.right,32);
            return g;
        }
        static Geometry FloorJack()
        {
            var g=new Geometry();
            // Real low-profile chassis, articulated arm, ram, front axle and rear swivel casters.
            var profile=new[]{new Vector2(-.432f,.057f),new Vector2(.430f,.057f),new Vector2(.430f,.116f),new Vector2(.345f,.153f),new Vector2(.070f,.156f),new Vector2(-.096f,.179f),new Vector2(-.345f,.127f),new Vector2(-.432f,.105f)};
            foreach(float x in new[]{-.215f,.215f})
            {
                Shape.Plate(g,new Vector3(x,0,0),Quaternion.Euler(0,90,0),profile,.022f,red);
                foreach(float z in new[]{-.380f,.335f})Bolt(g,new Vector3(x+Mathf.Sign(x)*.014f,.101f,z),Vector3.right*Mathf.Sign(x),.006f);
            }
            g.Box(new Vector3(0,.068f,-.117f),new Vector3(.425f,.017f,.579f),dark,.004f);
            Shape.Rod(g,new Vector3(-.267f,.074f,.335f),new Vector3(.267f,.074f,.335f),.014f,steel,32);
            foreach(int side in new[]{-1,1})
            {
                Shape.Axial(g,new Vector3(side*.265f,.074f,.335f),new[]{new Vector2(-.041f,.012f),new Vector2(-.041f,.059f),new Vector2(-.034f,.073f),new Vector2(.034f,.073f),new Vector2(.041f,.059f),new Vector2(.041f,.012f)},dark,Vector3.right,48);
                g.Cylinder(new Vector3(side*.309f,.074f,.335f),.031f,.007f,steel,Vector3.right,32);
                Bolt(g,new Vector3(side*.315f,.074f,.335f),Vector3.right*side,.006f);
                // Rear caster fork and axle remain below the frame, with an actual vertical swivel.
                g.Cylinder(new Vector3(side*.235f,.104f,-.367f),.026f,.015f,steel,Vector3.up,32);
                foreach(float offset in new[]{-.025f,.025f})g.Box(new Vector3(side*.235f+offset,.065f,-.385f),new Vector3(.009f,.060f,.054f),dark,.003f);
                g.Cylinder(new Vector3(side*.235f,.046f,-.392f),.045f,.040f,rubber,Vector3.right,40);
                g.Cylinder(new Vector3(side*.235f,.046f,-.392f),.017f,.059f,steel,Vector3.right,32);
            }
            Shape.Rod(g,new Vector3(-.232f,.157f,-.086f),new Vector3(.232f,.157f,-.086f),.017f,steel,32);
            var arm=new[]{new Vector2(-.302f,.200f),new Vector2(-.260f,.158f),new Vector2(.086f,.128f),new Vector2(.111f,.155f),new Vector2(.070f,.184f),new Vector2(-.227f,.245f),new Vector2(-.302f,.245f)};
            foreach(float x in new[]{-.080f,.080f})Shape.Plate(g,new Vector3(x,0,0),Quaternion.Euler(0,90,0),arm,.034f,red);
            Shape.Rod(g,new Vector3(-.101f,.218f,.264f),new Vector3(.101f,.218f,.264f),.014f,steel,32);
            g.Cylinder(new Vector3(0,.255f,.264f),.082f,.026f,steel,Vector3.up,48);
            g.Cylinder(new Vector3(0,.274f,.264f),.088f,.016f,rubber,Vector3.up,48);
            for(int i=0;i<4;i++)Shape.RotatedBox(g,new Vector3(0,.284f,.264f),Quaternion.Euler(0,i*45,0),Vector3.zero,new Vector3(.144f,.003f,.006f),dark,.001f);
            var ramStart=new Vector3(0,.112f,-.296f);var ramEnd=new Vector3(0,.192f,.130f);var direction=(ramEnd-ramStart).normalized;
            Shape.Rod(g,ramStart,Vector3.Lerp(ramStart,ramEnd,.58f),.028f,dark,40);
            Shape.Rod(g,Vector3.Lerp(ramStart,ramEnd,.54f),ramEnd,.013f,steel,32);
            Shape.Ring(g,Vector3.Lerp(ramStart,ramEnd,.58f),.028f,.003f,steel,direction,40);
            foreach(float x in new[]{-.123f,.123f})
            {
                var points=new List<Vector3>();
                for(int i=0;i<=180;i++){float t=i/180f,a=t*12*Mathf.PI*2;points.Add(new Vector3(x+Mathf.Cos(a)*.009f,.137f+t*.044f+Mathf.Sin(a)*.009f,Mathf.Lerp(-.230f,.047f,t)));}
                Shape.Tube(g,points,.0018f,steel,6);
            }
            g.Box(new Vector3(0,.156f,-.325f),new Vector3(.093f,.100f,.094f),red,.009f);
            Shape.Axial(g,new Vector3(0,.209f,-.325f),new[]{new Vector2(-.014f,.018f),new Vector2(-.014f,.033f),new Vector2(.018f,.033f),new Vector2(.018f,.018f)},dark,Vector3.up,40);
            // Two-piece operating handle is stowed along the chassis while the jack is carried.
            Shape.Rod(g,new Vector3(.158f,.187f,-.426f),new Vector3(.158f,.187f,.167f),.011f,steel,32);
            Shape.Rod(g,new Vector3(.158f,.187f,-.426f),new Vector3(.158f,.187f,-.300f),.016f,rubber,32);
            foreach(float z in new[]{-.245f,.119f})g.Box(new Vector3(.158f,.178f,z),new Vector3(.038f,.023f,.023f),dark,.003f);
            return g;
        }
    }
}
