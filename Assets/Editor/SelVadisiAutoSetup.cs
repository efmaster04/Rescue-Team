using System.IO;
using System.Linq;
using SelVadisi.EditorBuild;
using UnityEditor;
using UnityEngine;

// Sel Vadisi haritası kodla üretilir ve depoya girmez (üretilen meshler ~220 MB). Proje bir bilgisayarda ilk açıldığında harita
// yoksa ya da saha görevine çevrilmemişse (düz harita: görev nesnesi yok) bir kez sorulur; "Üret" haritayı kurar ve saha görevine
// çevirir (SelVadisiMissionSetup, yaklaşık 1 dakika). Batchmode'da soru sorulmaz.
// Elle: Rescue Team > Sel Vadisi > Haritayı Yeniden Üret ve Görevi Kur. Tools > Sel Vadisi menüleri de oyunda buraya yönlenir.
[InitializeOnLoad]
static class SelVadisiAutoSetup
{
    private const string AskedKey = "SelVadisi.AutoSetup.Asked";
    private const string MissionObject = "m_Name: SelVadisi_Gorev";

    static SelVadisiAutoSetup()
    {
        SVBuild.HostRebuild = SelVadisiMissionSetup.RebuildMenu;
        if (Application.isBatchMode) return;
        EditorApplication.delayCall += Ask;
    }

    // Sahne var ve görev kurulumu yapılmış mı (SelVadisiMissionSetup.Apply "SelVadisi_Gorev" nesnesini ekler)
    public static bool IsMissionScene()
    {
        if (!File.Exists(SVBuild.ScenePath)) return false;
        try { return File.ReadLines(SVBuild.ScenePath).Any(l => l.Trim() == MissionObject); }
        catch (IOException) { return true; }
    }

    private static void Ask()
    {
        if (SessionState.GetBool(AskedKey, false)) return;
        SessionState.SetBool(AskedKey, true);
        bool exists = File.Exists(SVBuild.ScenePath);
        if (exists && IsMissionScene()) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        string why = exists
            ? "Sel Vadisi haritası var ama saha görevine çevrilmemiş (oyuncu, görev ve koordinasyon masası yok; büyük olasılıkla Tools > Sel Vadisi menüsüyle düz harita olarak üretildi). Bu hâliyle göreve çıkılırsa görev bitmez."
            : "Sel Vadisi saha görevinin haritası bu bilgisayarda henüz üretilmemiş (kodla üretilir, depoda yer kaplamaz).";
        if (EditorUtility.DisplayDialog("Sel Vadisi",
                why + "\n\nŞimdi üretilsin mi? Yaklaşık 1 dakika sürer; açık sahnedeki kaydedilmemiş değişiklikler için önce sorulur.",
                "Üret", "Sonra"))
        {
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            SelVadisiMissionSetup.KeepingOpenScenes(() => SelVadisiMissionSetup.Apply(true));
        }
    }
}
