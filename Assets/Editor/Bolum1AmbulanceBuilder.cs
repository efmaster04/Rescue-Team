using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RescueTeam.Bolum1;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Yalnız sahip olunan prefab ve sahneye yazar; model importer'larını değiştirmez.
public static class Bolum1AmbulanceBuilder
{
    public const string Prefabs = "Assets/Prefabs/Bolum1/";
    public const string Settings = "Assets/Settings/Bolum1/";
    const string Crew = "Assets/Models/Characters/RescueCrew/SK_RescueCrew_";
    static Material white, red, dark, blue, canvas, grey;

    [MenuItem("Rescue Team/Bölüm 1/Ambulans döngüsünü kur")]
    public static void Build()
    {
        Directory.CreateDirectory(Prefabs); Directory.CreateDirectory(Settings); AssetDatabase.Refresh();
        white = Material("AmbulanceWhite", new Color(0.85f, 0.88f, 0.88f));
        red = Material("AmbulanceRed", new Color(0.7f, 0.025f, 0.015f));
        dark = Material("Rubber", new Color(0.035f, 0.045f, 0.06f));
        blue = Material("EmergencyBlue", new Color(0.02f, 0.15f, 0.7f)); blue.EnableKeyword("_EMISSION");
        canvas = Material("StretcherCanvas", new Color(0.2f, 0.35f, 0.22f));
        grey = Material("PatientPlaceholder", new Color(0.42f, 0.42f, 0.42f));
        var schedule = Setting<AmbulanceSchedule>("AmbulanceSchedule");
        var rules = Setting<CarryRules>("CarryRules");
        var locomotion = Locomotion();
        var ambulance = BuildAmbulance(); var stretcher = BuildStretcher(rules);
        var patients = new[] { BuildPatient("Tourist", "Turist"), BuildPatient("Civilian", "Sivil"), BuildPatient("Elder", "Yaşlı") };
        EditorSceneManager.OpenScene(Bolum1SceneBuilder.ScenePath);
        Install(schedule, rules, locomotion, ambulance, stretcher, patients);
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("AMBULANCE BUILD OK");
    }
    public static void BuildBatch()
    {
        try { Build(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    static T Setting<T>(string name) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(Settings + name + ".asset");
        if (!asset) { asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, Settings + name + ".asset"); }
        return asset;
    }
    static Material Material(string name, Color color)
    {
        string path = Prefabs + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", 0.25f); EditorUtility.SetDirty(material); return material;
    }
    public static Transform Part(GameObject root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
    static Transform Empty(Transform parent, string name, Vector3 position)
    {
        var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = position; return t;
    }
    static Transform Box(Transform parent, string name, Vector3 position, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
        go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material; Object.DestroyImmediate(go.GetComponent<Collider>()); return go.transform;
    }
    static GameObject Model(string path, Transform parent)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!asset) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset); go.transform.SetParent(parent, false); return go;
    }
    static T Save<T>(GameObject root, string name) where T : Component
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, Prefabs + name + ".prefab"); Object.DestroyImmediate(root); return prefab.GetComponent<T>();
    }
    static AmbulanceController BuildAmbulance()
    {
        var go = new GameObject("SM_Ambulance");
        var model = Model("Assets/Models/Vehicles/Ambulance/SM_Ambulance.fbx", go.transform);
        if (model)
        {
            model.name = "AmbulanceVisual";
            var load = Part(go, "LoadPoint");
            if (!load) throw new Exception("Ambulans modelinde LoadPoint eksik");
            Vector3 rear = Vector3.ProjectOnPlane(load.position - go.transform.position, Vector3.up);
            model.transform.rotation = Quaternion.FromToRotation(rear.normalized, Vector3.back) * model.transform.rotation;
        }
        else
        {
            // Arka taraf açık bırakılır; hasta yuvaları ve çift kapı dışarıdan görülebilir.
            Box(go.transform, "SM_Ambulance_Body", new Vector3(0, 1.2f, 1.8f), new Vector3(2, 2, 2.4f), white);
            Box(go.transform, "SM_Ambulance_Interior", new Vector3(0, 0.65f, -1.2f), new Vector3(1.9f, 0.12f, 3.6f), white);
            Box(go.transform, "Roof", new Vector3(0, 2.5f, -0.8f), new Vector3(2, 0.15f, 4.4f), white);
            foreach (float side in new[] { -1f, 1f })
            {
                Box(go.transform, "Side", new Vector3(side * 0.97f, 1.6f, -1.1f), new Vector3(0.06f, 1.8f, 3.8f), white);
                Box(go.transform, "Stripe", new Vector3(side * 1.005f, 1.35f, 0), new Vector3(0.025f, 0.25f, 5.8f), red);
            }
            Box(go.transform, "SM_Ambulance_Glass", new Vector3(0, 1.9f, 3.01f), new Vector3(1.7f, 0.6f, 0.02f), dark);
            var left = Empty(go.transform, "SM_Ambulance_Door_RL", new Vector3(-1, 0.7f, -3));
            var right = Empty(go.transform, "SM_Ambulance_Door_RR", new Vector3(1, 0.7f, -3));
            Box(left, "Panel", new Vector3(0.5f, 0.85f, 0), new Vector3(0.98f, 1.7f, 0.06f), white);
            Box(right, "Panel", new Vector3(-0.5f, 0.85f, 0), new Vector3(0.98f, 1.7f, 0.06f), white);
            var step = Empty(go.transform, "SM_Ambulance_Step_Rear", new Vector3(0, 0.45f, -3));
            Box(step, "Step", new Vector3(0, -0.22f, 0), new Vector3(1.6f, 0.44f, 0.07f), dark);
            foreach (string wheel in new[] { "FL", "FR", "RL", "RR" })
            {
                var w = Empty(go.transform, "SM_Ambulance_Wheel_" + wheel, new Vector3(wheel.EndsWith("L") ? -0.9f : 0.9f, 0.36f, wheel.StartsWith("F") ? 1.9f : -1.9f));
                var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder); cylinder.name = "Tire"; cylinder.transform.SetParent(w, false);
                cylinder.transform.localRotation = Quaternion.Euler(0, 0, 90); cylinder.transform.localScale = new Vector3(0.72f, 0.12f, 0.72f);
                cylinder.GetComponent<Renderer>().sharedMaterial = dark; Object.DestroyImmediate(cylinder.GetComponent<Collider>());
            }
            var bar = Empty(go.transform, "SM_Ambulance_Lightbar", new Vector3(0, 2.6f, 1.7f));
            Box(bar, "FlashLeft", new Vector3(-0.55f, 0, 0), new Vector3(0.65f, 0.1f, 0.3f), blue);
            Box(bar, "FlashRight", new Vector3(0.55f, 0, 0), new Vector3(0.65f, 0.1f, 0.3f), blue);
            Box(go.transform, "SM_Ambulance_Light_RearL", new Vector3(-0.85f, 2.3f, -3.05f), new Vector3(0.15f, 0.15f, 0.05f), blue);
            Box(go.transform, "SM_Ambulance_Light_RearR", new Vector3(0.85f, 2.3f, -3.05f), new Vector3(0.15f, 0.15f, 0.05f), blue);
            Empty(go.transform, "PatientSlot_01", new Vector3(-0.48f, 0.75f, -1.3f)); Empty(go.transform, "PatientSlot_02", new Vector3(0.48f, 0.75f, -1.3f));
            Empty(go.transform, "LoadPoint", new Vector3(0, 0, -4.2f)); Empty(go.transform, "SirenPoint", new Vector3(0, 2.5f, 1.7f));
        }
        var body = go.AddComponent<Rigidbody>(); body.isKinematic = true; body.mass = 3500;
        body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        var collider = go.AddComponent<BoxCollider>(); collider.center = new Vector3(0, 1.2f, 0); collider.size = new Vector3(2, 2.1f, 6);
        var follower = go.AddComponent<LaneFollower>();
        follower.wheels = new[] { "FL", "FR", "RL", "RR" }.Select(n => Part(go, "SM_Ambulance_Wheel_" + n)).ToArray();
        follower.wheelAxis = follower.wheels[0].InverseTransformDirection(go.transform.right);
        var controller = go.AddComponent<AmbulanceController>(); controller.follower = follower;
        controller.doorLeft = Part(go, "SM_Ambulance_Door_RL"); controller.doorRight = Part(go, "SM_Ambulance_Door_RR"); controller.step = Part(go, "SM_Ambulance_Step_Rear");
        controller.hingeAxis = controller.doorLeft.InverseTransformDirection(Vector3.up);
        controller.stepAxis = controller.step.InverseTransformDirection(Vector3.right);
        controller.loadPoint = Part(go, "LoadPoint");
        controller.patientSlots = new[] { Part(go, "PatientSlot_01"), Part(go, "PatientSlot_02") };
        foreach (var slot in controller.patientSlots) slot.rotation = go.transform.rotation;
        controller.sound = Part(go, "SirenPoint").gameObject.AddComponent<SynthesizedAmbulanceAudio>();
        var flashes = Part(go, "SM_Ambulance_Lightbar").GetComponentsInChildren<Renderer>();
        controller.flashLeft = flashes.Take(Mathf.Max(1, flashes.Length / 2)).Concat(Part(go, "SM_Ambulance_Light_RearL").GetComponentsInChildren<Renderer>()).ToArray();
        controller.flashRight = flashes.Skip(Mathf.Max(1, flashes.Length / 2)).Concat(Part(go, "SM_Ambulance_Light_RearR").GetComponentsInChildren<Renderer>()).ToArray();
        return Save<AmbulanceController>(go, "Ambulance");
    }
    static StretcherController BuildStretcher(CarryRules rules)
    {
        var go = new GameObject("SM_Stretcher");
        var model = Model("Assets/Models/Props/Stretcher/SM_Stretcher.fbx", go.transform);
        if (model)
        {
            model.name = "StretcherVisual";
            Vector3 front = Vector3.ProjectOnPlane(Part(go, "Handle_Front").position - Part(go, "Handle_Rear").position, Vector3.up);
            model.transform.rotation = Quaternion.FromToRotation(front.normalized, Vector3.forward) * model.transform.rotation;
        }
        else
        {
            Box(go.transform, "SM_Stretcher_Mesh", new Vector3(0, 0.1f, 0), new Vector3(0.55f, 0.04f, 1.65f), canvas);
            foreach (float x in new[] { -0.26f, 0.26f }) Box(go.transform, "Rail", new Vector3(x, 0.1f, 0), new Vector3(0.035f, 0.035f, 2.1f), white);
            Empty(go.transform, "Handle_Front", new Vector3(0, 0.12f, 1.05f)); Empty(go.transform, "Handle_Rear", new Vector3(0, 0.12f, -1.05f)); Empty(go.transform, "PatientAnchor", new Vector3(0, 0.12f, 0));
        }
        var s = go.AddComponent<StretcherController>(); s.rules = rules;
        s.frontHandle = Part(go, "Handle_Front"); s.rearHandle = Part(go, "Handle_Rear"); s.patientAnchor = Part(go, "PatientAnchor"); s.patientAnchor.rotation = Quaternion.identity;
        return Save<StretcherController>(go, "Stretcher");
    }
    static Patient BuildPatient(string variant, string displayName)
    {
        var go = new GameObject("SK_Patient_" + variant);
        string path = "Assets/Models/Characters/Patient/";
        var model = Model(path + "SK_Patient_" + variant + ".fbx", go.transform);
        bool placeholder = !model;
        if (!model) model = Model(Crew + "Blue.fbx", go.transform);
        model.name = "PatientVisual"; model.transform.localRotation = Quaternion.identity;
        if (placeholder) foreach (var renderer in model.GetComponentsInChildren<Renderer>()) renderer.sharedMaterials = renderer.sharedMaterials.Select(m => grey).ToArray();
        var p = go.AddComponent<Patient>(); p.displayName = displayName;
        p.animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
        p.animator.applyRootMotion = false;
        if (!p.animator.avatar) p.animator.avatar = AssetDatabase.LoadAssetAtPath<GameObject>(Crew + "Blue.fbx").GetComponent<Animator>().avatar;
        string poseName = variant == "Civilian" ? "Patient_LiePose_ArmSling" : "Patient_LiePose";
        var clip = Clip(path + poseName + ".fbx");
        if (clip)
        {
            var ctrl = Controller(poseName); var state = ctrl.layers[0].stateMachine.AddState("Lie"); state.motion = clip;
            p.animator.runtimeAnimatorController = ctrl; clip.SampleAnimation(model, 0);
        }
        else { p.animator.runtimeAnimatorController = null; p.animator.enabled = false; }
        p.hips = Part(go, "mixamorig:Hips") ?? Part(go, "mixamorigHips");
        if (!p.hips) throw new Exception("Hasta iskeletinde Hips yok: " + variant);
        model.transform.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
        model.transform.localPosition -= p.hips.position - go.transform.position;
        string meta = path + "patient_meta.json";
        if (File.Exists(meta))
        {
            var data = Bolum1Coordinates.LoadJson(meta);
            if (data.TryGetValue("hips_to_back_m", out var distance)) p.hipsToBack = Convert.ToSingle(distance);
        }
        else
        {
            float bottom = float.PositiveInfinity;
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh(); renderer.BakeMesh(mesh);
                foreach (var vertex in mesh.vertices) bottom = Mathf.Min(bottom, renderer.transform.TransformPoint(vertex).y);
                Object.DestroyImmediate(mesh);
            }
            p.hipsToBack = Mathf.Clamp(-bottom, 0.08f, 0.3f);
        }
        BuildRagdoll(p);
        return Save<Patient>(go, "Patient_" + variant);
    }
    static void BuildRagdoll(Patient p)
    {
        string[] names = { "Hips", "Spine", "Spine1", "Head", "LeftArm", "LeftForeArm", "RightArm", "RightForeArm", "LeftUpLeg", "LeftLeg", "RightUpLeg", "RightLeg" };
        var bodies = new List<Rigidbody>(); var colliders = new List<Collider>();
        foreach (string name in names)
        {
            var bone = Part(p.gameObject, "mixamorig:" + name) ?? Part(p.gameObject, "mixamorig" + name);
            if (!bone) continue;
            var body = bone.gameObject.AddComponent<Rigidbody>(); body.mass = name == "Hips" ? 12 : 4;
            body.isKinematic = true; body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var sphere = bone.gameObject.AddComponent<SphereCollider>();
            sphere.radius = (name.Contains("Leg") ? 0.085f : name == "Head" ? 0.105f : 0.075f) / Mathf.Max(0.001f, bone.lossyScale.x); sphere.enabled = false;
            bodies.Add(body); colliders.Add(sphere);
        }
        foreach (var body in bodies)
        {
            var parent = body.transform.parent;
            while (parent && !parent.GetComponent<Rigidbody>()) parent = parent.parent;
            if (!parent) continue;
            var joint = body.gameObject.AddComponent<CharacterJoint>(); joint.connectedBody = parent.GetComponent<Rigidbody>();
            joint.enableProjection = true; joint.enablePreprocessing = false;
            joint.lowTwistLimit = new SoftJointLimit { limit = -25 }; joint.highTwistLimit = new SoftJointLimit { limit = 25 };
            joint.swing1Limit = new SoftJointLimit { limit = 35 }; joint.swing2Limit = new SoftJointLimit { limit = 20 };
        }
        p.ragdollBodies = bodies.ToArray(); p.ragdollColliders = colliders.ToArray();
    }
    static AnimationClip Clip(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    static AnimatorController Controller(string name)
    {
        string path = Prefabs + name + ".controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (!controller) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        foreach (var state in controller.layers[0].stateMachine.states) controller.layers[0].stateMachine.RemoveState(state.state);
        return controller;
    }
    static AnimatorController Locomotion()
    {
        var controller = Controller("AmbulanceCrewLocomotion");
        if (!controller.parameters.Any(p => p.name == "Speed")) controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        var state = controller.layers[0].stateMachine.AddState("Locomotion");
        var blend = new BlendTree { name = "Speed", blendParameter = "Speed", useAutomaticThresholds = false };
        AssetDatabase.AddObjectToAsset(blend, controller);
        blend.AddChild(Clip("Assets/Animations/RescueCrew/Mixamo_Idle.fbx"), 0);
        blend.AddChild(Clip("Assets/Animations/RescueCrew/Mixamo_Walking.fbx"), 1.7f);
        blend.AddChild(Clip("Assets/Animations/RescueCrew/Mixamo_Running.fbx"), 5.5f);
        state.motion = blend; var layers = controller.layers; layers[0].iKPass = true; controller.layers = layers; return controller;
    }
    static void Install(AmbulanceSchedule schedule, CarryRules rules, AnimatorController locomotion,
        AmbulanceController ambulance, StretcherController stretcher, Patient[] patientPrefabs)
    {
        var old = GameObject.Find("Bolum1_AmbulanceMission");
        Vector3? preservedStop = old ? old.GetComponent<MissionDirector>().ambulanceStop.position : (Vector3?)null;
        if (old) Object.DestroyImmediate(old);
        var root = new GameObject("Bolum1_AmbulanceMission"); var mission = root.AddComponent<MissionDirector>();
        mission.schedule = schedule; mission.ambulancePrefab = ambulance;
        var markers = Bolum1Coordinates.LoadJson(Bolum1Coordinates.MarkersPath);
        var lane = Empty(root.transform, "TrafficLane_EastBound", Vector3.zero).gameObject.AddComponent<LanePath>();
        var data = Bolum1Coordinates.Obj(Bolum1Coordinates.Obj(markers, "markers"), "TrafficLane_EastBound");
        lane.points = ((List<object>)data["polyline"]).Select(p => Bolum1Coordinates.ToUnity(Bolum1Coordinates.Vec3(p))).ToArray(); lane.Rebuild(); mission.lane = lane;
        Physics.SyncTransforms();
        float mouth = FindRampMouth(lane);
        var stop = Empty(root.transform, "AmbulanceStop", lane.Sample(mouth + 7, out var tangent));
        if (preservedStop.HasValue) stop.position = lane.Sample(lane.Project(preservedStop.Value), out tangent);
        stop.rotation = Quaternion.LookRotation(tangent); mission.ambulanceStop = stop;
        Debug.Log("PLACEMENT AmbulanceStop=" + stop.position.ToString("F3") + " rampMouth=" + lane.Sample(mouth, out _).ToString("F3"));

        var character = GameObject.Find(Bolum1SceneBuilder.CharacterName);
        character.transform.position = GameObject.Find("PlayerSpawn").transform.position;
        if (CarrierActor.Ground(character.transform.position, out var spawnHit)) character.transform.position = spawnHit.point + Vector3.up * 0.02f;
        var cc = character.GetComponent<CharacterController>() ?? character.AddComponent<CharacterController>();
        cc.height = 1.5f; cc.radius = 0.23f; cc.center = Vector3.up * 0.75f; cc.slopeLimit = 45; cc.stepOffset = 0.25f; cc.skinWidth = 0.025f;
        var player = character.GetComponent<ThirdPersonPlayer>() ?? character.AddComponent<ThirdPersonPlayer>();
        player.rules = rules; player.animator = character.GetComponent<Animator>(); player.animator.runtimeAnimatorController = locomotion;
        player.animator.applyRootMotion = false;
        var camera = Camera.main; var orbit = camera.GetComponent<OrbitCamera>() ?? camera.gameObject.AddComponent<OrbitCamera>();
        orbit.target = character.transform; player.view = camera.transform;
        var interaction = character.GetComponent<PlayerInteraction>() ?? character.AddComponent<PlayerInteraction>(); interaction.player = player;
        var input = character.GetComponent<PlayerInputDriver>() ?? character.AddComponent<PlayerInputDriver>(); input.player = player; input.interaction = interaction; input.orbit = orbit;
        var helperObject = Model(Crew + "Green.fbx", root.transform); helperObject.name = "StretcherHelperBot";
        helperObject.transform.rotation = Quaternion.identity; helperObject.transform.position = character.transform.position + Vector3.right * 1.5f;
        var bot = helperObject.AddComponent<StretcherHelperBot>(); bot.rules = rules; bot.animator = helperObject.GetComponent<Animator>();
        bot.animator.runtimeAnimatorController = locomotion; bot.animator.applyRootMotion = false; interaction.helper = bot;
        helperObject.AddComponent<FootGroundingIK>();
        foreach (var col in helperObject.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);

        var path = ((List<object>)Bolum1Coordinates.Obj(markers, "geometry_probes")["path"]).Select(Bolum1Coordinates.Vec3).Where(p => p.x < 1).OrderBy(p => p.x).ToArray();
        var placed = new List<Patient>();
        foreach (var prefab in patientPrefabs)
        {
            bool found = false;
            foreach (var point in path)
            {
                var candidate = Bolum1Coordinates.ToUnity(point);
                if (!RayModule(candidate, "SM_B1_Ground", out var hit)) continue;
                candidate = hit.point;
                if (placed.Any(p => Vector3.Distance(p.transform.position, candidate) < 2.2f)) continue;
                bool safe = true;
                for (int i = 0; i < 12; i++)
                {
                    Vector3 edge = candidate + new Vector3(Mathf.Cos(i * Mathf.PI / 6), 0, Mathf.Sin(i * Mathf.PI / 6)) * 1.5f;
                    if (!RayModule(edge, "SM_B1_Ground", out var eh) || Mathf.Abs(eh.point.y - candidate.y) > 0.6f) safe = false;
                }
                foreach (var col in Physics.OverlapBox(candidate + Vector3.up * 0.3f, new Vector3(0.65f, 0.25f, 0.85f)))
                    if (!col.transform.IsChildOf(GameObject.Find("SM_B1_Ground").transform)) safe = false;
                if (!safe) continue;
                var patient = (Patient)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                patient.transform.position = candidate + Vector3.up * patient.hipsToBack;
                placed.Add(patient); found = true;
                Debug.Log("PLACEMENT " + patient.displayName + "=" + patient.transform.position.ToString("F3")); break;
            }
            if (!found) throw new Exception("Patikada güvenli hasta konumu bulunamadı: " + prefab.displayName);
        }
        mission.patients = placed.ToArray(); interaction.patients = mission.patients;
        var stretchers = new List<StretcherController>();
        var truck = GameObject.Find(Bolum1SceneBuilder.TowTruckName);
        foreach (int index in new[] { 0, 1 })
        {
            var s = (StretcherController)PrefabUtility.InstantiatePrefab(stretcher, root.transform); s.name = "SM_Stretcher_0" + (index + 1);
            bool onBed = PlaceOnTruck(s, truck, stretchers);
            if (!onBed)
            {
                Vector3 p = truck.transform.position + Vector3.forward * 2.5f + Vector3.right * (index * 2.5f - 1.25f);
                if (!CarrierActor.Ground(p, out var h)) throw new Exception("Sedye için banket zemini bulunamadı");
                s.transform.position = h.point + Vector3.up * 0.02f;
                Debug.LogWarning("AÇIK SORU: Kasa boşluğu yetersiz; " + s.name + " çekici yanında bankette.");
            }
            stretchers.Add(s); Debug.Log("PLACEMENT " + s.name + "=" + s.transform.position.ToString("F3") + " bed=" + onBed);
        }
        mission.stretchers = stretchers.ToArray(); interaction.stretchers = mission.stretchers;
        var hud = root.AddComponent<MissionHud>(); hud.mission = mission; hud.interaction = interaction;
    }
    static bool PlaceOnTruck(StretcherController stretcher, GameObject truck, List<StretcherController> placed)
    {
        var obstacles = truck.GetComponentsInChildren<Renderer>().Where(r => r.name.Contains("Crane") || r.name.Contains("Cone") || r.name.Contains("Holder")).ToArray();
        Vector3 forward = Vector3.ProjectOnPlane(truck.transform.TransformDirection(Vector3.up), Vector3.up).normalized;
        Quaternion rotation = Quaternion.LookRotation(forward);
        for (float x = -0.85f; x <= 0.86f; x += 0.425f)
            for (float y = 0.2f; y <= 2.81f; y += 0.25f)
            {
                Vector3 p = truck.transform.TransformPoint(new Vector3(x, y, 1.78f));
                var bounds = new Bounds(p + Vector3.up * 0.1f, Vector3.zero);
                foreach (float sx in new[] { -0.3f, 0.3f }) foreach (float sz in new[] { -1.1f, 1.1f }) bounds.Encapsulate(p + rotation * new Vector3(sx, 0.2f, sz));
                if (obstacles.Any(r => r.bounds.Intersects(bounds))) continue;
                if (placed.Any(s => Mathf.Abs(Vector3.Dot(s.transform.position - p, rotation * Vector3.right)) < 0.65f && Mathf.Abs(Vector3.Dot(s.transform.position - p, forward)) < 2.2f)) continue;
                stretcher.transform.SetPositionAndRotation(p, rotation); return true;
            }
        return false;
    }
    public static bool RayModule(Vector3 point, string name, out RaycastHit result)
    {
        var root = GameObject.Find(name).transform;
        foreach (var hit in Physics.RaycastAll(point + Vector3.up * 25, Vector3.down, 60).OrderBy(h => h.distance))
            if (hit.transform.IsChildOf(root)) { result = hit; return true; }
        result = default; return false;
    }
    static float FindRampMouth(LanePath lane)
    {
        // Korkuluk valódi hiányát a völgyoldali szegély teljes szélességében keressük.
        var gaps = new List<float>();
        for (float s = 0; s < lane.Length; s += 0.1f)
        {
            Vector3 point = lane.Sample(s, out var tangent);
            if (point.x < 30 || point.x > 40) continue;
            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
            bool rail = false;
            for (float side = 1.4f; side <= 3.1f; side += 0.1f)
                if (RayModule(point + right * side, "SM_B1_Guardrail", out _)) { rail = true; break; }
            if (!rail) gaps.Add(s);
        }
        if (gaps.Count == 0) throw new Exception("Rampa korkuluk boşluğu raycast ile bulunamadı");
        float nearest = gaps.OrderBy(s => Mathf.Abs(lane.Sample(s, out _).x - 35)).First();
        var contiguous = gaps.Where(s => Mathf.Abs(s - nearest) < 2).ToArray();
        float mouth = contiguous.Average();
        Debug.Log("RAMP GAP samples=" + gaps.Count + " s=" + mouth.ToString("F3")); return mouth;
    }
}
