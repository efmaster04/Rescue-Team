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

    // Mixamo kliplerinin ortak kaynak avatarı: T-pozdaki X Bot (With Skin).
    // "Without Skin" kliplerde avatar klibin kendisinden kurulursa Unity onu T-pozdan değil klibin ilk karesinden kurar.
    // O karede bacaklar simetrik olmadığı için retarget çarpılır: koşuda bacak yana açılır, yürüyüşte topallama olur.
    private const string SourceAvatarPath = "Assets/Models/Characters/XBot/XBot_TPose.fbx";
    private const string SourceAvatarMarker = "XBotSource:v1";

    // Ayarların uygulandığını gösteren işaret (.meta dosyasındaki userData alanına yazılır).
    // importSettingsMissing kopyalanan dosyalarda güvenilir çıkmadı, bu yüzden kendi işaretimizi tutuyoruz.
    // Animasyon işareti v2: avatar artık X Bot T-poz avatarından kopyalanıyor (v1'de klibin kendisinden kuruluyordu).
    private const string AppliedMarker = "RescueCrewDefaults:anim-v2";

    // Karakter FBX'leri için işaret v3: humanoid kas limitleri Unity varsayılanında (v2'deki kol daraltması geri alındı).
    // Kol gömülmesi avatarda değil, Running klibinde RescueCrewClipFixes ile giderilir.
    private const string CharacterMarker = "RescueCrewDefaults:v3";

    private void OnPreprocessModel()
    {
        if (assetPath == SourceAvatarPath)
        {
            PrepareSourceAvatar((ModelImporter)assetImporter);
            return;
        }

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

        if (isAnimation)
        {
            // Klipler avatarı T-pozdaki X Bot'tan kopyalar. X Bot henüz import edilmediyse avatar bulunamaz;
            // o zaman klip eski yöntemle kalır ve işaret konmaz, X Bot geldikten sonraki reimport'ta düzelir.
            var sourceAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(SourceAvatarPath);
            if (sourceAvatar != null)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = sourceAvatar;
            }
            else
            {
                Debug.LogWarning("RescueCrewImportSettings: " + SourceAvatarPath + " bulunamadı, " + assetPath + " klibin kendi avatarıyla import edildi (bacaklar çarpık olabilir).");
            }
        }

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

    // X Bot kaynak modeli: yalnızca avatarı lazım (T-poz), klip ve mesh ayarı önemsiz.
    private static void PrepareSourceAvatar(ModelImporter importer)
    {
        if (importer.userData == SourceAvatarMarker)
            return;

        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.bakeAxisConversion = false;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = false;
        importer.userData = SourceAvatarMarker;
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
        // Yürüme ve koşu klipleri yerinde oynar. Aday adları "Walk_SneakWalk", "Run_SlowRun" gibi; bu yüzden kök kelimeye bakılır
        bool inPlace = fileName.Contains("Walk") || fileName.Contains("Run");

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
        // Kaynak avatar bağlanamadıysa işaret koyma ki sonraki reimport tekrar denesin
        if (importer.avatarSetup == ModelImporterAvatarSetup.CopyFromOther && importer.sourceAvatar != null)
            importer.userData = AppliedMarker;
    }
}
