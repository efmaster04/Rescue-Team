using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;

namespace CompanySimEditor
{
    public static class VanRestorationBuilder
    {
        const string Model="Assets/_Project/Art/Models/Vehicles/Vehicle_ServiceVan_Complete.fbx";
        const string Folder="Assets/_Project/Art/Materials/Van/Complete";
        public static void ConfigureScene()
        {
            var vehicle=GameObject.Find("04_Vehicle_ServiceVan_Original");
            if(!vehicle) throw new Exception("Original service van is missing");
            ApplyVisuals(vehicle);
            const string prefabPath="Assets/_Project/Prefabs/Vehicles/Vehicle_ServiceVan_Original.prefab";
            var prefab=PrefabUtility.LoadPrefabContents(prefabPath);
            try { ApplyVisuals(prefab); PrefabUtility.SaveAsPrefabAsset(prefab,prefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }
        public static void ApplyVisuals(GameObject vehicle)
        {
            Directory.CreateDirectory(Folder);
            var importer=AssetImporter.GetAtPath(Model) as ModelImporter;
            if(importer && !importer.isReadable) { importer.isReadable=true; importer.SaveAndReimport(); }
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            if(!model) throw new Exception("Complete original van FBX is missing");
            var artRoots=vehicle.GetComponentsInChildren<MeshRenderer>(true).Select(renderer=>
            {
                var root=renderer.transform;
                while(root.parent && root.parent!=vehicle.transform) root=root.parent;
                return root.gameObject;
            }).Distinct().ToArray();
            foreach(var root in artRoots) UnityEngine.Object.DestroyImmediate(root);
            var art=(GameObject)PrefabUtility.InstantiatePrefab(model);
            art.name="OriginalVan_CompleteShell"; art.transform.SetParent(vehicle.transform,false);
            art.transform.localPosition=Vector3.zero; art.transform.localRotation=Quaternion.identity;
            foreach(var renderer in art.GetComponentsInChildren<MeshRenderer>())
            {
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++) materials[i]=Paint(materials[i]);
                renderer.sharedMaterials=materials;
            }
        }
        static Material Paint(Material source)
        {
            string name=source.name;
            string safe=new string(name.Select(c=>char.IsLetterOrDigit(c)?c:'_').ToArray());
            string path=Folder+"/"+safe+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material) {material=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material,path);}
            Color color=new Color(.4f,.42f,.42f); float smooth=.4f,metal=.1f;
            if(name.StartsWith("01 ")) {color=new Color(.76f,.75f,.69f);smooth=.43f;}
            else if(name.StartsWith("02 ")) {color=new Color(.79f,.78f,.73f);smooth=.44f;}
            else if(name.StartsWith("03 ")) color=new Color(.73f,.72f,.67f);
            else if(name.StartsWith("04 ")) color=new Color(.64f,.63f,.58f);
            else if(name.StartsWith("05 ") || name.StartsWith("06 ")) {color=new Color(.085f,.27f,.36f);smooth=.43f;}
            else if(name.StartsWith("07 ") || name.StartsWith("08 ")) {color=new Color(.045f,.050f,.055f);smooth=.12f;metal=0;}
            else if(name.StartsWith("09 ")) {color=new Color(.075f,.085f,.09f);smooth=.30f;metal=0;}
            else if(name.StartsWith("10 ")) {color=new Color(.025f,.030f,.035f);smooth=.12f;metal=0;}
            else if(name.StartsWith("11 ") || name.StartsWith("12 ")) {color=new Color(.52f,.55f,.56f);smooth=.48f;metal=.60f;}
            else if(name.Contains("glass") || name.Contains("Glass")) {color=new Color(.13f,.20f,.235f);smooth=.86f;metal=.12f;}
            else if(name.StartsWith("15 ")) {color=new Color(.86f,.89f,.90f);smooth=.76f;metal=.02f;}
            else if(name.StartsWith("16 ")) {color=new Color(.94f,.35f,.045f);smooth=.55f;metal=0;}
            else if(name.StartsWith("17 ")) {color=new Color(.55f,.035f,.025f);smooth=.55f;metal=0;}
            else if(name.StartsWith("18 ")) {color=new Color(.24f,.25f,.23f);smooth=.06f;metal=0;}
            else if(name.StartsWith("19 ")) {color=new Color(.46f,.46f,.43f);smooth=.20f;metal=0;}
            else if(name.StartsWith("20 ")) {color=new Color(.12f,.14f,.15f);smooth=.09f;metal=0;}
            else if(name.StartsWith("21 ")) {color=new Color(.31f,.34f,.35f);smooth=.32f;metal=.30f;}
            material.SetColor("_BaseColor",color); material.SetFloat("_Metallic",metal); material.SetFloat("_Smoothness",smooth);
            material.SetFloat("_Surface",0); material.SetFloat("_Cull",2); material.SetFloat("_ZWrite",1);
            material.SetTexture("_BaseMap",null); material.SetTexture("_BumpMap",null);
            material.SetColor("_EmissionColor",name.StartsWith("15 ")?new Color(.12f,.13f,.14f):Color.black);
            material.globalIlluminationFlags=name.StartsWith("15 ")?MaterialGlobalIlluminationFlags.BakedEmissive:MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            BaseShaderGUI.SetMaterialKeywords(material,LitGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(material); return material;
        }
    }
}
