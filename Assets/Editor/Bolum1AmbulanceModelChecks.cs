using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Gerçek modellerin kabulü. Ölçümler prefab referanslarından ve oynayan iskeletten alınır.
public static class Bolum1AmbulanceModelChecks
{
    public static void RunAssets()
    {
        bool ok = true;
        try { CheckAssets((label, pass, detail) => { ok &= pass; Debug.Log((pass ? "CHECK OK " : "CHECK FAIL ") + label + " " + detail); }); }
        catch (Exception e) { Debug.LogException(e); ok = false; }
        EditorApplication.Exit(ok ? 0 : 1);
    }
    public static void CheckAssets(Action<string, bool, string> check)
    {
        var paths = new[] { Bolum1AmbulanceBuilder.AmbulanceModelPath, Bolum1AmbulanceBuilder.StretcherModelPath }
            .Concat(Bolum1AmbulanceBuilder.PatientTypes.Select(Bolum1AmbulanceBuilder.PatientModelPath))
            .Concat(new[] { Bolum1AmbulanceBuilder.LiePosePath, Bolum1AmbulanceBuilder.LiePoseArmSlingPath });
        foreach (string path in paths)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            check("(k) importer " + System.IO.Path.GetFileName(path), importer != null && Bolum1ImportSettings.IsApplied(importer),
                importer == null ? "YOK" : $"scale={importer.globalScale}, units={importer.useFileScale}, bakeAxis={importer.bakeAxisConversion}, rig={importer.animationType}, animation={importer.importAnimation}");
            if (path.Contains("Patient_LiePose") && importer != null)
            {
                var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
                check("(m) humanoid döngü klibi " + System.IO.Path.GetFileName(path),
                    importer.avatarSetup == ModelImporterAvatarSetup.CopyFromOther && importer.sourceAvatar != null
                    && importer.sourceAvatar.isValid && importer.sourceAvatar.isHuman && clips.Length > 0
                    && clips.All(c => c.humanMotion && c.isLooping) && importer.clipAnimations.All(c => c.lastFrame - c.firstFrame <= 1.01f),
                    $"source={importer.sourceAvatar}, clips={clips.Length}, humanoid/loop={string.Join(",", clips.Select(c => c.humanMotion + "/" + c.isLooping))}");
            }
        }

        CheckPrefab(Bolum1AmbulanceBuilder.AmbulancePrefabPath, Bolum1AmbulanceBuilder.AmbulanceModelPath, check);
        CheckPrefab(Bolum1AmbulanceBuilder.StretcherPrefabPath, Bolum1AmbulanceBuilder.StretcherModelPath, check);
        foreach (var type in Bolum1AmbulanceBuilder.PatientTypes)
        {
            string path = Bolum1AmbulanceBuilder.PatientPrefabPath(type);
            CheckPrefab(path, Bolum1AmbulanceBuilder.PatientModelPath(type), check);
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Patient>();
            var a = p.animator.avatar;
            check("(m) Avatar/ragdoll " + type, a != null && a.isValid && a.isHuman && p.hasLiePose
                && p.ragdollBodies.Length >= 10 && p.ragdollBodies.All(b => b != null && b.transform.IsChildOf(p.transform)),
                $"valid={a != null && a.isValid}, human={a != null && a.isHuman}, ragdoll={p.ragdollBodies.Length}, hipsToBack={p.hipsToBack:F4} m");
        }
        var materials = AssetDatabase.LoadAllAssetsAtPath(Bolum1AmbulanceBuilder.AmbulanceModelPath).OfType<Material>()
            .Concat(AssetDatabase.LoadAllAssetsAtPath(Bolum1AmbulanceBuilder.StretcherModelPath).OfType<Material>()).ToArray();
        var expected = new Dictionary<string, string> { { "M_Ambulance_Paint", "#DCDBDC" }, { "M_Ambulance_Red", "#CF3533" }, { "M_Stretcher_Canvas", "#D97836" } };
        foreach (var pair in expected)
        {
            var mat = materials.FirstOrDefault(m => m.name == pair.Key);
            ColorUtility.TryParseHtmlString(pair.Value, out Color want);
            float delta = mat == null ? float.PositiveInfinity : Bolum1ColorProbe.DeltaE(mat.color, want);
            check("(l) renk " + pair.Key, delta < 2f, $"beklenen={pair.Value}, gerçek={(mat == null ? "YOK" : "#" + ColorUtility.ToHtmlStringRGB(mat.color))}, ΔE76={delta:F5}");
        }
        var glass = materials.FirstOrDefault(m => m.name == "M_Ambulance_Glass");
        check("(k) ambulans camı şeffaf", glass != null && glass.GetFloat("_Surface") == 1f && Mathf.Abs(glass.color.a - 0.3f) < 0.01f && glass.renderQueue == 3000,
            glass == null ? "YOK" : $"alpha={glass.color.a:F2}, queue={glass.renderQueue}");
        // D2 kararı 6: ekipli ambulans (şoför + hemşire Interior mesh'inde; ~10.3k üçgen; yeşil tulum, ten, saç malzemeleri)
        var ambFbx = AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1AmbulanceBuilder.AmbulanceModelPath);
        int tris = ambFbx.GetComponentsInChildren<MeshFilter>(true).Sum(f => f.sharedMesh.triangles.Length / 3);
        var interior = ambFbx.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.name == "SM_Ambulance_Interior");
        var crewMats = new[] { "M_Ambulance_Suit", "M_Ambulance_Skin", "M_Ambulance_SkinLight", "M_Ambulance_StripeYellow", "M_Ambulance_HairDark" };
        var missingCrew = crewMats.Where(n => interior == null || !interior.sharedMaterials.Any(m => m != null && m.name == n)).ToArray();
        var suit = materials.FirstOrDefault(m => m.name == "M_Ambulance_Suit");
        ColorUtility.TryParseHtmlString("#1F4D3A", out Color suitWant);
        float suitDelta = suit == null ? float.PositiveInfinity : Bolum1ColorProbe.DeltaE(suit.color, suitWant);
        check("(D2-f) ekipli ambulans: şoför ve hemşire Interior mesh'inde, " + tris + " üçgen (meta 10.348), koyu yeşil tulum",
            interior != null && missingCrew.Length == 0 && tris >= 9000 && tris <= 10500 && suitDelta < 2f,
            "Interior malzemeleri " + (interior != null ? interior.sharedMaterials.Length : 0) + ", eksik: " + (missingCrew.Length == 0 ? "yok" : string.Join(",", missingCrew))
            + "; tulum " + (suit == null ? "YOK" : "#" + ColorUtility.ToHtmlStringRGB(suit.color)) + " (ΔE76 " + suitDelta.ToString("F3") + ")");
        check("Build Settings: Bölüm 1 etkin", EditorBuildSettings.scenes.Any(s => s.enabled && s.path == Bolum1SceneBuilder.ScenePath), Bolum1SceneBuilder.ScenePath);
        // Poz testinin referans çerçevesi gerçek yüzey yüksekliğiyle bağımsız karşılaştırılır.
        var ambulance = AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1AmbulanceBuilder.AmbulancePrefabPath).GetComponent<AmbulanceController>();
        var stretcher = AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1AmbulanceBuilder.StretcherPrefabPath).GetComponent<StretcherController>();
        float bedHeight = Convert.ToSingle(Bolum1Coordinates.Obj(Bolum1Coordinates.LoadJson(Bolum1AmbulanceBuilder.AmbulanceMetaPath), "beds")["surface_z_m"]);
        float canvasHeight = Convert.ToSingle(Bolum1Coordinates.Obj(Bolum1Coordinates.LoadJson("Assets/Models/Props/Stretcher/stretcher_meta.json"), "dimensions")["canvas_centre_height_m"]);
        float bedError = ambulance.patientSlots.Max(f => Mathf.Abs(ambulance.transform.InverseTransformPoint(f.position).y - bedHeight));
        float canvasError = Mathf.Abs(stretcher.transform.InverseTransformPoint(stretcher.patientAnchor.position).y - canvasHeight);
        check("(m) yatma çerçeveleri gerçek yatak/kanvas yüzeyinde", bedError < 0.001f && canvasError < 0.001f,
            $"yatak={bedHeight:F3} m, hata={bedError:F5} m; kanvas={canvasHeight:F3} m, hata={canvasError:F5} m");
        CheckDoors(check);
    }

    private static void CheckPrefab(string prefab, string model, Action<string, bool, string> check)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
        var meshes = go.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh)
            .Concat(go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(m => m.sharedMesh)).ToArray();
        bool real = meshes.Length > 0 && meshes.All(m => m != null && AssetDatabase.GetAssetPath(m) == model)
            && go.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).All(m => m != null && !m.name.Contains("Placeholder"));
        check("(k) gerçek FBX " + go.name, real, $"mesh={meshes.Length}, kaynak={model}");
    }

    private static Bounds PartBounds(Transform root, Transform part)
    {
        bool any = false;
        Bounds result = default;
        foreach (var mf in part.GetComponentsInChildren<MeshFilter>())
        {
            // Mesh okunabilirliği gerektirmeyen, dönüştürülmüş yerel bounds köşeleri.
            var b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = root.InverseTransformPoint(mf.transform.TransformPoint(b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                if (!any) { result = new Bounds(p, Vector3.zero); any = true; } else result.Encapsulate(p);
            }
        }
        return result;
    }

    private static void CheckDoors(Action<string, bool, string> check)
    {
        var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1AmbulanceBuilder.AmbulancePrefabPath));
        try
        {
            var a = go.GetComponent<AmbulanceController>();
            var box = go.GetComponent<BoxCollider>();
            var body = new Bounds(box.center, box.size);
            var parts = new[] { a.doorLeft, a.doorRight, a.rearStep };
            var axes = new[] { a.doorLeftAxis, a.doorRightAxis, a.stepAxis };
            var angles = new[] { a.doorLeftOpenAngle, a.doorRightOpenAngle, a.stepOpenAngle };
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var before = PartBounds(go.transform, part);
                part.rotation = Quaternion.AngleAxis(angles[i], go.transform.TransformDirection(axes[i])) * part.rotation;
                var after = PartBounds(go.transform, part);
                var overlap = Vector3.Min(after.max, body.max) - Vector3.Max(after.min, body.min);
                float depth = Mathf.Max(0f, Mathf.Min(overlap.x, Mathf.Min(overlap.y, overlap.z)));
                float outward = before.center.z - after.center.z;
                bool ok = Mathf.Abs(Mathf.Abs(angles[i]) - (i == 2 ? 90f : 100f)) < 0.01f && outward > 0.05f && depth <= 0.02f;
                if (i == 2) ok &= after.size.y < 0.08f;
                check("(n) dışa açılma " + part.name, ok, $"açı={angles[i]:F1}°, dışa={outward:F4} m, bounds giriş={depth:F4} m, yükseklik={after.size.y:F4} m");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    public static IEnumerator CheckPoses(Action<string, bool, string> check)
    {
        var stretcher = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1AmbulanceBuilder.StretcherPrefabPath)).GetComponent<StretcherController>();
        var ambulance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1AmbulanceBuilder.AmbulancePrefabPath)).GetComponent<AmbulanceController>();
        stretcher.transform.position = new Vector3(1000f, 100f, 1000f);
        ambulance.transform.position = new Vector3(1010f, 100f, 1000f);
        stretcher.enabled = false;
        ambulance.enabled = false;
        try
        {
            foreach (var type in Bolum1AmbulanceBuilder.PatientTypes)
            {
                var patient = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1AmbulanceBuilder.PatientPrefabPath(type))).GetComponent<Patient>();
                try
                {
                    var frames = new[] { stretcher.patientAnchor, ambulance.patientSlots[0], ambulance.patientSlots[1] };
                    for (int i = 0; i < frames.Length; i++)
                    {
                        patient.LieOn(frames[i], i == 0 ? PatientState.OnStretcher : PatientState.InAmbulance, 0f);
                        yield return null;
                        yield return null;
                        var frame = frames[i];
                        float hips = frame.InverseTransformPoint(patient.hips.position).y;
                        Vector3 head = patient.animator.GetBoneTransform(HumanBodyBones.Head).position - patient.hips.position;
                        Vector3 expected = i == 0 ? stretcher.handleFront.position - stretcher.handleRear.position : ambulance.transform.forward;
                        float dot = Vector3.Dot(Vector3.ProjectOnPlane(head, frame.up).normalized, expected.normalized);
                        float back = float.PositiveInfinity;
                        var baked = new Mesh();
                        foreach (var smr in patient.GetComponentsInChildren<SkinnedMeshRenderer>())
                        {
                            if (!smr.name.Contains("Torso")) continue;
                            smr.BakeMesh(baked);
                            foreach (var v in baked.vertices)
                                back = Mathf.Min(back, frame.InverseTransformPoint(smr.transform.TransformPoint(v)).y);
                        }
                        UnityEngine.Object.Destroy(baked);
                        check("(m) LiePose " + type + " / " + (i == 0 ? "PatientAnchor" : "PatientSlot_0" + i),
                            Mathf.Abs(hips - patient.hipsToBack) <= 0.03f && Mathf.Abs(back) <= 0.03f && dot >= 0.99f,
                            $"kalça={hips:F4} m, hedef={patient.hipsToBack:F4} m, sırt-yüzey={back:F4} m, baş dot={dot:F5}");
                    }
                }
                finally { UnityEngine.Object.Destroy(patient.gameObject); }
            }
            // LaneFollower gerçek tekerleği araç aksı etrafında yol/r kadar döndürmeli.
            var follower = ambulance.GetComponent<LaneFollower>();
            var lane = UnityEngine.Object.FindAnyObjectByType<LanePath>();
            follower.Place(lane, 5f, 4f);
            var before = follower.wheels.Select(w => w.localRotation).ToArray();
            var localAxes = follower.wheels.Select(w => Quaternion.Inverse(w.rotation) * ambulance.transform.right).ToArray();
            float start = follower.Distance;
            follower.Tick(0.02f, 4f);
            float angle = (follower.Distance - start) / follower.wheelRadius * Mathf.Rad2Deg;
            float worst = 0f;
            for (int i = 0; i < before.Length; i++)
                worst = Mathf.Max(worst, Quaternion.Angle(before[i] * Quaternion.AngleAxis(angle, localAxes[i]), follower.wheels[i].localRotation));
            check("(c) gerçek tekerlek dönüş ekseni", worst < 0.1f && angle > 0f, $"yarıçap={follower.wheelRadius:F4} m, dönüş={angle:F3}°, hata={worst:F5}°");
        }
        finally
        {
            UnityEngine.Object.Destroy(stretcher.gameObject);
            UnityEngine.Object.Destroy(ambulance.gameObject);
        }
    }
}
