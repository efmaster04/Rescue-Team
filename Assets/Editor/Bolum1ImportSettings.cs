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
    // v5: renk telafisi kaldırıldı (FBX'ler artık lineer DiffuseColor taşıyor), yalnız cam şeffaflığı kaldı.
    public override uint GetVersion() { return 5; }

    public const string PaintMaterial = "M_Minivan_Paint";
    public const string GlassMaterial = "M_Minivan_Glass";
    public const float GlassAlpha = 0.35f;

    // Minibüs camı (FBX'e gömülü, URP Lit): Transparent yüzey, alfa 0.35 (minivan_meta.json glass_alpha 0.34).
    // Renk: Blender FBX'e lineer DiffuseColor yazar (#9AC4D2 → 0.323, 0.552, 0.644); URP'nin FBX okuyucusu
    // (FBXMaterialDescriptionPreprocessor) bunu lineer kabul edip sRGB'ye çevirir, sonuç doğrudan beklenen hex olur.
    // Bu yüzden renge dokunulmaz; yalnızca URP'nin yazdığı rengin alfası değiştirilir. Ölçüm: Bolum1ColorProbe.
    // Not: Materyal açıklaması modunda OnPostprocessMaterial çağrılmıyor (ölçüldü); bu yüzden OnPreprocessMaterialDescription.
    // Sıra: URP'ninki -980, bizimki varsayılan 0, yani URP'den SONRA çalışır.
    private void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
    {
        if (!assetPath.StartsWith(VehicleFolder) || description.materialName != GlassMaterial || !material.HasProperty("_BaseColor"))
            return;

        Color c = material.GetColor("_BaseColor");
        c.a = GlassAlpha;
        MakeTransparent(material);
        material.SetColor("_BaseColor", c);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", c);
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
