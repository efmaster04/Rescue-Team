using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// RescueCrew karakter ve animasyon FBX'lerine README'deki import ayarlarını otomatik uygular.
// Ayarlar elle girilmez; her dosyaya bir kez yazılır, sonra Inspector'dan yapılan değişiklikler ezilmez.
// Kaynak: C:/Users/W11/Projects/RescueTeam/Export/RescueCrew/README.md
internal class RescueCrewImportSettings : AssetPostprocessor
{
    private const string CharacterFolder = "Assets/Models/Characters/RescueCrew/";
    private const string AnimationFolder = "Assets/Animations/RescueCrew/";

    // Ayarların uygulandığını gösteren işaret (.meta dosyasındaki userData alanına yazılır).
    // importSettingsMissing kopyalanan dosyalarda güvenilir çıkmadı, bu yüzden kendi işaretimizi tutuyoruz.
    private const string AppliedMarker = "RescueCrewDefaults:v1";

    // Karakter FBX'leri için işaret v3: humanoid kas limitleri Unity varsayılanında (v2'deki kol daraltması geri alındı).
    // Kol gömülmesi avatarda değil, Running klibinde RescueCrewClipFixes ile giderilir.
    private const string CharacterMarker = "RescueCrewDefaults:v3";

    private void OnPreprocessModel()
    {
        bool isCharacter = assetPath.StartsWith(CharacterFolder);
        bool isAnimation = assetPath.StartsWith(AnimationFolder);
        if (!isCharacter && !isAnimation)
            return;

        var importer = (ModelImporter)assetImporter;
        string marker = isCharacter ? CharacterMarker : AppliedMarker;
        if (importer.userData == marker)
            return;

        importer.globalScale = 1f;
        importer.useFileScale = true;          // Inspector'daki "Convert Units"
        importer.bakeAxisConversion = false;   // Açıkken karakter ters dönüyor (README)
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

        if (isCharacter)
        {
            importer.importBlendShapes = true;
            importer.importAnimation = false;  // Karakter dosyasında klip yok, T-pose
            // İlk importta human listesi henüz boş olabilir; o zaman işaret konmaz, sonraki reimport'ta uygulanır
            if (UseDefaultMuscleLimits(importer))
                importer.userData = CharacterMarker;
        }
        // Animasyon dosyalarında işaret, klip ayarları da yazılınca OnPreprocessAnimation'da konur
    }

    // Tüm kemiklerin kas limitlerini Unity varsayılanına yazar (useDefaultValues = true).
    private static bool UseDefaultMuscleLimits(ModelImporter importer)
    {
        HumanDescription description = importer.humanDescription;
        if (description.human == null || description.human.Length == 0)
            return false;

        HumanBone[] humanBones = description.human;
        for (int i = 0; i < humanBones.Length; i++)
        {
            HumanLimit limit = humanBones[i].limit;
            limit.useDefaultValues = true;
            humanBones[i].limit = limit;
        }
        description.human = humanBones;
        importer.humanDescription = description;
        return true;
    }

    private void OnPreprocessAnimation()
    {
        if (!assetPath.StartsWith(AnimationFolder))
            return;

        var importer = (ModelImporter)assetImporter;
        if (importer.userData == AppliedMarker)
            return;

        string fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        bool loops = !fileName.Contains("Waving");                       // El sallama tek sefer oynar
        bool inPlace = fileName.Contains("Walking") || fileName.Contains("Running");

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        foreach (ModelImporterClipAnimation clip in clips)
        {
            clip.name = fileName;
            clip.loopTime = loops;
            if (inPlace)
            {
                // Kök kemiğin dönüşü ve konumu pozun içine gömülür; karakter yerinde yürür
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
            }
        }
        importer.clipAnimations = clips;
        importer.userData = AppliedMarker;
    }
}
