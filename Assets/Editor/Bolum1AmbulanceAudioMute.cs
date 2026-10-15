using UnityEditor;
using UnityEngine;

// Test ve doğrulama sırasında SESSİZLİK: batchmode'da (testler, builder, ekran görüntüleri) ya da
// Bolum1AmbulanceVerify çalışırken hoparlörden ses çıkmaz. Oyunun kendi ses ayarlarına ve
// ProjectSettings/AudioManager'a dokunmaz; editörün "Mute Audio" ayarı Unity kapanırken eski hâline döner.
[InitializeOnLoad]
public static class Bolum1AmbulanceAudioMute
{
    public const string TestFlagKey = "Bolum1AmbulanceVerify.Stage"; // doğrulama Play aşamasındayken dolu
    private const string PrevMuteKey = "Bolum1AmbulanceAudioMute.PrevMute";

    public static bool ShouldMute
    {
        get { return Application.isBatchMode || !string.IsNullOrEmpty(SessionState.GetString(TestFlagKey, "")); }
    }

    static Bolum1AmbulanceAudioMute()
    {
        if (!Application.isBatchMode) return;
        Apply();
        EditorApplication.playModeStateChanged += s =>
        {
            if (s == PlayModeStateChange.EnteredPlayMode) AudioListener.volume = 0f;
        };
        EditorApplication.quitting += Restore;
    }

    // Play moduna girmeden önce çağrılır (doğrulama da çağırır).
    public static void Apply()
    {
        if (SessionState.GetInt(PrevMuteKey, -1) < 0)
            SessionState.SetInt(PrevMuteKey, EditorUtility.audioMasterMute ? 1 : 0);
        EditorUtility.audioMasterMute = true;
        AudioListener.volume = 0f;
    }

    public static void Restore()
    {
        int prev = SessionState.GetInt(PrevMuteKey, -1);
        if (prev >= 0) EditorUtility.audioMasterMute = prev == 1;
        SessionState.EraseInt(PrevMuteKey);
    }

    // Sahne yüklenmeden önce (Play başlarken): test bayrağı varsa dinleyici sesi sıfır.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void MuteBeforeSceneLoad()
    {
        if (ShouldMute) AudioListener.volume = 0f;
    }
}
