using System.IO;
using UnityEditor;
using UnityEngine;

// Mixamo_Running klibinden, kol gömülmesi düzeltilmiş türetilmiş klip üretir:
// Assets/Animations/RescueCrew/Derived/Mixamo_Running_ArmFix.anim
// Chibi oranlarda koşarken kol göğse giriyordu. Düzeltme avatarda değil klipte yapılır: "Left/Right Arm Down-Up"
// kas eğrilerinin negatif (kolu gövdeye indirme) tarafına yumuşak bir taban konur, diğer her şey kaynakla aynı kalır.
// Kaynak FBX yeniden import edilince klip otomatik yeniden üretilir; elle: menü "RescueCrew > Running ArmFix Üret".
public static class RescueCrewClipFixes
{
    private const string SourcePath = "Assets/Animations/RescueCrew/Mixamo_Running.fbx";
    private const string DerivedFolder = "Assets/Animations/RescueCrew/Derived";
    public const string DerivedPath = DerivedFolder + "/Mixamo_Running_ArmFix.anim";

    // Kas değeri (-1..1) bu eşiğe kadar aynen kalır; daha negatifse yumuşakça CapValue'ya doğru sıkışır (asla geçmez).
    // CapValue 0.20 => kol, varsayılan limitte yaklaşık -12 derece (-1 = -60 derece) ötesine inmez.
    // Tarama (Running, göğüs/kalça en derin gömülme): cap 0.40: 3.8/3.8 cm, 0.30: 4.7/4.8, 0.25: 0/5.5, 0.20: 0/0.3 cm.
    internal static float KneeValue = 0.10f;
    internal static float CapValue = 0.20f;

    private static readonly string[] ArmDownUpProperties = { "Left Arm Down-Up", "Right Arm Down-Up" };

    // Tabanlı değer: m >= -knee ise m; değilse -knee - (cap-knee) * tanh((-m-knee)/(cap-knee))
    public static float SoftFloor(float m, float knee, float cap)
    {
        if (m >= -knee) return m;
        float headroom = cap - knee;
        return -knee - headroom * (float)System.Math.Tanh((-m - knee) / headroom);
    }

    [MenuItem("RescueCrew/Running ArmFix Üret")]
    public static void GenerateRunningArmFix()
    {
        AnimationClip source = null;
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(SourcePath))
            if (asset is AnimationClip c && !c.name.StartsWith("__preview__")) source = c;
        if (source == null)
        {
            Debug.LogError("RescueCrewClipFixes: kaynak klip bulunamadı: " + SourcePath);
            return;
        }

        AnimationClip copy = Object.Instantiate(source);
        copy.name = "Mixamo_Running_ArmFix";
        copy.frameRate = source.frameRate;
        copy.wrapMode = source.wrapMode;
        // Loop ve kök kilit ayarları (Bake Into Pose karşılıkları) kaynaktan aynen alınır
        AnimationUtility.SetAnimationClipSettings(copy, AnimationUtility.GetAnimationClipSettings(source));

        int frames = Mathf.Max(2, Mathf.RoundToInt(source.length * source.frameRate));
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
        {
            if (binding.type != typeof(Animator) || System.Array.IndexOf(ArmDownUpProperties, binding.propertyName) < 0)
                continue;

            AnimationCurve original = AnimationUtility.GetEditorCurve(source, binding);
            var fixedCurve = new AnimationCurve();
            // Orijinal eğri her karede örneklenir, tabanlanır ve yeniden yazılır
            for (int i = 0; i <= frames; i++)
            {
                float t = Mathf.Min(source.length, i / source.frameRate);
                fixedCurve.AddKey(new Keyframe(t, SoftFloor(original.Evaluate(t), KneeValue, CapValue)));
            }
            for (int i = 0; i < fixedCurve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(fixedCurve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(fixedCurve, i, AnimationUtility.TangentMode.ClampedAuto);
            }
            AnimationUtility.SetEditorCurve(copy, binding, fixedCurve);
        }

        if (!AssetDatabase.IsValidFolder(DerivedFolder))
            AssetDatabase.CreateFolder("Assets/Animations/RescueCrew", "Derived");

        // Varsa aynı asset'in üstüne yazılır; GUID sabit kalır, controller referansı bozulmaz
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(DerivedPath);
        if (existing != null)
        {
            EditorUtility.CopySerialized(copy, existing);
            Object.DestroyImmediate(copy);
            EditorUtility.SetDirty(existing);
        }
        else
        {
            AssetDatabase.CreateAsset(copy, DerivedPath);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("RescueCrewClipFixes: Mixamo_Running_ArmFix üretildi (knee=" + KneeValue + ", cap=" + CapValue + ")");
    }
}

// Kaynak Running FBX'i yeniden import edilince türetilmiş klibi tazeler.
internal class RescueCrewClipFixesWatcher : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        foreach (string path in imported)
        {
            if (path == "Assets/Animations/RescueCrew/Mixamo_Running.fbx")
            {
                EditorApplication.delayCall += RescueCrewClipFixes.GenerateRunningArmFix;
                return;
            }
        }
    }
}
