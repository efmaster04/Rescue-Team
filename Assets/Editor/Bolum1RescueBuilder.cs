using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

// D2 (2026-10-17 kullanıcı kararları 1–5): hasta yerleşimi, minibüs bagaj kapısı, halat noktası, sırt soketi.
// Bolum1AmbulanceBuilder.SetupScene içinden çağrılır (sahne her kurulumda baştan kurulur).
//  - Turist Seat_05'te, Sivil Seat_06'da oturur (InVehicle, Patient_SitDazed / _ArmSling); Yaşlı kaya çıkıntısında (LedgeZone) yatar.
//  - Minibüse MinivanTailgate (minivan_meta.json: SM_Minivan_Tailgate hinge, open_angle_deg).
//  - SM_RopeAnchor patika kenarında, Yaşlı'nın tam üstünde; tırmanış hattı kaya duvarına klibin duvar düzlemi (0.36 m) kadar uzakta.
//  - Oyuncunun Spine1'ine PiggybackSocket (clips_meta.json piggyback_socket).
public static class Bolum1RescueBuilder
{
    public const string RopeAnchorModelPath = "Assets/Models/Props/RopeAnchor/SM_RopeAnchor.fbx";
    public const string RopeMaterialPath = Bolum1AmbulanceBuilder.MaterialFolder + "/M_Rope.mat";
    public const string RopeAnchorName = "RopeAnchor";
    public const string SocketName = "PiggybackSocket";
    // Halat kazığının Blender X'i: kaya çıkıntısı (x −4…10) üstünde, minibüsün (x 3…5) ve çantaların batısında.
    public const float AnchorBlenderX = 0f;
    // Klip sözleşmesi (clips_meta.json rope_grip / wall_plane_root_local_y): kökten duvar düzlemine 0.36 m.
    public const float WallPlane = 0.36f;
    // Patika kenarına göre: kazık 0.5 m, çeken 1.4 m, bağlanma/çıkış noktası 3.3 m içeride (çıkış yanda 1 m; çıkan kişi
    // patikaya döner, sırtından arkaya bırakılan hasta uçurum kenarına 1 m'den yakın olmaz).
    public const float AnchorInset = 0.5f, PullerInset = 1.4f, StandInset = 3.3f, StandSide = 1.0f;
    // Tepede asılı kişinin kökü patika yüzeyinin bu kadar altında (eller ~kenar hizasında).
    public const float TopBelowPath = 1.1f;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static void Line(string s) { Bolum1AmbulanceBuilder.ReportLine(s); }
    private static string V(Vector3 v) { return "(" + v.x.ToString("F2", Inv) + ", " + v.y.ToString("F2", Inv) + ", " + v.z.ToString("F2", Inv) + ")"; }

    // Unity: vadi yönü +Z (Blender −Y), dağ yönü −Z.
    private static readonly Vector3 Valley = Vector3.forward;

    public static Patient[] PlacePatients(Dictionary<string, object> markers, GameObject minivan, Transform parent)
    {
        Physics.SyncTransforms();
        SetupTailgate(minivan);
        var result = new List<Patient>();
        for (int i = 0; i < Bolum1AmbulanceBuilder.PatientTypes.Length; i++)
        {
            string type = Bolum1AmbulanceBuilder.PatientTypes[i];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1AmbulanceBuilder.PatientPrefabPath(type));
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = "Patient_" + type;
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            result.Add(go.GetComponent<Patient>());
        }
        Seat(result[0], minivan.transform, "Seat_05");
        Seat(result[1], minivan.transform, "Seat_06");
        var elder = result[2];
        SetupRopeAndLedge(markers, minivan.transform, elder, parent);
        return result.ToArray();
    }

    // Koltuk işareti = kalça/minder temas noktası; Patient_SitDazed koltuk yüzeyi kökün 0.262 m üstünde. Yüz minibüsün önüne.
    private static void Seat(Patient p, Transform van, string seat)
    {
        var marker = van.GetComponentsInChildren<Transform>(true).First(t => t.name == seat);
        float seatZ = Bolum1AnimBuilder.MetaFloat("Patient_SitDazed", "seat_surface_root_local_z", 0.262f);
        Vector3 up = van.TransformDirection(0f, 0f, 1f);       // Blender Z
        Vector3 fwd = van.TransformDirection(0f, -1f, 0f);     // Blender −Y = ön
        Vector3 pos = marker.position - up * seatZ;
        Quaternion rot = Quaternion.LookRotation(fwd, up);
        p.startSeated = true;
        p.onLedge = false;
        p.transform.SetParent(van, true);
        p.transform.SetPositionAndRotation(pos, rot);
        PrefabUtility.RecordPrefabInstancePropertyModifications(p);
        Line("Hasta " + p.name + ": " + seat + " (InVehicle, kök " + V(pos) + ", minibüs eğimi " + Vector3.Angle(up, Vector3.up).ToString("F0", Inv) + "°)");
    }

    // Bagaj kapısı: menteşe ekseni minibüs kök uzayında (Blender X; kök uzayı Blender minibüs uzayıdır, X işareti ters).
    // İşaret, açıkken kapının dışa (minibüsün arkasına ve yukarı) kalktığı yönden seçilir.
    private static void SetupTailgate(GameObject minivan)
    {
        var van = minivan.transform;
        var gate = van.GetComponentsInChildren<Transform>(true).First(t => t.name == "SM_Minivan_Tailgate");
        var meta = Bolum1Coordinates.LoadJson(Bolum1Coordinates.MinivanMetaPath);
        var motion = Bolum1Coordinates.Obj(Bolum1Coordinates.Obj(Bolum1Coordinates.Obj(meta, "parts"), "SM_Minivan_Tailgate"), "motion");
        float angle = Mathf.Abs(Convert.ToSingle(motion["open_angle_deg"], Inv));
        Vector3 axis = Vector3.right;
        var mf = gate.GetComponent<MeshFilter>();
        Vector3 c = mf.sharedMesh.bounds.center; // kapı yerel uzayında
        Vector3 rear = van.TransformDirection(0f, 1f, 0f);
        Quaternion closed = gate.localRotation;
        float Score(float a)
        {
            Vector3 local = Quaternion.AngleAxis(a, axis) * closed * c;           // kök uzayında, menteşeye göre
            Vector3 w = van.TransformVector(local);
            return Vector3.Dot(w, rear) + w.y;
        }
        float signed = Score(angle) >= Score(-angle) ? angle : -angle;
        var tg = minivan.AddComponent<MinivanTailgate>();
        tg.tailgate = gate;
        tg.hingeAxis = axis;
        tg.openAngle = signed;
        // Arka nokta: arka tampon ortasının altında, zeminde
        var rp = new GameObject("RearPoint").transform;
        rp.SetParent(van, false);
        Vector3 w0 = van.TransformPoint(new Vector3(0f, 2.5f, 0.5f));
        if (GroundProbe.Find(w0 + Vector3.up * 2f, 6f, out RaycastHit g) && !g.transform.IsChildOf(van)) w0.y = g.point.y;
        else if (Physics.Raycast(w0 + Vector3.up * 2f, Vector3.down, out g, 6f)) w0.y = g.point.y;
        rp.position = w0;
        tg.rearPoint = rp;
        tg.dragSeconds = Bolum1AnimBuilder.MetaFloat("Patient_DraggedOut", "duration_s", 1.5f);
        tg.pullSeconds = Bolum1AnimBuilder.MetaFloat("Extract_Pull", "duration_s", 1.5f);
        Line("Minibüs bagaj kapısı: eksen kök " + V(axis) + ", açık açı " + signed.ToString("F0", Inv) + "° (meta " + angle.ToString("F0", Inv)
            + "), arka nokta " + V(rp.position) + ", açma menzili " + tg.openRange + " m, çekme menzili " + tg.extractRange + " m");
    }

    private static float GroundY(Vector3 p, float from, float range, out bool found)
    {
        var hits = Physics.RaycastAll(new Vector3(p.x, from, p.z), Vector3.down, range, ~0, QueryTriggerInteraction.Ignore)
            .Where(h => h.collider.GetComponentInParent<Patient>() == null && h.collider.GetComponentInParent<CharacterController>() == null)
            .OrderBy(h => h.distance).ToArray();
        found = hits.Length > 0;
        return found ? hits[0].point.y : float.NegativeInfinity;
    }

    private static void SetupRopeAndLedge(Dictionary<string, object> markers, Transform van, Patient elder, Transform parent)
    {
        var markerData = Bolum1Coordinates.Obj(markers, "markers");
        Vector3 ledgeMarker = Bolum1Coordinates.ToUnity(Bolum1Coordinates.Vec3(markerData["LedgeZone"], "position"));
        var ledgeBounds = Bolum1Coordinates.Obj(markerData["LedgeZone"], "bounds");
        float ledgeY = ledgeMarker.y;
        float x = -AnchorBlenderX;
        // Patika yüzeyi ve uçurum kenarı (vadi yönünde zemin patika yüzeyinden 0.5 m düşene kadar)
        var path = ((List<object>)Bolum1Coordinates.Obj(markers, "geometry_probes")["path"]).Select(Bolum1Coordinates.Vec3).OrderBy(v => v.x).ToList();
        float pathCenterZ = -Interp(path, AnchorBlenderX).y;
        float pathY = GroundY(new Vector3(x, 0f, pathCenterZ), 20f, 60f, out _);
        float edgeZ = pathCenterZ;
        for (float z = pathCenterZ; z < pathCenterZ + 8f; z += 0.05f)
        {
            float y = GroundY(new Vector3(x, 0f, z), pathY + 3f, 20f, out bool f);
            if (!f || y < pathY - 0.5f) break;
            edgeZ = z;
        }
        // Duvar: belli bir yükseklikte, dağdan vadiye giderken zeminin o yüksekliğin altına indiği ilk z
        float WallZ(float height)
        {
            for (float z = edgeZ - 0.5f; z < edgeZ + 8f; z += 0.02f)
            {
                float y = GroundY(new Vector3(x, 0f, z), pathY + 3f, 30f, out bool f);
                if (!f || y < height) return z;
            }
            return edgeZ;
        }
        float bottomHeight = ledgeY + 0.3f;
        float wallBottom = WallZ(bottomHeight);
        float topRootY = pathY - TopBelowPath;
        float wallTop = WallZ(topRootY + 0.3f);
        Vector3 bottom = new Vector3(x, 0f, wallBottom + WallPlane);
        bottom.y = GroundY(bottom, ledgeY + 2f, 5f, out bool bf);
        if (!bf || Mathf.Abs(bottom.y - ledgeY) > 0.6f) throw new Exception("Halat dibi kaya çıkıntısında değil: " + V(bottom));
        Vector3 top = new Vector3(x, topRootY, Mathf.Max(wallTop + WallPlane, edgeZ + 0.05f + 0.29f));
        Quaternion faceWall = Quaternion.LookRotation(-Valley, Vector3.up);

        var anchorFbx = AssetDatabase.LoadAssetAtPath<GameObject>(RopeAnchorModelPath);
        if (anchorFbx == null) throw new Exception("SM_RopeAnchor yok: " + RopeAnchorModelPath);
        var root = new GameObject(RopeAnchorName).transform;
        root.SetParent(parent, false);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(anchorFbx);
        model.transform.SetParent(root, false);
        Vector3 anchorPos = new Vector3(x, 0f, edgeZ - AnchorInset);
        anchorPos.y = GroundY(anchorPos, pathY + 3f, 10f, out _);
        root.position = anchorPos;
        var rope = root.gameObject.AddComponent<RopeAnchor>();
        rope.ropeAttach = model.GetComponentsInChildren<Transform>(true).First(t => t.name == "RopeAttach");
        Transform Empty(string n, Vector3 p, Quaternion r) { var t = new GameObject(n).transform; t.SetParent(root, true); t.SetPositionAndRotation(p, r); return t; }
        rope.edgePoint = Empty("EdgePoint", new Vector3(x, pathY + 0.04f, edgeZ + 0.05f), Quaternion.identity);
        rope.climbBottom = Empty("ClimbBottom", bottom, faceWall);
        rope.climbTop = Empty("ClimbTop", top, faceWall);
        // Bağlanma/çıkış noktası: patika zemininde (eşyanın üstünde değil), 0.6 m içinde eşya / minibüs yok; tercihen yanda 1 m.
        var groundT = GameObject.Find("SM_B1_Ground").transform;
        var envT = new[] { "SM_B1_Ground", "SM_B1_Road", "SM_B1_Guardrail" }.Select(n => GameObject.Find(n).transform).ToArray();
        Vector3 stand = default;
        float bestCost = float.MaxValue;
        for (float sx = -1.5f; sx <= 1.51f; sx += 0.25f)
            for (float inset = StandInset - 0.3f; inset <= StandInset + 0.51f; inset += 0.1f)
            {
                Vector3 c = new Vector3(x + sx, 0f, edgeZ - inset);
                var hit = Physics.RaycastAll(new Vector3(c.x, pathY + 3f, c.z), Vector3.down, 10f).OrderBy(h => h.distance).FirstOrDefault();
                if (hit.collider == null || !hit.transform.IsChildOf(groundT) || Mathf.Abs(hit.point.y - pathY) > 0.15f || hit.normal.y < 0.95f) continue;
                c.y = hit.point.y;
                if (Physics.OverlapSphere(c + Vector3.up * 0.6f, 0.6f).Any(o => !envT.Any(e => o.transform.IsChildOf(e)))) continue;
                float cost = Mathf.Abs(sx - StandSide) + Mathf.Abs(inset - StandInset) * 2f;
                if (cost < bestCost) { bestCost = cost; stand = c; }
            }
        if (bestCost == float.MaxValue) throw new Exception("Halat bağlanma/çıkış noktası için boş patika yeri bulunamadı");
        stand.y += 0.02f;
        rope.topStand = Empty("TopStand", stand, faceWall);
        Vector3 pull = new Vector3(x, 0f, edgeZ - PullerInset);
        pull.y = GroundY(pull, pathY + 3f, 10f, out _);
        rope.pullerStand = Empty("PullerStand", pull, Quaternion.LookRotation(Valley, Vector3.up));
        rope.descendSpeed = -Bolum1AnimBuilder.MetaFloat("Rope_Descend", "vertical_speed_mps", -0.6f);
        rope.climbSpeed = Bolum1AnimBuilder.MetaFloat("Rope_Climb", "vertical_speed_mps", 0.3f);
        rope.pullSpeed = Bolum1AnimBuilder.RopePullSpeed;

        // Halat: LineRenderer, turuncu (rope_anchor_meta M_RopeAnchor_Rope #F29E1F)
        var line = root.gameObject.AddComponent<LineRenderer>();
        var mat = AssetDatabase.LoadAssetAtPath<Material>(RopeMaterialPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, RopeMaterialPath);
        }
        mat.SetColor("_BaseColor", new Color(0.95f, 0.62f, 0.12f));
        mat.SetFloat("_Smoothness", 0.2f);
        EditorUtility.SetDirty(mat);
        line.sharedMaterial = mat;
        line.widthMultiplier = 0.03f;
        line.numCapVertices = 2;
        line.numCornerVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        line.useWorldSpace = true;
        line.positionCount = 3;
        line.SetPositions(new[] { rope.ropeAttach.position, rope.edgePoint.position, bottom + Vector3.up * 0.05f });
        rope.line = line;

        // Tırmanış hattı boyunca duvara uzaklık (ölçüm): her yükseklikte kökten duvara yatay mesafe
        var gaps = new List<float>();
        for (float t = 0f; t <= 1.001f; t += 0.1f)
        {
            Vector3 p = Vector3.Lerp(bottom, top, t);
            float wz = WallZ(p.y + 0.15f);
            gaps.Add(p.z - wz);
        }

        // Yaşlı: kaya çıkıntısında, halat dibine 0.8–1.4 m, vadi kenarına (gövdenin her yeri) ≥ 1 m, düz zemin; baş +X (minibüsten uzağa)
        var hipsInRoot = elder.transform.InverseTransformPoint(elder.hips.position);
        Vector3 bestPoint = default, bestNormal = Vector3.up;
        float bestClear = -1f;
        for (float dx = -1.6f; dx <= 1.6f; dx += 0.1f)
            for (float dz = 0.5f; dz <= 2.2f; dz += 0.1f)
            {
                Vector3 c = new Vector3(bottom.x + dx, 0f, bottom.z + dz);
                float d = Vector2.Distance(new Vector2(c.x, c.z), new Vector2(bottom.x, bottom.z));
                if (d < 0.8f || d > 1.4f) continue;
                if (!LedgeSpot(c, ledgeY, out Vector3 point, out Vector3 normal, out float clear)) continue;
                if (clear > bestClear) { bestClear = clear; bestPoint = point; bestNormal = normal; }
            }
        if (bestClear < 1f) throw new Exception("Yaşlı için kaya çıkıntısında uçurum kenarından ≥ 1 m içeride yer bulunamadı (en iyi " + bestClear.ToString("F2", Inv) + " m)");
        Patient.LiePoseInFrame(hipsInRoot, elder.hipsToBack, out Vector3 lp, out Quaternion lr);
        Quaternion frame = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.right, bestNormal).normalized, bestNormal);
        elder.transform.SetPositionAndRotation(bestPoint + frame * lp, frame * lr);
        elder.onLedge = true;
        elder.startSeated = false;
        PrefabUtility.RecordPrefabInstancePropertyModifications(elder);

        // Kazık minibüse ve çantalara değmiyor mu (ölçüm)
        var clearHits = Physics.OverlapSphere(anchorPos + Vector3.up * 0.3f, 0.6f).Where(h => h.GetComponentInParent<RopeAnchor>() == null
            && !new[] { "SM_B1_Ground", "SM_B1_Road", "SM_B1_Guardrail" }.Any(n => h.transform.IsChildOf(GameObject.Find(n).transform))).Select(h => h.name).ToArray();
        float vanDist = van.GetComponentsInChildren<Collider>().Where(c => c.enabled && c.GetComponentInParent<Patient>() == null)
            .Min(c => Vector3.Distance(c.ClosestPoint(anchorPos + Vector3.up * 0.2f), anchorPos + Vector3.up * 0.2f));
        Line("Halat kazığı " + V(anchorPos) + " (Blender X " + AnchorBlenderX.ToString("F1", Inv) + "), patika yüzeyi y " + pathY.ToString("F2", Inv)
            + ", uçurum kenarı z " + edgeZ.ToString("F2", Inv) + "; 0.6 m içinde engel: " + (clearHits.Length == 0 ? "yok" : string.Join(",", clearHits))
            + "; minibüse en yakın " + vanDist.ToString("F2", Inv) + " m");
        Line("Tırmanış hattı: dip " + V(bottom) + " (kaya, LedgeZone y " + ledgeY.ToString("F2", Inv) + "), tepe " + V(top) + ", boy " + (top.y - bottom.y).ToString("F2", Inv)
            + " m; kökten duvara (dipten tepeye) " + string.Join(" / ", gaps.Select(g => g.ToString("F2", Inv))) + " m (klip duvar düzlemi " + WallPlane.ToString(Inv) + ")");
        Line("Yaşlı: kaya çıkıntısında " + V(bestPoint) + ", halat dibine " + Vector3.Distance(Flat(bestPoint), Flat(bottom)).ToString("F2", Inv)
            + " m, vadi kenarına en az " + bestClear.ToString("F2", Inv) + " m; LedgeZone sınırı Blender " + V(Bolum1Coordinates.Vec3(ledgeBounds, "min")) + "…" + V(Bolum1Coordinates.Vec3(ledgeBounds, "max")));
        Line("Bağlanma/çıkış " + V(stand) + ", çeken " + V(pull) + "; hızlar: iniş " + rope.descendSpeed.ToString(Inv) + ", tırmanış " + rope.climbSpeed.ToString(Inv) + ", çekme " + rope.pullSpeed.ToString(Inv) + " m/sn");
    }

    // Kaya çıkıntısında yatış yeri: gövde boyunca (+X baş) düz; gövdenin her köşesinden vadi kenarına uzaklık (zemin 0.5 m düşene kadar).
    private static bool LedgeSpot(Vector3 c, float ledgeY, out Vector3 point, out Vector3 normal, out float clearance)
    {
        point = c; normal = Vector3.up; clearance = 0f;
        float y = GroundY(c, ledgeY + 2f, 4f, out bool f);
        if (!f || Mathf.Abs(y - ledgeY) > 0.4f) return false;
        if (!Physics.Raycast(new Vector3(c.x, y + 0.5f, c.z), Vector3.down, out RaycastHit h, 1f) || h.normal.y < 0.9f) return false;
        point = h.point; normal = h.normal;
        clearance = 99f;
        foreach (float a in new[] { -0.85f, 0f, 0.75f })
            foreach (float s in new[] { -0.3f, 0f, 0.3f })
            {
                Vector3 q = point + Vector3.right * a + Vector3.forward * s;
                float gy = GroundY(q, point.y + 2f, 4f, out bool ok);
                if (!ok || Mathf.Abs(gy - point.y) > 0.15f) return false;
                // vadi yönünde kenar
                float d = 0f;
                for (; d <= 4f; d += 0.05f)
                {
                    float yy = GroundY(q + Valley * d, point.y + 2f, 4f, out bool ok2);
                    if (!ok2 || yy < point.y - 0.5f) break;
                }
                clearance = Mathf.Min(clearance, d);
            }
        // Duvar ya da başka bir şey gövdenin içine girmesin
        foreach (var col in Physics.OverlapBox(point + Vector3.up * 0.35f, new Vector3(0.9f, 0.25f, 0.4f)))
            if (col.GetComponentInParent<Patient>() == null && col.GetComponentInParent<CharacterController>() == null) return false;
        return true;
    }

    private static Vector3 Interp(List<Vector3> path, float bx)
    {
        for (int i = 0; i + 1 < path.Count; i++)
            if (bx >= path[i].x && bx <= path[i + 1].x)
                return Vector3.Lerp(path[i], path[i + 1], Mathf.InverseLerp(path[i].x, path[i + 1].x, bx));
        return bx < path[0].x ? path[0] : path[path.Count - 1];
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // Oyuncunun sırt soketi: taşıyan bağ pozundayken kökünün 0.312 m arkası, 0.292 m yukarısı; dönüşü kökle aynı; Spine1'e bağlı.
    public static Transform SetupPiggybackSocket(GameObject character)
    {
        var spine1 = character.GetComponentsInChildren<Transform>(true).First(t => t.name == "mixamorig:Spine1");
        var old = spine1.Find(SocketName);
        if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        var socket = new GameObject(SocketName).transform;
        float back = 0.312f, up = 0.292f;
        try
        {
            var meta = Bolum1Coordinates.LoadJson(Bolum1AnimBuilder.ClipsMetaPath);
            var s = Bolum1Coordinates.Obj(Bolum1Coordinates.Obj(Bolum1Coordinates.Obj(meta, "clips"), "Piggyback_Rider"), "piggyback_socket");
            var off = (List<object>)s["offset_carrier_bind_m"];
            back = Convert.ToSingle(off[1], Inv); up = Convert.ToSingle(off[2], Inv);
        }
        catch { }
        var root = character.transform;
        socket.SetPositionAndRotation(root.TransformPoint(new Vector3(0f, up, -back)), root.rotation);
        socket.SetParent(spine1, true);
        return socket;
    }
}
