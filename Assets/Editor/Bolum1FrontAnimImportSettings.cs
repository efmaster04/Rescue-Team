using UnityEditor;
// Ortak importer sahiplik dışında: yalnız yeni klibin döngüsünü tamamlar.
public sealed class Bolum1FrontAnimImportSettings : AssetPostprocessor
{
    public const string ClipName = "Carry_Stretcher_Front_Walk";
    public override int GetPostprocessOrder() => 100;
    private void OnPreprocessAnimation()
    {
        if (assetPath != ProceduralAnimImportSettings.ClipPath(ClipName)) return;
        context.DependsOnArtifact(ProceduralAnimImportSettings.SourceAvatarPath);
        var importer = (ModelImporter)assetImporter;
        var clips = importer.clipAnimations;
        foreach (var clip in clips) { clip.loopTime = true; clip.loopPose = true; }
        importer.clipAnimations = clips;
    }
}
