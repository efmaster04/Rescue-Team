using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// U4 (kullanıcı kararı "Hırsızlar", 2026-10-19): hırsız mekaniği kurulumu. Bolum1SceneBuilder.Build içinden çağrılır
// (BuildAssets eşya ayarlarından sonra, SetupScene en sonda).
//  - Modeller: Assets/Models/Characters/Thief/SK_Thief_{Black,Red,Green}.fbx (sanat reposu Export/Thief kopyası; ekip iskeleti, 31 kemik).
//    Import: Humanoid, Avatar bu modelden (armature kökü SK_Thief_X olduğu için SK_RescueCrew_Blue'dan kopya hiyerarşi
//    doğrulamasını geçmez; hastalardaki gibi), kas limitleri varsayılan, klip yok. Klipler (SK_RescueCrew_Blue avatarı) retarget edilir.
//  - AC_Thief: Move (Sneak ↔ Run, Speed), MoveCarry (Run_Carry), Stunned, Grab, Punch, ThrowInVan, GetUp (GetUp_FromBack).
//  - Thief_<Varyant>.prefab: model + Animator + Thief + kapsül (kinematik Rigidbody) + ragdoll (60 kg) + ses.
//  - Vehicle_ThiefVan.prefab: trafik PanelVan prefab'ının koyu antrasit kopyası (M_ThiefVan_Paint) + arka kapaklar + ThiefVan.
//  - Sahne: ThiefVanStop (çekiciye 15–25 m, şeridin bankete bakan tarafında, şeridi kapatmayan düz yer), Bolum1_Thieves/ThiefDirector
//    (rampa ara noktaları markers.json geometry_probes.ramp), oyuncuda PlayerCombat, minibüste MinivanRoofLuggage.
public static class Bolum1ThiefBuilder
{
    public const string ModelFolder = "Assets/Models/Characters/Thief/";
    public const string MetaPath = ModelFolder + "thief_meta.json";
    public static readonly string[] Variants = { "Black", "Red", "Green" };
    public static string ModelPath(string v) { return ModelFolder + "SK_Thief_" + v + ".fbx"; }
    public static string PrefabPath(string v) { return Bolum1AmbulanceBuilder.PrefabFolder + "/Thief_" + v + ".prefab"; }
    public const string SettingsPath = Bolum1AmbulanceBuilder.SettingsFolder + "/ThiefSettings.asset";
    public const string ControllerPath = Bolum1AmbulanceBuilder.AnimationFolder + "/AC_Thief.controller";
    public const string VanPrefabPath = Bolum1TrafficBuilder.PrefabFolder + "/Vehicle_ThiefVan.prefab";
    public const string VanPaintMaterialPath = Bolum1AmbulanceBuilder.MaterialFolder + "/M_ThiefVan_Paint.mat";
    public const string VanDoorMaterialPath = Bolum1AmbulanceBuilder.MaterialFolder + "/M_ThiefVan_Door.mat";
    public const string ReportPath = "Captures/Bolum1/thief_build_report.txt";
    public const string RootName = "Bolum1_Thieves";
    public const string DirectorName = "ThiefDirector";
    public const string VanStopName = "ThiefVanStop";
    public const string VanPaintMaterialName = "M_TrafficTrucks_VanPaint";
    public static readonly Color VanPaint = new Color(0.12f, 0.13f, 0.16f); // koyu antrasit (#1F2229 civarı, sRGB)
    // Durak arama: çekiciye uzaklık aralığı (m), ambulans durağına en az uzaklık (m)
    public const float StopMinFromTruck = 15f, StopMaxFromTruck = 25f, StopMinFromAmbulance = 14f;
    public const float ThiefMass = 60f;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly StringBuilder report = new StringBuilder();
    private static void Line(string s) { report.AppendLine(s); }
    private static string V(Vector3 v) { return "(" + v.x.ToString("F2", Inv) + ", " + v.y.ToString("F2", Inv) + ", " + v.z.ToString("F2", Inv) + ")"; }

    public static ThiefSettings Settings { get { return AssetDatabase.LoadAssetAtPath<ThiefSettings>(SettingsPath); } }

    // =====================================================================================
    // Asset'ler
    // =====================================================================================
    public static void BuildAssets()
    {
        report.Length = 0;
        Line("THIEF BUILD " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
        foreach (var v in Variants)
        {
            var importer = AssetImporter.GetAtPath(ModelPath(v)) as ModelImporter;
            if (importer == null) throw new Exception("Hırsız FBX bulunamadı: " + ModelPath(v));
            if (!ThiefImportSettings.IsApplied(importer)) { ThiefImportSettings.Apply(importer); importer.SaveAndReimport(); }
        }
        if (Settings == null) AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<ThiefSettings>(), SettingsPath);
        AssetDatabase.SaveAssets();
        var settings = Settings;
        BuildController(settings);
        foreach (var v in Variants) BuildThiefPrefab(v, settings);
        BuildVanPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    // Hırsız Animator'ı: Speed (float), Carry / Stunned (bool), Grab / Punch / Throw / GetUp (trigger).
    private static void BuildController(ThiefSettings s)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null) AssetDatabase.DeleteAsset(ControllerPath);
        var ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        ac.AddParameter(Thief.ParamSpeed, AnimatorControllerParameterType.Float);
        ac.AddParameter(Thief.ParamCarry, AnimatorControllerParameterType.Bool);
        ac.AddParameter(Thief.ParamStunned, AnimatorControllerParameterType.Bool);
        foreach (var t in new[] { Thief.TriggerGrab, Thief.TriggerPunch, Thief.TriggerThrow, Thief.TriggerGetUp }) ac.AddParameter(t, AnimatorControllerParameterType.Trigger);
        var sm = ac.layers[0].stateMachine;
        float sneak = Bolum1AnimBuilder.MetaFloat("Thief_Sneak_Walk", "target_speed_mps", s.sneakSpeed);
        float run = Bolum1AnimBuilder.MetaFloat("Thief_Run", "target_speed_mps", s.runSpeed);

        // Move: 0 → yavaşlatılmış sinsi, sneak → sinsi ×1, run → koşu ×1
        var moveTree = new BlendTree { name = "Move_Blend", blendType = BlendTreeType.Simple1D, blendParameter = Thief.ParamSpeed, useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(moveTree, ac);
        moveTree.children = new[]
        {
            new ChildMotion { motion = Bolum1AnimBuilder.Clip("Thief_Sneak_Walk"), threshold = 0f, timeScale = 0.3f },
            new ChildMotion { motion = Bolum1AnimBuilder.Clip("Thief_Sneak_Walk"), threshold = sneak, timeScale = 1f },
            new ChildMotion { motion = Bolum1AnimBuilder.Clip("Thief_Run"), threshold = run, timeScale = 1f },
        };
        var move = sm.AddState(Thief.StateMove, new Vector3(300f, 100f));
        move.motion = moveTree;
        sm.defaultState = move;
        // MoveCarry: ganimet göğüste; 0 → yavaşlatılmış, run → ×1
        var carryTree = new BlendTree { name = "MoveCarry_Blend", blendType = BlendTreeType.Simple1D, blendParameter = Thief.ParamSpeed, useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(carryTree, ac);
        carryTree.children = new[]
        {
            new ChildMotion { motion = Bolum1AnimBuilder.Clip("Thief_Run_Carry"), threshold = 0f, timeScale = 0.25f },
            new ChildMotion { motion = Bolum1AnimBuilder.Clip("Thief_Run_Carry"), threshold = run, timeScale = 1f },
        };
        var moveCarry = sm.AddState(Thief.StateMoveCarry, new Vector3(300f, 220f));
        moveCarry.motion = carryTree;
        var toCarry = move.AddTransition(moveCarry); toCarry.hasExitTime = false; toCarry.duration = 0.15f; toCarry.hasFixedDuration = true; toCarry.AddCondition(AnimatorConditionMode.If, 0f, Thief.ParamCarry);
        var toMove = moveCarry.AddTransition(move); toMove.hasExitTime = false; toMove.duration = 0.15f; toMove.hasFixedDuration = true; toMove.AddCondition(AnimatorConditionMode.IfNot, 0f, Thief.ParamCarry);
        var loops = new[] { move, moveCarry };

        // Stunned: AnyState → (Stunned), çıkış !Stunned
        var stunned = sm.AddState(Thief.StateStunnedAnim, new Vector3(560f, 100f));
        stunned.motion = Bolum1AnimBuilder.Clip("Thief_Stunned");
        var toStun = sm.AddAnyStateTransition(stunned); toStun.canTransitionToSelf = false; toStun.hasExitTime = false; toStun.duration = 0.15f; toStun.hasFixedDuration = true; toStun.AddCondition(AnimatorConditionMode.If, 0f, Thief.ParamStunned);
        ReturnByCarry(stunned, loops, false, 0.2f, Thief.ParamStunned);

        // Tek seferlikler: Grab, Punch, ThrowInVan (AnyState tetikleyici), GetUp (kodla Play)
        var grab = OneShot(sm, Thief.StateGrabAnim, "Thief_Grab", new Vector3(560f, 200f));
        AnyTo(sm, grab, Thief.TriggerGrab, 0.1f);
        ReturnByCarry(grab, loops, true, 0.12f, null);
        var punch = OneShot(sm, Thief.StatePunchAnim, "Thief_Punch", new Vector3(560f, 280f));
        AnyTo(sm, punch, Thief.TriggerPunch, 0.1f);
        ReturnByCarry(punch, loops, true, 0.12f, null);
        var thr = OneShot(sm, Thief.StateThrowAnim, "Thief_ThrowInVan", new Vector3(560f, 360f));
        AnyTo(sm, thr, Thief.TriggerThrow, 0.1f);
        ReturnByCarry(thr, loops, true, 0.12f, null);
        var getUp = OneShot(sm, Thief.StateGetUpAnim, "GetUp_FromBack", new Vector3(560f, 440f));
        AnyTo(sm, getUp, Thief.TriggerGetUp, 0.1f);
        ReturnByCarry(getUp, loops, true, 0.15f, null);
        EditorUtility.SetDirty(ac);
        Line("Hırsız Animator'ı: " + ControllerPath + " → " + string.Join(", ", sm.states.Select(x => x.state.name)) + "; sinsi " + sneak.ToString(Inv) + " m/sn, koşu " + run.ToString(Inv) + " m/sn");
    }

    private static AnimatorState OneShot(AnimatorStateMachine sm, string name, string clip, Vector3 pos)
    {
        var st = sm.AddState(name, pos);
        st.motion = Bolum1AnimBuilder.Clip(clip);
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

    // Döngüye dönüş: Carry'ye göre Move / MoveCarry. exit: çıkış zamanıyla (0.9) ya da koşulla (notBool false olunca).
    private static void ReturnByCarry(AnimatorState from, AnimatorState[] loops, bool exit, float duration, string notBool)
    {
        for (int c = 0; c < loops.Length; c++)
        {
            var t = from.AddTransition(loops[c]);
            t.hasExitTime = exit;
            t.exitTime = 0.9f;
            t.duration = duration;
            t.hasFixedDuration = true;
            if (notBool != null) t.AddCondition(AnimatorConditionMode.IfNot, 0f, notBool);
            t.AddCondition(c == 1 ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, Thief.ParamCarry);
        }
    }

    private static void BuildThiefPrefab(string variant, ThiefSettings settings)
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(variant));
        if (fbx == null) throw new Exception("Hırsız modeli yok: " + ModelPath(variant));
        var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        try
        {
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "Thief_" + variant;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = go.GetComponent<Animator>();
            if (animator == null) animator = go.AddComponent<Animator>();
            if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman) throw new Exception("Hırsız avatarı Humanoid değil: " + ModelPath(variant));
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null) throw new Exception("Hırsız modelinde Humanoid Hips yok");
            var thief = go.AddComponent<Thief>();
            thief.settings = settings;
            thief.animator = animator;
            thief.hips = hips;
            thief.variant = variant;
            // Gövde: oyuncu içinden geçemesin (CharacterController kinematik gövdeye çarpar); GroundProbe hırsızı zemin saymaz
            var cap = go.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, 0.75f, 0f);
            cap.radius = 0.28f;
            cap.height = 1.5f;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;
            Bolum1AmbulanceBuilder.BuildRagdoll(animator, ThiefMass, out thief.ragdollBodies, out thief.ragdollColliders);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            thief.audioSource = src;
            int tris = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(r => r.sharedMesh != null ? r.sharedMesh.triangles.Length / 3 : 0);
            var mats = go.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct().ToList();
            Line("Hırsız " + variant + ": " + ModelPath(variant) + ", avatar " + animator.avatar.name + " (Humanoid " + animator.avatar.isHuman + "), " + tris + " üçgen, malzemeler " + string.Join("/", mats)
                + ", ragdoll " + thief.ragdollBodies.Length + " gövde (" + ThiefMass.ToString(Inv) + " kg), kapsül r " + cap.radius.ToString(Inv) + " h " + cap.height.ToString(Inv));
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath(variant));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    // Kamyonet: trafik PanelVan prefab'ının kopyası, koyu boya, arka kapaklar, ThiefVan.
    private static void BuildVanPrefab()
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1TrafficBuilder.PrefabPath(TrafficVehicleType.PanelVan));
        if (src == null) throw new Exception("PanelVan prefab'ı yok: " + Bolum1TrafficBuilder.PrefabPath(TrafficVehicleType.PanelVan));
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        try
        {
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            go.name = "Vehicle_ThiefVan";
            var vehicle = go.GetComponent<TrafficVehicle>();
            var follower = go.GetComponent<LaneFollower>();
            // Koyu boya: VanPaint malzemesinin yuvaları yeni koyu malzemeye
            Material paint = AssetDatabase.LoadAssetAtPath<Material>(VanPaintMaterialPath);
            Material original = go.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null && m.name == VanPaintMaterialName);
            if (paint == null)
            {
                paint = original != null ? new Material(original) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(paint, VanPaintMaterialPath);
            }
            paint.SetColor("_BaseColor", VanPaint);
            if (paint.HasProperty("_Color")) paint.SetColor("_Color", VanPaint);
            paint.SetFloat("_Smoothness", 0.45f);
            EditorUtility.SetDirty(paint);
            int painted = 0;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].name == VanPaintMaterialName) { mats[i] = paint; changed = true; painted++; }
                if (changed) r.sharedMaterials = mats;
            }
            // Arka kapaklar: gövde kutusunun arka düzleminde iki ince levha, menteşe köşelerde (kök uzayında dikey eksen)
            var door = AssetDatabase.LoadAssetAtPath<Material>(VanDoorMaterialPath);
            if (door == null) { door = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(door, VanDoorMaterialPath); }
            door.SetColor("_BaseColor", VanPaint * 0.85f);
            door.SetFloat("_Smoothness", 0.4f);
            EditorUtility.SetDirty(door);
            float rearZ = -follower.rearOverhang + 0.02f;
            float halfW = vehicle.bodySize.x * 0.5f - 0.04f;
            float bottom = 0.55f, top = vehicle.bodyCenter.y + vehicle.bodySize.y * 0.5f - 0.08f;
            Transform MakeDoor(string name, float sideSign)
            {
                var hinge = new GameObject(name).transform;
                hinge.SetParent(go.transform, false);
                hinge.localPosition = new Vector3(sideSign * halfW, bottom, rearZ);
                var leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.Object.DestroyImmediate(leaf.GetComponent<Collider>());
                leaf.name = "Leaf";
                leaf.transform.SetParent(hinge, false);
                leaf.transform.localPosition = new Vector3(-sideSign * halfW * 0.5f, (top - bottom) * 0.5f, 0f);
                leaf.transform.localScale = new Vector3(halfW - 0.02f, top - bottom, 0.05f);
                leaf.GetComponent<MeshRenderer>().sharedMaterial = door;
                return hinge;
            }
            var left = MakeDoor("Door_L", -1f);
            var right = MakeDoor("Door_R", 1f);
            var van = go.AddComponent<ThiefVan>();
            van.doorLeft = left;
            van.doorRight = right;
            van.doorLeftOpenAngle = -150f;
            van.doorRightOpenAngle = 150f;
            var ds = go.AddComponent<AudioSource>();
            ds.playOnAwake = false;
            ds.spatialBlend = 1f;
            van.doorSource = ds;
            Line("Kamyonet: " + VanPrefabPath + " (PanelVan kopyası), boya " + VanPaintMaterialPath + " #" + ColorUtility.ToHtmlStringRGB(VanPaint) + " (" + painted + " yuva), kapaklar " + (halfW - 0.02f).ToString("F2", Inv) + " × " + (top - bottom).ToString("F2", Inv)
                + " m, arka düzlem z " + rearZ.ToString("F2", Inv) + "; şoför: mevcut silüet füme camın arkasında");
            PrefabUtility.SaveAsPrefabAsset(go, VanPrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    // =====================================================================================
    // Sahne
    // =====================================================================================
    public static void SetupScene(Dictionary<string, object> markers, GameObject minivan, GameObject truck, GameObject character)
    {
        Physics.SyncTransforms();
        var settings = Settings;
        if (settings == null) throw new Exception("ThiefSettings yok (BuildAssets çağrılmadı mı?)");
        var gameplay = GameObject.Find(Bolum1AmbulanceBuilder.GameplayRootName).transform;
        var east = GameObject.Find(Bolum1AmbulanceBuilder.LaneName).GetComponent<LanePath>();
        var west = GameObject.Find(Bolum1TrafficBuilder.WestLaneName).GetComponent<LanePath>();
        var ambStop = GameObject.Find(Bolum1AmbulanceBuilder.StopName).transform;
        var vanPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VanPrefabPath).GetComponent<ThiefVan>();
        var vehicle = vanPrefab.GetComponent<TrafficVehicle>();
        float halfW = vehicle.bodySize.x * 0.5f, halfL = vehicle.bodySize.z * 0.5f;

        // 1) Durak: çekiciye 15–25 m, tercihen şeridin sağı (banket), kamyonet şeridin dışında, düz zemin, statik engel yok
        if (!FindVanStop(new[] { east, west }, truck.transform, ambStop, east, halfW, halfL, settings.shoulderClearance, out LanePath lane, out float stopS, out float lateral, out Vector3 stopPos, out string stopInfo))
            throw new Exception("ThiefVanStop için yer bulunamadı: " + stopInfo);
        var stopGo = new GameObject(VanStopName);
        stopGo.transform.SetParent(gameplay, false);
        stopGo.transform.SetPositionAndRotation(stopPos, Quaternion.LookRotation(lane.TangentAt(stopS), Vector3.up));
        Line("ThiefVanStop: " + V(stopPos) + " şerit " + lane.name + " s " + stopS.ToString("F1", Inv) + ", yanal " + lateral.ToString("F2", Inv) + " m (şerit yarı genişliği " + (lane.width * 0.5f).ToString(Inv)
            + " + boşluk " + settings.shoulderClearance.ToString(Inv) + " + kamyonet yarı genişliği " + halfW.ToString("F2", Inv) + "); " + stopInfo);

        // 2) Rampa ara noktaları (yoldan patikaya), seviye yükseklikleri
        var ramp = ((List<object>)Bolum1Coordinates.Obj(markers, "geometry_probes")["ramp"]).Select(p => Bolum1Coordinates.ToUnity(Bolum1Coordinates.Vec3(p))).ToList();
        var rampPts = new List<Vector3>();
        foreach (var p in ramp)
        {
            Vector3 q = p;
            if (GroundProbe.Find(q + Vector3.up * 2f, 6f, out RaycastHit g)) q.y = g.point.y + 0.02f;
            rampPts.Add(q);
        }
        rampPts = rampPts.OrderByDescending(p => p.y).ToList();
        // Seyreltme: ~2 m aralık (ilk ve son korunur)
        var thinned = new List<Vector3> { rampPts[0] };
        for (int i = 1; i < rampPts.Count - 1; i++) if (Vector3.Distance(thinned[thinned.Count - 1], rampPts[i]) >= 2f) thinned.Add(rampPts[i]);
        thinned.Add(rampPts[rampPts.Count - 1]);
        var path = ((List<object>)Bolum1Coordinates.Obj(markers, "geometry_probes")["path"]).Select(p => Bolum1Coordinates.ToUnity(Bolum1Coordinates.Vec3(p))).ToList();
        float pathY = path.Average(p => p.y);
        float roadY = east.PointAt(east.Project(truck.transform.position)).y;
        Line("Rampa ara noktaları: " + thinned.Count + " (" + V(thinned[0]) + " → " + V(thinned[thinned.Count - 1]) + "); yol seviyesi y " + roadY.ToString("F2", Inv) + ", patika y " + pathY.ToString("F2", Inv));

        // 3) Yönetmen
        var root = new GameObject(RootName).transform;
        root.SetParent(gameplay, false);
        var dirGo = new GameObject(DirectorName);
        dirGo.transform.SetParent(root, false);
        var director = dirGo.AddComponent<ThiefDirector>();
        director.settings = settings;
        director.lane = lane;
        director.vanStop = stopGo.transform;
        director.vanPrefab = vanPrefab;
        director.thiefPrefabs = Variants.Select(v => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(v)).GetComponent<Thief>()).ToArray();
        director.mission = GameObject.Find(Bolum1AmbulanceBuilder.MissionName).GetComponent<MissionDirector>();
        director.traffic = GameObject.Find(Bolum1TrafficBuilder.DirectorName).GetComponent<TrafficDirector>();
        director.rampPoints = thinned.ToArray();
        director.roadLevelY = roadY;
        director.pathLevelY = pathY;
        director.killBelowY = GameObject.Find("FallRespawnPlane").transform.position.y;
        Line("Yönetmen: ilk dalga " + settings.firstWaveSeconds.ToString(Inv) + " sn, aralık " + settings.waveIntervalSeconds.ToString(Inv) + " sn, hırsız " + string.Join("→", Enumerable.Range(0, 6).Select(k => settings.ThievesForWave(k)))
            + ", uyarı " + settings.warnLeadSeconds.ToString(Inv) + " sn önce, kamyonet bekleme " + settings.vanWaitSeconds.ToString(Inv) + " sn");

        // 4) Oyuncu savunması
        var player = character.GetComponent<ThirdPersonPlayer>();
        var combat = character.GetComponent<PlayerCombat>();
        if (combat == null) combat = character.AddComponent<PlayerCombat>();
        combat.player = player;
        combat.settings = settings;
        combat.crew = character.GetComponent<CrewAnimation>();
        combat.killBelowY = director.killBelowY;
        var reader = character.GetComponent<PlayerInputReader>();
        if (reader != null) reader.combat = combat;
        Line("Oyuncu savunması: itme " + settings.pushRange.ToString(Inv) + " m/" + settings.pushAngle.ToString(Inv) + "°/" + settings.pushStunSeconds.ToString(Inv) + " sn, tackle " + settings.tackleDistance.ToString(Inv) + " m/" + settings.tackleStunSeconds.ToString(Inv)
            + " sn, fırlatma " + settings.throwSpeed.ToString(Inv) + " m/sn/" + settings.throwHitStunSeconds.ToString(Inv) + " sn, düdük " + settings.whistleRadius.ToString(Inv) + " m/" + settings.fearSeconds.ToString(Inv) + " sn/bekleme " + settings.whistleCooldown.ToString(Inv) + " sn");

        // 5) Tavan valizleri
        var rack = minivan.GetComponentsInChildren<Transform>(true).Where(t => t.name.Contains("RackSuitcase")).OrderBy(t => t.name).ToArray();
        var roof = minivan.GetComponent<MinivanRoofLuggage>();
        if (roof == null) roof = minivan.AddComponent<MinivanRoofLuggage>();
        roof.tailgate = minivan.GetComponent<MinivanTailgate>();
        roof.suitcases = rack;
        roof.values = Bolum1LootBuilder.Values;
        roof.contentsSeedBase = 5;
        Line("Tavan valizleri: " + rack.Length + " (" + string.Join(", ", rack.Select(r => r.name)) + "): bagaj kapısı açılınca " + roof.behind.ToString(Inv) + " m geriye düşer, LootItem olur (valiz " + (roof.values != null ? roof.values.suitcaseValue : 20) + " + içerik tohumu " + roof.contentsSeedBase + "…)");
        WriteReport();
    }

    // Şerit kenarı dışında, kamyonet ayak izi düz (≤ 20°), statik engel yok, şeride göre yükseklik farkı küçük. Tercih: sağ taraf, çekiciye ~20 m.
    private static bool FindVanStop(LanePath[] lanes, Transform truck, Transform ambStop, LanePath east, float halfW, float halfL, float clearance,
        out LanePath bestLane, out float bestS, out float bestLateral, out Vector3 bestPos, out string info)
    {
        bestLane = null; bestS = 0f; bestLateral = 0f; bestPos = Vector3.zero;
        float bestScore = float.MinValue;
        var notes = new List<string>();
        int tested = 0, flatOk = 0, clearOk = 0;
        foreach (var lane in lanes)
        {
            float truckS = lane.Project(truck.position);
            float ambS = lane == east ? east.Project(ambStop.position) : -9999f;
            foreach (float side in new[] { 1f, -1f })
            {
                float lateral = side * (lane.width * 0.5f + clearance + halfW);
                for (float d = -StopMaxFromTruck; d <= StopMaxFromTruck; d += 0.5f)
                {
                    if (Mathf.Abs(d) < StopMinFromTruck) continue;
                    float s = truckS + d;
                    if (s < halfL + 2f || s > lane.Length - halfL - 2f) continue;
                    if (Mathf.Abs(s - ambS) < StopMinFromAmbulance) continue;
                    tested++;
                    Vector3 center = lane.PointAt(s) + lane.RightAt(s) * lateral;
                    Vector3 fwd = Vector3.ProjectOnPlane(lane.TangentAt(s), Vector3.up).normalized;
                    Vector3 right = Vector3.Cross(Vector3.up, fwd);
                    // Ayak izi: 6 nokta düz zeminde, şerit yüksekliğine yakın
                    bool flat = true; float worstNormal = 1f, maxDy = 0f;
                    foreach (float a in new[] { -halfL, 0f, halfL })
                        foreach (float b in new[] { -halfW, halfW })
                        {
                            Vector3 p = center + fwd * a + right * b;
                            if (!StaticGround(p, truck, out RaycastHit g)) { flat = false; break; }
                            worstNormal = Mathf.Min(worstNormal, g.normal.y);
                            maxDy = Mathf.Max(maxDy, Mathf.Abs(g.point.y - lane.PointAt(s).y));
                            if (g.normal.y < Mathf.Cos(20f * Mathf.Deg2Rad) || maxDy > 0.6f) { flat = false; break; }
                        }
                    if (!flat) continue;
                    flatOk++;
                    // Gövde hacmi: statik engel (korkuluk, çekici, kaya, eşya) yok
                    Vector3 boxCenter = center + Vector3.up * 1.4f;
                    var hits = Physics.OverlapBox(boxCenter, new Vector3(halfW + 0.2f, 1.2f, halfL + 0.3f), Quaternion.LookRotation(fwd, Vector3.up), ~0, QueryTriggerInteraction.Ignore);
                    bool clear = true;
                    foreach (var h in hits)
                    {
                        if (h == null || h.isTrigger) continue;
                        string n = h.transform.root.name;
                        if (n.StartsWith("SM_B1_Ground") || n.StartsWith("SM_B1_Road") || n.StartsWith("SM_B1_RoadMarkings")) continue;
                        clear = false; break;
                    }
                    if (!clear) continue;
                    clearOk++;
                    float score = (side > 0f ? 10f : 0f) - Mathf.Abs(Mathf.Abs(d) - 20f) * 0.3f + worstNormal * 5f - maxDy * 2f + (lane == east ? 1f : 0f);
                    if (score > bestScore)
                    {
                        bestScore = score; bestLane = lane; bestS = s; bestLateral = lateral;
                        bestPos = center;
                        if (StaticGround(center, truck, out RaycastHit gc)) bestPos.y = gc.point.y;
                        notes.Clear();
                        notes.Add(lane.name + " " + (side > 0 ? "sağ" : "sol") + ", çekiciye " + Mathf.Abs(d).ToString("F1", Inv) + " m, en kötü eğim " + (Mathf.Acos(worstNormal) * Mathf.Rad2Deg).ToString("F1", Inv) + "°, şeride yükseklik farkı " + maxDy.ToString("F2", Inv) + " m");
                    }
                }
            }
        }
        info = "denenen " + tested + ", düz " + flatOk + ", engelsiz " + clearOk + (notes.Count > 0 ? "; seçim: " + notes[0] : "");
        return bestLane != null;
    }

    private static bool StaticGround(Vector3 p, Transform truck, out RaycastHit ground)
    {
        ground = default;
        float best = float.MaxValue;
        foreach (var h in Physics.RaycastAll(p + Vector3.up * 3f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.isTrigger || GroundProbe.IsMovingGameObject(h.collider) || (truck != null && h.transform.IsChildOf(truck))) continue;
            if (h.distance < best) { best = h.distance; ground = h; }
        }
        return best < float.MaxValue;
    }

    private static void WriteReport()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, report.ToString());
        Debug.Log(report.ToString());
    }
}

// Hırsız modelleri import ayarı (Assets/Models/Characters/Thief): Humanoid, Avatar bu modelden, kas limitleri varsayılan, klip yok.
// Ayarlar bir kez yazılır (işaret userData'da); kaynak avatarın (SK_RescueCrew_Blue) hiyerarşi adı farklı olduğundan kopya değil.
internal class ThiefImportSettings : AssetPostprocessor
{
    public const string AppliedMarker = "ThiefDefaults:v1";

    private void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(Bolum1ThiefBuilder.ModelFolder) || !assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return;
        var importer = (ModelImporter)assetImporter;
        if (importer.userData == AppliedMarker) return;
        Apply(importer);
    }

    public static void Apply(ModelImporter importer)
    {
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.bakeAxisConversion = false;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = false;
        importer.importBlendShapes = false;
        var description = importer.humanDescription;
        if (description.human != null && description.human.Length > 0)
        {
            var bones = description.human;
            for (int i = 0; i < bones.Length; i++) { var limit = bones[i].limit; limit.useDefaultValues = true; bones[i].limit = limit; }
            description.human = bones;
            importer.humanDescription = description;
            importer.userData = AppliedMarker; // insan listesi hazırsa işaret konur; ilk importta boşsa sonraki reimport'ta
        }
    }

    public static bool IsApplied(ModelImporter importer)
    {
        return importer.userData == AppliedMarker && importer.animationType == ModelImporterAnimationType.Human && !importer.importAnimation
            && importer.globalScale == 1f && importer.useFileScale && !importer.bakeAxisConversion;
    }
}
