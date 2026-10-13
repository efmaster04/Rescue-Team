using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

// Bölüm 1 mekân modülleri ve minibüs FBX'lerine import ayarlarını otomatik uygular.
// Ayarlar her dosyaya bir kez yazılır (işaret importer.userData'da), sonra Inspector'dan yapılan değişiklikler ezilmez.
// importSettingsMissing kopyalanan FBX'lerde güvenilir çıkmadığı için kendi işaretimizi tutuyoruz (bkz. RescueCrewImportSettings).
internal class Bolum1ImportSettings : AssetPostprocessor
{
    public const string EnvironmentFolder = "Assets/Models/Environment/Bolum1/";
    public const string VehicleFolder = "Assets/Models/Vehicles/Minivan/";
    public const string AppliedMarker = "Bolum1Defaults:v1";

    public static bool IsBolum1Model(string path)
    {
        return path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)
            && (path.StartsWith(EnvironmentFolder) || path.StartsWith(VehicleFolder));
    }

    private void OnPreprocessModel()
    {
        if (!IsBolum1Model(assetPath))
            return;

        var importer = (ModelImporter)assetImporter;
        if (importer.userData == AppliedMarker)
            return;

        Apply(importer);
        importer.userData = AppliedMarker;
    }

    // Postprocessor sürümü: materyal düzeltmesi değişirse artır, Unity ilgili FBX'leri yeniden import eder.
    public override uint GetVersion() { return 4; }

    public const string PaintMaterial = "M_Minivan_Paint";
    public const string GlassMaterial = "M_Minivan_Glass";
    public const float GlassAlpha = 0.35f;

    // Minibüs materyalleri (FBX'e gömülü, URP Lit).
    // Renk: Blender export'u FBX'e sRGB değerleri yazdı (#9AC4D2 → 0.604, 0.769, 0.824). URP'nin FBX okuyucusu
    // (FBXMaterialDescriptionPreprocessor) bu sayıyı lineer sayıp bir kez daha sRGB'ye çeviriyor (diffuseColor.gamma);
    // sonuç soluk (#CCE3EA, beklenene ΔE76 ≈ 14). Burada FBX'teki sayı doğrudan sRGB renk olarak yazılır. Ölçüm: Bolum1ColorProbe.
    // Cam: URP'de Transparent yüzey, alfa 0.35 (minivan_meta.json glass_alpha 0.34).
    // Not: Materyal açıklaması modunda OnPostprocessMaterial çağrılmıyor (ölçüldü); bu yüzden OnPreprocessMaterialDescription.
    // Sıra: URP'ninki -980, bizimki varsayılan 0, yani URP'den SONRA çalışıp onun yazdığı rengi ezer.
    private void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
    {
        if (!assetPath.StartsWith(VehicleFolder) || !material.HasProperty("_BaseColor")
            || !description.TryGetProperty("DiffuseColor", out Vector4 diffuse))
            return;

        Color c = material.GetColor("_BaseColor");
        Color fixedColor = new Color(diffuse.x, diffuse.y, diffuse.z, c.a);
        if (description.materialName == GlassMaterial)
        {
            fixedColor.a = GlassAlpha;
            MakeTransparent(material);
        }
        material.SetColor("_BaseColor", fixedColor);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", fixedColor);
    }

    // URP Lit'i alfa karışımlı şeffaf yüzeye çevirir (Inspector'daki Surface Type = Transparent, Blending = Alpha ile aynı ayarlar).
    private static void MakeTransparent(Material m)
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

    // Ayarların kendisi. Bolum1Verify, importer'dan geri okuyup IsApplied ile karşılaştırır.
    public static void Apply(ModelImporter importer)
    {
        importer.globalScale = 1f;
        importer.useFileScale = true;          // Inspector'daki "Convert Units"
        importer.bakeAxisConversion = false;   // Açıkken modeller ters dönüyor (karakterde görüldü)
        // Statik modeller ve minibüs: iskelet/klip yok. None seçilmezse Unity köke gereksiz bir Animator ekler.
        importer.animationType = ModelImporterAnimationType.None;
        importer.importAnimation = false;
    }

    public static bool IsApplied(ModelImporter importer)
    {
        return importer.userData == AppliedMarker
            && importer.globalScale == 1f
            && importer.useFileScale
            && !importer.bakeAxisConversion
            && importer.animationType == ModelImporterAnimationType.None
            && !importer.importAnimation;
    }
}
