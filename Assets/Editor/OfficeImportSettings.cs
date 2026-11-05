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
// - v2 (ofis güzelleştirme): düz renkli yüzeylere URP Lit ayrıntı haritası (Detail Albedo ×2 + Detail Normal) bağlanır.
//   Ayrıntı dokuları 0.5 gri merkezlidir (Art/OfficeDetail/Textures), yani palet rengi değişmez; sıva, ahşap damarı, kumaş,
//   toz boya, fırçalı çelik, karton, kauçuk ve beton dokusu üstüne biner. UV'ler 0.5/m kutu izdüşümü (build_office.py Geo.finish).
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

    // v1: ilk sürüm. v2: ayrıntı haritaları (DetailFor).
    public override uint GetVersion() { return 2; }

    public const string DetailFolder = "Assets/_Project/Art/OfficeDetail/Textures/";
    public const string GarageTextures = "Assets/_Project/Art/Garage/Textures/";
    public const string CraftTextures = "Assets/_Project/Art/WorkshopCraft/Textures/";

    // Malzeme adı → (ayrıntı albedo, ayrıntı normal ya da null, döşeme sıklığı, albedo gücü, normal gücü)
    private static readonly Dictionary<string, (string albedo, string normal, float tiling, float albedoScale, float normalScale)> details =
        new Dictionary<string, (string, string, float, float, float)>
        {
            { "M_Plaster", ("OfficeDetail_Plaster", GarageTextures + "Plaster_Normal.png", 2.5f, 1f, 0.22f) },
            { "M_PlasterLight", ("OfficeDetail_Plaster", GarageTextures + "Plaster_Normal.png", 2.5f, 0.6f, 0.15f) },
            { "M_WallBlue", ("OfficeDetail_Paint", GarageTextures + "Paint_Normal.png", 2f, 1f, 0.3f) },
            { "M_MutedBlue", ("OfficeDetail_Paint", GarageTextures + "Paint_Normal.png", 2f, 1f, 0.3f) },
            { "M_CobaltBlue", ("OfficeDetail_Paint", GarageTextures + "Paint_Normal.png", 2f, 1f, 0.3f) },
            { "M_RingBlue", ("OfficeDetail_Fabric", CraftTextures + "Cloth_Normal.png", 4f, 0.9f, 0.4f) },
            { "M_Oak", ("OfficeDetail_WoodFloor", null, 2.5f, 1f, 0f) },
            { "M_OakLight", ("OfficeDetail_WoodFloor", null, 2.5f, 1f, 0f) },
            { "M_Timber", ("OfficeDetail_WoodFloor", null, 2.5f, 1f, 0f) },
            { "M_OakDark", ("OfficeDetail_Wood", null, 3f, 1f, 0f) },
            { "M_TimberEdge", ("OfficeDetail_Wood", null, 3f, 0.8f, 0f) },
            { "M_BeigeFabric", ("OfficeDetail_Fabric", CraftTextures + "Cloth_Normal.png", 6f, 1f, 0.5f) },
            { "M_BeigeCushion", ("OfficeDetail_Fabric", CraftTextures + "Cloth_Normal.png", 6f, 1f, 0.5f) },
            { "M_SoftBeige", ("OfficeDetail_Fabric", CraftTextures + "Cloth_Normal.png", 6f, 0.8f, 0.4f) },
            { "M_NavyCanvas", ("OfficeDetail_Fabric", CraftTextures + "Cloth_Normal.png", 5f, 1f, 0.5f) },
            { "M_FilingGreen", ("OfficeDetail_PowderCoat", GarageTextures + "PowderCoat_Normal.png", 3f, 1f, 0.3f) },
            { "M_TealMetal", ("OfficeDetail_PowderCoat", GarageTextures + "PowderCoat_Normal.png", 3f, 1f, 0.3f) },
            { "M_Anthracite", ("OfficeDetail_PowderCoat", GarageTextures + "PowderCoat_Normal.png", 3f, 1f, 0.3f) },
            { "M_WarmGrayMetal", ("OfficeDetail_PowderCoat", GarageTextures + "PowderCoat_Normal.png", 3f, 1f, 0.3f) },
            { "M_GalvanizedSteel", ("OfficeDetail_Steel", GarageTextures + "BrushedSteel_Normal.png", 3f, 1f, 0.35f) },
            { "M_SilverSteel", ("OfficeDetail_Steel", GarageTextures + "BrushedSteel_Normal.png", 3f, 1f, 0.35f) },
            { "M_ToolSteel", ("OfficeDetail_Steel", GarageTextures + "BrushedSteel_Normal.png", 3f, 0.8f, 0.3f) },
            { "M_Fibreboard", ("OfficeDetail_Cardboard", CraftTextures + "Cardboard_Normal.png", 3f, 1f, 0.4f) },
            { "M_DarkRubber", ("OfficeDetail_Rubber", GarageTextures + "GripRubber_Normal.png", 4f, 1f, 0.45f) },
            { "M_CharcoalABS", ("OfficeDetail_Plastic", GarageTextures + "MouldedPlastic_Normal.png", 4f, 1f, 0.3f) },
            { "M_Concrete", ("OfficeDetail_Concrete", null, 1.5f, 1f, 0f) },
        };

    // Ayrıntı haritası: palet rengi korunur (×2 çarpımı 0.5 gride nötr), yüzeye doku biner.
    private void ApplyDetail(string name, Material material)
    {
        if (!details.TryGetValue(name, out var d) || !material.HasProperty("_DetailAlbedoMap")) return;
        string albedoPath = DetailFolder + d.albedo + ".png";
        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
        context.DependsOnSourceAsset(albedoPath);
        if (albedo == null) { Debug.LogWarning("OfficeImportSettings: ayrıntı dokusu yok: " + albedoPath); return; }
        material.SetTexture("_DetailAlbedoMap", albedo);
        material.SetTextureScale("_DetailAlbedoMap", new Vector2(d.tiling, d.tiling));
        material.SetFloat("_DetailAlbedoMapScale", d.albedoScale);
        if (d.normal != null && material.HasProperty("_DetailNormalMap"))
        {
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(d.normal);
            context.DependsOnSourceAsset(d.normal);
            if (normal != null)
            {
                material.SetTexture("_DetailNormalMap", normal);
                material.SetFloat("_DetailNormalMapScale", d.normalScale);
            }
        }
        if (material.HasProperty("_DetailMask")) material.SetTexture("_DetailMask", null);
        material.EnableKeyword("_DETAIL_MULX2");
    }

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
        ApplyDetail(name, material);
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
