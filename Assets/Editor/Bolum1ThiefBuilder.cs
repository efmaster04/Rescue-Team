using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

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
    // U4c: kasa içi
    public const string CargoMaterialPath = Bolum1AmbulanceBuilder.MaterialFolder + "/M_ThiefVan_Cargo.mat";
    public const string OpenBodyMeshName = "SM_ThiefVan_Body_Open";
    public const string OpenBodyMeshPath = Bolum1TrafficBuilder.PrefabFolder + "/" + OpenBodyMeshName + ".asset";
    public const string CargoInteriorName = "CargoInterior", RearFrameName = "RearFrame";
    public static readonly Color CargoColor = new Color(0.16f, 0.165f, 0.18f); // koyu iç gri
    // Arka yüz kesimi: gövdenin arka düzleminden bu kadar derin (m), tampon üstünden (y ≥) başlar
    public const float RearCutDepth = 0.2f, RearCutMinY = 0.40f;
    // Ön bölme (koltukların arkası; iç mesh koltuk arkası z ≈ 0.69) ve panel kalınlığı
    public const float CargoFrontZ = 0.5f, CargoPanelThickness = 0.04f;
    public const string ReportPath = "Captures/Bolum1/thief_build_report.txt";
    public const string NavMeshPath = Bolum1AmbulanceBuilder.SettingsFolder + "/ThiefNavMesh.asset";
    // NavMesh ajanı (hırsız): yarıçap, boy, eğim (kullanıcı kararı: ≤ 35° yürünebilir), basamak
    public const float AgentRadius = 0.3f, AgentHeight = 1.5f, AgentSlope = 35f, AgentClimb = 0.5f;
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
    // Batchmode: -executeMethod Bolum1ThiefBuilder.BuildAssetsBatch (U4b: kamyonet prefab'ına ön kapılar; sahne kurulumu gerekmez)
    public static void BuildAssetsBatch()
    {
        try
        {
            BuildAssets();
            Debug.Log(report.ToString());
            Debug.Log("THIEF BUILD OK");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("THIEF BUILD FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }

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
            // Hareket: NavMeshAgent (hızı Thief verir); diğer hırsızlardan kaçınır, oyuncuyla çarpışma kapsülden
            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = AgentRadius;
            agent.height = AgentHeight;
            agent.speed = settings.sneakSpeed;
            agent.acceleration = 12f;
            agent.angularSpeed = 720f;
            agent.stoppingDistance = 0.2f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
            agent.avoidancePriority = 50;
            thief.agent = agent;
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
            // U4b: arka kapaklar ortak "RearDoors" kökünde (U1 beyaz vurgusu bagaj açıkken ikisini birden çizer)
            var rearRoot = new GameObject("RearDoors").transform;
            rearRoot.SetParent(go.transform, false);
            Transform MakeDoor(string name, float sideSign)
            {
                var hinge = new GameObject(name).transform;
                hinge.SetParent(rearRoot, false);
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
            // U4b: ön kapılar (şoför sol, yolcu sağ): gövde yan düzleminde ince levha, menteşe ön kenarda, 0.9 m geniş, 0.55–1.75 m
            float frontZ = follower.frontOverhang - 1.4f;
            float doorW = 0.9f, frontBottom = 0.55f, frontTop = Mathf.Min(top, 1.75f);
            float sideX = vehicle.bodySize.x * 0.5f + 0.01f;
            Transform MakeFrontDoor(string name, float sideSign)
            {
                var hinge = new GameObject(name).transform;
                hinge.SetParent(go.transform, false);
                hinge.localPosition = new Vector3(sideSign * sideX, frontBottom, frontZ);
                var leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.Object.DestroyImmediate(leaf.GetComponent<Collider>());
                leaf.name = "Leaf";
                leaf.transform.SetParent(hinge, false);
                leaf.transform.localPosition = new Vector3(0f, (frontTop - frontBottom) * 0.5f, -doorW * 0.5f);
                leaf.transform.localScale = new Vector3(0.05f, frontTop - frontBottom, doorW - 0.02f);
                leaf.GetComponent<MeshRenderer>().sharedMaterial = door;
                return hinge;
            }
            var frontLeft = MakeFrontDoor("Door_FrontL", -1f);
            var frontRight = MakeFrontDoor("Door_FrontR", 1f);
            // U4c (kullanıcı: "bagaj açılıyor ama içinde hâlâ kapı var, eşyaları göremiyoruz"): PanelVan gövdesi kapalı bir kabuk;
            // sahte kapaklar açılınca arkada gövdenin kendi arka yüzü kalıyordu. Gövde mesh'inin kopyasında arka yüz (tampon üstü)
            // kesilir, içeri koyu bir kasa (zemin, iki yan duvar, tavan, ön bölme) ve kesik kenarı örten ince bir çerçeve kurulur.
            float bodyTop = vehicle.bodyCenter.y + vehicle.bodySize.y * 0.5f;
            string cutInfo = CutRearFace(go, rearZ, out int cutTris, out int keptTris);
            var cargoMat = AssetDatabase.LoadAssetAtPath<Material>(CargoMaterialPath);
            if (cargoMat == null) { cargoMat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(cargoMat, CargoMaterialPath); }
            cargoMat.SetColor("_BaseColor", CargoColor);
            cargoMat.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(cargoMat);
            float wi = vehicle.bodySize.x * 0.5f - 0.05f;          // kasa iç yarı genişliği (duvar iç yüzü)
            float cargoRear = rearZ - 0.02f;                        // gövdenin arka düzlemi
            float cargoFront = CargoFrontZ;                         // ön bölme iç yüzü (koltukların arkası)
            float panel = CargoPanelThickness;
            float midZ = (cargoRear + cargoFront) * 0.5f, lenZ = cargoFront - cargoRear;
            var cargoRoot = new GameObject(CargoInteriorName).transform;
            cargoRoot.SetParent(go.transform, false);
            MakePanel(cargoRoot, "Floor", new Vector3(0f, bottom, midZ), new Vector3(wi * 2f + panel, panel, lenZ), cargoMat);
            MakePanel(cargoRoot, "Ceiling", new Vector3(0f, top, midZ), new Vector3(wi * 2f + panel, panel, lenZ), cargoMat);
            MakePanel(cargoRoot, "Wall_L", new Vector3(-(wi + panel * 0.5f), (bottom + top) * 0.5f, midZ), new Vector3(panel, top - bottom + panel, lenZ), cargoMat);
            MakePanel(cargoRoot, "Wall_R", new Vector3(wi + panel * 0.5f, (bottom + top) * 0.5f, midZ), new Vector3(panel, top - bottom + panel, lenZ), cargoMat);
            MakePanel(cargoRoot, "Bulkhead", new Vector3(0f, (bottom + top) * 0.5f, cargoFront + panel * 0.5f), new Vector3(wi * 2f + panel, top - bottom + panel, panel), cargoMat);
            // Çerçeve: açıklığın çevresi (kapak üstü–tavan, kapak altı–tampon, iki yan şerit) dış boyada; kapalıyken dış görünüş aynı
            var frame = new GameObject(RearFrameName).transform;
            frame.SetParent(go.transform, false);
            float frameBottom = RearCutMinY - 0.05f, bodyHalfW = vehicle.bodySize.x * 0.5f;
            MakePanel(frame, "Top", new Vector3(0f, (top + bodyTop) * 0.5f, rearZ), new Vector3(bodyHalfW * 2f, bodyTop - top, 0.06f), paint);
            MakePanel(frame, "Bottom", new Vector3(0f, (frameBottom + bottom) * 0.5f, rearZ), new Vector3(bodyHalfW * 2f, bottom - frameBottom, 0.06f), paint);
            MakePanel(frame, "Side_L", new Vector3(-(halfW + bodyHalfW) * 0.5f, (frameBottom + bodyTop) * 0.5f, rearZ), new Vector3(bodyHalfW - halfW, bodyTop - frameBottom, 0.06f), paint);
            MakePanel(frame, "Side_R", new Vector3((halfW + bodyHalfW) * 0.5f, (frameBottom + bodyTop) * 0.5f, rearZ), new Vector3(bodyHalfW - halfW, bodyTop - frameBottom, 0.06f), paint);
            var van = go.AddComponent<ThiefVan>();
            van.doorLeft = left;
            van.doorRight = right;
            van.rearDoors = rearRoot;
            van.doorFrontLeft = frontLeft;
            van.doorFrontRight = frontRight;
            van.frontDoorOpenAngle = 70f;
            van.doorLeftOpenAngle = -150f;
            van.doorRightOpenAngle = 150f;
            van.cargoRoot = cargoRoot;
            van.cargoFloorY = bottom + panel * 0.5f;
            van.cargoCeilingY = top - panel * 0.5f;
            van.cargoHalfWidth = wi;
            van.cargoRearZ = cargoRear;
            van.cargoFrontZ = cargoFront;
            Line("Kasa (U4c): arka yüz kesildi (" + cutTris + " üçgen çıktı, " + keptTris + " kaldı; " + cutInfo + ") → " + OpenBodyMeshPath + "; iç kasa zemin y " + van.cargoFloorY.ToString("F2", Inv) + ", tavan y " + van.cargoCeilingY.ToString("F2", Inv)
                + ", yarı genişlik " + wi.ToString("F2", Inv) + ", z " + cargoRear.ToString("F2", Inv) + " … " + cargoFront.ToString("F2", Inv) + " (" + lenZ.ToString("F2", Inv) + " m), renk #" + ColorUtility.ToHtmlStringRGB(CargoColor) + "; yuva " + ThiefVan.CargoFloorSlots + " (2 × 3, sütun ±" + ThiefVan.CargoColumnX.ToString(Inv) + " m), fazlası üst üste");
            // Park edince NavMesh'i oyar: hırsızlar gövdenin içinden geçmez (hareket hâlinde oyma yok)
            var obstacle = go.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = vehicle.bodyCenter;
            obstacle.size = vehicle.bodySize;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
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

    // U4c: gövde mesh'inin kopyasında arka yüzü kes. Kök uzayında z ≤ rearZ + RearCutDepth ve bütün köşeleri y ≥ RearCutMinY olan
    // üçgenler (far/stop lambası alt mesh'leri hariç) çıkarılır. Kopya Assets'e kaydedilir; gövde MeshFilter'ı ona bağlanır.
    private static string CutRearFace(GameObject root, float rearZ, out int cut, out int kept)
    {
        cut = 0; kept = 0;
        var filters = root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null && f.name.EndsWith("_Body")).ToArray();
        if (filters.Length != 1) throw new Exception("Kamyonet gövde mesh'i bulunamadı (" + filters.Length + " aday: " + string.Join(", ", filters.Select(f => f.name)) + ")");
        var mf = filters[0];
        var src = mf.sharedMesh;
        var renderer = mf.GetComponent<MeshRenderer>();
        var open = UnityEngine.Object.Instantiate(src);
        open.name = OpenBodyMeshName;
        var verts = src.vertices.Select(v => root.transform.InverseTransformPoint(mf.transform.TransformPoint(v))).ToArray();
        float zCut = rearZ + RearCutDepth;
        var perSub = new List<string>();
        for (int s = 0; s < src.subMeshCount; s++)
        {
            var tris = src.GetTriangles(s);
            string mat = renderer != null && s < renderer.sharedMaterials.Length && renderer.sharedMaterials[s] != null ? renderer.sharedMaterials[s].name : "?";
            bool lamp = mat.Contains("light") || mat.Contains("Amber");
            var keepList = new List<int>(tris.Length);
            int removed = 0;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
                bool rear = !lamp && a.z <= zCut && b.z <= zCut && c.z <= zCut && a.y >= RearCutMinY && b.y >= RearCutMinY && c.y >= RearCutMinY;
                if (rear) { removed++; continue; }
                keepList.Add(tris[i]); keepList.Add(tris[i + 1]); keepList.Add(tris[i + 2]);
            }
            open.SetTriangles(keepList, s);
            cut += removed;
            kept += keepList.Count / 3;
            if (removed > 0) perSub.Add(mat + " −" + removed);
        }
        if (cut == 0) throw new Exception("Kamyonet gövdesinde arka yüz üçgeni bulunamadı (z ≤ " + zCut.ToString("F2", Inv) + ", y ≥ " + RearCutMinY.ToString(Inv) + ")");
        open.RecalculateBounds();
        if (AssetDatabase.LoadAssetAtPath<Mesh>(OpenBodyMeshPath) != null) AssetDatabase.DeleteAsset(OpenBodyMeshPath);
        AssetDatabase.CreateAsset(open, OpenBodyMeshPath);
        mf.sharedMesh = open;
        return "kesim z ≤ " + zCut.ToString("F2", Inv) + ", y ≥ " + RearCutMinY.ToString(Inv) + ": " + string.Join(", ", perSub);
    }

    private static Transform MakePanel(Transform parent, string name, Vector3 center, Vector3 size, Material mat)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = center;
        cube.transform.localScale = size;
        cube.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return cube.transform;
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

        // 2) NavMesh: fiziksel collider'lardan (zemin, yol, korkuluk, kayalar, minibüs, çekici); oyuncu, bot, hastalar, sedyeler, eşyalar hariç
        var path = ((List<object>)Bolum1Coordinates.Obj(markers, "geometry_probes")["path"]).Select(p => Bolum1Coordinates.ToUnity(Bolum1Coordinates.Vec3(p))).ToList();
        var ramp = ((List<object>)Bolum1Coordinates.Obj(markers, "geometry_probes")["ramp"]).Select(p => Bolum1Coordinates.ToUnity(Bolum1Coordinates.Vec3(p))).ToList();
        float pathY = path.Average(p => p.y);
        float roadY = east.PointAt(east.Project(truck.transform.position)).y;
        var navData = BakeNavMesh(minivan, truck, character, out string navInfo);
        if (AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshPath) != null) AssetDatabase.DeleteAsset(NavMeshPath);
        AssetDatabase.CreateAsset(navData, NavMeshPath);
        // Ölçüm: yol, patika, rampa ve çekici banketi NavMesh'te; kaya çıkıntısı değil
        var instance = NavMesh.AddNavMeshData(navData);
        try
        {
            bool On(Vector3 p) { return NavMesh.SamplePosition(p, out NavMeshHit h, 0.6f, NavMesh.AllAreas); }
            int pathOn = path.Count(p => { Vector3 q = p; if (GroundProbe.Find(q + Vector3.up * 2f, 6f, out RaycastHit g)) q.y = g.point.y; return On(q); });
            int rampOn = ramp.Count(p => { Vector3 q = p; if (GroundProbe.Find(q + Vector3.up * 2f, 6f, out RaycastHit g)) q.y = g.point.y; return On(q); });
            bool stopOn = On(stopPos);
            bool roadOn = On(east.PointAt(east.Project(truck.transform.position)));
            Vector3 ledge = Bolum1Coordinates.ToUnity(Bolum1Coordinates.Vec3(Bolum1Coordinates.Obj(Bolum1Coordinates.Obj(markers, "markers"), "LedgeZone"), "position"));
            bool ledgeOn = On(ledge);
            var navPath = new NavMeshPath();
            bool roadToPath = NavMesh.SamplePosition(east.PointAt(east.Project(truck.transform.position)), out NavMeshHit a, 1f, NavMesh.AllAreas)
                && NavMesh.SamplePosition(new Vector3(path[path.Count / 2].x, pathY, path[path.Count / 2].z), out NavMeshHit b, 1.5f, NavMesh.AllAreas)
                && NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, navPath) && navPath.status == NavMeshPathStatus.PathComplete;
            float len = 0f; for (int i = 1; i < navPath.corners.Length; i++) len += Vector3.Distance(navPath.corners[i - 1], navPath.corners[i]);
            Line("NavMesh: " + NavMeshPath + " (" + navInfo + "); patika " + pathOn + "/" + path.Count + ", rampa " + rampOn + "/" + ramp.Count + ", durak " + stopOn + ", yol " + roadOn + ", kaya çıkıntısı " + ledgeOn
                + " (beklenen: hayır); yol → patika yolu " + roadToPath + " (" + len.ToString("F1", Inv) + " m); yol seviyesi y " + roadY.ToString("F2", Inv) + ", patika y " + pathY.ToString("F2", Inv));
            if (pathOn < path.Count / 2 || rampOn < ramp.Count / 2 || !stopOn || !roadToPath) throw new Exception("NavMesh eksik: patika " + pathOn + "/" + path.Count + ", rampa " + rampOn + "/" + ramp.Count + ", durak " + stopOn + ", yol→patika " + roadToPath);
        }
        finally { instance.Remove(); }

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
        director.navMeshData = navData;
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

    // Her iki şeridin de dışında (karşı şeridi de kapatmaz), kamyonet ayak izi düz (≤ 20°), statik engel yok, şeride göre yükseklik farkı
    // küçük. Tercih: kendi şeridinin sağı (banket), çekiciye ~20 m. 15–25 m'de yer yoksa aralık 8–30, sonra 5–40 m'ye genişletilir (rapor edilir).
    private static bool FindVanStop(LanePath[] lanes, Transform truck, Transform ambStop, LanePath east, float halfW, float halfL, float clearance,
        out LanePath bestLane, out float bestS, out float bestLateral, out Vector3 bestPos, out string info)
    {
        bestLane = null; bestS = 0f; bestLateral = 0f; bestPos = Vector3.zero;
        float bestScore = float.MinValue;
        var notes = new List<string>();
        var diag = new Dictionary<string, int>();
        var blockers = new Dictionary<string, int>();
        var flatSpots = new List<string>();
        void Count(string k) { diag[k] = (diag.TryGetValue(k, out int n) ? n : 0) + 1; }
        int tested = 0;
        string range = "";
        foreach (var window in new[] { new Vector2(StopMinFromTruck, StopMaxFromTruck), new Vector2(8f, 30f), new Vector2(5f, 40f) })
        {
            range = window.x.ToString("F0", Inv) + "–" + window.y.ToString("F0", Inv) + " m";
            foreach (var lane in lanes)
            {
                float truckS = lane.Project(truck.position);
                float ambS = lane == east ? east.Project(ambStop.position) : -9999f;
                foreach (float side in new[] { 1f, -1f })
                    for (float extra = 0f; extra <= 2.51f; extra += 0.5f) // banket daha dıştaysa yanal ofset artar
                    {
                        float lateral = side * (lane.width * 0.5f + clearance + halfW + extra);
                        for (float d = -window.y; d <= window.y; d += 0.5f)
                        {
                            if (Mathf.Abs(d) < window.x) continue;
                            float s = truckS + d;
                            if (s < halfL + 2f || s > lane.Length - halfL - 2f) continue;
                            if (Mathf.Abs(s - ambS) < StopMinFromAmbulance) continue;
                            tested++;
                            string tag = lane.name.Replace("TrafficLane_", "") + (side > 0 ? "/sağ" : "/sol");
                            Vector3 center = lane.PointAt(s) + lane.RightAt(s) * lateral;
                            Vector3 fwd = Vector3.ProjectOnPlane(lane.TangentAt(s), Vector3.up).normalized;
                            Vector3 right = Vector3.Cross(Vector3.up, fwd);
                            // Hiçbir şeridi kapatmaz: gövde köşeleri her şeridin orta çizgisinden en az yarı genişlik + boşluk uzakta
                            bool offLanes = true;
                            foreach (var other in lanes)
                                foreach (float a in new[] { -halfL, halfL })
                                    foreach (float b in new[] { -halfW, halfW })
                                    {
                                        Vector3 corner = center + fwd * a + right * b;
                                        other.Project(corner, out float lat, out Vector3 on);
                                        if (Mathf.Abs(lat) < other.width * 0.5f + clearance - 0.05f && Mathf.Abs(corner.y - on.y) < 2f) { offLanes = false; }
                                    }
                            if (!offLanes) { Count(tag + " şeritte"); continue; }
                            // Ayak izi: 6 nokta düz zeminde (≤ 25°), şerit yüksekliğine yakın (≤ 0.8 m)
                            bool flat = true; float worstNormal = 1f, maxDy = 0f, groundY = center.y; string flatWhy = "";
                            foreach (float a in new[] { -halfL, 0f, halfL })
                                foreach (float b in new[] { -halfW, halfW })
                                {
                                    Vector3 p = center + fwd * a + right * b;
                                    if (!StaticGround(p, truck, out RaycastHit g)) { flat = false; flatWhy = "zemin yok"; break; }
                                    worstNormal = Mathf.Min(worstNormal, g.normal.y);
                                    maxDy = Mathf.Max(maxDy, Mathf.Abs(g.point.y - lane.PointAt(s).y));
                                    groundY = Mathf.Max(groundY, g.point.y);
                                    if (g.normal.y < Mathf.Cos(25f * Mathf.Deg2Rad)) { flat = false; flatWhy = "eğim>25"; break; }
                                    if (maxDy > 0.8f) { flat = false; flatWhy = "kot>0.8"; break; }
                                }
                            if (!flat) { Count(tag + " " + flatWhy); continue; }
                            // Gövde hacmi (zeminin 0.3 m üstünden başlar): statik engel (korkuluk, çekici, kaya, eşya) yok
                            Vector3 boxCenter = new Vector3(center.x, groundY + 0.3f + 1.1f, center.z);
                            var hits = Physics.OverlapBox(boxCenter, new Vector3(halfW + 0.2f, 1.1f, halfL + 0.3f), Quaternion.LookRotation(fwd, Vector3.up), ~0, QueryTriggerInteraction.Ignore);
                            string blocker = null;
                            foreach (var h in hits)
                            {
                                if (h == null || h.isTrigger) continue;
                                string n = h.transform.root.name;
                                if (n.StartsWith("SM_B1_Ground") || n.StartsWith("SM_B1_Road") || n.StartsWith("SM_B1_RoadMarkings")) continue;
                                blocker = h.transform.root.name + "/" + h.name; break;
                            }
                            if (blocker != null)
                            {
                                Count(tag + " engel");
                                blockers[blocker] = (blockers.TryGetValue(blocker, out int bn) ? bn : 0) + 1;
                                if (flatSpots.Count < 12) flatSpots.Add(tag + " d " + d.ToString("F1", Inv) + " yanal " + lateral.ToString("F1", Inv) + " → " + blocker);
                                continue;
                            }
                            Count(tag + " UYGUN");
                            float score = (side > 0f ? 10f : 0f) - Mathf.Abs(Mathf.Abs(d) - 20f) * 0.3f + worstNormal * 5f - maxDy * 2f + (lane == east ? 1f : 0f) - extra * 2f;
                            if (score > bestScore)
                            {
                                bestScore = score; bestLane = lane; bestS = s; bestLateral = lateral;
                                bestPos = center;
                                if (StaticGround(center, truck, out RaycastHit gc)) bestPos.y = gc.point.y;
                                notes.Clear();
                                notes.Add(lane.name + " " + (side > 0 ? "sağ" : "sol") + ", çekiciye " + Mathf.Abs(d).ToString("F1", Inv) + " m, ek yanal " + extra.ToString("F1", Inv) + " m, en kötü eğim " + (Mathf.Acos(worstNormal) * Mathf.Rad2Deg).ToString("F1", Inv) + "°, şeride yükseklik farkı " + maxDy.ToString("F2", Inv) + " m");
                            }
                        }
                    }
            }
            if (bestLane != null) break;
        }
        info = "aralık " + range + ": denenen " + tested + "; " + string.Join(", ", diag.OrderBy(k => k.Key).Select(k => k.Key + " " + k.Value))
            + (blockers.Count > 0 ? "; engeller: " + string.Join(", ", blockers.OrderByDescending(b => b.Value).Take(6).Select(b => b.Key + "×" + b.Value)) : "")
            + (flatSpots.Count > 0 ? "; düz ama engelli: " + string.Join(" | ", flatSpots) : "")
            + (notes.Count > 0 ? "; seçim: " + notes[0] : "");
        return bestLane != null;
    }

    // NavMesh fırını: sahnedeki fiziksel collider'lar (etkin olanlar); oyuncu, bot, hastalar, sedyeler, eşya prop'ları ve ambulans
    // durağı hariç (hareketli / alınabilir şeyler NavMesh'e delik açmasın). Ajan: yarıçap 0.3, boy 1.5, eğim 35°, basamak 0.5, voksel 0.1.
    private static NavMeshData BakeNavMesh(GameObject minivan, GameObject truck, GameObject character, out string info)
    {
        var settings = NavMesh.GetSettingsByID(0);
        settings.agentRadius = AgentRadius;
        settings.agentHeight = AgentHeight;
        settings.agentSlope = AgentSlope;
        settings.agentClimb = AgentClimb;
        settings.minRegionArea = 1.5f;
        settings.overrideVoxelSize = true;
        settings.voxelSize = 0.1f;
        settings.overrideTileSize = true;
        settings.tileSize = 128;
        var markups = new List<NavMeshBuildMarkup>();
        void Ignore(Transform t) { if (t != null) markups.Add(new NavMeshBuildMarkup { root = t, ignoreFromBuild = true }); }
        Ignore(character.transform);
        var bot = UnityEngine.Object.FindAnyObjectByType<StretcherHelperBot>();
        if (bot != null) Ignore(bot.transform);
        foreach (var p in UnityEngine.Object.FindObjectsByType<Patient>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Ignore(p.transform);
        foreach (var s in UnityEngine.Object.FindObjectsByType<StretcherController>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Ignore(s.transform);
        var props = GameObject.Find(Bolum1LootBuilder.PropsRootName);
        if (props != null) Ignore(props.transform);
        var coil = UnityEngine.Object.FindAnyObjectByType<RopeCoil>();
        if (coil != null) Ignore(coil.transform);
        // Sınırlar: zemin + yol renderer sınırları (+ pay)
        Bounds bounds = new Bounds(Vector3.zero, Vector3.one);
        bool any = false;
        foreach (var n in new[] { "SM_B1_Ground", "SM_B1_Road" })
        {
            var go = GameObject.Find(n);
            if (go == null) continue;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
            }
        }
        bounds.Expand(new Vector3(4f, 10f, 4f));
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
        var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        if (data == null) throw new Exception("NavMesh fırınlanamadı");
        data.name = "ThiefNavMesh";
        info = sources.Count + " kaynak, sınır " + V(bounds.size) + ", ajan r " + AgentRadius.ToString(Inv) + " h " + AgentHeight.ToString(Inv) + " eğim " + AgentSlope.ToString(Inv) + "° basamak " + AgentClimb.ToString(Inv) + " voksel " + settings.voxelSize.ToString("F2", Inv);
        return data;
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
