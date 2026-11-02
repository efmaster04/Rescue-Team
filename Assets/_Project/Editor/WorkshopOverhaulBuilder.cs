using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using CompanySim;

namespace CompanySimEditor
{
    public static class WorkshopOverhaulBuilder
    {
        const string Folder="Assets/_Project/Art/Garage/Meshes/";
        static Material Material(string name,Color color,float smooth=.35f,float metal=.12f,string texture=null)
            => GarageRefinementBuilder.Surface(name,color,smooth,metal,texture);
        public static void Configure()
        {
            var old=GameObject.Find("11_WorkshopInterior"); if(old) Object.DestroyImmediate(old);
            var root=new GameObject("11_WorkshopInterior").transform;
            var graphite=Material("Workshop_Graphite",new Color(.075f,.10f,.12f),.36f,.35f,"PowderCoat");
            var red=Material("Workshop_CabinetRed",new Color(.42f,.065f,.048f),.42f,.20f,"PowderCoat");
            var steel=Material("Workshop_BrushedMetal",new Color(.62f,.66f,.67f),.46f,.75f,"BrushedSteel");
            var white=Material("Workshop_EnamelWhite",new Color(.73f,.77f,.78f),.31f,.12f,"PowderCoat");
            var pegboard=Material("Workshop_PerforatedSteel",new Color(.15f,.19f,.205f),.28f,.22f,"Pegboard");
            var blueFloor=Material("Workshop_BayFloor",new Color(.15f,.28f,.35f),.36f,.02f,"Epoxy");
            var passage=Material("Workshop_Walkway",new Color(.43f,.48f,.48f),.32f,.02f,"Epoxy");
            var yellow=Material("Workshop_Marking",new Color(.95f,.68f,.15f),.27f,0);
            var rubber=Material("Workshop_Rubber",new Color(.055f,.068f,.075f),.12f,0,"RibbedRubber");
            var felt=Material("Workshop_DrawerLiner",new Color(.10f,.16f,.145f),.06f,0);
            var env=GameObject.Find("01_Environment_OfficeGarage");
            foreach(var renderer in env.GetComponentsInChildren<MeshRenderer>())
            {
                if(renderer.name.StartsWith("A07_"))
                    renderer.sharedMaterial=Material("Garage_Epoxy",new Color(.29f,.34f,.37f),.36f,.02f,"Epoxy");
                if(renderer.name.StartsWith("A43_") || renderer.name.StartsWith("A41_") || renderer.name.StartsWith("A44_") ||
                   ((renderer.name.StartsWith("A02_") || renderer.name.StartsWith("A42_")) &&
                    (renderer.name.Contains("paint chip") || renderer.name.Contains("hairline") || renderer.name.Contains("forked"))))
                    renderer.enabled=false;
                if(renderer.name.StartsWith("T30",System.StringComparison.Ordinal)) renderer.enabled=false;
                if(renderer.name.StartsWith("G06_") && renderer.bounds.center.x>6.4f && renderer.bounds.center.z< -3.0f &&
                    renderer.bounds.max.y<.60f) renderer.enabled=false;
                if(renderer.bounds.center.x>8.0f && renderer.bounds.center.x<8.95f && renderer.bounds.center.z> -1.86f &&
                    renderer.bounds.center.z<2.20f && renderer.bounds.max.y<.91f) renderer.enabled=false;
                // The supplied tools remain in their original working positions.
                // Refinish only their garage bench and tool-board backing.
                if(renderer.bounds.center.x<7.9f || renderer.bounds.center.z< -2.0f || renderer.bounds.center.z>2.5f) continue;
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)
                {
                    string name=materials[i]?materials[i].name:"";
                    if(name.Contains("workshop oak") || name.Contains("oak edge") || name.Contains("Oak end")) materials[i]=graphite;
                    if(name.Contains("fibreboard") || renderer.name.Contains("Pegboard backing")) materials[i]=pegboard;
                }
                renderer.sharedMaterials=materials;
                if(renderer.name.Contains("Pegboard backing")) GarageRefinementBuilder.PlanarUV(renderer);
            }
            // Architectural changes are confined to the garage footprint x > 0.
            Part(root,"ServiceFloor",new Vector3(4.65f,.007f,-.42f),new Vector3(4.48f,.008f,7.66f),blueFloor,0);
            Part(root,"WalkwayFloor",new Vector3(1.24f,.007f,-.25f),new Vector3(.98f,.008f,8.55f),passage,0);
            foreach(float x in new[]{2.37f,6.93f})
                Part(root,"BayBoundary",new Vector3(x,.016f,-.42f),new Vector3(.065f,.004f,7.66f),yellow,0);
            foreach(float x in new[]{.72f,1.76f})
                for(int i=0;i<13;i++) Part(root,"WalkwayDash",new Vector3(x,.016f,-4.35f+i*.65f),new Vector3(.035f,.004f,.30f),white,0);
            Part(root,"BayHeadLine",new Vector3(4.65f,.016f,-4.26f),new Vector3(4.48f,.004f,.065f),yellow,0);
            Part(root,"BayRearLine",new Vector3(4.65f,.016f,3.42f),new Vector3(4.48f,.004f,.065f),yellow,0);
            // Clean cladding between the existing structural ceiling beams.
            foreach(float x in new[]{1.55f,4.65f,7.75f})
            {
                Part(root,"CeilingCassette",new Vector3(x,4.012f,0),new Vector3(2.50f,.055f,10.12f),white,.012f);
                for(int z=0;z<9;z++) Part(root,"CeilingPanelJoint",new Vector3(x,3.981f,-4.6f+z*1.15f),new Vector3(2.48f,.006f,.012f),steel,0);
            }
            // Four real cabinet carcasses with recessed drawer fronts and metal pulls.
            for(int i=0;i<4;i++)
            {
                float z=-1.34f+i*1.02f;
                Part(root,"CabinetCarcass_"+i,new Vector3(8.88f,.47f,z),new Vector3(.022f,.82f,.98f),graphite,.006f);
                foreach(float side in new[]{-1f,1f})
                    Part(root,"CabinetSide",new Vector3(8.47f,.47f,z+side*.479f),new Vector3(.84f,.82f,.022f),graphite,.006f);
                Part(root,"CabinetBottom",new Vector3(8.47f,.092f,z),new Vector3(.84f,.028f,.94f),graphite,.005f);
                Part(root,"CabinetPlinth_"+i,new Vector3(8.47f,.055f,z),new Vector3(.78f,.10f,.89f),rubber,.006f);
                for(int drawer=0;drawer<4;drawer++)
                {
                    float y=.18f+drawer*.185f;
                    var tray=new GameObject("WorkshopDrawer_"+i+"_"+drawer).transform;
                    tray.SetParent(root,false); tray.position=new Vector3(8.028f,y,z);
                    Part(tray,"Drawer_"+i+"_"+drawer,tray.position,new Vector3(.035f,.169f,.91f),red,.009f);
                    Part(tray,"DrawerPull",tray.position+new Vector3(-.031f,.050f,0),new Vector3(.035f,.022f,.65f),steel,.004f);
                    Part(tray,"TrayBottom",tray.position+new Vector3(.33f,-.066f,0),new Vector3(.65f,.012f,.88f),steel,.004f);
                    Part(tray,"TrayLiner",tray.position+new Vector3(.33f,-.057f,0),new Vector3(.62f,.004f,.85f),felt,.001f);
                    foreach(float side in new[]{-1f,1f})
                        Part(tray,"TraySide",tray.position+new Vector3(.33f,-.010f,side*.441f),new Vector3(.65f,.112f,.012f),steel,.003f);
                    Part(tray,"TrayBack",tray.position+new Vector3(.655f,-.010f,0),new Vector3(.012f,.112f,.89f),steel,.003f);
                    var collider=tray.gameObject.AddComponent<BoxCollider>(); collider.size=new Vector3(.70f,.169f,.91f); collider.center=new Vector3(.33f,0,0);
                    var body=tray.gameObject.AddComponent<Rigidbody>(); body.isKinematic=true; body.useGravity=false;
                    body.interpolation=RigidbodyInterpolation.Interpolate; body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
                    tray.gameObject.AddComponent<WorkshopDrawer>();
                    if(drawer==3) PopulateTray(tray,env);
                }
            }
            Part(root,"WorkbenchCounter",new Vector3(8.46f,.957f,.25f),new Vector3(.96f,.055f,4.36f),steel,.012f);
            Part(root,"CounterFrontRubber",new Vector3(7.973f,.95f,.25f),new Vector3(.022f,.068f,4.34f),graphite,.004f);
            UpperCabinets(root,graphite,white,steel);
            Part(root,"StandingMat",new Vector3(7.55f,.018f,.25f),new Vector3(.66f,.014f,4.12f),rubber,.005f);
            var glow=Material("Workshop_TaskLens",Color.white,.42f,0);
            glow.SetColor("_EmissionColor",new Color(2.7f,2.85f,3)); glow.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;
            BaseShaderGUI.SetMaterialKeywords(glow,UnityEditor.Rendering.Universal.ShaderGUI.LitGUI.SetMaterialKeywords); EditorUtility.SetDirty(glow);
            Part(root,"TaskHood",new Vector3(8.82f,2.40f,.25f),new Vector3(.37f,.09f,4.34f),graphite,.012f);
            Part(root,"TaskStrip",new Vector3(8.76f,2.348f,.25f),new Vector3(.12f,.012f,4.20f),glow,.003f);
            Part(root,"RearTaskHousing",new Vector3(4.3f,3.19f,4.96f),new Vector3(1.72f,.075f,.12f),graphite,.010f);
            Part(root,"RearTaskLens",new Vector3(4.3f,3.145f,4.92f),new Vector3(1.62f,.015f,.075f),glow,.003f);
            var rearFill=new GameObject("RearVehicleWorkLight"); rearFill.transform.SetParent(root,false);
            rearFill.transform.position=new Vector3(4.3f,3.1f,4.80f); rearFill.transform.LookAt(new Vector3(4.65f,1.35f,2.40f));
            var rearLight=rearFill.AddComponent<Light>(); rearLight.type=LightType.Spot; rearLight.intensity=4.2f; rearLight.range=5.5f;
            rearLight.spotAngle=105; rearLight.innerSpotAngle=80; rearLight.color=new Color(.97f,.985f,1); rearLight.shadows=LightShadows.None;
            LiftController(root,graphite,steel,white,red);
            // A shallow corrugated metal liner gives the lower walls a consistent finish.
            foreach(float z in new[]{-4.30f,-.58f})
            {
                float length=z< -2?1.8f:3.56f;
                Part(root,"PartitionKickPanel",new Vector3(.092f,.55f,z),new Vector3(.014f,1.08f,length),graphite,.003f);
                Part(root,"PartitionCapRail",new Vector3(.105f,1.10f,z),new Vector3(.025f,.026f,length),steel,.004f);
            }
            Part(root,"RearWallCapRail",new Vector3(4.5f,1.10f,5.055f),new Vector3(8.9f,.030f,.03f),steel,.005f);
            Part(root,"RightWallCapRail",new Vector3(9.095f,1.10f,0),new Vector3(.030f,.030f,10.12f),steel,.005f);
            AirHoseReel(root,graphite,steel,rubber);
            ServiceCart(root,graphite,red,steel,rubber);
            foreach(var light in GameObject.Find("10_GarageFinish").GetComponentsInChildren<Light>())
                if(light.name.StartsWith("Garage_LED_Light")) { light.intensity=11; light.color=new Color(1,.965f,.90f); }
            // Soft light entering the open garage reaches the vehicle's front.
            var portal=new GameObject("Garage_PortalDaylight"); portal.transform.SetParent(root,false);
            portal.transform.position=new Vector3(4.65f,2.7f,-4.95f); portal.transform.LookAt(new Vector3(4.65f,1.2f,1.2f));
            var daylight=portal.AddComponent<Light>(); daylight.type=LightType.Spot; daylight.intensity=3.2f; daylight.range=10;
            daylight.color=new Color(.87f,.94f,1); daylight.spotAngle=105; daylight.innerSpotAngle=75;
            daylight.shadows=LightShadows.None;
            // Remove the old extra floor text when the bay surface supersedes it.
            var oldLabel=GameObject.Find("ServiceBayLabel"); if(oldLabel) oldLabel.SetActive(false);
            AssetDatabase.SaveAssets();
        }
        static void UpperCabinets(Transform root,Material graphite,Material enamel,Material steel)
        {
            // A shallow wall unit sits above the original tool board and task hood.
            // Its whole volume is above head height and behind the worktop edge.
            for(int bay=0;bay<3;bay++)
            {
                float z=-1.20f+bay*1.45f;
                var cabinet=Part(root,"UpperCabinetBody_"+bay,new Vector3(8.845f,2.85f,z),new Vector3(.43f,.68f,1.43f),graphite,.009f);
                cabinet.AddComponent<BoxCollider>().size=new Vector3(.43f,.68f,1.43f);
                for(int door=0;door<2;door++)
                {
                    float center=z+(door==0?-.351f:.351f);
                    Part(root,"UpperCabinetDoor",new Vector3(8.615f,2.85f,center),new Vector3(.030f,.638f,.684f),enamel,.007f);
                    // Recessed lower lip, visible hinge gap and short satin pull.
                    Part(root,"UpperCabinetLip",new Vector3(8.594f,2.554f,center),new Vector3(.018f,.018f,.64f),graphite,.003f);
                    float handleZ=center+(door==0?.245f:-.245f);
                    Part(root,"UpperCabinetPull",new Vector3(8.568f,2.75f,handleZ),new Vector3(.025f,.17f,.019f),steel,.004f);
                    for(int vent=0;vent<4;vent++)
                        Part(root,"UpperVent",new Vector3(8.597f,3.05f-vent*.022f,center),new Vector3(.004f,.006f,.30f),graphite,.001f);
                }
            }
            Part(root,"UpperCabinetTopRail",new Vector3(8.83f,3.203f,.25f),new Vector3(.46f,.030f,4.36f),steel,.005f);
        }
        static void ServiceCart(Transform parent,Material dark,Material red,Material steel,Material rubber)
        {
            var root=new GameObject("ServicePartsCart").transform; root.SetParent(parent,false);
            root.position=new Vector3(8.56f,0,-4.17f);
            var collider=root.gameObject.AddComponent<BoxCollider>(); collider.center=new Vector3(0,.48f,0); collider.size=new Vector3(.66f,.92f,.93f);
            foreach(float side in new[]{-1f,1f}) foreach(float end in new[]{-1f,1f})
            {
                var p=root.position+new Vector3(side*.255f,.13f,end*.335f);
                Cylinder(root,"CartCaster",p,.080f,.043f,rubber,Vector3.forward);
                Cylinder(root,"CartWheelHub",p+new Vector3(0,0,-.024f),.032f,.010f,steel,Vector3.forward);
                Part(root,"CasterFork",p+new Vector3(0,.057f,0),new Vector3(.035f,.11f,.060f),steel,.004f);
                Part(root,"CartUpright",root.position+new Vector3(side*.274f,.52f,end*.38f),new Vector3(.035f,.64f,.035f),dark,.005f);
            }
            foreach(float y in new[]{.245f,.775f})
            {
                Part(root,"CartTray",root.position+new Vector3(0,y,0),new Vector3(.61f,.020f,.86f),red,.008f);
                foreach(float side in new[]{-1f,1f})
                    Part(root,"CartTraySide",root.position+new Vector3(side*.30f,y+.042f,0),new Vector3(.018f,.084f,.86f),red,.005f);
                foreach(float end in new[]{-1f,1f})
                    Part(root,"CartTrayEnd",root.position+new Vector3(0,y+.042f,end*.42f),new Vector3(.61f,.084f,.018f),red,.005f);
                Part(root,"CartTrayMat",root.position+new Vector3(0,y+.013f,0),new Vector3(.56f,.004f,.80f),rubber,.001f);
            }
            foreach(float side in new[]{-1f,1f})
                Cylinder(root,"CartHandlePost",root.position+new Vector3(side*.25f,.88f,-.39f),.012f,.18f,steel,Vector3.up);
            Cylinder(root,"CartPushHandle",root.position+new Vector3(0,.97f,-.39f),.018f,.53f,rubber,Vector3.right);
            // Two removable-looking metal parts trays, inset into the cart's top.
            for(int i=0;i<2;i++)
            {
                float z=-.19f+i*.38f;
                Part(root,"CartPartsPan",root.position+new Vector3(0,.798f,z),new Vector3(.40f,.012f,.31f),steel,.005f);
                foreach(float side in new[]{-1f,1f})
                    Part(root,"CartPanRim",root.position+new Vector3(side*.195f,.82f,z),new Vector3(.010f,.045f,.31f),steel,.003f);
                foreach(float end in new[]{-1f,1f})
                    Part(root,"CartPanEnd",root.position+new Vector3(0,.82f,z+end*.15f),new Vector3(.40f,.045f,.01f),steel,.003f);
            }
        }
        static void AirHoseReel(Transform parent,Material dark,Material steel,Material hose)
        {
            var root=new GameObject("WallAirHoseReel").transform; root.SetParent(parent,false);
            root.position=new Vector3(8.86f,1.60f,3.15f);
            Part(root,"ReelMount",new Vector3(9.076f,1.60f,3.15f),new Vector3(.036f,.46f,.32f),steel,.006f);
            var enamel=Material("Workshop_ReelEnamel",new Color(.16f,.28f,.30f),.36f,.16f,"PowderCoat");
            foreach(float x in new[]{8.735f,8.975f})
            {
                Cylinder(root,"ReelCheek",new Vector3(x,1.60f,3.15f),.245f,.023f,enamel,Vector3.right);
                Cylinder(root,"ReelHub",new Vector3(x-.014f,1.60f,3.15f),.062f,.03f,steel,Vector3.right);
            }
            Cylinder(root,"ReelCore",root.position,.115f,.22f,dark,Vector3.right);
            Cylinder(root,"ReelFrontRecess",new Vector3(8.719f,1.60f,3.15f),.184f,.008f,dark,Vector3.right);
            for(int i=0;i<3;i++)
            {
                float angle=i*120*Mathf.Deg2Rad;
                var rib=Part(root,"ReelFaceRib",new Vector3(8.708f,1.60f+Mathf.Cos(angle)*.12f,3.15f+Mathf.Sin(angle)*.12f),
                    new Vector3(.012f,.18f,.028f),enamel,.005f);
                rib.transform.rotation=Quaternion.AngleAxis(i*120,Vector3.right);
            }
            Cylinder(root,"ReelFrontAxle",new Vector3(8.695f,1.60f,3.15f),.047f,.024f,steel,Vector3.right);
            var points=new List<Vector3>();
            const int count=200;
            for(int i=0;i<=count;i++)
            {
                float t=i/(float)count,angle=t*5*Mathf.PI*2;
                points.Add(new Vector3(Mathf.Lerp(8.76f,8.95f,t),1.60f+Mathf.Sin(angle)*.191f,3.15f+Mathf.Cos(angle)*.191f));
            }
            Tube(root,"WoundAirHose",points,.013f,hose,"WoundAirHose");
            Tube(root,"HangingAirHose",new List<Vector3>{new Vector3(8.76f,1.60f,3.341f),new Vector3(8.71f,1.45f,3.36f),
                new Vector3(8.70f,1.13f,3.36f),new Vector3(8.70f,.89f,3.34f),new Vector3(8.70f,.83f,3.29f)},.013f,hose,"HangingAirHose");
            Cylinder(root,"HoseCoupling",new Vector3(8.70f,.83f,3.25f),.020f,.085f,steel,Vector3.forward);
            Part(root,"CouplingGrip",new Vector3(8.70f,.83f,3.27f),new Vector3(.041f,.041f,.045f),dark,.006f);
            var collider=root.gameObject.AddComponent<BoxCollider>(); collider.center=new Vector3(0,0,.01f); collider.size=new Vector3(.36f,.50f,.52f);
        }
        static void Cylinder(Transform parent,string name,Vector3 center,float radius,float length,Material material,Vector3 axis)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name=name; go.transform.SetParent(parent,false);
            go.transform.position=center; go.transform.rotation=Quaternion.FromToRotation(Vector3.up,axis);
            go.transform.localScale=new Vector3(radius*2,length*.5f,radius*2);
            Object.DestroyImmediate(go.GetComponent<Collider>()); go.GetComponent<MeshRenderer>().sharedMaterial=material;
        }
        static void Tube(Transform parent,string name,List<Vector3> points,float radius,Material material,string key)
        {
            string path=Folder+"Kit_"+key+".asset"; var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            var center=parent.position;
            if(!mesh)
            {
                const int sides=8; var vertices=new List<Vector3>(); var triangles=new List<int>(); var uv=new List<Vector2>();
                for(int i=0;i<points.Count;i++)
                {
                    var tangent=(points[Mathf.Min(i+1,points.Count-1)]-points[Mathf.Max(0,i-1)]).normalized;
                    var normal=Vector3.Cross(tangent,Mathf.Abs(tangent.x)<.9f?Vector3.right:Vector3.up).normalized;
                    var binormal=Vector3.Cross(tangent,normal);
                    for(int side=0;side<sides;side++)
                    {
                        float angle=side*Mathf.PI*2/sides;
                        vertices.Add(points[i]-center+(normal*Mathf.Cos(angle)+binormal*Mathf.Sin(angle))*radius);
                        uv.Add(new Vector2(side/(float)sides,i*.05f));
                        if(i==0) continue;
                        int a=(i-1)*sides+side,b=(i-1)*sides+(side+1)%sides,c=i*sides+(side+1)%sides,d=i*sides+side;
                        triangles.AddRange(new[]{a,b,c,a,c,d});
                    }
                }
                mesh=new Mesh {name=key}; mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents(); AssetDatabase.CreateAsset(mesh,path);
            }
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer)); go.transform.SetParent(parent,false);
            go.GetComponent<MeshFilter>().sharedMesh=mesh; go.GetComponent<MeshRenderer>().sharedMaterial=material;
        }
        static void PopulateTray(Transform tray,GameObject environment)
        {
            var source=environment.GetComponentsInChildren<MeshFilter>().First(x=>x.name.StartsWith("Forged open-ended wrench"));
            var mesh=source.sharedMesh; var bounds=mesh.bounds;
            int axis=bounds.size.x>bounds.size.y?0:1; if(bounds.size.z>bounds.size[axis]) axis=2;
            var along=Vector3.zero; along[axis]=1;
            for(int i=0;i<3;i++)
            {
                var go=new GameObject("DrawerWrench",typeof(MeshFilter),typeof(MeshRenderer)); go.transform.SetParent(tray,false);
                float scale=(.21f+i*.035f)/bounds.size[axis];
                var rotation=Quaternion.AngleAxis(-6+i*6,Vector3.up)*Quaternion.FromToRotation(along,Vector3.forward);
                go.transform.localRotation=rotation; go.transform.localScale=Vector3.one*scale;
                float halfHeight=(Mathf.Abs((rotation*Vector3.right).y)*bounds.extents.x+
                    Mathf.Abs((rotation*Vector3.up).y)*bounds.extents.y+Mathf.Abs((rotation*Vector3.forward).y)*bounds.extents.z)*scale;
                go.transform.localPosition=new Vector3(.17f,-.054f+halfHeight,-.27f+i*.27f)-rotation*(bounds.center*scale);
                go.GetComponent<MeshFilter>().sharedMesh=mesh;
                go.GetComponent<MeshRenderer>().sharedMaterials=source.GetComponent<MeshRenderer>().sharedMaterials;
            }
        }
        static void LiftController(Transform parent,Material dark,Material steel,Material white,Material red)
        {
            var root=new GameObject("LiftController").transform; root.SetParent(parent,false); root.position=new Vector3(6.26f,1.48f,.13f);
            var housing=Part(root,"LiftControlHousing",root.position,new Vector3(.28f,.48f,.14f),dark,.014f);
            housing.AddComponent<BoxCollider>().size=new Vector3(.28f,.48f,.14f);
            Part(root,"LiftControlFascia",root.position+new Vector3(0,0,-.077f),new Vector3(.235f,.43f,.013f),steel,.006f);
            var green=Material("Workshop_ButtonGreen",new Color(.07f,.36f,.20f),.35f,0);
            for(int i=0;i<2;i++)
            {
                float y=i==0?.075f:-.060f;
                var button=GameObject.CreatePrimitive(PrimitiveType.Cylinder); button.name=i==0?"LiftUpButton":"LiftDownButton";
                button.transform.SetParent(root,false); button.transform.localPosition=new Vector3(0,y,-.095f);
                button.transform.localRotation=Quaternion.Euler(90,0,0); button.transform.localScale=new Vector3(.085f,.012f,.085f);
                button.GetComponent<MeshRenderer>().sharedMaterial=i==0?green:dark; Object.DestroyImmediate(button.GetComponent<Collider>());
                var symbol=new GameObject("DirectionArrow",typeof(MeshFilter),typeof(MeshRenderer)); symbol.transform.SetParent(root,false);
                symbol.transform.localPosition=new Vector3(0,y,-.109f); if(i==1) symbol.transform.localRotation=Quaternion.Euler(0,0,180);
                const string path=Folder+"Kit_ControlArrow.asset"; var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(!mesh) {mesh=new Mesh {name="ControlArrow",vertices=new[]{new Vector3(-.022f,-.016f,0),new Vector3(0,.021f,0),new Vector3(.022f,-.016f,0)},triangles=new[]{0,1,2}}; mesh.RecalculateNormals(); AssetDatabase.CreateAsset(mesh,path);}
                symbol.GetComponent<MeshFilter>().sharedMesh=mesh; symbol.GetComponent<MeshRenderer>().sharedMaterial=white;
            }
            var target=GameObject.Find("ModuleStation_ControlPanel"); target.transform.position=root.position;
            target.GetComponent<BoxCollider>().size=new Vector3(.40f,.60f,.32f);
        }
        static GameObject Part(Transform parent,string name,Vector3 position,Vector3 size,Material material,float bevel)
        {
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer)); go.transform.SetParent(parent,false); go.transform.position=position;
            go.GetComponent<MeshFilter>().sharedMesh=BoxMesh(size,bevel);
            go.GetComponent<MeshRenderer>().sharedMaterial=material; return go;
        }
        internal static Mesh BoxMesh(Vector3 size,float radius)
        {
            string key="Kit_"+size.x.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"_"+
                size.y.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"_"+size.z.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"_"+
                radius.ToString("F3",System.Globalization.CultureInfo.InvariantCulture);
            string path=Folder+key+".asset"; var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path); if(mesh) return mesh;
            var vertices=new List<Vector3>(); var normals=new List<Vector3>(); var uv=new List<Vector2>(); var triangles=new List<int>();
            var half=size*.5f; radius=Mathf.Min(radius,Mathf.Min(half.x,Mathf.Min(half.y,half.z))*.8f);
            var inner=half-Vector3.one*radius;
            foreach(var face in new[]{Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back})
            {
                int axis=Mathf.Abs(face.x)>.5f?0:Mathf.Abs(face.y)>.5f?1:2;
                int u=(axis+1)%3,v=(axis+2)%3,start=vertices.Count;
                float[] us={-half[u],-inner[u],inner[u],half[u]},vs={-half[v],-inner[v],inner[v],half[v]};
                for(int row=0;row<4;row++) for(int col=0;col<4;col++)
                {
                    Vector3 p=Vector3.zero; p[axis]=face[axis]*half[axis]; p[u]=us[col]; p[v]=vs[row];
                    var core=new Vector3(Mathf.Clamp(p.x,-inner.x,inner.x),Mathf.Clamp(p.y,-inner.y,inner.y),Mathf.Clamp(p.z,-inner.z,inner.z));
                    var n=radius>0?(p-core).normalized:face;
                    if(radius>0) p=core+n*radius;
                    vertices.Add(p); normals.Add(n); uv.Add(new Vector2(p[u],p[v])*.5f);
                }
                for(int row=0;row<3;row++) for(int col=0;col<3;col++)
                {
                    int a=start+row*4+col,b=a+1,c=a+5,d=a+4;
                    var tangentU=Vector3.zero; tangentU[u]=1;
                    var tangentV=Vector3.zero; tangentV[v]=1;
                    bool reverse=Vector3.Dot(Vector3.Cross(tangentU,tangentV),face)<0;
                    triangles.AddRange(reverse?new[]{a,c,b,a,d,c}:new[]{a,b,c,a,c,d});
                }
            }
            mesh=new Mesh {name=key}; mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0);
            mesh.RecalculateBounds(); mesh.RecalculateTangents(); AssetDatabase.CreateAsset(mesh,path); return mesh;
        }
    }
}
