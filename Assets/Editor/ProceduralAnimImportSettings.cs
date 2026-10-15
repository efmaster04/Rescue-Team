using System.Linq;
using UnityEditor;
using UnityEngine;

// Prosedürel animasyon klipleri (Assets/Animations/Procedural, 11 FBX) için README'deki import önerileri.
// Kaynak: sanat reposu Export/Animations/Procedural/README.md (kopyası aynı klasörde).
//  - Humanoid, Avatar = Copy From Other (SK_RescueCrew_Blue'nun Avatar'ı; aynı 31 kemikli iskelet).
//  - Scale 1, Convert Units açık, Bake Axis Conversion kapalı.
//  - Döngüler (Loop Time + Loop Pose): Carry_Stretcher_Walk, Carry_OneHand_Walk, Revive_Helper, Lying_HelpWave, Lying_Groan.
//  - Bütün kliplerde Root Transform Rotation / Position (Y) / Position (XZ) pozun içine gömülü, Based Upon = Original
//    (GetUp_FromBack'te kalça yataydan dikeye döner; rotation gömülmezse Unity bütün objeyi devirir).
//  - İSTİSNA Jump: Position (Y) Based Upon = Feet. Oyuncu CharacterController ile gerçekten zıplıyor; klipteki kalça
//    yüksekliği de eklenirse karakter iki kat zıplar. Feet ile klibin çömelme/kol pozu kalır, yüksekliği fizik verir.
// Ayarlar her importta yazılır (Inspector'dan elle ayar beklenmez); RescueCrewImportSettings'e dokunulmaz.
internal class ProceduralAnimImportSettings : AssetPostprocessor
{
    public const string Folder = "Assets/Animations/Procedural/";
    public const string SourceAvatarPath = "Assets/Models/Characters/RescueCrew/SK_RescueCrew_Blue.fbx";

    public static readonly string[] ClipNames =
    {
        "Carry_Stretcher_Walk", "Carry_OneHand_Walk", "PickUp", "PutDown", "GetUp_FromBack", "Revive_Helper",
        "Jump", "Startled", "Lying_HelpWave", "Lying_Groan", "Celebrate",
    };

    public static readonly string[] LoopClips = { "Carry_Stretcher_Walk", "Carry_OneHand_Walk", "Revive_Helper", "Lying_HelpWave", "Lying_Groan" };

    public static string ClipPath(string clip) { return Folder + clip + ".fbx"; }
    public static bool IsLoop(string clip) { return LoopClips.Contains(clip); }

    public static AnimationClip LoadClip(string clip)
    {
        return AssetDatabase.LoadAllAssetsAtPath(ClipPath(clip)).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }

    // Sürüm: ayar değişirse artır, Unity klasördeki FBX'leri yeniden import eder.
    public override uint GetVersion() { return 2; }

    // Fizik zıplamasıyla birlikte oynayan klipler: yükseklik ayaklara göre (Feet).
    public static bool HeightFromFeet(string clip) { return clip == "Jump"; }

    private bool IsOurs { get { return assetPath.StartsWith(Folder) && assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase); } }

    private void OnPreprocessModel()
    {
        if (!IsOurs) return;
        var importer = (ModelImporter)assetImporter;
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.bakeAxisConversion = false;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.importAnimation = true;
        importer.importBlendShapes = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None; // yalnız iskelet + 1 mm bağ çapası
        var source = AssetDatabase.LoadAssetAtPath<Avatar>(SourceAvatarPath);
        if (source != null)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = source;
        }
        else
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            Debug.LogWarning("ProceduralAnimImportSettings: " + SourceAvatarPath + " bulunamadı; " + assetPath + " kendi Avatar'ıyla import edildi.");
        }
    }

    private void OnPreprocessAnimation()
    {
        if (!IsOurs) return;
        var importer = (ModelImporter)assetImporter;
        string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        bool loop = IsLoop(name);
        var clips = importer.defaultClipAnimations;
        foreach (var clip in clips)
        {
            clip.name = name;
            clip.loopTime = loop;
            clip.loopPose = loop;
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;
            clip.keepOriginalOrientation = true;
            clip.keepOriginalPositionY = !HeightFromFeet(name);
            clip.heightFromFeet = HeightFromFeet(name);
            clip.keepOriginalPositionXZ = true;
        }
        importer.clipAnimations = clips;
    }

    // Doğrulama: importer ayarları README ile aynı mı?
    public static bool IsApplied(ModelImporter importer, out string detail)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(importer.assetPath);
        var clip = importer.clipAnimations.FirstOrDefault();
        bool ok = importer.animationType == ModelImporterAnimationType.Human && importer.globalScale == 1f && importer.useFileScale && !importer.bakeAxisConversion
            && importer.avatarSetup == ModelImporterAvatarSetup.CopyFromOther && importer.sourceAvatar != null
            && clip != null && clip.loopTime == IsLoop(name) && clip.lockRootRotation && clip.lockRootHeightY && clip.lockRootPositionXZ
            && clip.keepOriginalOrientation && clip.keepOriginalPositionY == !HeightFromFeet(name) && clip.keepOriginalPositionXZ;
        detail = importer.animationType + ", avatar " + importer.avatarSetup + (importer.sourceAvatar != null ? "(" + importer.sourceAvatar.name + ")" : "")
            + ", loop " + (clip != null && clip.loopTime) + ", bake R/Y/XZ " + (clip != null && clip.lockRootRotation && clip.lockRootHeightY && clip.lockRootPositionXZ);
        return ok;
    }
}
