using UnityEditor;

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
