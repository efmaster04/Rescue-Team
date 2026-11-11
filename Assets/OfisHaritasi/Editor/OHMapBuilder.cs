using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Menü: Rescue Team > Ofis Haritası > Haritayı Kur / Haritayı Kaldır
// Batchmode: Unity.exe -batchmode -projectPath ... -executeMethod OfisHaritasi.EditorTools.OHMapBuilder.BuildBatch
namespace OfisHaritasi.EditorTools
{
    public static class OHMapBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/OfficeGarage.unity";
        public const string RootName = "20_OfisHaritasi";
        public const string MeshAsset = OHAssetPrep.Root + "/Uretilen/OH_Harita_Meshleri.asset";
        public const int Seed = 20261106;

        static readonly string[] LegacyPrefixes = {
            "E00_continuous grassy site", "E01_asphalt strip", "E02_reflective paint dash", "E04_formed curb",
            "E11_sloping terrain wedge", "E12_faceted silhouette ridge", "EK_karsi kaldirim", "EK_yakin kaldirim" };

        [MenuItem("Rescue Team/Ofis Haritası/Haritayı Kur")]
        public static void BuildMenu()
        {
            // Build sahneyi yeniden açar: açık sahnedeki kaydedilmemiş iş sorulmadan kaybolmasın
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(true);
        }

        [MenuItem("Rescue Team/Ofis Haritası/Haritayı Kaldır")]
        public static void RemoveMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath);
            Remove();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static void BuildBatch()
        {
            try { Build(true); EditorApplication.Exit(0); }
            catch (System.Exception ex) { Debug.LogError("OHMapBuilder: " + ex); EditorApplication.Exit(1); }
        }

        static void Remove()
        {
            var old = GameObject.Find(RootName);
            if (!old) return;
            var mr = old.GetComponent<OHMapRoot>();
            if (mr) foreach (var g in mr.disabledLegacy) if (g) g.SetActive(true);
            if (mr && mr.hiddenRenderers != null) foreach (var r in mr.hiddenRenderers) if (r) r.enabled = true;
            var ground = FindPath("00_WorldCollision/Ground");
            if (ground) ground.SetActive(true);
            Object.DestroyImmediate(old);
        }

        static GameObject FindPath(string path)
        {
            var parts = path.Split('/');
            foreach (var r in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (r.name != parts[0]) continue;
                Transform t = r.transform;
                for (int i = 1; i < parts.Length && t; i++) t = t.Find(parts[i]);
                if (t) return t.gameObject;
            }
            return null;
        }

        // ---------------------------------------------------------------- malzemeler

        static Material Mat(string name, Color c, float smooth = 0.25f, float metal = 0f, Color? emission = null)
        {
            return OHAssetPrep.MakeMat($"{OHAssetPrep.MatDir}/{name}.mat", c, metal, smooth, emission ?? Color.black);
        }

        static Material ShaderMat(string name, string shader)
        {
            string path = $"{OHAssetPrep.MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shader);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            if (m.shader != sh) m.shader = sh;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material[] RoadMats(GameObject env)
        {
            var m = new Material[OHSub.Count];
            m[OHSub.Asfalt] = Mat("OH_Asfalt", new Color(0.25f, 0.255f, 0.265f), 0.18f);
            m[OHSub.Bordur] = Mat("OH_Bordur", new Color(0.63f, 0.62f, 0.59f), 0.15f);
            m[OHSub.Kaldirim] = Mat("OH_Kaldirim", new Color(0.66f, 0.645f, 0.61f), 0.12f);
            m[OHSub.Cizgi] = Mat("OH_Cizgi", new Color(0.90f, 0.89f, 0.84f), 0.3f);
            m[OHSub.Banket] = Mat("OH_Banket", new Color(0.47f, 0.44f, 0.37f), 0.05f);
            m[OHSub.Park] = Mat("OH_ParkSeridi", new Color(0.29f, 0.295f, 0.30f), 0.15f);
            m[OHSub.Beton] = Mat("OH_Beton", new Color(0.60f, 0.59f, 0.56f), 0.12f);
            m[OHSub.TunelIc] = Mat("OH_TunelIc", new Color(0.055f, 0.055f, 0.052f), 0.05f);
            m[OHSub.TunelIsik] = Mat("OH_TunelIsik", new Color(1f, 0.9f, 0.7f), 0.3f, 0, new Color(1f, 0.82f, 0.55f) * 2.2f);
            // mevcut ofis çevresiyle aynı malzeme (varsa)
            if (env)
            {
                foreach (var r in env.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.name.StartsWith("E01_asphalt strip_World") && r.sharedMaterial) m[OHSub.Asfalt] = r.sharedMaterial;
                    if (r.name.StartsWith("EK_yakin kaldirim") && r.sharedMaterial) m[OHSub.Kaldirim] = r.sharedMaterial;
                    if (r.name.StartsWith("E04_formed curb_World") && r.sharedMaterial) m[OHSub.Bordur] = r.sharedMaterial;
                    if (r.name.StartsWith("E02_reflective paint dash_World") && r.sharedMaterial) m[OHSub.Cizgi] = r.sharedMaterial;
                }
            }
            return m;
        }

        // ---------------------------------------------------------------- ana akış

        public static StringBuilder Report = new StringBuilder();

        public static void Build(bool saveScene)
        {
            Report.Clear();
            var t0 = System.DateTime.Now;
            OHAssetPrep.EnsureDir(OHAssetPrep.Root + "/Uretilen");
            OHAssetPrep.PrepareAll(System.Environment.GetEnvironmentVariable("OH_FORCE") == "1");
            foreach (var l in OHAssetPrep.Log) Report.AppendLine("  " + l);

            OHNetBuild.Geometry();
            var roadsAll = OHRoadIndex.Build(false);
            var roadsClamp = OHRoadIndex.Build(true);
            var plan = new OHPlan();
            OHPlacement.PlaceLots(plan, roadsAll, Seed);
            var bridges = OHRoadMeshGen.FindBridges();
            OHPlacement.PlaceStreet(plan, roadsAll, bridges);
            OHPlacement.PlacePark(plan);

            var scene = EditorSceneManager.OpenScene(ScenePath);
            Remove();
            var env = GameObject.Find("01_Environment_OfficeGarage");

            var ter = new OHTerrain(roadsClamp);
            foreach (var l in plan.lots) ter.lots.Add(l.rect);
            if (env)
            {
                var g = env.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.name.StartsWith("E00_continuous grassy site"));
                if (g && g.sharedMaterial && g.sharedMaterial.HasProperty("_BaseColor")) ter.legacyGrass = g.sharedMaterial.GetColor("_BaseColor");
            }
            var net = OHNetBuild.Lanes();
            OHPlacement.PlaceNature(plan, roadsAll, ter, Seed + 1);
            OHPlacement.PlaceParked(plan, Seed + 2);
            foreach (var l in plan.log) Report.AppendLine("  " + l);

            // ---- mesh'ler
            if (File.Exists(Path.Combine(Application.dataPath, "..", MeshAsset))) AssetDatabase.DeleteAsset(MeshAsset);
            var holder = ScriptableObject.CreateInstance<OHMeshHolder>();
            AssetDatabase.CreateAsset(holder, MeshAsset);
            var meshes = new List<Mesh>();
            System.Func<Mesh, Mesh> Keep = m => { AssetDatabase.AddObjectToAsset(m, holder); meshes.Add(m); return m; };

            var root = new GameObject(RootName);
            var mapRoot = root.AddComponent<OHMapRoot>();
            var world = root.AddComponent<OHWorldSettings>();
            var roadMats = RoadMats(env);
            var zeminMat = ShaderMat("OH_Zemin", "OfisHaritasi/Zemin");
            var suMat = ShaderMat("OH_Su", "OfisHaritasi/Su");
            var telMat = ShaderMat("OH_Tel", "Universal Render Pipeline/Unlit");
            telMat.SetColor("_BaseColor", new Color(0.09f, 0.09f, 0.09f));
            var kumMat = Mat("OH_Kum", new Color(0.74f, 0.66f, 0.49f), 0.05f);

            // arazi
            var gTer = Child(root, "Arazi");
            int tri = 0;
            foreach (var m in ter.BuildChunks())
            {
                Keep(m);
                var go = MeshGO(gTer, m.name, m, new[] { zeminMat }, true);
                tri += m.triangles.Length / 3;
            }
            Report.AppendLine($"  arazi parca: {gTer.transform.childCount}, ucgen {tri}");
            var water = Keep(ter.Water());
            var wgo = MeshGO(Child(root, "Dere"), "Dere_Su", water, new[] { suMat }, false);
            wgo.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // yollar
            var gRoad = Child(root, "Yollar");
            foreach (var e in OHLayout.Edges) MeshGO(gRoad, e.name, Keep(OHRoadMeshGen.Edge(e)), roadMats, true);
            var gJ = Child(root, "Kavsaklar");
            foreach (var j in OHLayout.Junctions) MeshGO(gJ, j.name, Keep(OHRoadMeshGen.Junction(j)), roadMats, true);
            var gB = Child(root, "Kopruler");
            foreach (var b in bridges) MeshGO(gB, "Kopru_" + b.e.name, Keep(OHRoadMeshGen.Bridge(b)), roadMats, true);
            var gT = Child(root, "Tuneller");
            var playerOnly = new List<Collider>();
            foreach (var t in OHLayout.Tunnels)
            {
                var tm = Keep(OHRoadMeshGen.Tunnel(t, (x, z) => ter.Height(x, z, ter.InTunnelRect(x, z))));
                var go = MeshGO(gT, t.name, tm, roadMats, true);
                // oyuncu duvarı
                var wall = new GameObject(t.name + "_OyuncuDuvari");
                wall.transform.SetParent(gT.transform, false);
                wall.transform.position = t.portal + t.inward * 13f + Vector3.up * 3f;
                wall.transform.rotation = Quaternion.LookRotation(t.inward);
                var bc = wall.AddComponent<BoxCollider>(); bc.size = new Vector3(t.halfWidth * 2f, 7f, 0.5f);
                playerOnly.Add(bc);
                // içeride hafif ışık
                var lg = new GameObject(t.name + "_Isik");
                lg.transform.SetParent(gT.transform, false);
                lg.transform.position = t.portal + t.inward * 9f + Vector3.up * 4.5f;
                var li = lg.AddComponent<Light>(); li.type = LightType.Point; li.range = 12f; li.intensity = 0.9f; li.color = new Color(1f, 0.82f, 0.6f); li.shadows = LightShadows.None;
            }
            // park yolları ve kum
            var parkMb = new OHMeshBuilder(OHSub.Count, false, true);
            foreach (var p in plan.parkPaths) Slab(parkMb, OHSub.Kaldirim, p, p.c.y);
            var gPark = Child(root, "Park");
            MeshGO(gPark, "Park_Yollari", Keep(parkMb.ToMesh("OH_Park_Yollari")), roadMats, true);
            var sandMb = new OHMeshBuilder(1, false, false);
            foreach (var s in plan.sand) Slab(sandMb, 0, s, s.c.y);
            MeshGO(gPark, "Oyun_Kumu", Keep(sandMb.ToMesh("OH_Oyun_Kumu")), new[] { kumMat }, true);

            // ---- eski dış çevre
            // Nesneleri kapatmıyoruz (ofis doğrulaması ortamdaki renderer sayısını sayıyor); yalnız çizimlerini kapatıyoruz.
            var hidden = new List<Renderer>();
            if (env)
            {
                foreach (var r in env.GetComponentsInChildren<Renderer>(false))
                    if (LegacyPrefixes.Any(p => r.name.StartsWith(p)) && r.enabled) { r.enabled = false; hidden.Add(r); }
                // eski ofis önü döşemelerine çarpışma kutusu (eski düz zemin kutusu kapanıyor)
                var gCol = Child(root, "Eski_Cevre_Carpisma");
                foreach (var r in env.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!(r.name.StartsWith("E03_poured concrete slab") || r.name.StartsWith("E26_roadside fill strip"))) continue;
                    var b = r.bounds;
                    var go = new GameObject("COL_" + r.name);
                    go.transform.SetParent(gCol.transform, false);
                    go.transform.position = new Vector3(b.center.x, b.max.y - 0.15f, b.center.z);
                    go.AddComponent<BoxCollider>().size = new Vector3(b.size.x, 0.3f, b.size.z);
                }
            }
            var ground = FindPath("00_WorldCollision/Ground");
            if (ground) ground.SetActive(false);
            var floor = new GameObject("Bina_Zemini");
            floor.transform.SetParent(root.transform, false);
            floor.transform.position = new Vector3(-0.3f, -0.16f, 3.42f);
            floor.AddComponent<BoxCollider>().size = new Vector3(19.7f, 0.30f, 17.75f);
            // Kurtarma Hangarı bu ayak izinin doğusunda kalır ve kendi zemin çarpıştırıcısı yoktur (kapanan Ground'a basıyordu).
            // Hangar kuruluysa plakasının altına aynı üst kotta ikinci bir kutu konur; harita kaldırılınca kökle birlikte silinir.
            if (GameObject.Find(GarageHangarBuilder.RootName))
            {
                float hx0 = GarageHangarBuilder.X0, hx1 = GarageHangarBuilder.X1 + GarageHangarBuilder.T;
                float hz0 = GarageHangarBuilder.Z0 - GarageHangarBuilder.T, hz1 = GarageHangarBuilder.Z1 + GarageHangarBuilder.T;
                var hangarFloor = new GameObject("Hangar_Zemini");
                hangarFloor.transform.SetParent(root.transform, false);
                hangarFloor.transform.position = new Vector3((hx0 + hx1) * 0.5f, -0.16f, (hz0 + hz1) * 0.5f);
                hangarFloor.AddComponent<BoxCollider>().size = new Vector3(hx1 - hx0, 0.30f, hz1 - hz0);
            }
            mapRoot.disabledLegacy = new GameObject[0];
            mapRoot.hiddenRenderers = hidden.ToArray();
            Report.AppendLine($"  gizlenen eski dis parca: {hidden.Count}");

            // ---- yerleşim
            var gLots = Child(root, "Parseller");
            int lotsMade = 0;
            var placedLots = new List<KeyValuePair<OHLot, GameObject>>();
            foreach (var l in plan.lots)
            {
                if (l.a == null || !l.a.prefab) continue;
                var go = Place(Child(gLots, ZoneName(l.zone)), l.a.prefab, l.origin, l.rot, 1f);
                ApplyVariants(go, l.seed);
                placedLots.Add(new KeyValuePair<OHLot, GameObject>(l, go));
                lotsMade++;
            }
            Report.AppendLine($"  gizlenen ortak yan duvar: {HideSharedWalls(placedLots)}");
            var gProps = Child(root, "Donati");
            int propsMade = 0;
            var poles = new List<KeyValuePair<OHPlaced, GameObject>>();
            var signals = new List<OHSignalLight>();
            foreach (var p in plan.props)
            {
                var a = OHAssetPrep.Get(p.id);
                if (a == null) continue;
                var go = Place(Child(gProps, p.group ?? "Diger"), a.prefab, p.pos, p.rot, p.scale);
                propsMade++;
                if (p.id == "Elektrik_Diregi") poles.Add(new KeyValuePair<OHPlaced, GameObject>(p, go));
                var sl = go.GetComponent<OHSignalLight>();
                if (sl) { sl.junction = p.junction; sl.group = p.signalGroup; signals.Add(sl); }
            }
            var gTrees = Child(root, "Agaclar");
            int treesMade = 0;
            foreach (var t in plan.trees)
            {
                var a = OHAssetPrep.Get(t.id);
                if (a == null) continue;
                Place(Child(gTrees, t.group), a.prefab, t.pos, t.rot, t.scale);
                treesMade++;
            }
            var gPark2 = Child(root, "Park_Etmis_Araclar");
            int parkedMade = 0;
            foreach (var p in plan.parked)
            {
                var a = OHAssetPrep.Get(p.id);
                if (a == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(a.prefab, Child(gPark2, p.group).transform);
                go.transform.SetPositionAndRotation(p.pos, p.rot);
                var v = go.GetComponent<OHVehicle>();
                if (v && v.bodyRenderer && v.paintIndex >= 0)
                {
                    var sm = v.bodyRenderer.sharedMaterials;
                    string baseName = sm[v.paintIndex] ? sm[v.paintIndex].name : "";
                    var variant = OHAssetPrep.Variant(baseName, p.seed);
                    if (variant) { sm[v.paintIndex] = variant; v.bodyRenderer.sharedMaterials = sm; }
                }
                parkedMade++;
            }
            // uzak küçük nesneleri ele (kamera artık 1500 m görüyor)
            int lods = 0;
            lods += Cull(gTrees, 0.010f);
            lods += Cull(gProps, 0.018f);
            lods += Cull(gPark2, 0.012f);
            lods += Cull(gLots, 0.0045f);
            Report.AppendLine($"  LOD grubu: {lods}");
            // teller
            var gWire = Child(root, "Teller");
            int wires = Wires(gWire, poles, telMat);
            Report.AppendLine($"  sahne: parsel {lotsMade}, donati {propsMade}, agac {treesMade}, park arac {parkedMade}, tel {wires}");

            // ---- trafik
            var gTraffic = Child(root, "Trafik");
            var netC = gTraffic.AddComponent<OHTrafficNetwork>();
            netC.lanes = net.lanes;
            netC.junctions = net.junctions;
            netC.sources = net.sources.ToArray();
            netC.sinks = net.sinks.ToArray();
            var man = gTraffic.AddComponent<OHTrafficManager>();
            man.network = netC;
            var ids = new[] { "Arac_Hatchback", "Arac_Sedan", "Arac_Taksi", "Arac_SUV", "Arac_Pickup", "Arac_HafifTicari", "Arac_Minibus", "Arac_Kamyon", "Arac_Traktor" };
            var w = new[] { 18f, 18f, 8f, 12f, 9f, 12f, 9f, 6f, 2f };
            var mx = new[] { 0, 0, 5, 0, 0, 0, 6, 4, 2 };
            var pf = new List<OHVehicle>(); var pw = new List<float>(); var pm = new List<int>();
            for (int i = 0; i < ids.Length; i++)
            {
                var a = OHAssetPrep.Get(ids[i]);
                if (a == null) continue;
                var v = a.prefab.GetComponent<OHVehicle>();
                if (!v) continue;
                pf.Add(v); pw.Add(w[i]); pm.Add(mx[i]);
            }
            man.prefabs = pf.ToArray(); man.weights = pw.ToArray(); man.maxPerType = pm.ToArray();
            man.movingCount = 44;

            // ---- sınırlar
            var gB2 = Child(root, "Sinirlar");
            playerOnly.Add(Wall(gB2, "Bati", new Vector3(OHLayout.PlayMinX, 40, (OHLayout.PlayMinZ + OHLayout.PlayMaxZ) / 2), new Vector3(1, 200, OHLayout.PlayMaxZ - OHLayout.PlayMinZ)));
            playerOnly.Add(Wall(gB2, "Dogu", new Vector3(OHLayout.PlayMaxX, 40, (OHLayout.PlayMinZ + OHLayout.PlayMaxZ) / 2), new Vector3(1, 200, OHLayout.PlayMaxZ - OHLayout.PlayMinZ)));
            playerOnly.Add(Wall(gB2, "Guney", new Vector3(0, 40, OHLayout.PlayMinZ), new Vector3(OHLayout.PlayMaxX - OHLayout.PlayMinX, 200, 1)));
            playerOnly.Add(Wall(gB2, "Kuzey", new Vector3(0, 40, OHLayout.PlayMaxZ), new Vector3(OHLayout.PlayMaxX - OHLayout.PlayMinX, 200, 1)));
            mapRoot.playerOnly = playerOnly.ToArray();

            mapRoot.info = $"Ofis haritasi {System.DateTime.Now:yyyy-MM-dd HH:mm}\n" + Report.ToString();
            AssetDatabase.SaveAssets();
            if (saveScene)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            Report.AppendLine($"  sure: {(System.DateTime.Now - t0).TotalSeconds:F0} sn");
            WriteReport("Kurulum_Raporu.txt");
            Debug.Log("OHMapBuilder bitti\n" + Report);
        }

        public static void WriteReport(string name)
        {
            File.WriteAllText(Path.Combine(OHAssetPrep.ReportDir(), name), Report.ToString());
        }

        static string ZoneName(string z) { return string.IsNullOrEmpty(z) ? "Diger" : z; }

        static GameObject Child(GameObject parent, string name)
        {
            var t = parent.transform.Find(name);
            if (t) return t.gameObject;
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        static GameObject MeshGO(GameObject parent, string name, Mesh m, Material[] mats, bool collider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            var used = new Material[m.subMeshCount];
            for (int i = 0; i < used.Length; i++) used[i] = mats[Mathf.Min(i, mats.Length - 1)];
            mr.sharedMaterials = used;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = m;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        static void Slab(OHMeshBuilder mb, int sub, OHRect r, float y)
        {
            var c = r.Corners();
            for (int i = 0; i < 4; i++) c[i].y = y;
            mb.QuadUp(sub, c[0], c[1], c[2], c[3]);
            for (int i = 0; i < 4; i++) mb.Wall(sub, c[i], c[(i + 1) % 4], y - 0.4f, y, (c[i] + c[(i + 1) % 4]) * 0.5f - r.c);
        }

        static GameObject Place(GameObject parent, GameObject prefab, Vector3 pos, Quaternion rot, float scale)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
            go.transform.SetPositionAndRotation(pos, rot);
            if (Mathf.Abs(scale - 1f) > 1e-3f) go.transform.localScale = Vector3.one * scale;
            return go;
        }

        static void ApplyVariants(GameObject go, int seed)
        {
            var rnd = new System.Random(seed);
            var pick = new Dictionary<string, int>();
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                var sm = r.sharedMaterials;
                bool ch = false;
                for (int i = 0; i < sm.Length; i++)
                {
                    if (!sm[i] || !OHAssetPrep.Variants.ContainsKey(sm[i].name)) continue;
                    int k;
                    if (!pick.TryGetValue(sm[i].name, out k)) { k = rnd.Next(1000); pick[sm[i].name] = k; }
                    sm[i] = OHAssetPrep.Variant(sm[i].name, k); ch = true;
                }
                if (ch) r.sharedMaterials = sm;
            }
        }

        // Aynı sokak kenarında yan yana duran iki konut parselinde ikinci evin komşuya bakan yan duvarını kapatır.
        static int HideSharedWalls(List<KeyValuePair<OHLot, GameObject>> placed)
        {
            int n = 0;
            foreach (var g in placed.GroupBy(p => p.Key.e.id * 2 + (p.Key.left ? 1 : 0)))
            {
                var list = g.OrderBy(p => p.Key.sCenter).ToList();
                for (int i = 1; i < list.Count; i++)
                {
                    var prev = list[i - 1].Key; var cur = list[i].Key;
                    if (cur.a.set != "Konutlar") continue;
                    float wPrev = OHPlacement.Parcel(prev.a.id).x, wCur = OHPlacement.Parcel(cur.a.id).x;
                    float gap = (cur.sCenter - wCur * 0.5f) - (prev.sCenter + wPrev * 0.5f);
                    if (gap > 3.2f) continue;
                    string side = cur.left ? "_Duvar_Sol" : "_Duvar_Sag";
                    bool any = false;
                    foreach (var t in list[i].Value.GetComponentsInChildren<Transform>(true))
                        if (t.name.Contains(side)) { t.gameObject.SetActive(false); any = true; }
                    if (any) n++;
                }
            }
            return n;
        }

        // Her örneğe tek seviyeli LODGroup: ekranda belli bir boyutun altına inince çizilmez.
        static int Cull(GameObject group, float screenHeight)
        {
            int n = 0;
            foreach (Transform cat in group.transform)
            {
                foreach (Transform inst in cat)
                {
                    var rs = inst.GetComponentsInChildren<Renderer>(true).Where(r => !(r is LineRenderer)).ToArray();
                    if (rs.Length == 0 || inst.GetComponent<LODGroup>()) continue;
                    var lg = inst.gameObject.AddComponent<LODGroup>();
                    lg.SetLODs(new[] { new LOD(screenHeight, rs) });
                    lg.RecalculateBounds();
                    n++;
                }
            }
            return n;
        }

        static Collider Wall(GameObject parent, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject("Sinir_" + name);
            go.transform.SetParent(parent.transform, false);
            go.transform.position = center;
            var bc = go.AddComponent<BoxCollider>(); bc.size = size;
            return bc;
        }

        static int Wires(GameObject parent, List<KeyValuePair<OHPlaced, GameObject>> poles, Material mat)
        {
            int n = 0;
            var byEdge = poles.GroupBy(p => p.Key.edge * 2 + (p.Key.left ? 1 : 0));
            foreach (var g in byEdge)
            {
                var list = g.OrderBy(p => p.Key.s).ToList();
                for (int i = 0; i < list.Count - 1; i++)
                {
                    if (list[i + 1].Key.s - list[i].Key.s > 62f) continue;
                    var a = list[i].Value.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Tel_")).OrderBy(t => t.name).ToList();
                    var b = list[i + 1].Value.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Tel_")).OrderBy(t => t.name).ToList();
                    for (int k = 0; k < Mathf.Min(a.Count, b.Count); k++)
                    {
                        var go = new GameObject($"Tel_{n}");
                        go.transform.SetParent(parent.transform, false);
                        var lr = go.AddComponent<LineRenderer>();
                        lr.sharedMaterial = mat;
                        lr.widthMultiplier = 0.035f;
                        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        lr.receiveShadows = false;
                        lr.useWorldSpace = true;
                        int seg = 10;
                        lr.positionCount = seg + 1;
                        Vector3 p0 = a[k].position, p1 = b[k].position;
                        float sag = Vector3.Distance(p0, p1) * 0.018f + 0.15f;
                        for (int s = 0; s <= seg; s++)
                        {
                            float t = s / (float)seg;
                            lr.SetPosition(s, Vector3.Lerp(p0, p1, t) - Vector3.up * (4f * sag * t * (1 - t)));
                        }
                        n++;
                    }
                }
            }
            return n;
        }
    }
}
