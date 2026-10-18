using System;
using UnityEditor;
using UnityEngine;
public static class Bolum1E2Build
{
    public static void Run()
    {
        try
        {
            Bolum1AmbulanceAudioMute.Apply();
            Bolum1SceneBuilder.Build(false);
            Debug.Log("E2 BUILD OK");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
