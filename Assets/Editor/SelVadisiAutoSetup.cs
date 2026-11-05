using System.IO;
using SelVadisi.EditorBuild;
using UnityEditor;
using UnityEngine;

// Sel Vadisi haritası kodla üretilir ve depoya girmez (üretilen meshler ~220 MB). Proje bir bilgisayarda ilk açıldığında harita
// yoksa bir kez sorulur; "Üret" haritayı kurar ve saha görevine çevirir (SelVadisiMissionSetup, yaklaşık 1 dakika).
// Batchmode'da hiçbir şey yapmaz. Elle: Rescue Team > Sel Vadisi > Haritayı Yeniden Üret ve Görevi Kur.
[InitializeOnLoad]
static class SelVadisiAutoSetup
{
    private const string AskedKey = "SelVadisi.AutoSetup.Asked";

    static SelVadisiAutoSetup()
    {
        if (Application.isBatchMode) return;
        EditorApplication.delayCall += Ask;
    }

    private static void Ask()
    {
        if (File.Exists(SVBuild.ScenePath) || SessionState.GetBool(AskedKey, false)) return;
        SessionState.SetBool(AskedKey, true);
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorUtility.DisplayDialog("Sel Vadisi",
                "Sel Vadisi saha görevinin haritası bu bilgisayarda henüz üretilmemiş (kodla üretilir, depoda yer kaplamaz).\n\nŞimdi üretilsin mi? Yaklaşık 1 dakika sürer; açık sahnedeki kaydedilmemiş değişiklikler için önce sorulur.",
                "Üret", "Sonra"))
        {
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            SelVadisiMissionSetup.Apply(true);
        }
    }
}
