using System.Linq;
using UnityEditor;
using UnityEngine;

// Prosedürel animasyon klipleri (Assets/Animations/Procedural, 24 FBX; r3 ile 13 yeni) için README'deki import önerileri.
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
        // r3: minibüsten çıkarma, halat, sırtta taşıma
        "Patient_SitDazed", "Patient_SitDazed_ArmSling", "Patient_DraggedOut", "Piggyback_Rider", "Extract_Pull",
        "Rope_Descend", "Rope_Climb", "Rope_Hang_Idle", "Piggyback_PickUp", "Piggyback_Rope_Climb", "Piggyback_Walk",
        "Piggyback_PutDown", "Rope_Pull",
        // U2 (r4): halat zinciri — dolaptan kangal alma, kangal taşıma, kazığa bağlama, atma
        "Cabinet_TakeCoil", "Carry_Coil_Idle", "Carry_Coil_Walk", "Rope_TieStake", "Rope_Throw",
        // U3 (r4b): darbe tepkisi (can sistemi)
        "HitReact",
        // U4 (r4b): oyuncu savunması ve hırsız
        "Push_Shove", "Tackle_Dive", "Tackle_GetUp", "Throw_Aim", "Throw_Release", "Whistle_Blow",
        "Thief_Sneak_Walk", "Thief_Run", "Thief_Run_Carry", "Thief_Grab", "Thief_Punch", "Thief_Stunned", "Thief_ThrowInVan",
    };

    public static readonly string[] LoopClips = { "Carry_Stretcher_Walk", "Carry_OneHand_Walk", "Revive_Helper", "Lying_HelpWave", "Lying_Groan",
        "Patient_SitDazed", "Patient_SitDazed_ArmSling", "Piggyback_Rider", "Rope_Descend", "Rope_Climb", "Rope_Hang_Idle",
        "Piggyback_Rope_Climb", "Piggyback_Walk", "Rope_Pull", "Carry_Coil_Idle", "Carry_Coil_Walk",
        // U4 (README r4b): yalnız bunlar döngü
        "Throw_Aim", "Thief_Sneak_Walk", "Thief_Run", "Thief_Run_Carry", "Thief_Stunned" };

    public static string ClipPath(string clip) { return Folder + clip + ".fbx"; }
    public static bool IsLoop(string clip) { return LoopClips.Contains(clip); }

    public static AnimationClip LoadClip(string clip)
    {
        return AssetDatabase.LoadAllAssetsAtPath(ClipPath(clip)).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }

    // Sürüm: ayar değişirse artır, Unity klasördeki FBX'leri yeniden import eder.
    public override uint GetVersion() { return 3; }

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
        // Kök neden (D2 madde 7): temiz import'ta (Library yok) klasördeki bazı klipler SK_RescueCrew_Blue'dan ÖNCE
        // import ediliyordu; kaynak Avatar bulunamayınca klip kendi Avatar'ıyla (kol limitleri 0) import ediliyor ve bu
        // ayar .meta'ya yazılıyordu, kaynak sonradan gelince de yeniden import olmuyordu. Bağımlılık bildirimi kaynağın
        // önce import edilmesini ve kaynak değişince klibin yeniden import edilmesini sağlar.
        context.DependsOnArtifact(SourceAvatarPath);
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
