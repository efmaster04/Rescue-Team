using System.IO;
using System.Linq;
using CompanySim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CompanySimEditor
{
    public sealed class WorkOrderTextureImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if(!assetPath.StartsWith("Assets/_Project/Resources/OfficeIcons/") && !assetPath.StartsWith("Assets/_Project/Resources/OfficeThumbnails/")) return;
            var importer=(TextureImporter)assetImporter; importer.textureType=TextureImporterType.Sprite;
            importer.spriteImportMode=SpriteImportMode.Single; importer.alphaIsTransparency=true; importer.mipmapEnabled=false;
            importer.textureCompression=TextureImporterCompression.Uncompressed; importer.filterMode=FilterMode.Bilinear;
            importer.maxTextureSize=1024; importer.spritePixelsPerUnit=100;
        }
    }
    public static class WorkOrderAssets
    {
        const string Target="Assets/_Project/Resources/OfficeThumbnails";
        public static void PrepareAndValidate() { Prepare(); ProjectValidation.Run(); }
        [MenuItem("Company/Prepare Work Order UI Assets")]
        public static void Prepare()
        {
            Directory.CreateDirectory(Target);
            foreach(var guid in AssetDatabase.FindAssets("t:EquipmentDefinition",new[]{"Assets/_Project/Data/Equipment"}))
            {
                var def=AssetDatabase.LoadAssetAtPath<EquipmentDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                Render(def.visualPrefab,Target+"/"+def.id+".png");
            }
            Render(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Models/Vehicles/Vehicle_ServiceVan_Complete.fbx"),Target+"/vehicle.png");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); AssetDatabase.SaveAssets();
        }
        internal static void Render(GameObject prefab,string path)
        {
            if(!prefab) throw new System.Exception("Missing original model for UI thumbnail: "+path);
            var scene=EditorSceneManager.NewPreviewScene(); var previous=RenderTexture.active;
            RenderTexture target=null; Texture2D result=null;
            try
            {
                var model=Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(model,scene);
                model.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                foreach(var behaviour in model.GetComponentsInChildren<MonoBehaviour>()) behaviour.enabled=false;
                var renderers=model.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();
                var bounds=renderers[0].bounds; foreach(var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                var cameraGO=new GameObject("ThumbnailCamera"); SceneManager.MoveGameObjectToScene(cameraGO,scene);
                var camera=cameraGO.AddComponent<Camera>(); camera.scene=scene; camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(0,0,0,0); camera.orthographic=true; camera.allowHDR=false;
                camera.nearClipPlane=.01f; camera.farClipPlane=100; camera.cullingMask=~0;
                var direction=new Vector3(1,.68f,-1).normalized;
                camera.transform.position=bounds.center+direction*(bounds.size.magnitude+4); camera.transform.LookAt(bounds.center);
                var data=camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing=false; data.renderShadows=false;
                // Fit projected bounds, preserving one common angle and generous crop padding.
                float extent=0;
                foreach(float x in new[]{-1f,1f}) foreach(float y in new[]{-1f,1f}) foreach(float z in new[]{-1f,1f})
                {
                    var point=camera.transform.InverseTransformPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z)));
                    extent=Mathf.Max(extent,Mathf.Abs(point.x),Mathf.Abs(point.y));
                }
                camera.orthographicSize=extent*1.12f;
                Light(scene,new Vector3(45,-35,0),1.55f); Light(scene,new Vector3(28,145,0),.9f);
                target=RenderTexture.GetTemporary(512,512,24,RenderTextureFormat.ARGB32); target.antiAliasing=4;
                camera.targetTexture=target; camera.Render(); RenderTexture.active=target;
                result=new Texture2D(512,512,TextureFormat.RGBA32,false); result.ReadPixels(new Rect(0,0,512,512),0,0); result.Apply();
                File.WriteAllBytes(path,result.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=previous; if(target) RenderTexture.ReleaseTemporary(target);
                if(result) Object.DestroyImmediate(result); EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        static void Light(Scene scene,Vector3 angles,float intensity)
        {
            var go=new GameObject("ThumbnailLight"); SceneManager.MoveGameObjectToScene(go,scene);
            go.transform.rotation=Quaternion.Euler(angles); var light=go.AddComponent<Light>(); light.type=LightType.Directional;
            light.intensity=intensity; light.color=Color.white; light.shadows=LightShadows.None;
        }
    }
}
