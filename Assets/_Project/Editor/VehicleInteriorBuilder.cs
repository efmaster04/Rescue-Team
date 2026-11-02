using System;
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
    public static class VehicleInteriorBuilder
    {
        const string Folder="Assets/_Project/Art/VehicleInterior";
        static Material trim,cloth,insert,belt,stitch,metal,wood,deck,dial,needle,headlining;
        [MenuItem("Company/Refine Van Cabin and Cargo Lining")]
        public static void Apply()
        {
            if(EditorApplication.isPlaying) throw new Exception("Stop Play Mode before editing the van interior.");
            EditorSceneManager.OpenScene(GarageSystemsBuilder.ScenePath);
            Configure(Object.FindFirstObjectByType<ServiceVehicle>());
            const string prefabPath="Assets/_Project/Prefabs/Vehicles/Vehicle_ServiceVan_Original.prefab";
            var prefab=PrefabUtility.LoadPrefabContents(prefabPath);
            try {Configure(prefab.GetComponent<ServiceVehicle>());PrefabUtility.SaveAsPrefabAsset(prefab,prefabPath);}
            finally {PrefabUtility.UnloadPrefabContents(prefab);}
            var vehicle=Object.FindFirstObjectByType<ServiceVehicle>();
            WorkOrderAssets.Render(vehicle.gameObject,"Assets/_Project/Resources/OfficeThumbnails/vehicle.png");
            var probe=GameObject.Find("GarageReflection").GetComponent<ReflectionProbe>(); string path=AssetDatabase.GetAssetPath(probe.customBakedTexture);
            if(!Lightmapping.BakeReflectionProbe(probe,path)) throw new Exception("Garage reflection bake failed.");
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport); probe.customBakedTexture=AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            AssetDatabase.SaveAssets(); Capture(vehicle);
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
        public static void ApplyAndValidate() {Apply(); ProjectValidation.Run();}
        internal static void Configure(ServiceVehicle vehicle)
        {
            Directory.CreateDirectory(Folder+"/Meshes"); Directory.CreateDirectory(Folder+"/Materials"); ImportTextures();
            trim=Surface("CabTrim",new Color(.075f,.085f,.08f),null,.23f);
            cloth=Surface("SeatCloth",new Color(.26f,.28f,.26f),"SeatWeave",.14f,3.2f);
            insert=Surface("SeatInsert",new Color(.34f,.355f,.32f),"SeatWeave",.14f,3.2f);
            belt=Surface("SeatBelt",new Color(.045f,.053f,.05f),"SeatWeave",.10f,2);
            stitch=Surface("UpholsteryThread",new Color(.47f,.48f,.42f),null,.10f);
            metal=Surface("CabHardware",new Color(.40f,.44f,.43f),null,.38f);metal.SetFloat("_Metallic",.65f);
            wood=Surface("BirchLining",new Color(.64f,.57f,.45f),"Birch",.22f);
            deck=Surface("PhenolicDeck",new Color(.16f,.145f,.12f),"DeckGrip",.20f);
            dial=Surface("InstrumentFace",new Color(.77f,.76f,.68f),null,.22f);
            needle=Surface("InstrumentNeedle",new Color(.43f,.075f,.045f),null,.20f);
            headlining=Surface("CargoHeadlining",new Color(.68f,.69f,.65f),null,.16f);
            // A small constant bounce term keeps the closed, unbaked ceiling readable
            // without another realtime light in the workshop.
            headlining.SetColor("_EmissionColor",new Color(.018f,.020f,.018f));headlining.EnableKeyword("_EMISSION");
            var extra=vehicle.transform.Find("CabinAndCargoDetails"); if(extra) Object.DestroyImmediate(extra.gameObject);
            var details=new Geometry();
            foreach(var filter in vehicle.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.StartsWith("Complete_V18")).ToArray())
            {
                float x=filter.name.Contains("Sürücü")?.43f:-.43f;
                Assign(vehicle,filter,Seat(x),"Seat_"+(x>0?"Driver":"Passenger"));
                BeltAndBuckle(details,x);
            }
            var wheel=vehicle.GetComponentsInChildren<MeshFilter>().Single(f=>f.name.StartsWith("Complete_V17"));
            Assign(vehicle,wheel,Steering(),"SteeringWheel");
            var dashboard=vehicle.GetComponentsInChildren<MeshFilter>().Single(f=>f.name.StartsWith("Complete_V16"));
            Assign(vehicle,dashboard,Dashboard(),"Dashboard");
            GearLever(details); Cargo(vehicle,details);
            var mesh=details.Build(out var materials); mesh.name="CabinAndCargoDetails";
            var go=new GameObject("CabinAndCargoDetails",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(vehicle.transform,false);
            go.GetComponent<MeshFilter>().sharedMesh=GeneratedMeshAsset.Store(mesh,Folder+"/Meshes/CabinAndCargoDetails.asset");go.GetComponent<MeshRenderer>().sharedMaterials=materials;
            if(go.GetComponentsInChildren<Collider>().Length!=0) throw new Exception("Cabin detail meshes must not change walking collision.");
            File.WriteAllText("Logs/vehicle_interior_art.txt","Two upholstered seats, rounded steering wheel, analog instrument cluster and gear lever. Seat backs remain forward of the cargo partition.\nBirch cargo lining, gripped loading deck, flush tie-down rings and slotted side rails. One extra combined renderer; zero new colliders or lights.\n");
        }
        static void ImportTextures()
        {
            AssetDatabase.Refresh();
            if(Directory.GetFiles(Folder+"/Textures","*.png").Length!=9) throw new Exception("Run Art/generate_vehicle_cabin.py before building the van interior.");
            foreach(var path in Directory.GetFiles(Folder+"/Textures","*.png"))
            {
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                var type=path.Contains("_Normal")?TextureImporterType.NormalMap:TextureImporterType.Default;
                bool srgb=!path.Contains("_Normal")&&!path.Contains("_MetalSmooth");
                if(importer.textureType==type && importer.sRGBTexture==srgb && importer.mipmapEnabled &&
                    importer.wrapMode==TextureWrapMode.Repeat && importer.maxTextureSize==512 && importer.anisoLevel==4 &&
                    importer.textureCompression==TextureImporterCompression.CompressedHQ) continue;
                importer.textureType=type;importer.sRGBTexture=srgb; importer.mipmapEnabled=true;
                importer.wrapMode=TextureWrapMode.Repeat; importer.maxTextureSize=512;importer.anisoLevel=4;
                importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();
            }
        }
        static Material Surface(string name,Color color,string texture,float smoothness,float tiling=1)
        {
            string path=Folder+"/Materials/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material) {material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
            material.SetColor("_BaseColor",color);material.SetFloat("_Surface",0);material.SetFloat("_Cull",2);material.SetFloat("_ZWrite",1);
            material.SetFloat("_Metallic",0);material.SetFloat("_Smoothness",smoothness);
            if(texture!=null)
            {
                material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/"+texture+"_Albedo.png"));
                material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/"+texture+"_Normal.png"));
                material.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/"+texture+"_MetalSmooth.png"));
                material.SetFloat("_BumpScale",.30f);material.SetFloat("_Smoothness",1);material.SetTextureScale("_BaseMap",Vector2.one*tiling);
            }
            BaseShaderGUI.SetMaterialKeywords(material,LitGUI.SetMaterialKeywords);EditorUtility.SetDirty(material);return material;
        }
        static Geometry Seat(float x)
        {
            var g=new Geometry();
            g.Box(new Vector3(x,.735f,-1.12f),new Vector3(.46f,.22f,.39f),trim,.025f);
            g.Box(new Vector3(x,.925f,-1.15f),new Vector3(.51f,.16f,.48f),cloth,.040f);
            g.Box(new Vector3(x,1.008f,-1.16f),new Vector3(.33f,.012f,.38f),insert,.006f);
            foreach(int side in new[]{-1,1})
            {
                g.Box(new Vector3(x+side*.219f,.985f,-1.16f),new Vector3(.068f,.080f,.43f),cloth,.027f);
                g.Box(new Vector3(x+side*.170f,1.016f,-1.16f),new Vector3(.0015f,.001f,.35f),stitch,.0002f);
            }
            var back=new Geometry();
            back.Box(Vector3.zero,new Vector3(.49f,.68f,.13f),cloth,.04f);
            back.Box(new Vector3(0,0,-.071f),new Vector3(.33f,.52f,.012f),insert,.006f);
            foreach(float side in new[]{-.17f,.17f}) back.Box(new Vector3(side,0,-.079f),new Vector3(.0015f,.50f,.001f),stitch,.0002f);
            g.Append(back,Matrix4x4.TRS(new Vector3(x,1.31f,-.88f),Quaternion.Euler(8,0,0),Vector3.one));
            foreach(float side in new[]{-.10f,.10f}) g.Cylinder(new Vector3(x+side,1.70f,-.83f),.008f,.14f,metal,Vector3.up,16);
            g.Box(new Vector3(x,1.80f,-.83f),new Vector3(.28f,.18f,.12f),cloth,.035f);
            return g;
        }
        static void BeltAndBuckle(Geometry g,float x)
        {
            float sign=Mathf.Sign(x);
            g.Box(new Vector3(x+sign*.267f,1.32f,-.85f),new Vector3(.028f,.66f,.006f),belt,.001f);
            g.Cylinder(new Vector3(x+sign*.267f,1.655f,-.845f),.018f,.009f,trim,Vector3.forward,24);
            g.Box(new Vector3(x-sign*.265f,1.015f,-.98f),new Vector3(.032f,.07f,.04f),trim,.007f);
            g.Box(new Vector3(x-sign*.265f,1.052f,-.975f),new Vector3(.022f,.008f,.026f),needle,.002f);
        }
        static Geometry Steering()
        {
            var wheel=new Geometry();
            var profile=Enumerable.Range(0,12).Select(i=>new Vector2(Mathf.Cos(i*Mathf.PI/6)*.013f,.192f+Mathf.Sin(i*Mathf.PI/6)*.013f)).ToArray();
            var ring=new Geometry();ring.Lathe(Vector3.zero,profile,trim,64);
            wheel.Append(ring,Matrix4x4.Rotate(Quaternion.FromToRotation(Vector3.right,Vector3.forward)));
            wheel.Box(Vector3.zero,new Vector3(.13f,.09f,.042f),trim,.016f);
            foreach(int side in new[]{-1,1}) wheel.Box(new Vector3(side*.12f,0,-.004f),new Vector3(.15f,.030f,.025f),trim,.006f);
            wheel.Box(new Vector3(0,-.119f,-.004f),new Vector3(.038f,.15f,.025f),trim,.007f);
            var g=new Geometry();g.Append(wheel,Matrix4x4.TRS(new Vector3(.43f,1.50f,-1.29f),Quaternion.Euler(-25,0,0),Vector3.one));
            g.Cylinder(new Vector3(.43f,1.46f,-1.39f),.030f,.23f,trim,new Vector3(0,.38f,.92f),32);
            return g;
        }
        static Geometry Dashboard()
        {
            var g=new Geometry();g.Box(new Vector3(0,1.44f,-1.61f),new Vector3(1.68f,.20f,.43f),trim,.025f);
            g.Box(new Vector3(.43f,1.565f,-1.43f),new Vector3(.31f,.12f,.12f),trim,.016f);
            foreach(float x in new[]{.36f,.50f}) Dial(g,new Vector3(x,1.565f,-1.365f),.049f);
            foreach(float x in new[]{-.65f,.65f})
            {
                g.Box(new Vector3(x,1.48f,-1.391f),new Vector3(.19f,.070f,.018f),belt,.005f);
                for(int i=0;i<5;i++) g.Box(new Vector3(x,1.456f+i*.012f,-1.378f),new Vector3(.166f,.004f,.006f),trim,.001f);
            }
            g.Box(new Vector3(-.05f,1.455f,-1.385f),new Vector3(.22f,.085f,.022f),belt,.005f);
            g.Box(new Vector3(-.05f,1.47f,-1.371f),new Vector3(.13f,.025f,.003f),trim,.001f);
            foreach(float x in new[]{-.135f,.035f})g.Cylinder(new Vector3(x,1.445f,-1.367f),.010f,.013f,trim,Vector3.forward,24);
            return g;
        }
        static void Dial(Geometry g,Vector3 center,float radius)
        {
            g.Cylinder(center,radius,.006f,metal,Vector3.forward,48);
            g.Cylinder(center+Vector3.forward*.004f,radius*.86f,.003f,dial,Vector3.forward,48);
            for(int i=0;i<11;i++)
            {
                var mark=new Geometry();mark.Box(new Vector3(0,radius*.68f,0),new Vector3(.0015f,.006f,.001f),trim,.0002f);
                g.Append(mark,Matrix4x4.TRS(center+Vector3.forward*.0065f,Quaternion.Euler(0,0,-120+i*24),Vector3.one));
            }
            var pointer=new Geometry();pointer.Box(new Vector3(0,radius*.28f,0),new Vector3(.0017f,radius*.87f,.001f),needle,.0002f);
            g.Append(pointer,Matrix4x4.TRS(center+Vector3.forward*.008f,Quaternion.Euler(0,0,118),Vector3.one));
        }
        static void GearLever(Geometry g)
        {
            g.Box(new Vector3(0,.69f,-1.32f),new Vector3(.11f,.075f,.13f),belt,.014f);
            g.Cylinder(new Vector3(0,.825f,-1.29f),.009f,.23f,trim,new Vector3(0,.97f,.24f),20);
            g.Box(new Vector3(0,.95f,-1.26f),new Vector3(.045f,.050f,.047f),trim,.014f);
        }
        static void Cargo(ServiceVehicle vehicle,Geometry g)
        {
            foreach(var filter in vehicle.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.StartsWith("Complete_V22")).ToArray())
            {
                var renderer=filter.GetComponent<MeshRenderer>();var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)
                {
                    if(materials[i].name.StartsWith("19_")||materials[i].name=="BirchLining")materials[i]=wood;
                    if(materials[i].name.StartsWith("20_")||materials[i].name=="PhenolicDeck")materials[i]=deck;
                }
                renderer.sharedMaterials=materials;
                var mesh=Object.Instantiate(filter.sharedMesh);var points=mesh.vertices;var normals=mesh.normals;var uv=new Vector2[points.Length];
                for(int i=0;i<points.Length;i++)
                {
                    var p=vehicle.transform.InverseTransformPoint(filter.transform.TransformPoint(points[i]));var n=vehicle.transform.InverseTransformDirection(filter.transform.TransformDirection(normals[i]));
                    uv[i]=Mathf.Abs(n.y)>.6f?new Vector2(p.x,p.z)*.75f:Mathf.Abs(n.x)>.6f?new Vector2(p.z,p.y)*.75f:new Vector2(p.x,p.y)*.75f;
                }
                mesh.uv=uv;mesh.RecalculateTangents();mesh.name="CargoMetricLining";filter.sharedMesh=GeneratedMeshAsset.Store(mesh,Folder+"/Meshes/CargoMetricLining.asset");
            }
            LiningPanel(vehicle,"CargoInnerLeft",new Vector3(-.90f,1.56f,.95f),new Vector3(.025f,1.88f,3.25f),wood);
            LiningPanel(vehicle,"CargoInnerRight",new Vector3(.90f,1.56f,.95f),new Vector3(.025f,1.88f,3.25f),wood);
            LiningPanel(vehicle,"CargoInnerBulkhead",new Vector3(0,1.56f,-.68f),new Vector3(1.80f,1.88f,.025f),wood);
            LiningPanel(vehicle,"CargoInnerRoof",new Vector3(0,2.49f,.95f),new Vector3(1.80f,.025f,3.25f),headlining);
            foreach(float z in new[]{-.35f,.32f,.99f,1.66f,2.33f})
                g.Box(new Vector3(0,2.462f,z),new Vector3(1.76f,.030f,.035f),headlining,.009f);
            foreach(int side in new[]{-1,1})
            {
                foreach(float z in new[]{.10f,1.20f,2.20f})
                {
                    var ring=new Geometry();ring.Lathe(Vector3.zero,new[]{new Vector2(-.0025f,.021f),new Vector2(.0025f,.021f),new Vector2(.0025f,.027f),new Vector2(-.0025f,.027f)},metal,32);
                    g.Append(ring,Matrix4x4.TRS(new Vector3(side*.72f,.684f,z),Quaternion.FromToRotation(Vector3.right,Vector3.up),Vector3.one));
                    g.Box(new Vector3(side*.72f,.681f,z),new Vector3(.065f,.003f,.065f),trim,.004f);
                }
                g.Box(new Vector3(side*.84f,1.03f,.98f),new Vector3(.015f,.055f,2.80f),trim,.004f);
                foreach(float y in new[]{1.009f,1.051f})g.Box(new Vector3(side*.828f,y,.98f),new Vector3(.011f,.009f,2.77f),metal,.002f);
                for(int i=0;i<15;i++)g.Box(new Vector3(side*.826f,1.03f,-.35f+i*.19f),new Vector3(.010f,.041f,.034f),metal,.002f);
            }
        }
        static void LiningPanel(ServiceVehicle vehicle,string name,Vector3 position,Vector3 size,Material material)
        {
            var panel=vehicle.transform.Find(name);
            // The reusable van prefab predates the scene's four inner closing faces.
            if(!panel)
            {
                panel=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer)).transform;
                panel.SetParent(vehicle.transform,false);panel.localPosition=position;
                panel.GetComponent<MeshFilter>().sharedMesh=WorkshopOverhaulBuilder.BoxMesh(size,Mathf.Min(.006f,Mathf.Min(size.x,Mathf.Min(size.y,size.z))*.18f));
            }
            panel.GetComponent<MeshRenderer>().sharedMaterial=material;
        }
        static void Assign(ServiceVehicle vehicle,MeshFilter filter,Geometry g,string key)
        {
            var mesh=g.Build(out var materials);var vertices=mesh.vertices;var normals=mesh.normals;
            for(int i=0;i<vertices.Length;i++) {vertices[i]=filter.transform.InverseTransformPoint(vehicle.transform.TransformPoint(vertices[i]));normals[i]=filter.transform.InverseTransformDirection(vehicle.transform.TransformDirection(normals[i]));}
            mesh.vertices=vertices;mesh.normals=normals;mesh.RecalculateBounds();mesh.RecalculateTangents();mesh.name=key;
            filter.sharedMesh=GeneratedMeshAsset.Store(mesh,Folder+"/Meshes/"+key+".asset");filter.GetComponent<MeshRenderer>().sharedMaterials=materials;
        }
        public static void Capture(ServiceVehicle vehicle)
        {
            var rotations=vehicle.doors.Select(d=>d.transform.localRotation).ToArray();
            try
            {
                foreach(var door in vehicle.doors)door.transform.localRotation*=Quaternion.Euler(0,door.openAngle,0);
                GarageRefinementBuilder.SignagePreview(vehicle.transform.TransformPoint(new Vector3(2.05f,1.73f,-1.10f)),vehicle.transform.TransformPoint(new Vector3(.20f,1.35f,-1.30f)),"Documentation/VanInteriorAfter_Cab.png",62);
                GarageRefinementBuilder.SignagePreview(vehicle.transform.TransformPoint(new Vector3(.43f,1.72f,-1.03f)),vehicle.transform.TransformPoint(new Vector3(.30f,1.50f,-1.47f)),"Documentation/VanInteriorAfter_Dashboard.png",65);
                GarageRefinementBuilder.SignagePreview(vehicle.transform.TransformPoint(new Vector3(0,1.65f,3.85f)),vehicle.transform.TransformPoint(new Vector3(0,1.48f,.50f)),"Documentation/VanInteriorAfter_Cargo.png",65);
            }
            finally {for(int i=0;i<vehicle.doors.Length;i++)vehicle.doors[i].transform.localRotation=rotations[i];}
        }
    }
}
