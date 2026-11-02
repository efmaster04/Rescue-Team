using CompanySim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CompanySimEditor
{
    public static class WorkshopPolishBuilder
    {
        [MenuItem("Company/Polish Workshop Details")]
        public static void Apply()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/OfficeGarage.unity");
            GarageRefinementBuilder.ConfigureGarage();
            OfficePresentationBuilder.ConfigureAudio(Object.FindAnyObjectByType<AudioFeedback>());
            OfficePresentationBuilder.BakeReflections();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }
        public static void ApplyAndValidate() { Apply(); CaptureViews(); ProjectValidation.Run(); }
        public static void CaptureViews()
        {
            GarageRefinementBuilder.SignagePreview(new Vector3(6.98f,1.70f,-3.65f),new Vector3(8.65f,1.55f,.15f),
                "Documentation/WorkshopFinishedBench.png",64);
            GarageRefinementBuilder.SignagePreview(new Vector3(7.80f,1.55f,2.20f),new Vector3(8.86f,1.40f,3.15f),
                "Documentation/WorkshopAirReel.png",64);
            GarageRefinementBuilder.SignagePreview(new Vector3(7.2f,1.2f,-4.3f),new Vector3(8.56f,.48f,-4.17f),
                "Documentation/WorkshopPartsCart.png",55);
        }
    }
}
