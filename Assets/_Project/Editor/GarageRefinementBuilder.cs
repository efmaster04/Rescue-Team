using System.IO;
using System.Linq;
using CompanySim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal.ShaderGUI;

namespace CompanySimEditor
{
    public static class GarageRefinementBuilder
    {
        const string Scene="Assets/_Project/Scenes/OfficeGarage.unity";
        const string Folder="Assets/_Project/Art/Garage";
        static readonly Color Wall=new Color(.74f,.76f,.75f);
        static readonly Color Petrol=new Color(.12f,.23f,.27f);
        static readonly Color Floor=new Color(.43f,.48f,.50f);

        [MenuItem("Company/Refine Garage and Doors")]
        public static void Apply()
        {
            EditorSceneManager.OpenScene(Scene);
            VanRestorationBuilder.ConfigureScene();
            ConfigureDoors(); ConfigureGarage();
            OfficePresentationBuilder.BakeReflections();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }
        public static void ApplyAndValidate() { Apply(); ProjectValidation.Run(); }

        [MenuItem("Company/Simplify Workshop Signs")]
        public static void ApplySignage()
        {
            EditorSceneManager.OpenScene(Scene);
            ConfigureSignage();
            OfficePresentationBuilder.BakeReflections();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            SignagePreview(new Vector3(2.05f,2.34f,2.92f),new Vector3(1.42f,2.57f,5.01f),"Documentation/SimpleDepotSign.png",52);
            SignagePreview(new Vector3(2.0f,2.08f,-3.60f),new Vector3(.12f,2.35f,-2.88f),"Documentation/SimpleOfficeSign.png",48);
            SignagePreview(new Vector3(7.9f,2.30f,-4.2f),new Vector3(4.65f,1.30f,-.15f),"Documentation/WorkshopSimpleSigns.png",65);
        }

        public static void ConfigureDoors()
        {
            foreach(var door in Object.FindObjectsByType<OfficeDoor>())
            {
                var col=door.GetComponent<BoxCollider>();
                if(door.name=="OfficeDoor_A11")
                {
                    col.center=new Vector3(0,1.05f,.44975f);
                    col.size=new Vector3(.065f,2.075f,.8775f);
                }
                else
                {
                    col.center=new Vector3(.46f,1.075f,0);
                    col.size=new Vector3(.92f,2.13f,.075f);
                }
                var body=door.GetComponent<Rigidbody>(); if(!body) body=door.gameObject.AddComponent<Rigidbody>();
                body.isKinematic=true; body.useGravity=false;
                body.interpolation=RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
                EditorUtility.SetDirty(door);
            }
        }

        public static void ConfigureGarage()
        {
            Directory.CreateDirectory(Folder+"/Materials"); Directory.CreateDirectory(Folder+"/Meshes");
            AssetDatabase.Refresh(); ImportTextures();
            var old=GameObject.Find("10_GarageFinish"); if(old) Object.DestroyImmediate(old);
            var root=new GameObject("10_GarageFinish").transform;
            var rearWall=GameObject.Find("00_WorldCollision").transform.Find("Garage_Left_Rear");
            rearWall.position=new Vector3(0,2,3.2f);
            rearWall.GetComponent<BoxCollider>().size=new Vector3(.28f,4,4);
            var floor=Surface("Garage_Epoxy",Floor,.36f,.02f,"Epoxy");
            var plaster=Surface("Garage_Plaster",Wall,.16f,0,"Plaster");
            var band=Surface("Garage_ProtectionBand",Petrol,.30f,.08f,"Paint");
            var roof=Surface("Garage_Ceiling",new Color(.39f,.43f,.44f),.24f,.20f,"Paint");
            var trim=Surface("Garage_Steel",new Color(.28f,.34f,.37f),.40f,.65f,null);
            var stain=Surface("Garage_FloorWear",Floor*.91f,.29f,.02f,"Epoxy");
            var wear=Surface("Garage_WallWear",Wall*.88f,.14f,0,"Plaster");
            var yellow=Surface("Garage_SafetyPaint",new Color(.92f,.65f,.13f),.28f,.06f,"Paint");
            var lift=Surface("Garage_LiftEnamel",new Color(.075f,.19f,.29f),.42f,.35f,"Paint");
            var env=GameObject.Find("01_Environment_OfficeGarage");
            foreach(var renderer in env.GetComponentsInChildren<MeshRenderer>())
            {
                string name=renderer.name; Material replacement=null; bool planar=false;
                if(name.StartsWith("A07_")) { replacement=floor; planar=true; }
                else if(name.StartsWith("A41_")) { replacement=stain; planar=true; }
                else if(name.StartsWith("A02_") || name.StartsWith("A42_") || name.StartsWith("A47_"))
                {
                    if(name.Contains("blue")) replacement=band;
                    else if(name.Contains("plastered") || name.Contains("solid lintel") || name.Contains("wall below")) replacement=plaster;
                    else if(name.Contains("paint chip") || name.Contains("hairline")) replacement=wear;
                    planar=replacement;
                }
                else if(name.StartsWith("A03_") && name.Contains("garage side")) replacement=band;
                else if(name.StartsWith("A36_") || name.StartsWith("A37_"))
                {
                    replacement=name.Contains("roof slab")?roof:trim; planar=name.Contains("roof slab");
                }
                else if(name.StartsWith("A04_") && renderer.bounds.min.x>1 && name.Contains("structural pier"))
                { replacement=plaster; planar=true; }
                else if(name.StartsWith("T"))
                {
                    if(renderer.sharedMaterials.Any(x=>x && (x.name.ToLowerInvariant().Contains("blue") || x.name.ToLowerInvariant().Contains("cobalt"))))
                        replacement=lift;
                }
                if(replacement)
                {
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(_=>replacement).ToArray();
                    if(planar) PlanarUV(renderer);
                }
                else if((name.StartsWith("A") || name.StartsWith("T")) && renderer.bounds.min.x>.1f)
                {
                    var materials=renderer.sharedMaterials;
                    for(int i=0;i<materials.Length;i++)
                        if(materials[i] && materials[i].name.Contains("safety yellow")) materials[i]=yellow;
                    renderer.sharedMaterials=materials;
                }
            }
            // The partition is shared with the protected office. Add a finish
            // only on its garage face instead of changing the source material.
            Finish(root,"PartitionFront",new Vector3(.083f,2.105f,-4.3f),new Vector3(.006f,2.29f,1.8f),plaster);
            Finish(root,"PartitionRear",new Vector3(.083f,2.105f,-.58f),new Vector3(.006f,2.29f,3.56f),plaster);
            Finish(root,"PartitionLintel",new Vector3(.083f,2.73f,-2.88f),new Vector3(.006f,1.04f,1.04f),plaster);
            foreach(float z in new[]{-5.15f,1.2f})
                Finish(root,"PartitionPierFinish",new Vector3(.195f,2.44f,z),new Vector3(.006f,2.92f,.40f),plaster);

            var dark=Surface("Garage_SignBacking",new Color(.055f,.080f,.095f),.25f,.1f,null);
            var white=Surface("Garage_OffWhite",new Color(.87f,.89f,.87f),.30f,.1f,null);
            var glow=Surface("Garage_LED",new Color(.88f,.94f,1),.35f,0,null);
            glow.SetColor("_EmissionColor",new Color(3.5f,3.7f,3.9f));
            glow.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;
            BaseShaderGUI.SetMaterialKeywords(glow,LitGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(glow);
            foreach(var lens in env.GetComponentsInChildren<MeshRenderer>().Where(x=>x.name.StartsWith("A23_") && x.name.Contains("diffuser")))
                lens.sharedMaterial=glow;
            ConfigureLights(root,dark,glow);
            // Short return marks define the bay without filling circulation space.
            Finish(root,"BayStopLeft",new Vector3(2.84f,.014f,-4.05f),new Vector3(.55f,.006f,.055f),yellow);
            Finish(root,"BayStopRight",new Vector3(6.46f,.014f,-4.05f),new Vector3(.55f,.006f,.055f),yellow);
            Finish(root,"DepotStrip",new Vector3(4.45f,.013f,3.60f),new Vector3(7.0f,.005f,.04f),white);
            WorkshopOverhaulBuilder.Configure();
            ConfigureSignage();
            EquipmentPresentationBuilder.Configure();
            AssetDatabase.SaveAssets();
        }

        public static void ConfigureSignage()
        {
            foreach(string name in new[]{"StockSign","WorkbenchSign","OfficeSign","ServiceBayLabel","WorkshopBayStencil"})
            {
                foreach(var old in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Where(x=>x.name==name).ToArray())
                    Object.DestroyImmediate(old.gameObject);
            }
            var parent=GameObject.Find("10_GarageFinish").transform;
            var face=Surface("Workshop_LabelWhite",new Color(.88f,.875f,.845f),.15f,0,null);
            var edge=Surface("Workshop_LabelEdge",new Color(.30f,.32f,.32f),.28f,.35f,null);
            Sign(parent,"StockSign",new Vector3(1.42f,2.56f,5.038f),Vector3.back,"DEPO",new Vector2(.54f,.16f),face,edge);
            Sign(parent,"OfficeSign",new Vector3(.105f,2.35f,-2.88f),Vector3.right,"OFİS",new Vector2(.40f,.14f),face,edge);
        }
        static void ImportTextures()
        {
            foreach(string path in Directory.GetFiles(Folder+"/Textures","*.png"))
            {
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                bool normal=path.Contains("_Normal");
                importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
                importer.sRGBTexture=!normal && !path.Contains("MetalSmooth");
                importer.wrapMode=TextureWrapMode.Repeat; importer.mipmapEnabled=true;
                importer.anisoLevel=8; importer.maxTextureSize=1024;
                importer.textureCompression=TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
        }
        internal static Material Surface(string name,Color color,float smooth,float metallic,string texture)
        {
            string path=Folder+"/Materials/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material) {material=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material,path);}
            material.SetColor("_BaseColor",color); material.SetFloat("_Smoothness",smooth); material.SetFloat("_Metallic",metallic);
            if(texture!=null)
            {
                material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/"+texture+"_Albedo.png"));
                material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/"+texture+"_Normal.png"));
                material.SetFloat("_BumpScale",texture=="Plaster"?.34f:texture=="PowderCoat"?.24f:texture=="BrushedSteel"?.32f:.50f);
                material.EnableKeyword("_NORMALMAP");
                var metalMap=AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/"+texture+"_MetalSmooth.png");
                if(metalMap)
                {
                    material.SetTexture("_MetallicGlossMap",metalMap);
                    material.EnableKeyword("_METALLICSPECGLOSSMAP"); material.SetFloat("_Smoothness",1);
                }
            }
            BaseShaderGUI.SetMaterialKeywords(material,LitGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(material); return material;
        }
        internal static void PlanarUV(MeshRenderer renderer)
        {
            var filter=renderer.GetComponent<MeshFilter>(); if(!filter || !filter.sharedMesh) return;
            string path=Folder+"/Meshes/"+renderer.name.Replace(" ","_")+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!mesh)
            {
                mesh=Object.Instantiate(filter.sharedMesh); mesh.name=renderer.name+"_SurfaceUV";
                var vertices=mesh.vertices; var normals=mesh.normals; var uv=new Vector2[vertices.Length];
                for(int i=0;i<vertices.Length;i++)
                {
                    var p=renderer.transform.TransformPoint(vertices[i]); var n=renderer.transform.TransformDirection(normals[i]);
                    if(Mathf.Abs(n.y)>.6f) uv[i]=new Vector2(p.x,p.z)*.5f;
                    else if(Mathf.Abs(n.x)>Mathf.Abs(n.z)) uv[i]=new Vector2(p.z,p.y)*.5f;
                    else uv[i]=new Vector2(p.x,p.y)*.5f;
                }
                mesh.uv=uv; mesh.RecalculateTangents(); AssetDatabase.CreateAsset(mesh,path);
            }
            filter.sharedMesh=mesh;
        }
        static GameObject Finish(Transform parent,string name,Vector3 position,Vector3 size,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name;
            go.transform.SetParent(parent,false); go.transform.position=position; go.transform.localScale=size;
            Object.DestroyImmediate(go.GetComponent<Collider>()); go.GetComponent<MeshRenderer>().sharedMaterial=material;
            return go;
        }
        static void ConfigureLights(Transform root,Material housing,Material lens)
        {
            var front=GameObject.Find("Garage_FrontLight"); if(front) Object.DestroyImmediate(front);
            var rear=GameObject.Find("Garage_RearLight"); if(rear) Object.DestroyImmediate(rear);
            for(int x=0;x<2;x++) for(int z=0;z<2;z++)
            {
                var position=new Vector3(x==0?2.05f:7.1f,3.80f,z==0?-1.65f:2.4f);
                if(!(x==1 && z==1))
                {
                    Finish(root,"LED_Housing_"+x+z,position+Vector3.up*.06f,new Vector3(1.5f,.09f,.23f),housing);
                    Finish(root,"LED_Lens_"+x+z,position,new Vector3(1.35f,.028f,.15f),lens);
                    foreach(float dx in new[]{-.55f,.55f})
                        Finish(root,"LED_Suspension",position+new Vector3(dx,.16f,0),new Vector3(.009f,.24f,.009f),housing);
                }
                else position.y=3.52f;
                var go=new GameObject("Garage_LED_Light_"+x+z); go.transform.SetParent(root,false);
                go.transform.position=position-Vector3.up*.06f; go.transform.rotation=Quaternion.Euler(90,0,0);
                var light=go.AddComponent<Light>(); light.type=LightType.Spot; light.color=new Color(.94f,.97f,1);
                light.intensity=8; light.range=8.5f; light.spotAngle=125; light.innerSpotAngle=95;
                light.shadows=LightShadows.Soft; light.shadowStrength=.70f;
                light.shadowBias=.015f; light.shadowNormalBias=.12f; light.shadowNearPlane=.06f;
                var data=go.AddComponent<UniversalAdditionalLightData>();
                var serialized=new SerializedObject(data);
                serialized.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue=1;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var task=new GameObject("Garage_WorkbenchTaskLight"); task.transform.SetParent(root,false);
            task.transform.position=new Vector3(8.7f,2.32f,.5f); task.transform.rotation=Quaternion.Euler(65,270,0);
            var fill=task.AddComponent<Light>(); fill.type=LightType.Spot; fill.range=3; fill.intensity=2.2f;
            fill.color=new Color(1,.93f,.83f); fill.spotAngle=105; fill.innerSpotAngle=75; fill.shadows=LightShadows.None;
            Finish(root,"WorkbenchTaskBar",new Vector3(8.95f,2.39f,.5f),new Vector3(.08f,.035f,1.1f),lens);
        }
        static void Sign(Transform parent,string name,Vector3 position,Vector3 normal,string label,Vector2 size,Material face,Material edge)
        {
            var root=new GameObject(name).transform; root.SetParent(parent,false);
            root.position=position; root.rotation=Quaternion.LookRotation(-normal,Vector3.up);
            var panel=Finish(root,"PlateEdge",position,new Vector3(size.x,size.y,.010f),edge);
            panel.transform.localPosition=Vector3.zero; panel.transform.localRotation=Quaternion.identity;
            var enamel=Finish(root,"PlateFace",position,new Vector3(size.x-.008f,size.y-.008f,.002f),face);
            enamel.transform.localPosition=new Vector3(0,0,-.006f); enamel.transform.localRotation=Quaternion.identity;
            var canvas=WorldCanvas(root,"Lettering",size*1000,.001f); canvas.localPosition=new Vector3(0,0,-.008f);
            Letter(canvas,label,Mathf.RoundToInt(size.y*600),Vector2.zero,size*1000,TextAnchor.MiddleCenter,new Color(.10f,.105f,.105f));
            canvas.GetComponentInChildren<Text>().font=Resources.Load<Font>("Fonts/Desktop-Regular");
        }
        static RectTransform WorldCanvas(Transform parent,string name,Vector2 size,float scale)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Canvas)); var rect=go.GetComponent<RectTransform>();
            rect.SetParent(parent,false); rect.sizeDelta=size; rect.localPosition=new Vector3(0,0,-.018f); rect.localScale=Vector3.one*scale;
            go.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace; return rect;
        }
        static void Letter(RectTransform parent,string value,int fontSize,Vector2 position,Vector2 size,TextAnchor alignment,Color color)
        {
            var go=new GameObject(value,typeof(RectTransform),typeof(Text)); var rect=go.GetComponent<RectTransform>(); rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=new Vector2(0,.5f); rect.pivot=new Vector2(0,.5f); rect.anchoredPosition=position; rect.sizeDelta=size;
            var text=go.GetComponent<Text>(); text.font=Resources.Load<Font>("Fonts/Desktop-Medium"); text.text=value; text.fontSize=fontSize;
            text.alignment=alignment; text.color=color; text.raycastTarget=false; text.horizontalOverflow=HorizontalWrapMode.Overflow;
        }
        internal static void SignagePreview(Vector3 position,Vector3 target,string path,float fov)
        {
            foreach(var text in Object.FindObjectsByType<Text>())
            {
                text.font.RequestCharactersInTexture(text.text,text.fontSize,text.fontStyle); text.SetAllDirty();
            }
            Canvas.ForceUpdateCanvases();
            var view=new GameObject("Temporary_SignagePreview"); var camera=view.AddComponent<Camera>();
            camera.transform.position=position; camera.transform.LookAt(target); camera.fieldOfView=fov; camera.nearClipPlane=.03f;
            OfficePresentationBuilder.ConfigureCamera(camera);
            var render=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32); render.Create();
            var previous=RenderTexture.active; Texture2D image=null;
            try
            {
                RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest {destination=render});
                RenderTexture.active=render; image=new Texture2D(1600,900,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,1600,900),0,0); image.Apply();
                File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=previous; render.Release();
                if(image) Object.DestroyImmediate(image); Object.DestroyImmediate(render); Object.DestroyImmediate(view);
            }
        }
        public static void Audit()
        {
            EditorSceneManager.OpenScene(Scene);
            var lines=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Where(x=>x.name.StartsWith("A") || x.name.StartsWith("T28") || x.name.StartsWith("T31"))
                .OrderBy(x=>x.name).Select(x=>x.name+"\t"+x.bounds.center.ToString("F3")+"\t"+x.bounds.size.ToString("F3")+
                    "\t"+string.Join(";",x.sharedMaterials.Select(m=>m?m.name:"missing"))).ToList();
            foreach(var door in Object.FindObjectsByType<OfficeDoor>(FindObjectsSortMode.None))
            {
                var col=door.GetComponent<BoxCollider>();
                lines.Add("DOOR "+door.name+" pivot="+door.transform.position+" angle="+door.openAngle+" center="+col.center+" size="+col.size);
                lines.AddRange(door.GetComponentsInChildren<MeshRenderer>().Select(x=>"  MOVING "+x.name));
            }
            Directory.CreateDirectory("Logs"); File.WriteAllLines("Logs/garage_before_audit.tsv",lines);
        }
    }
}
