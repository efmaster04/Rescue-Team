using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Bölüm 1 prosedürel animasyonları (Assets/Animations/Procedural, 11 klip): Animator durumları ve sahne bağlantıları.
//  - AddCrewStates: AC_Crew_Locomotion'a (oyuncu + bot) taşıma, alma/bırakma, zıplama, irkilme, kalkış, kaldırma, kutlama.
//  - PatientController: hasta Animator'ı (yerde Lying_HelpWave ↔ Lying_Groan; Civilian: askılı kol pozu + maskeli Lying_Groan).
//  - SetupScene: oyuncu ve botta CrewAnimation, bot ragdoll'u (şef kararı 6), oyuncu yeniden doğma (şef kararı 8).
// Bolum1AmbulanceBuilder.BuildLocomotionController / BuildPatientPrefab ve Bolum1SceneBuilder.Build içinden çağrılır.
public static class Bolum1AnimBuilder
{
    public const string AnimationFolder = Bolum1AmbulanceBuilder.AnimationFolder;
    public const string PatientLyingControllerPath = AnimationFolder + "/AC_Patient_Lying.controller";
    public const string PatientSlingControllerPath = AnimationFolder + "/AC_Patient_Lying_ArmSling.controller";
    public const string NoArmsMaskPath = AnimationFolder + "/AM_Patient_NoArms.mask";
    public const string ReportPath = "Captures/Bolum1/anim_build_report.txt";

    // Taşıma döngülerinin hedef hızdaki oynatma hızı ve hız 0'daki (bekleme) yavaşlatılmış hızı (yazıldığı hıza göre).
    // Klipler hedef hızda (clips_meta.json target_speed_mps: sedye 1.4, koni 2.4 m/sn) ×1 oynarken ayak kaymayacak
    // adımla yazıldı. Ölçüm: Bolum1AnimVerify (b1s)/(b2s) taşırken ayak kayması.
    public const float CarryWalkScale = 1f;
    public const float CarryIdleScale = 0.25f;
    public const string ClipsMetaPath = ProceduralAnimImportSettings.Folder + "clips_meta.json";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly StringBuilder report = new StringBuilder();

    // clips_meta.json'dan bir klibin sayı alanı (yoksa fallback).
    public static float MetaFloat(string name, string key, float fallback)
    {
        try
        {
            var meta = Bolum1Coordinates.LoadJson(ClipsMetaPath);
            var c = Bolum1Coordinates.Obj(Bolum1Coordinates.Obj(meta, "clips"), name);
            return Convert.ToSingle(c[key], Inv);
        }
        catch { return fallback; }
    }

    // İlk importta kaynak Avatar henüz yoksa klip kendi Avatar'ıyla kalmış olabilir: ayar tutmayanları yeniden import et.
    public static void EnsureClipImports()
    {
        foreach (var n in ProceduralAnimImportSettings.ClipNames)
        {
            var imp = AssetImporter.GetAtPath(ProceduralAnimImportSettings.ClipPath(n)) as ModelImporter;
            if (imp == null) throw new Exception("Prosedürel klip yok: " + ProceduralAnimImportSettings.ClipPath(n));
            if (!ProceduralAnimImportSettings.IsApplied(imp, out _)) imp.SaveAndReimport();
        }
    }

    public static AnimationClip Clip(string name)
    {
        var c = ProceduralAnimImportSettings.LoadClip(name);
        if (c == null) throw new Exception("Prosedürel klip bulunamadı: " + ProceduralAnimImportSettings.ClipPath(name));
        return c;
    }

    // =====================================================================================
    // Ekip Animator'ı
    // =====================================================================================

    // walkSpeed: normal yürüme (koni taşırken de); carryStretcherSpeed: sedye taşırken yürüme hızı (m/sn).
    public static void AddCrewStates(AnimatorController ac, AnimatorState locomotion, float walkSpeed, float carryStretcherSpeed)
    {
        report.Length = 0;
        EnsureClipImports();
        // Hasta Animator'ı da her kurulumda baştan kurulur (ilk PatientController çağrısı oluşturur).
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(PatientLyingControllerPath) != null) AssetDatabase.DeleteAsset(PatientLyingControllerPath);
        ac.AddParameter(CrewAnimation.ParamCarry, AnimatorControllerParameterType.Int);
        ac.AddParameter(CrewAnimation.ParamReviving, AnimatorControllerParameterType.Bool);
        foreach (var t in new[] { CrewAnimation.TriggerPickUp, CrewAnimation.TriggerPutDown, CrewAnimation.TriggerJump, CrewAnimation.TriggerStartled, CrewAnimation.TriggerCelebrate })
            ac.AddParameter(t, AnimatorControllerParameterType.Trigger);
        ac.AddParameter(CrewAnimation.ParamRope, AnimatorControllerParameterType.Int);
        ac.AddParameter(CrewAnimation.ParamPulling, AnimatorControllerParameterType.Bool);
        ac.AddParameter(CrewAnimation.ParamAction, AnimatorControllerParameterType.Bool);

        var sm = ac.layers[0].stateMachine;
        locomotion.name = CrewAnimation.StateLocomotion;
        var carryStretcher = CarryState(ac, sm, CrewAnimation.StateCarryStretcher, "Carry_Stretcher_Walk", carryStretcherSpeed, new Vector3(300f, 120f));
        var carryCone = CarryState(ac, sm, CrewAnimation.StateCarryCone, "Carry_OneHand_Walk", walkSpeed, new Vector3(300f, 220f));
        // D2: sırtta hastayla yürüyüş (Piggyback_Walk 1.2 m/sn'de ×1, beklerken yavaşlatılmış)
        var carryPiggyback = CarryState(ac, sm, CrewAnimation.StateCarryPiggyback, "Piggyback_Walk", PiggybackSpeed, new Vector3(300f, 320f));
        var carryFront = CarryState(ac, sm, CrewAnimation.StateCarryStretcherFront, "Carry_Stretcher_Front_Walk", carryStretcherSpeed, new Vector3(300f, 420f));
        // U2: kangal taşıma — ayrı bekleme ve yürüyüş klipleri (Carry_Coil_Idle 0, Carry_Coil_Walk 2.4 m/sn ×1); koşu yok
        var carryCoil = CoilCarryState(ac, sm, walkSpeed, new Vector3(300f, 520f));
        var loops = new[] { locomotion, carryStretcher, carryCone, carryPiggyback, carryFront, carryCoil }; // dizin = Carry parametresi

        // Döngüler arası (Carry değişince; AnyState tetikleyicileri önceliklidir)
        for (int a = 0; a < loops.Length; a++)
            for (int b = 0; b < loops.Length; b++)
            {
                if (a == b) continue;
                var t = loops[a].AddTransition(loops[b]);
                t.hasExitTime = false;
                t.duration = 0.2f;
                t.hasFixedDuration = true;
                t.AddCondition(AnimatorConditionMode.Equals, b, CrewAnimation.ParamCarry);
            }

        var pickUp = OneShot(sm, CrewAnimation.StatePickUp, "PickUp", 1f, new Vector3(560f, 0f));
        AnyTo(sm, pickUp, CrewAnimation.TriggerPickUp, 0.12f);
        ReturnByCarry(pickUp, loops, 0.85f, 0.15f);

        var putDown = OneShot(sm, CrewAnimation.StatePutDown, "PutDown", 1f, new Vector3(560f, 60f));
        AnyTo(sm, putDown, CrewAnimation.TriggerPutDown, 0.12f);
        ReturnByCarry(putDown, loops, 0.85f, 0.15f);

        var jump = OneShot(sm, CrewAnimation.StateJump, "Jump", 1f, new Vector3(560f, 120f));
        AnyTo(sm, jump, CrewAnimation.TriggerJump, 0.08f);
        ReturnByCarry(jump, loops, 0.85f, 0.15f);

        var startled = OneShot(sm, CrewAnimation.StateStartled, "Startled", 1f, new Vector3(560f, 180f));
        AnyTo(sm, startled, CrewAnimation.TriggerStartled, 0.08f);
        ReturnByCarry(startled, loops, 0.85f, 0.15f);

        var celebrate = OneShot(sm, CrewAnimation.StateCelebrate, "Celebrate", 1f, new Vector3(560f, 240f));
        AnyTo(sm, celebrate, CrewAnimation.TriggerCelebrate, 0.15f);
        ReturnByCarry(celebrate, loops, 0.9f, 0.2f);

        // U3: darbe tepkisi — yalnız kodla (PlayerHealth.TakeDamage → CrewAnimation.PlayAction, 0.5 sn); bitince taşımaya göre döngüye
        var hitReact = OneShot(sm, CrewAnimation.StateHitReact, "HitReact", 1f, new Vector3(560f, 420f));
        ReturnByCarry(hitReact, loops, 0.9f, 0.12f);

        // Kalkış: yalnız kodla (CrewAnimation.BeginGetUp → Animator.Play); geçiş ragdoll pozundan kodda yapılır.
        var getUp = OneShot(sm, CrewAnimation.StateGetUp, "GetUp_FromBack", 1f, new Vector3(560f, 300f));
        ReturnByCarry(getUp, loops, 0.9f, 0.15f);

        // Kaldıran taraf: Reviving açıkken (her kare işaretlenir)
        var revive = sm.AddState(CrewAnimation.StateRevive, new Vector3(560f, 360f));
        revive.motion = Clip("Revive_Helper");
        var toRevive = sm.AddAnyStateTransition(revive);
        toRevive.canTransitionToSelf = false;
        toRevive.hasExitTime = false;
        toRevive.duration = 0.25f;
        toRevive.hasFixedDuration = true;
        toRevive.AddCondition(AnimatorConditionMode.If, 0f, CrewAnimation.ParamReviving);
        for (int c = 0; c < loops.Length; c++)
        {
            var t = revive.AddTransition(loops[c]);
            t.hasExitTime = false;
            t.duration = 0.3f;
            t.hasFixedDuration = true;
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, CrewAnimation.ParamReviving);
            t.AddCondition(AnimatorConditionMode.Equals, c, CrewAnimation.ParamCarry);
        }
        AddRescueStates(sm, loops);
        EditorUtility.SetDirty(ac);
        Line("Ekip Animator'ı: " + AssetDatabase.GetAssetPath(ac) + " → " + string.Join(", ", sm.states.Select(s => s.state.name))
            + "; taşıma döngüleri sedye " + carryStretcherSpeed.ToString(Inv) + " m/sn, koni " + walkSpeed.ToString(Inv) + " m/sn'de ×" + CarryWalkScale.ToString(Inv)
            + ", beklemede ×" + CarryIdleScale.ToString(Inv));
    }

    // Sırtta taşıma yürüyüş hızı (Piggyback_Walk target_speed_mps).
    public const float PiggybackSpeed = 1.2f;
    // Rope_Pull halatı 0.255 m/sn çeker, halattaki Piggyback_Rope_Climb 0.25 m/sn tırmanır: çekme hızı 0.25'e eşitlenir,
    // Rope_Pull oynatması 0.25 / 0.255 = 0.98 olur (iki klip aynı halat hızında).
    public const float RopePullSpeed = 0.25f;
    public static float RopePullPlayback { get { return RopePullSpeed / MetaFloat("Rope_Pull", "rope_speed_mps", 0.255f); } }

    // D2: çekme, sırta alma/bırakma (PlayAction ile), halat durumları (Rope parametresi), halatı çekme (Pulling).
    private static void AddRescueStates(AnimatorStateMachine sm, AnimatorState[] loops)
    {
        var extract = OneShot(sm, CrewAnimation.StateExtract, "Extract_Pull", 1f, new Vector3(820f, 0f));
        var pick = OneShot(sm, CrewAnimation.StatePiggybackPickUp, "Piggyback_PickUp", 1f, new Vector3(820f, 60f));
        var put = OneShot(sm, CrewAnimation.StatePiggybackPutDown, "Piggyback_PutDown", 1f, new Vector3(820f, 120f));
        var hang = OneShot(sm, CrewAnimation.StateRopeHang, "Rope_Hang_Idle", 1f, new Vector3(820f, 200f));
        var climb = OneShot(sm, CrewAnimation.StateRopeClimb, "Rope_Climb", 1f, new Vector3(820f, 260f));
        var descend = OneShot(sm, CrewAnimation.StateRopeDescend, "Rope_Descend", 1f, new Vector3(820f, 320f));
        var pclimb = OneShot(sm, CrewAnimation.StateRopePiggybackClimb, "Piggyback_Rope_Climb", 1f, new Vector3(820f, 380f));
        var pull = OneShot(sm, CrewAnimation.StateRopePull, "Rope_Pull", RopePullPlayback, new Vector3(820f, 440f));
        // U2: halat zinciri eylemleri (PlayAction ile; bitince Carry'ye göre döngüye)
        var takeCoil = OneShot(sm, CrewAnimation.StateTakeCoil, "Cabinet_TakeCoil", 1f, new Vector3(820f, 520f));
        var tieStake = OneShot(sm, CrewAnimation.StateTieStake, "Rope_TieStake", 1f, new Vector3(820f, 580f));
        var ropeThrow = OneShot(sm, CrewAnimation.StateRopeThrow, "Rope_Throw", 1f, new Vector3(820f, 640f));
        var ropes = new[] { hang, climb, descend, pclimb };
        foreach (var a in new[] { extract, pick, put, takeCoil, tieStake, ropeThrow })
        {
            // Eylem bitince: halatta değilse taşımaya göre döngüye (halattaysa Action kapanınca AnyState halat durumuna geçer)
            for (int c = 0; c < loops.Length; c++)
            {
                var t = a.AddTransition(loops[c]);
                t.hasExitTime = true; t.exitTime = 0.95f; t.duration = 0.15f; t.hasFixedDuration = true;
                t.AddCondition(AnimatorConditionMode.Equals, c, CrewAnimation.ParamCarry);
                t.AddCondition(AnimatorConditionMode.Equals, 0, CrewAnimation.ParamRope);
            }
        }
        for (int k = 0; k < ropes.Length; k++)
        {
            var t = sm.AddAnyStateTransition(ropes[k]);
            t.canTransitionToSelf = false;
            t.hasExitTime = false; t.duration = 0.2f; t.hasFixedDuration = true;
            t.AddCondition(AnimatorConditionMode.Equals, k + 1, CrewAnimation.ParamRope);
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, CrewAnimation.ParamAction);
            for (int c = 0; c < loops.Length; c++)
            {
                var back = ropes[k].AddTransition(loops[c]);
                back.hasExitTime = false; back.duration = 0.2f; back.hasFixedDuration = true;
                back.AddCondition(AnimatorConditionMode.Equals, 0, CrewAnimation.ParamRope);
                back.AddCondition(AnimatorConditionMode.Equals, c, CrewAnimation.ParamCarry);
            }
        }
        var toPull = sm.AddAnyStateTransition(pull);
        toPull.canTransitionToSelf = false;
        toPull.hasExitTime = false; toPull.duration = 0.25f; toPull.hasFixedDuration = true;
        toPull.AddCondition(AnimatorConditionMode.If, 0f, CrewAnimation.ParamPulling);
        for (int c = 0; c < loops.Length; c++)
        {
            var t = pull.AddTransition(loops[c]);
            t.hasExitTime = false; t.duration = 0.3f; t.hasFixedDuration = true;
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, CrewAnimation.ParamPulling);
            t.AddCondition(AnimatorConditionMode.Equals, c, CrewAnimation.ParamCarry);
        }
        Line("D2/U2 durumları: " + string.Join(", ", new[] { extract, pick, put, hang, climb, descend, pclimb, pull, takeCoil, tieStake, ropeThrow }.Select(s => s.name + " (" + s.motion.name + " x" + s.speed.ToString("F3", Inv) + ")"))
            + "; sırtta yürüyüş " + PiggybackSpeed.ToString(Inv) + " m/sn, çekme " + RopePullSpeed.ToString(Inv) + " m/sn");
    }

    // Taşıma döngüsü: aynı klibin iki çocuğu (hız 0'da yavaşlatılmış, hedef hızda tam) Speed ile karışır.
    private static AnimatorState CarryState(AnimatorController ac, AnimatorStateMachine sm, string name, string clipName, float walkSpeed, Vector3 pos)
    {
        var clip = Clip(clipName);
        var tree = new BlendTree { name = name + "_Blend", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(tree, ac);
        tree.children = new[]
        {
            new ChildMotion { motion = clip, threshold = 0f, timeScale = CarryIdleScale },
            new ChildMotion { motion = clip, threshold = walkSpeed, timeScale = CarryWalkScale },
        };
        if (name == CrewAnimation.StateCarryCone)
            tree.children = new[] { tree.children[0], tree.children[1], new ChildMotion { motion = clip, threshold = 3.2f, timeScale = CarryWalkScale * 3.2f / walkSpeed } };
        var st = sm.AddState(name, pos);
        st.motion = tree;
        return st;
    }

    // U2: kangal taşıma döngüsü — hız 0'da Carry_Coil_Idle, yürüme hızında Carry_Coil_Walk (ikisi de yazıldığı hızda ×1).
    private static AnimatorState CoilCarryState(AnimatorController ac, AnimatorStateMachine sm, float walkSpeed, Vector3 pos)
    {
        var tree = new BlendTree { name = CrewAnimation.StateCarryCoil + "_Blend", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(tree, ac);
        float coilWalk = MetaFloat("Carry_Coil_Walk", "target_speed_mps", walkSpeed);
        tree.children = new[]
        {
            new ChildMotion { motion = Clip("Carry_Coil_Idle"), threshold = 0f, timeScale = 1f },
            new ChildMotion { motion = Clip("Carry_Coil_Walk"), threshold = coilWalk, timeScale = 1f },
        };
        var st = sm.AddState(CrewAnimation.StateCarryCoil, pos);
        st.motion = tree;
        return st;
    }

    private static AnimatorState OneShot(AnimatorStateMachine sm, string name, string clipName, float speed, Vector3 pos)
    {
        var st = sm.AddState(name, pos);
        st.motion = Clip(clipName);
        st.speed = speed;
        return st;
    }

    private static void AnyTo(AnimatorStateMachine sm, AnimatorState st, string trigger, float duration)
    {
        var t = sm.AddAnyStateTransition(st);
        t.canTransitionToSelf = false;
        t.hasExitTime = false;
        t.duration = duration;
        t.hasFixedDuration = true;
        t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
    }

    private static void ReturnByCarry(AnimatorState from, AnimatorState[] loops, float exitTime, float duration)
    {
        for (int c = 0; c < loops.Length; c++)
        {
            var t = from.AddTransition(loops[c]);
            t.hasExitTime = true;
            t.exitTime = exitTime;
            t.duration = duration;
            t.hasFixedDuration = true;
            t.AddCondition(AnimatorConditionMode.Equals, c, CrewAnimation.ParamCarry);
            t.AddCondition(AnimatorConditionMode.Equals, 0, CrewAnimation.ParamRope);
        }
    }

    // =====================================================================================
    // Hasta Animator'ları
    // =====================================================================================

    // Civilian: askılı kol pozu (Patient_LiePose_ArmSling) taban katmanda; Lying_Groan kolsuz maskeyle üstte (kol askıda kalır).
    // Diğerleri: Groan (varsayılan) ↔ HelpWave, Wave parametresiyle (Patient.cs yerde 5–10 sn'de bir değiştirir).
    public static RuntimeAnimatorController PatientController(string type, AnimationClip liePose, out bool canWave)
    {
        var groan = Clip("Lying_Groan");
        if (type == "Civilian")
        {
            canWave = false;
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(PatientSlingControllerPath) != null) AssetDatabase.DeleteAsset(PatientSlingControllerPath);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(PatientSlingControllerPath);
            var lie = ac.layers[0].stateMachine.AddState("Lie");
            lie.motion = liePose;
            ac.AddLayer("Groan");
            var layers = ac.layers;
            layers[1].defaultWeight = 1f;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[1].avatarMask = NoArmsMask();
            ac.layers = layers;
            var gs = ac.layers[1].stateMachine.AddState("Groan");
            gs.motion = groan;
            AddPatientRescueStates(ac.layers[0].stateMachine, "Patient_SitDazed_ArmSling");
            EditorUtility.SetDirty(ac);
            Line("Hasta " + type + ": " + PatientSlingControllerPath + " (taban: " + (liePose != null ? liePose.name : "-") + ", üst katman Lying_Groan, maske: kollar hariç)");
            return ac;
        }
        canWave = true;
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(PatientLyingControllerPath) == null)
        {
            var ac = AnimatorController.CreateAnimatorControllerAtPath(PatientLyingControllerPath);
            ac.AddParameter(Patient.WaveParameter, AnimatorControllerParameterType.Bool);
            var sm = ac.layers[0].stateMachine;
            var g = sm.AddState("Groan");
            g.motion = groan;
            var w = sm.AddState("HelpWave");
            w.motion = Clip("Lying_HelpWave");
            sm.defaultState = g;
            var t1 = g.AddTransition(w);
            t1.hasExitTime = false; t1.duration = 0.5f; t1.hasFixedDuration = true;
            t1.AddCondition(AnimatorConditionMode.If, 0f, Patient.WaveParameter);
            var t2 = w.AddTransition(g);
            t2.hasExitTime = false; t2.duration = 0.5f; t2.hasFixedDuration = true;
            t2.AddCondition(AnimatorConditionMode.IfNot, 0f, Patient.WaveParameter);
            AddPatientRescueStates(sm, "Patient_SitDazed");
            EditorUtility.SetDirty(ac);
            Line("Hasta Animator'ı: " + PatientLyingControllerPath + " (Groan varsayılan ↔ HelpWave, " + Patient.WaveParameter + ")");
        }
        Line("Hasta " + type + ": " + PatientLyingControllerPath);
        return AssetDatabase.LoadAssetAtPath<AnimatorController>(PatientLyingControllerPath);
    }

    // D2: koltukta oturma, arka kapıdan çekilme, sırtta binme. Patient.cs Animator.Play ile geçer (geçişsiz durumlar).
    private static void AddPatientRescueStates(AnimatorStateMachine sm, string sitClip)
    {
        sm.AddState(Patient.StateSit, new Vector3(500f, 0f)).motion = Clip(sitClip);
        sm.AddState(Patient.StateDraggedOut, new Vector3(500f, 60f)).motion = Clip("Patient_DraggedOut");
        sm.AddState(Patient.StateRider, new Vector3(500f, 120f)).motion = Clip("Piggyback_Rider");
    }

    private static AvatarMask NoArmsMask()
    {
        var m = AssetDatabase.LoadAssetAtPath<AvatarMask>(NoArmsMaskPath);
        if (m == null)
        {
            m = new AvatarMask();
            AssetDatabase.CreateAsset(m, NoArmsMaskPath);
        }
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
        {
            var part = (AvatarMaskBodyPart)i;
            bool arm = part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm || part == AvatarMaskBodyPart.LeftFingers
                || part == AvatarMaskBodyPart.RightFingers || part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
            m.SetHumanoidBodyPartActive(part, !arm);
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    // =====================================================================================
    // Sahne
    // =====================================================================================

    public static void SetupScene(Dictionary<string, object> markers, GameObject character)
    {
        var player = character.GetComponent<ThirdPersonPlayer>();
        var animator = character.GetComponent<Animator>();
        float getUp = Clip("GetUp_FromBack").length;

        // Oyuncu
        // Ekran dışındayken de kemikler yazılsın (kalkış, kaldırma ve ölçümler; batchmode'da kamera render etmiyor)
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var crew = character.AddComponent<CrewAnimation>();
        crew.animator = animator;
        crew.player = player;
        character.AddComponent<StretcherHandIK>();
        crew.getUpSeconds = getUp;
        var ik = character.GetComponent<FootGroundingIK>();
        crew.disableDuringGetUp = ik != null ? new Behaviour[] { ik } : new Behaviour[0];
        player.knockdown.crewAnimation = crew;
        // D2: sırt soketi (Spine1 altında, clips_meta piggyback_socket)
        player.piggybackSocket = Bolum1RescueBuilder.SetupPiggybackSocket(character);
        Line("Oyuncu sırt soketi: " + player.piggybackSocket.parent.name + "/" + player.piggybackSocket.name + " kökten " + V(character.transform.InverseTransformPoint(player.piggybackSocket.position)) + " (bağ pozu)");

        // Oyuncu yeniden doğma (şef kararı 8)
        var respawn = character.AddComponent<PlayerFallRespawn>();
        respawn.player = player;
        respawn.spawnPoint = GameObject.Find("PlayerSpawn").transform;
        respawn.killBelowY = GameObject.Find("FallRespawnPlane").transform.position.y;

        // Bot: ragdoll (şef kararı 6) + animasyon
        var bot = UnityEngine.Object.FindAnyObjectByType<StretcherHelperBot>();
        var botAnimator = bot.GetComponent<Animator>();
        botAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var botCrew = bot.gameObject.AddComponent<CrewAnimation>();
        botCrew.animator = botAnimator;
        botCrew.bot = bot;
        bot.gameObject.AddComponent<StretcherHandIK>();
        botCrew.getUpSeconds = getUp;
        bot.crewAnimation = botCrew;
        bot.hips = botAnimator.GetBoneTransform(HumanBodyBones.Hips);
        Bolum1AmbulanceBuilder.BuildRagdoll(botAnimator, 70f, out bot.ragdollBodies, out bot.ragdollColliders);

        Line("Oyuncu: CrewAnimation (irkilme " + crew.startleRadius.ToString(Inv) + " m, kalkış " + getUp.ToString("F2", Inv) + " sn, ayak IK'sı kalkışta kapalı), "
            + "yeniden doğma: FallRespawnPlane y < " + respawn.killBelowY.ToString("F1", Inv) + " → PlayerSpawn " + V(respawn.spawnPoint.position));
        Line("Bot: CrewAnimation, ragdoll " + bot.ragdollBodies.Length + " gövde (70 kg), çarpılınca " + bot.getUpDelay.ToString(Inv) + " sn sonra kendi kalkar");
        WriteReport();
    }

    private static void WriteReport()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, report.ToString());
        Debug.Log(report.ToString());
    }

    private static void Line(string s) { report.AppendLine(s); }
    private static string V(Vector3 v) { return "(" + v.x.ToString("F2", Inv) + ", " + v.y.ToString("F2", Inv) + ", " + v.z.ToString("F2", Inv) + ")"; }
}
