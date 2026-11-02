using System.IO;
using System.Linq;
using CompanySim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CompanySimEditor
{
    public static class OfficePresentationBuilder
    {
        const string Folder="Assets/_Project/Art/Lighting";
        const string Scene="Assets/_Project/Scenes/OfficeGarage.unity";

        [MenuItem("Company/Apply Lighting and Presentation")]
        public static void Apply()
        {
            EditorSceneManager.OpenScene(Scene);
            ConfigureLighting();
            var player=Object.FindAnyObjectByType<PlayerInteractor>();
            ConfigureCamera(player.viewCamera);
            ConfigureAudio(Object.FindAnyObjectByType<AudioFeedback>());
            VanRestorationBuilder.ConfigureScene();
            GarageRefinementBuilder.ConfigureDoors();
            GarageRefinementBuilder.ConfigureGarage();
            BakeReflections();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }
        public static void ApplyAndValidate() { Apply(); ProjectValidation.Run(); }

        public static void ConfigureCamera(Camera camera)
        {
            var data=camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing=true;
            data.antialiasing=AntialiasingMode.FastApproximateAntialiasing;
            data.antialiasingQuality=AntialiasingQuality.Medium;
            data.stopNaN=true; data.dithering=true;
            camera.allowHDR=true;
        }
        public static void ConfigureAudio(AudioFeedback feedback)
        {
            feedback.uiClick=feedback.notification=feedback.garageMotor=feedback.itemDrop=null;
            feedback.footstep=feedback.doorOpen=feedback.doorClose=feedback.drawerSlide=null;
            foreach(var source in feedback.GetComponents<AudioSource>()) Object.DestroyImmediate(source);
            EditorUtility.SetDirty(feedback);
        }
        public static void ConfigureLighting()
        {
            Directory.CreateDirectory(Folder);
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.40f,.46f,.51f);
            RenderSettings.ambientEquatorColor=new Color(.30f,.32f,.33f);
            RenderSettings.ambientGroundColor=new Color(.15f,.16f,.17f);
            RenderSettings.reflectionIntensity=.75f;
            var sun=Light("Sun_Directional",LightType.Directional,new Vector3(0,6,0),.9f,0,Color.white);
            sun.transform.rotation=Quaternion.Euler(42,-32,0); sun.color=new Color(1,.97f,.91f);
            sun.shadows=LightShadows.Soft; sun.shadowStrength=.78f; sun.shadowBias=.04f; sun.shadowNormalBias=.3f;
            RenderSettings.sun=sun;
            Spot("Office_WarmLight",new Vector3(-2.65f,2.83f,-.05f),4.2f,6,new Color(1,.91f,.79f));
            Spot("Garage_FrontLight",new Vector3(3.25f,3.65f,-1.55f),9.0f,9,new Color(.90f,.95f,1));
            Spot("Garage_RearLight",new Vector3(7.1f,3.48f,2.4f),8.0f,8,new Color(.95f,.97f,1));
            var root=GameObject.Find("09_PresentationLighting");
            if(root) Object.DestroyImmediate(root);
            root=new GameObject("09_PresentationLighting");
            var fill=Light("Office_DaylightFill",LightType.Point,new Vector3(-2.4f,1.9f,-4.3f),1.2f,5,new Color(.73f,.85f,1));
            fill.transform.SetParent(root.transform,true);
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(Folder+"/OfficeGarageGrade.asset");
            if(!profile)
            {
                profile=ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile,Folder+"/OfficeGarageGrade.asset");
            }
            var tone=Component<Tonemapping>(profile); tone.mode.Override(TonemappingMode.Neutral);
            var color=Component<ColorAdjustments>(profile);
            color.postExposure.Override(.15f); color.contrast.Override(7); color.saturation.Override(-3);
            var bloom=Component<Bloom>(profile); bloom.threshold.Override(1.2f); bloom.intensity.Override(.12f); bloom.scatter.Override(.45f);
            var volume=root.AddComponent<Volume>(); volume.isGlobal=true; volume.priority=10; volume.sharedProfile=profile;
            ConfigureFixtureEmission();
            Probe(root.transform,"OfficeReflection",new Vector3(-2.45f,1.45f,-2.1f),new Vector3(4.6f,3.1f,6.1f));
            Probe(root.transform,"GarageReflection",new Vector3(4.65f,1.7f,.15f),new Vector3(9.0f,3.8f,10));
            EditorUtility.SetDirty(profile);
        }
        static void ConfigureFixtureEmission()
        {
            var environment=GameObject.Find("01_Environment_OfficeGarage");
            foreach(var renderer in environment.GetComponentsInChildren<MeshRenderer>())
            {
                bool diffuser=renderer.name.StartsWith("A23") && renderer.name.Contains("diffuser");
                bool wallLens=renderer.name.StartsWith("A22") && renderer.name.Contains("glass lens");
                if(!diffuser && !wallLens) continue;
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)
                {
                    var source=materials[i]; if(!source) continue;
                    string name=source.name.Replace("_FixtureGlow","");
                    string path=Folder+"/"+string.Concat(name.Select(c=>char.IsLetterOrDigit(c)?c:'_'))+"_FixtureGlow.mat";
                    var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(!material) { material=new Material(source); material.name=name+"_FixtureGlow"; AssetDatabase.CreateAsset(material,path); }
                    material.SetColor("_EmissionColor",diffuser?new Color(1.5f,1.42f,1.27f):new Color(.8f,.65f,.4f));
                    material.EnableKeyword("_EMISSION"); EditorUtility.SetDirty(material); materials[i]=material;
                }
                renderer.sharedMaterials=materials;
            }
        }
        static T Component<T>(VolumeProfile profile) where T:VolumeComponent
        {
            if(!profile.TryGet<T>(out var component))
            {
                component=profile.Add<T>(); AssetDatabase.AddObjectToAsset(component,profile);
            }
            component.active=true; EditorUtility.SetDirty(component); return component;
        }
        static Light Light(string name,LightType type,Vector3 position,float intensity,float range,Color color)
        {
            var go=GameObject.Find(name); if(!go) go=new GameObject(name);
            var light=go.GetComponent<Light>(); if(!light) light=go.AddComponent<Light>();
            go.transform.position=position; light.type=type; light.intensity=intensity; light.range=range;
            light.color=color; light.shadows=LightShadows.None; return light;
        }
        static void Spot(string name,Vector3 position,float intensity,float range,Color color)
        {
            var light=Light(name,LightType.Spot,position,intensity,range,color);
            light.transform.rotation=Quaternion.Euler(90,0,0);
            light.spotAngle=135; light.innerSpotAngle=105;
            light.shadows=LightShadows.Soft; light.shadowStrength=.65f;
            light.shadowBias=.025f; light.shadowNormalBias=.25f; light.shadowNearPlane=.08f;
        }
        static void Probe(Transform parent,string name,Vector3 center,Vector3 size)
        {
            var go=new GameObject(name); go.transform.SetParent(parent,false); go.transform.position=center;
            var probe=go.AddComponent<ReflectionProbe>(); probe.size=size; probe.resolution=128;
            probe.mode=ReflectionProbeMode.Baked; probe.boxProjection=true; probe.intensity=.65f;
            probe.clearFlags=ReflectionProbeClearFlags.Skybox; probe.nearClipPlane=.1f; probe.farClipPlane=35;
        }
        public static void BakeReflections()
        {
            // Reflection flags affect capture eligibility only; geometry and placement stay authored.
            foreach(var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if(renderer.transform.GetComponentInParent<CarryableItem>() || renderer.shadowCastingMode==ShadowCastingMode.ShadowsOnly) continue;
                var flags=GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,flags|StaticEditorFlags.ReflectionProbeStatic);
            }
            foreach(var probe in GameObject.Find("09_PresentationLighting").GetComponentsInChildren<ReflectionProbe>())
            {
                string path=Folder+"/"+probe.name+".exr";
                if(!Lightmapping.BakeReflectionProbe(probe,path)) throw new System.Exception("Reflection capture failed: "+probe.name);
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                probe.mode=ReflectionProbeMode.Custom; probe.customBakedTexture=AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                if(!probe.customBakedTexture) throw new System.Exception("Reflection cubemap missing: "+path);
            }
        }
    }
}
