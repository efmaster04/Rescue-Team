using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tüm sahneyi yeniden kurmaz: hastalar, ışıklar ve modeller olduğu gibi korunur.
public static class Bolum1Corrections
{
    public static void Apply()
    {
        try
        {
            Bolum1AmbulanceAudioMute.Apply();
            var scene = EditorSceneManager.OpenScene(Bolum1SceneBuilder.ScenePath);
            var stretchers = UnityEngine.Object.FindObjectsByType<StretcherController>(FindObjectsSortMode.None);
            Bolum1AmbulanceBuilder.PlaceStretchers(GameObject.Find(Bolum1SceneBuilder.TowTruckName), stretchers[0].transform.parent);
            var player = UnityEngine.Object.FindAnyObjectByType<ThirdPersonPlayer>();
            player.runSpeed = 3.2f;
            player.hardLandingHeight = 3f;
            EditorUtility.SetDirty(player);
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(Bolum1AmbulanceBuilder.LocomotionControllerPath);
            var state = ac.layers[0].stateMachine.states.First(s => s.state.name == CrewAnimation.StateCarryCone).state;
            var tree = (BlendTree)state.motion;
            var children = tree.children.Take(2).ToList();
            children.Add(new ChildMotion { motion = children[1].motion, threshold = 3.2f, timeScale = 3.2f / 2.4f });
            tree.children = children.ToArray();
            EditorUtility.SetDirty(tree);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("CORRECTIONS APPLY OK");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
