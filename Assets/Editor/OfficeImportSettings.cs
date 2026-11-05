using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

// Çok odalı ofis FBX'lerine (build_office.py çıktısı) import ayarlarını uygular. Desen: Bolum1ImportSettings.
// - Model: ölçek 1, Convert Units açık, Bake Axis Conversion kapalı, animasyon yok.
// - Materyal (FBX'e gömülü, URP Lit): renk FBX'teki lineer DiffuseColor'dan gelir (URP okuyucusu doğru çeviriyor, dokunulmaz).
//   Pürüzlülük / metalik / alfa office_materials.json paletinden yazılır (Blender render'ıyla aynı değerler).
//   "Glass" (pencere camı) şeffaf; M_LightDiffuser ışıldar; M_CityMap'e şehir haritası dokusu bağlanır.
internal class OfficeImportSettings : AssetPostprocessor
{
    public const string Folder = "Assets/_Project/Art/Models/Environment/Office/";
    public const string PalettePath = Folder + "office_materials.json";
    public const string CityMapTexturePath = "Assets/_Project/Art/Models/Environment/Environment_OfficeGarage.fbm/O26_city_map.png";
    public const string AppliedMarker = "OfficeDefaults:v1";
    public const float DiffuserEmission = 1.6f; // build_office.py: Emission Strength 1.6

    private static Dictionary<string, Vector3> palette; // rough, metal, alpha

    public static bool IsOfficeModel(string path)
    {
        return path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) && path.StartsWith(Folder);
    }

    // v1: ilk sürüm.
    public override uint GetVersion() { return 1; }

    private void OnPreprocessModel()
    {
        if (!IsOfficeModel(assetPath)) return;
        var importer = (ModelImporter)assetImporter;
        if (importer.userData == AppliedMarker) return;
        Apply(importer);
        importer.userData = AppliedMarker;
    }

    public static void Apply(ModelImporter importer)
    {
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.bakeAxisConversion = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importAnimation = false;
        importer.importBlendShapes = false;
    }

    public static bool IsApplied(ModelImporter importer)
    {
        return importer.userData == AppliedMarker && importer.globalScale == 1f && importer.useFileScale
            && !importer.bakeAxisConversion && importer.animationType == ModelImporterAnimationType.None && !importer.importAnimation;
    }

    // Paleti bir kez okur: {"materials": {"M_Oak": {"rough": .., "metal": .., "alpha": ..}, ...}} (JsonUtility sözlük okuyamıyor)
    public static Dictionary<string, Vector3> Palette()
    {
        if (palette != null) return palette;
        palette = new Dictionary<string, Vector3>();
        if (!File.Exists(PalettePath)) return palette;
        var text = File.ReadAllText(PalettePath);
        var rx = new Regex("\"([A-Za-z0-9_]+)\"\\s*:\\s*\\{\\s*\"rough\"\\s*:\\s*([0-9.]+)\\s*,\\s*\"metal\"\\s*:\\s*([0-9.]+)\\s*,\\s*\"alpha\"\\s*:\\s*([0-9.]+)\\s*\\}");
        foreach (Match m in rx.Matches(text))
        {
            palette[m.Groups[1].Value] = new Vector3(
                float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture));
        }
        return palette;
    }

    // URP'nin FBX materyal okuyucusundan (-980) sonra çalışır; renk bırakılır, yüzey parametreleri palete göre yazılır.
    private void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
    {
        if (!IsOfficeModel(assetPath) || material == null) return;
        string name = description.materialName;
        var pal = Palette();
        if (pal.TryGetValue(name, out var p))
        {
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", Mathf.Clamp01(1f - p.x));
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", Mathf.Clamp01(p.y));
            if (p.z < 0.999f && material.HasProperty("_BaseColor"))
            {
                Color c = material.GetColor("_BaseColor"); c.a = p.z;
                MakeTransparent(material);
                material.SetColor("_BaseColor", c);
                if (material.HasProperty("_Color")) material.SetColor("_Color", c);
            }
        }
        if (name == "Glass" && !(pal.ContainsKey(name) && pal[name].z < 0.999f) && material.HasProperty("_BaseColor"))
        {
            // Palet yoksa da pencere camı şeffaf kalsın
            Color c = material.GetColor("_BaseColor"); c.a = 0.32f;
            MakeTransparent(material);
            material.SetColor("_BaseColor", c);
        }
        if (name == "M_LightDiffuser" && material.HasProperty("_EmissionColor"))
        {
            Color baseColor = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;
            material.SetColor("_EmissionColor", new Color(baseColor.r, baseColor.g, baseColor.b, 1f) * DiffuserEmission);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        if (name == "M_CityMap" && material.HasProperty("_BaseMap"))
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(CityMapTexturePath);
            if (tex != null)
            {
                material.SetTexture("_BaseMap", tex);
                material.SetColor("_BaseColor", Color.white); // doku rengi olduğu gibi görünsün
                context.DependsOnSourceAsset(CityMapTexturePath);
            }
            else Debug.LogWarning("OfficeImportSettings: şehir haritası dokusu bulunamadı: " + CityMapTexturePath);
        }
    }

    // URP Lit'i alfa karışımlı şeffaf yüzeye çevirir (Bolum1ImportSettings ile aynı).
    public static void MakeTransparent(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.SetShaderPassEnabled("DepthOnly", false);
        m.SetShaderPassEnabled("ShadowCaster", false); // cam gölge düşürmesin
        m.renderQueue = (int)RenderQueue.Transparent;
    }
}
