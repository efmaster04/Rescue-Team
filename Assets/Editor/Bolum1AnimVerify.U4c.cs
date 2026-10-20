using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// U4c (kullanıcı kararları 2026-10-21, brief u4c_bagaj_nisan.txt):
//  (U4c-0, edit) kamyonet prefab'ı: gövde mesh'inin arka yüzü kesik (açıklıkta üçgen yok), içeride 5 panelli koyu kasa, kapalıyken
//          dış ölçü PanelVan ile aynı.
//  (U4c-1) kapaklar açıkken arkadan içeri atılan ışın kasa iç panelini bulur (eski arka yüzü değil); kamyonete atılan eşyalar kasa
//          zemininde görünür (etkin, renderer açık, kasa sınırları içinde, collider kapalı, alınamaz/çalınamaz, ayrı yuvalar);
//          "E: Geri al" vurgusu eşyada; geri alınan eşya kasadan çıkar, yuvası boşalır.
//  (U4c-2) sağ tık basılı → HUD'da ekran ortasında nişangah, gövde kameraya döner, hedef kameranın merkez ışını (8 m'deki hırsız);
//          bırakınca eşya o hırsıza isabet eder; nişangah kaybolur. Işın boşa giderse hedef 20 m ilerisi.
//  (U4c-3) pes eden hırsızın can barı gizli; ResetForTest ile geri gelir.
public static partial class Bolum1AnimVerify
{
    // ---- (U4c-0) edit aşaması: prefab ----
    private static void CheckU4cVanPrefab()
    {
        var vanPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1ThiefBuilder.VanPrefabPath);
        var srcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1TrafficBuilder.PrefabPath(TrafficVehicleType.PanelVan));
        var openMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Bolum1ThiefBuilder.OpenBodyMeshPath);
        if (vanPrefab == null || srcPrefab == null) { Check("(U4c-0) kamyonet prefab'ı", false, "prefab yok: kamyonet " + (vanPrefab != null) + ", PanelVan " + (srcPrefab != null)); return; }
        var a = (GameObject)PrefabUtility.InstantiatePrefab(vanPrefab);
        var b = (GameObject)PrefabUtility.InstantiatePrefab(srcPrefab);
        try
        {
            a.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            b.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var van = a.GetComponent<ThiefVan>();
            var vehicle = a.GetComponent<TrafficVehicle>();
            float rearZ = -a.GetComponent<LaneFollower>().rearOverhang + 0.02f;
            var bodyA = a.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name.EndsWith("_Body"));
            var bodyB = b.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name.EndsWith("_Body"));
            int openCut = RearTriangles(bodyA, a.transform, rearZ, false), openRect = RearTriangles(bodyA, a.transform, rearZ, true);
            int srcCut = RearTriangles(bodyB, b.transform, rearZ, false), srcRect = RearTriangles(bodyB, b.transform, rearZ, true);
            bool meshOk = openMesh != null && bodyA != null && bodyA.sharedMesh == openMesh && openCut == 0 && openRect == 0 && srcCut > 0 && bodyB != null && openMesh.triangles.Length < bodyB.sharedMesh.triangles.Length;
            // Paneller gövde kutusunun içinde
            var bodyBox = new Bounds(vehicle.bodyCenter, vehicle.bodySize + Vector3.one * 0.02f);
            var panels = van.cargoRoot != null ? van.cargoRoot.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            bool panelsOk = van.cargoRoot != null && panels.Length == 5 && panels.All(p => bodyBox.Contains(p.bounds.min) && bodyBox.Contains(p.bounds.max))
                && van.cargoFloorY > vehicle.bodyCenter.y - vehicle.bodySize.y * 0.5f && van.cargoCeilingY < vehicle.bodyCenter.y + vehicle.bodySize.y * 0.5f && van.cargoRearZ < van.cargoFrontZ;
            // Kapalıyken dış ölçü (bütün renderer'lar; kapaklar kapalı pozda) PanelVan ile aynı
            Bounds ba = RendererBounds(a), bb = RendererBounds(b);
            bool sizeOk = (ba.size - bb.size).magnitude < 0.08f && (ba.center - bb.center).magnitude < 0.06f;
            Check("(U4c-0) kamyonet prefab'ı: gövde mesh kopyasında arka yüz kesik (açıklıkta üçgen yok), kasa 5 panel gövde içinde, kapalıyken dış ölçü PanelVan ile aynı",
                meshOk && panelsOk && sizeOk,
                "mesh " + (openMesh != null ? openMesh.name + " " + openMesh.triangles.Length / 3 : "yok") + " / kaynak " + (bodyB != null ? bodyB.sharedMesh.triangles.Length / 3 : 0) + " üçgen; arka bölgede kesilen kopya " + openCut + " (açıklık dikdörtgeni " + openRect + "), kaynak " + srcCut + " (" + srcRect + ")"
                + "; paneller " + panels.Length + " (" + string.Join("/", panels.Select(p => p.name)) + ") gövde içinde " + panelsOk + ", kasa y " + van.cargoFloorY.ToString("F2", Inv) + "–" + van.cargoCeilingY.ToString("F2", Inv) + " z " + van.cargoRearZ.ToString("F2", Inv) + "…" + van.cargoFrontZ.ToString("F2", Inv)
                + "; dış ölçü " + V(ba.size) + " / " + V(bb.size) + " merkez fark " + M((ba.center - bb.center).magnitude));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(a);
            UnityEngine.Object.DestroyImmediate(b);
        }
    }

    // Kök uzayında arka bölgedeki üçgen sayısı. rect=false: builder'ın kesim ölçütü (z ≤ arka + derinlik, y ≥ tampon üstü), lamba alt mesh'leri hariç;
    // rect=true: kapak açıklığı dikdörtgeni (|x| < 0.9, y 0.6–2.25).
    private static int RearTriangles(MeshFilter mf, Transform root, float rearZ, bool rect)
    {
        if (mf == null || mf.sharedMesh == null) return -1;
        var m = mf.sharedMesh;
        var r = mf.GetComponent<MeshRenderer>();
        var verts = m.vertices.Select(v => root.InverseTransformPoint(mf.transform.TransformPoint(v))).ToArray();
        float zCut = rearZ + Bolum1ThiefBuilder.RearCutDepth;
        int n = 0;
        for (int s = 0; s < m.subMeshCount; s++)
        {
            string mat = r != null && s < r.sharedMaterials.Length && r.sharedMaterials[s] != null ? r.sharedMaterials[s].name : "";
            if (mat.Contains("light") || mat.Contains("Amber")) continue;
            var tris = m.GetTriangles(s);
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
                bool inside = rect
                    ? In(a) && In(b) && In(c)
                    : a.z <= zCut && b.z <= zCut && c.z <= zCut && a.y >= Bolum1ThiefBuilder.RearCutMinY && b.y >= Bolum1ThiefBuilder.RearCutMinY && c.y >= Bolum1ThiefBuilder.RearCutMinY;
                if (inside) n++;
            }
        }
        return n;
        bool In(Vector3 p) { return p.z <= zCut && Mathf.Abs(p.x) < 0.9f && p.y >= 0.6f && p.y <= 2.25f; }
    }

    private static Bounds RendererBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // ---- Play aşaması ----
    private static IEnumerator PhaseU4c(ThiefDirector dir)
    {
        ParkCarriersOffLane();
        Loot.ResetForTest();
        Health.SetHealthForTest(Health.Max);
        Mission.Begin(TestSchedule(99999f, 99999f));
        Mission.missLimit = 999;
        if (Player.HeldValuable != null) { var v = Player.HeldValuable; Player.HeldValuable = null; v.ResetToStart(); }
        var luggage = LootItem.All.Where(l => l.kind == LootKind.Luggage && l.CanPickUp).OrderByDescending(l => l.TotalValue).ThenBy(l => l.name).ToList();
        var hidden = HideLootExcept();
        dir.Begin(TestThiefSettings(99999f, 99999f, 9999f)); // kamyonet kalkmasın
        yield return null;
        ThiefVan van = null;
        yield return SpawnParkedVan(dir, 1, v => van = v);
        var A = van.Thieves[0];
        A.ResetForTest();
        A.gameObject.SetActive(false);
        yield return PhaseU4cCargo(dir, van, A, luggage);
        yield return PhaseU4cAim(dir, van, A, luggage[0]);
        yield return PhaseU4cBar(van, A);
        // Toparla
        if (Player.HeldValuable != null) { var v = Player.HeldValuable; Player.HeldValuable = null; v.ResetToStart(); }
        Player.cameraTransform = null;
        RestoreLoot(hidden);
        Loot.ResetForTest();
        Mission.Begin(TestSchedule(99999f, 99999f));
        Mission.missLimit = 999;
        Health.SetHealthForTest(Health.Max);
        dir.Begin(TestThiefSettings(99999f, 99999f));
        BeforeCapture = null;
        Time.timeScale = 1f;
        yield return null;
    }

    // ---- (U4c-1) kasa içi görünür, eşyalar kasada, geri alma ----
    private static IEnumerator PhaseU4cCargo(ThiefDirector dir, ThiefVan van, Thief A, List<LootItem> luggage)
    {
        var parts = new List<string>();
        bool ok = true;
        void Note(string what, bool c, string detail) { ok &= c; parts.Add(what + (c ? " ✓" : " ✗") + " " + detail); }
        Vector3 vfwd = Flat(van.transform.forward).normalized, vright = Vector3.Cross(Vector3.up, vfwd);
        Bounds cargo = van.CargoWorldBounds;
        Bounds cargoLoose = cargo; cargoLoose.Expand(0.08f);
        // Işın testi: gövde mesh'ine ve panellere geçici collider; arkadan (2 m geride, kasa orta yüksekliğinde) ileri ışın
        var temps = new List<Collider>();
        var bodyMf = van.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name.EndsWith("_Body"));
        if (bodyMf != null) { var mc = bodyMf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = bodyMf.sharedMesh; temps.Add(mc); }
        if (van.cargoRoot != null) foreach (var r in van.cargoRoot.GetComponentsInChildren<Renderer>(true)) temps.Add(r.gameObject.AddComponent<BoxCollider>());
        Physics.SyncTransforms();
        float midY = (van.cargoFloorY + van.cargoCeilingY) * 0.5f;
        Vector3 start = van.transform.TransformPoint(new Vector3(0f, midY, van.cargoRearZ - 2f));
        string RayFirst(Vector3 from, Vector3 dirn, out float dist)
        {
            dist = -1f; string name = "yok"; float best = float.MaxValue;
            foreach (var h in Physics.RaycastAll(from, dirn, 12f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider == null || !h.collider.transform.IsChildOf(van.transform) || h.collider.gameObject == van.gameObject) continue; // kök BoxCollider (gövde kutusu) sayılmaz
                if (h.distance < best) { best = h.distance; name = (h.collider.transform.parent != null ? h.collider.transform.parent.name + "/" : "") + h.collider.name; }
            }
            if (best < float.MaxValue) dist = best;
            return name;
        }
        string hitMid = RayFirst(start, vfwd, out float dMid);
        // Hafif aşağı: zemine çarpmalı
        Vector3 down = (vfwd * 3f - Vector3.up * (midY - van.cargoFloorY + 0.05f)).normalized;
        string hitDown = RayFirst(start, down, out float dDown);
        foreach (var c in temps) if (c != null) UnityEngine.Object.Destroy(c);
        float depth = van.cargoFrontZ - van.cargoRearZ;
        bool rayOk = van.TrunkOpen && hitMid.EndsWith("Bulkhead") && dMid > 2f + depth - 0.3f && hitDown.EndsWith("Floor") && dDown > 2.2f;
        Note("kapaklar açıkken arkadan içeri ışın → kasa ön bölmesi / zemini (eski arka yüz değil)", rayOk, "orta ışın " + hitMid + " " + M(dMid) + " (beklenen ≥ " + M(2f + depth - 0.3f) + "), aşağı ışın " + hitDown + " " + M(dDown) + ", bagaj açık " + van.TrunkOpen);
        // Eşyalar kasada: 3 valiz atılmış sayılır
        var items = luggage.Take(3).ToList();
        foreach (var it in items) { it.gameObject.SetActive(true); van.AddLoot(it); }
        yield return null; yield return null;
        var slotInfo = new List<string>();
        bool itemsOk = items.Count == 3;
        var slots = new HashSet<int>();
        foreach (var it in items)
        {
            var rs = it.GetComponentsInChildren<Renderer>(true);
            Bounds wb = it.WorldBounds;
            bool inside = cargoLoose.Contains(wb.min) && cargoLoose.Contains(wb.max);
            bool visible = it.gameObject.activeInHierarchy && rs.Length > 0 && rs.All(r => r.enabled);
            bool cols = it.GetComponentsInChildren<Collider>(true).All(c => !c.enabled);
            bool c = it.IsInVan && visible && inside && cols && !it.CanPickUp && it.VanSlot >= 0 && slots.Add(it.VanSlot) && wb.min.y >= cargo.min.y - 0.03f;
            itemsOk &= c;
            slotInfo.Add(it.name + (c ? "" : "[HATA]") + " yuva " + it.VanSlot + " " + V(wb.center) + (inside ? "" : " DIŞARIDA") + (visible ? "" : " GÖRÜNMEZ") + (cols ? "" : " COLLIDER AÇIK"));
        }
        A.gameObject.SetActive(true);
        A.ResetForTest();
        A.TeleportForTest(LanePoint(dir, 20f, 0.5f), Quaternion.LookRotation(-vfwd));
        A.Stun(60f, Vector3.zero, "test-bekleme");
        yield return null;
        bool notStealable = items.All(it => !A.IsStealable(it));
        Note("atılan 3 valiz kasa zemininde görünür (etkin, renderer açık, kasa içinde, collider kapalı, alınamaz, ayrı yuvalar), hırsız hedef seçmez", itemsOk && notStealable,
            string.Join("; ", slotInfo) + "; kasa " + V(cargo.min) + "–" + V(cargo.max) + ", çalınabilir değil " + notStealable);
        BeforeCapture = FaceLabels;
        // Görüntü: arkadan, hafif yukarıdan kasaya bak (kapaklar yana açık)
        Vector3 trunk = van.transform.TransformPoint(new Vector3(0f, van.cargoFloorY + 0.35f, van.cargoRearZ + 0.9f));
        Vector3 camPos = van.transform.TransformPoint(new Vector3(0.6f, van.cargoFloorY + 1.1f, van.cargoRearZ - 3.4f));
        yield return Capture("u4c_bagaj_ici", camPos, trunk, 48f);
        // Geri alma: bagaja yaklaş, ipucu en değerli, vurgu eşyada
        var item1 = items[0];
        float rear = van.Follower.rearOverhang, halfW = van.Vehicle.bodySize.x * 0.5f;
        Vector3 bc = van.transform.position - vfwd * rear;
        var stands = new List<Vector3> { bc - vfwd * 0.8f, bc - vfwd * 0.45f + vright * (halfW + 0.35f), bc - vfwd * 0.45f - vright * (halfW + 0.35f), bc - vfwd * 0.9f + vright * (halfW + 0.2f), bc - vfwd * 0.9f - vright * (halfW + 0.2f) };
        bool hintOk = false; string standInfo = "";
        foreach (var cand in stands)
        {
            Vector3 p = U3Ground(cand, van.transform.position.y);
            Player.Teleport(p, Quaternion.LookRotation(Flat(van.TrunkCenter - p).normalized));
            yield return null; yield return null;
            standInfo = V(p) + " ipucu \"" + Interaction.CurrentHint + "\"";
            if (Interaction.CurrentHint.StartsWith(PlayerInteraction.HintRetrievePrefix)) { hintOk = true; break; }
        }
        bool hintItem = Interaction.CurrentHint == PlayerInteraction.HintRetrieve(item1) && Interaction.LootTarget == item1;
        bool hlItem = Interaction.HighlightTarget == item1.transform && Highlight.Current == item1.transform && Highlight.CopyCount >= 1;
        Vector3 standP = Player.transform.position;
        float roadSide = van.ShoulderLateral >= 0f ? -1f : 1f;
        yield return CaptureWithHud("u4c_geri_al", van.TrunkCenter + vright * roadSide * 3.2f - vfwd * 1.6f + Vector3.up * 1.7f, (standP + van.TrunkCenter) * 0.5f + Vector3.up * 0.4f, 52f);
        int slot1 = item1.VanSlot;
        Interaction.Interact();
        yield return null; yield return null;
        bool retrieved = Player.HeldValuable == item1 && item1.State == LootItem.LootState.Held && !cargoLoose.Contains(item1.WorldBounds.center) && van.Loot.Count == 2 && item1.VanSlot < 0 && item1.Van == null;
        // Boşalan yuva yeniden kullanılır
        var item4 = luggage.Count > 3 ? luggage[3] : null;
        bool reuse = true;
        if (item4 != null) { item4.gameObject.SetActive(true); van.AddLoot(item4); yield return null; reuse = item4.VanSlot == slot1 && item4.IsInVan && cargoLoose.Contains(item4.WorldBounds.center); }
        Note("\"E: Geri al (en değerli)\" + eşya vurgusu → eşya kasadan çıkar (elde), yuvası boşalır ve yeniden kullanılır", hintOk && hintItem && hlItem && retrieved && reuse,
            standInfo + ", vurgu " + HlName() + " (" + Highlight.CopyCount + " kopya), elde " + (Player.HeldValuable == item1) + ", kasa dışında " + !cargoLoose.Contains(item1.WorldBounds.center) + ", kamyonette " + van.Loot.Count + ", yuva " + slot1 + " → " + (item4 != null ? item4.name + " yuva " + item4.VanSlot : "4. valiz yok"));
        Check("(U4c-1) bagaj içi: kapaklar açıkken arka açıklıktan kasa içi görünür (ışın kasa panelini bulur), kamyonete atılan eşyalar kasa zemininde yuvalarda görünür durur (≤ 6 yuva, fazlası üst üste), alınamaz/çalınamaz; \"E: Geri al\" vurgusu eşyada, geri alınan kasadan çıkar",
            ok, string.Join("; ", parts));
        BeforeCapture = null;
        if (Player.HeldValuable != null) { var v = Player.HeldValuable; Player.HeldValuable = null; v.ResetToStart(); }
        foreach (var it in items) if (it.IsInVan) Loot.RestoreForTest(it);
        if (item4 != null && item4.IsInVan) Loot.RestoreForTest(item4);
        foreach (var it in items) it.gameObject.SetActive(false);
        if (item4 != null) item4.gameObject.SetActive(false);
    }

    // ---- (U4c-2) nişangah ve kamera merkezine atış ----
    private static IEnumerator PhaseU4cAim(ThiefDirector dir, ThiefVan van, Thief A, LootItem item)
    {
        var parts = new List<string>();
        bool ok = true;
        void Note(string what, bool c, string detail) { ok &= c; parts.Add(what + (c ? " ✓" : " ✗") + " " + detail); }
        var combat = Combat;
        var hud = Hud;
        Vector3 vf = LaneDir(dir, 10f), vr = Vector3.Cross(Vector3.up, vf);
        Vector3 arena = LanePoint(dir, 10f, 0.5f);
        combat.ResetCooldownsForTest();
        // Hırsız 8 m ileride, sersem; kamera oyuncunun 2.5 m arkasında, hırsızın göğsüne bakar (ekran ortası = hırsız)
        A.ResetForTest();
        A.TeleportForTest(LanePoint(dir, 18f, 0.5f), Quaternion.LookRotation(-vf));
        A.Stun(60f, Vector3.zero, "test-hedef");
        item.gameObject.SetActive(true);
        item.Drop(U3Ground(arena + vf * 1.0f, arena.y), 0f);
        Player.Teleport(arena, Quaternion.LookRotation(vf));
        yield return null; yield return null;
        Interaction.Interact();
        yield return null;
        bool held = Player.HeldValuable == item;
        var cam = new GameObject("U4c_AimCam").transform;
        cam.position = arena - vf * 2.5f + Vector3.up * 1.6f;
        cam.LookAt(A.transform.position + Vector3.up * 0.9f);
        Player.cameraTransform = cam;
        // Gövde yana dönük: nişan alınca kameraya dönmeli
        Player.Teleport(arena, Quaternion.LookRotation(vr));
        yield return null;
        bool crossBefore = hud.CrosshairVisible;
        float thiefDist = Flat(A.transform.position - Player.transform.position).magnitude;
        combat.SetThrowHeld(true);
        yield return null; yield return null; yield return null;
        bool aiming = combat.Aiming && PlayerAnim.InState(CrewAnimation.StateThrowAim);
        bool crossOn = hud.CrosshairVisible;
        var cr = hud.Crosshair != null ? hud.Crosshair.GetComponent<RectTransform>() : null;
        bool centered = cr != null && cr.anchorMin == new Vector2(0.5f, 0.5f) && cr.anchorMax == new Vector2(0.5f, 0.5f) && cr.anchoredPosition.magnitude < 0.5f && hud.Crosshair.GetComponentsInChildren<UnityEngine.UI.Image>().Length == 2;
        float faceErr = Vector3.Angle(Flat(Player.transform.forward), Flat(cam.forward));
        Vector3 aimPoint = combat.AimPoint();
        string aimHit = combat.LastAimHit;
        float aimErr = Vector3.Distance(aimPoint, A.transform.position + Vector3.up * 0.9f);
        Note("sağ tık basılı → nişangah ekran ortasında, Throw_Aim, gövde kameraya döner, hedef = merkez ışınındaki hırsız", held && !crossBefore && aiming && crossOn && centered && faceErr < 5f && aimHit.Contains(A.name) && aimErr < 0.8f,
            "elde " + held + ", önce nişangah " + crossBefore + " → " + crossOn + " (ortada " + centered + "), nişan " + aiming + ", gövde-kamera " + faceErr.ToString("F0", Inv) + "°, ışın: " + aimHit + " (hırsız göğsüne " + M(aimErr) + ", uzaklık " + M(thiefDist) + ")");
        BeforeCapture = FaceLabels;
        yield return CaptureWithHud("u4c_nisangah", cam.position, A.transform.position + Vector3.up * 0.9f, 50f);
        // Bırak → eşya hırsıza isabet eder, nişangah kaybolur
        int hits0 = item.ThrowHits; float hp0 = A.Health;
        combat.SetThrowHeld(false);
        yield return null; yield return null;
        bool crossOff = !hud.CrosshairVisible && !combat.Aiming;
        float tTh = Time.time;
        yield return (Func<bool>)(() => item.ThrowHits > hits0 || (Time.time - tTh > 0.9f && !item.IsFlying) || Time.time - tTh > 5f);
        yield return null;
        string arc = combat.LastAimArc;
        Note("bırak → nişangah kaybolur, eşya 8 m'deki hırsıza isabet (" + arc + " yay)", crossOff && item.ThrowHits == hits0 + 1 && A.LastHitReason == "firlatma" && Mathf.Abs(hp0 - A.Health - combat.settings.throwHitDamage) < 0.01f,
            "nişangah kapandı " + crossOff + ", isabet +" + (item.ThrowHits - hits0) + ", can " + hp0.ToString("F0", Inv) + " → " + A.Health.ToString("F0", Inv) + " (" + A.LastHitReason + "), hedef " + V(combat.LastAimPoint) + ", yay " + arc);
        yield return Until(() => !item.IsFlying, 8f, "eşya indi");
        yield return Until(() => !combat.Busy && Player.HasControl, 3f, "atış bitti");
        // Işın boşa giderse: gökyüzüne bakan kamera → hedef 20 m ilerisi
        cam.position = arena - vf * 2.5f + Vector3.up * 1.6f;
        cam.rotation = Quaternion.LookRotation((vf + Vector3.up * 0.9f).normalized, Vector3.up);
        yield return null;
        Vector3 emptyPoint = combat.AimPoint();
        bool emptyOk = combat.LastAimHit.StartsWith("boşluk") && Mathf.Abs(Vector3.Distance(emptyPoint, cam.position) - PlayerCombat.AimEmptyDistance) < 0.05f;
        Vector3 ballistic = PlayerCombat.BallisticDirection(Player.transform.position + Vector3.up * 1.4f, emptyPoint, combat.settings.throwSpeed, out string arcEmpty);
        Note("ışın boşa → hedef kameranın 20 m ilerisi", emptyOk && ballistic.y > 0f, combat.LastAimHit + ", hedef " + V(emptyPoint) + ", yön y " + ballistic.y.ToString("F2", Inv) + " (" + arcEmpty + ")");
        // Menzil içi alçak yay: 6 m düz hedef → düşük yay, eşya o noktaya iner (balistik denklem kontrolü)
        Vector3 from = Vector3.zero, to = new Vector3(0f, 0f, 6f);
        Vector3 lowDir = PlayerCombat.BallisticDirection(from, to, combat.settings.throwSpeed, out string arcLow);
        float g = -Physics.gravity.y, v = combat.settings.throwSpeed;
        float tFlight = 6f / (lowDir.z * v);
        float landY = lowDir.y * v * tFlight - 0.5f * g * tFlight * tFlight;
        Vector3 farDir = PlayerCombat.BallisticDirection(from, new Vector3(0f, 0f, 40f), v, out string arcFar);
        Note("balistik: 6 m hedefe alçak yay tam iner; 40 m → 45°", arcLow == "alçak" && Mathf.Abs(landY) < 0.02f && arcFar == "45°" && Mathf.Abs(Vector3.Angle(farDir, Vector3.forward) - 45f) < 0.5f,
            "6 m: açı " + Vector3.Angle(lowDir, Vector3.forward).ToString("F1", Inv) + "° iniş y " + landY.ToString("F3", Inv) + " (" + arcLow + "); 40 m: " + Vector3.Angle(farDir, Vector3.forward).ToString("F1", Inv) + "° (" + arcFar + ")");
        Check("(U4c-2) nişangah: sağ tık basılıyken ekran ortasında artı (bırakınca kaybolur), gövde kamera yönüne döner; atış hedefi kameranın merkez ışınının çarptığı nokta (yoksa 20 m ilerisi), eşya " + combat.settings.throwSpeed.ToString(Inv) + " m/sn ile oraya düşecek balistik yay (menzil dışı 45°); 8 m'deki hırsıza isabet",
            ok, string.Join("; ", parts));
        BeforeCapture = null;
        Player.cameraTransform = null;
        UnityEngine.Object.Destroy(cam.gameObject);
        if (Player.HeldValuable != null) { var hv = Player.HeldValuable; Player.HeldValuable = null; hv.ResetToStart(); }
        item.ResetToStart();
        item.gameObject.SetActive(false);
    }

    // ---- (U4c-3) pes edenin barı gizli ----
    private static IEnumerator PhaseU4cBar(ThiefVan van, Thief A)
    {
        A.ResetForTest();
        A.Stun(30f, Vector3.zero, "test-bekleme");
        yield return null;
        var label = UnityEngine.Object.FindObjectsByType<ThiefLabel>(FindObjectsSortMode.None).FirstOrDefault(l => l.thief == A);
        bool barBefore = label != null && label.BarVisible;
        bool hit = A.Hit(A.MaxHealth, 2f, Vector3.zero, "test");
        yield return null; yield return null;
        bool barHidden = label != null && !label.BarVisible && A.Surrendered && A.gameObject.activeInHierarchy && !A.IsGone;
        A.ResetForTest();
        A.Stun(30f, Vector3.zero, "test-bekleme");
        yield return null; yield return null;
        bool barBack = label != null && label.BarVisible;
        Check("(U4c-3) pes eden hırsızın (can 0) can barı gizlenir; can dolunca (ResetForTest) geri gelir", barBefore && hit && barHidden && barBack,
            "önce bar " + barBefore + ", darbe " + hit + " → pes " + A.Surrendered + " bar gizli " + barHidden + ", sıfırlayınca bar " + barBack);
        A.gameObject.SetActive(false);
    }
}
