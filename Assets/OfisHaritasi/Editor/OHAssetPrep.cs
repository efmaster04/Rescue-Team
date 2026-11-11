using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Alt agentlerin Blender'dan verdiği FBX + manifest + malzeme JSON'larını Unity'ye hazırlar:
// URP malzemeleri, renk varyantları, FBX malzeme eşlemesi, bina/donatı/araç prefab'ları.
namespace OfisHaritasi.EditorTools
{
    public class OHAssetInfo
    {
        public string id, set, fbxPath, prefabPath;
        public Dictionary<string, object> json;
        public Vector2 parsel;
        public readonly List<Vector2> cuts = new List<Vector2>();
        public readonly List<KeyValuePair<Vector3, float>> parks = new List<KeyValuePair<Vector3, float>>();
        public readonly List<Vector3> trees = new List<Vector3>();
        public GameObject prefab;
        public Bounds bounds;
        public bool Has { get { return prefab != null; } }
    }

    public static class OHAssetPrep
    {
        public const string Root = "Assets/OfisHaritasi";
        public const string ModelDir = Root + "/Modeller", MatDir = Root + "/Malzemeler", PrefabDir = Root + "/Prefablar";
        public static readonly string[] Sets = { "Araclar", "Ticari_Binalar", "Konutlar", "Donati" };

        public static readonly Dictionary<string, OHAssetInfo> Assets = new Dictionary<string, OHAssetInfo>();
        public static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        public static readonly Dictionary<string, List<Material>> Variants = new Dictionary<string, List<Material>>();
        public static readonly Dictionary<string, List<Color>> Palettes = new Dictionary<string, List<Color>>();
        public static readonly Dictionary<string, string> RandomKey = new Dictionary<string, string>();
        public static readonly List<string> Log = new List<string>();

        static Shader Lit { get { return Shader.Find("Universal Render Pipeline/Lit"); } }

        public static void EnsureDir(string path)
        {
            var parts = path.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        public static Material MakeMat(string path, Color c, float metallic, float smooth, Color emission, bool forceEmission = false)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Lit); AssetDatabase.CreateAsset(m, path); }
            if (m.shader != Lit) m.shader = Lit;
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_EnvironmentReflections", 1f);
            bool emit = forceEmission || emission.maxColorComponent > 0.001f;
            if (emit)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                m.SetColor("_EmissionColor", emission);
            }
            else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Color Col(object o, Color def)
        {
            var f = OHJson.Fs(o);
            if (f == null || f.Length < 3) return def;
            return new Color(f[0], f[1], f[2], f.Length > 3 ? f[3] : 1f);
        }

        // ---------------------------------------------------------------- malzemeler

        public static void LoadMaterials()
        {
            Mats.Clear(); Variants.Clear(); Palettes.Clear(); RandomKey.Clear();
            EnsureDir(MatDir);
            foreach (var set in Sets)
            {
                string dir = ModelDir + "/" + set;
                string abs = Path.Combine(Application.dataPath, "..", dir);
                if (!Directory.Exists(abs)) continue;
                foreach (var file in Directory.GetFiles(abs, "*_malzemeler.json"))
                {
                    var root = OHJson.O(OHJson.Parse(File.ReadAllText(file)));
                    var pal = OHJson.O(OHJson.Get(root, "palettes", "paletler"));
                    if (pal != null)
                        foreach (var kv in pal)
                        {
                            var list = new List<Color>();
                            var arr = OHJson.A(kv.Value);
                            if (arr != null) foreach (var c in arr)
                            {
                                // palet girdisi düz [r,g,b] ya da {"ad":..., "color":[r,g,b]} olabilir
                                var cd = OHJson.O(c);
                                list.Add(cd != null ? Col(OHJson.Get(cd, "color", "renk", "rgb"), Color.white) : Col(c, Color.white));
                            }
                            Palettes[kv.Key] = list;
                        }
                    var mats = OHJson.A(OHJson.Get(root, "materials", "malzemeler"));
                    if (mats == null) continue;
                    foreach (var mo in mats)
                    {
                        var md = OHJson.O(mo);
                        string name = OHJson.S(OHJson.Get(md, "name", "ad"));
                        if (string.IsNullOrEmpty(name)) continue;
                        Color c = Col(OHJson.Get(md, "color", "renk"), Color.gray);
                        float metallic = OHJson.F(OHJson.Get(md, "metallic"), 0);
                        float smooth = OHJson.F(OHJson.Get(md, "smoothness"), 0.3f);
                        var rough = OHJson.Get(md, "roughness");
                        if (rough != null && OHJson.Get(md, "smoothness") == null) smooth = 1f - OHJson.F(rough);
                        Color em = Col(OHJson.Get(md, "emission"), Color.black);
                        bool lens = name.Contains("Sinyal") || name == "AR_Stop" || name.Contains("Lamba") || name.Contains("Far");
                        Mats[name] = MakeMat($"{MatDir}/{name}.mat", c, metallic, smooth, em, lens);
                        string rk = OHJson.S(OHJson.Get(md, "randomize"));
                        if (!string.IsNullOrEmpty(rk)) RandomKey[name] = rk;
                    }
                }
            }
            // varyantlar
            foreach (var kv in RandomKey)
            {
                List<Color> pal;
                if (!Palettes.TryGetValue(kv.Value, out pal) || pal.Count == 0) continue;
                var baseM = Mats[kv.Key];
                var list = new List<Material>();
                for (int i = 0; i < pal.Count; i++)
                {
                    var m = MakeMat($"{MatDir}/{kv.Key}__{i}.mat", pal[i], baseM.GetFloat("_Metallic"), baseM.GetFloat("_Smoothness"), Color.black);
                    list.Add(m);
                }
                Variants[kv.Key] = list;
            }
            AssetDatabase.SaveAssets();
            Log.Add($"malzeme: {Mats.Count}, varyant grubu: {Variants.Count}");
        }

        static string StripSuffix(string n)
        {
            int dot = n.LastIndexOf('.');
            if (dot > 0 && dot < n.Length - 1 && n.Substring(dot + 1).All(char.IsDigit)) n = n.Substring(0, dot);
            return n;
        }

        // ---------------------------------------------------------------- FBX içe aktarım

        static void ConfigureFbx(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) return;
            bool dirty = false;
            if (imp.importAnimation) { imp.importAnimation = false; dirty = true; }
            if (imp.animationType != ModelImporterAnimationType.None) { imp.animationType = ModelImporterAnimationType.None; dirty = true; }
            if (imp.importCameras || imp.importLights) { imp.importCameras = false; imp.importLights = false; dirty = true; }
            if (imp.materialImportMode != ModelImporterMaterialImportMode.ImportStandard) { imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; dirty = true; }
            if (imp.materialLocation != ModelImporterMaterialLocation.InPrefab) { imp.materialLocation = ModelImporterMaterialLocation.InPrefab; dirty = true; }
            if (imp.importNormals != ModelImporterNormals.Import) { imp.importNormals = ModelImporterNormals.Import; dirty = true; }
            if (imp.meshCompression != ModelImporterMeshCompression.Off) { imp.meshCompression = ModelImporterMeshCompression.Off; dirty = true; }
            if (dirty) imp.SaveAndReimport();
            // malzeme eşlemesi
            var embedded = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().ToList();
            var map = imp.GetExternalObjectMap();
            bool changed = false;
            var names = new HashSet<string>();
            foreach (var m in embedded) names.Add(m.name);
            foreach (var kv in map) if (kv.Key.type == typeof(Material)) names.Add(kv.Key.name);
            foreach (var n in names)
            {
                Material target;
                if (n == "No Name" || string.IsNullOrEmpty(n)) continue;
                if (!Mats.TryGetValue(StripSuffix(n), out target)) { Log.Add($"eslesmeyen malzeme {Path.GetFileName(path)}: {n}"); continue; }
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), n);
                Object cur;
                if (map.TryGetValue(id, out cur) && cur == target) continue;
                imp.AddRemap(id, target);
                changed = true;
            }
            if (changed) imp.SaveAndReimport();
        }

        // ---------------------------------------------------------------- manifestler

        static List<object> Items(object root)
        {
            var l = OHJson.A(root);
            if (l != null) return l;
            var d = OHJson.O(root);
            if (d == null) return new List<object>();
            foreach (var k in new[] { "assets", "varliklar", "items", "araclar", "binalar", "donati" })
            {
                var v = OHJson.A(OHJson.Get(d, k)); if (v != null) return v;
            }
            foreach (var kv in d) { var v = OHJson.A(kv.Value); if (v != null && v.Count > 0 && OHJson.O(v[0]) != null) return v; }
            return new List<object>();
        }

        public static void LoadManifests()
        {
            Assets.Clear();
            foreach (var set in Sets)
            {
                string dir = ModelDir + "/" + set;
                string abs = Path.Combine(Application.dataPath, "..", dir);
                if (!Directory.Exists(abs)) continue;
                foreach (var file in Directory.GetFiles(abs, "*_manifest.json"))
                {
                    foreach (var it in Items(OHJson.Parse(File.ReadAllText(file))))
                    {
                        var d = OHJson.O(it);
                        string id = OHJson.S(OHJson.Get(d, "id", "ad"));
                        if (string.IsNullOrEmpty(id)) continue;
                        string fbx = OHJson.S(OHJson.Get(d, "fbx"), id + ".fbx");
                        var info = new OHAssetInfo { id = id, set = set, json = d, fbxPath = dir + "/" + Path.GetFileName(fbx), prefabPath = $"{PrefabDir}/{set}/{id}.prefab" };
                        var ps = OHJson.Fs(OHJson.Get(d, "parsel"));
                        if (ps != null && ps.Length >= 2) info.parsel = new Vector2(ps[0], ps[1]);
                        var cuts = OHJson.A(OHJson.Get(d, "giris_kesimleri"));
                        if (cuts != null) foreach (var c in cuts) { var f = OHJson.Fs(c); if (f != null && f.Length >= 2) info.cuts.Add(new Vector2(Mathf.Min(f[0], f[1]), Mathf.Max(f[0], f[1]))); }
                        var parks = OHJson.A(OHJson.Get(d, "park_yerleri"));
                        if (parks != null) foreach (var p in parks)
                        {
                            var pd = OHJson.O(p);
                            if (pd != null) info.parks.Add(new KeyValuePair<Vector3, float>(OHJson.V3(OHJson.Get(pd, "konum", "pos")), OHJson.F(OHJson.Get(pd, "yon_derece", "yaw"))));
                        }
                        var trees = OHJson.A(OHJson.Get(d, "agac_noktalari"));
                        if (trees != null) foreach (var t in trees)
                        {
                            var td = OHJson.O(t);
                            info.trees.Add(td != null ? OHJson.V3(OHJson.Get(td, "konum", "pos")) : OHJson.V3(t));
                        }
                        Assets[id] = info;
                    }
                }
            }
            Log.Add("manifest varlik: " + Assets.Count);
        }

        // ---------------------------------------------------------------- prefab

        public static void BuildPrefabs(bool force)
        {
            foreach (var set in Sets) EnsureDir(PrefabDir + "/" + set);
            AssetDatabase.Refresh();
            foreach (var info in Assets.Values)
            {
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(info.fbxPath);
                if (!fbx) { Log.Add("FBX yok: " + info.fbxPath); continue; }
                ConfigureFbx(info.fbxPath);
                fbx = AssetDatabase.LoadAssetAtPath<GameObject>(info.fbxPath);
                bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(info.prefabPath) != null;
                bool stale = !exists || File.GetLastWriteTimeUtc(AbsPath(info.fbxPath)) > File.GetLastWriteTimeUtc(AbsPath(info.prefabPath));
                if (force || stale)
                {
                    if (info.set == "Araclar") BuildVehicle(info, fbx);
                    else BuildStatic(info, fbx);
                }
                info.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(info.prefabPath);
                if (info.prefab) info.bounds = RenderBounds(info.prefab);
            }
            AssetDatabase.SaveAssets();
        }

        static string AbsPath(string p) { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", p)); }

        public static Bounds RenderBounds(GameObject go)
        {
            bool first = true; var b = new Bounds();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.name.StartsWith("COL_") || !mf.sharedMesh) continue;
                var mb = mf.sharedMesh.bounds;
                var m = go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        static GameObject Instance(GameObject fbx, string name)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = name;
            // Blender FBX'leri Unity'ye Y ekseni etrafında 180° dönük geliyor (Blender +Y = Unity -Z).
            // Model düğümünü çevirerek harita kuralına getiriyoruz: Blender +Y = Unity +Z, Blender +X = Unity +X.
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, 180f, 0));
            return inst;
        }

        static void ColBoxes(GameObject root, PhysicsMaterial pm)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true).ToArray())
            {
                if (!mf.name.StartsWith("COL_") || !mf.sharedMesh) continue;
                var b = mf.sharedMesh.bounds;
                var go = mf.gameObject;
                var mr = go.GetComponent<MeshRenderer>();
                Object.DestroyImmediate(mr);
                Object.DestroyImmediate(mf);
                var bc = go.AddComponent<BoxCollider>();
                bc.center = b.center; bc.size = Vector3.Max(b.size, Vector3.one * 0.02f);
                if (pm) bc.sharedMaterial = pm;
            }
        }

        static void BuildStatic(OHAssetInfo info, GameObject fbx)
        {
            var root = new GameObject(info.id);
            var model = Instance(fbx, "Model");
            model.transform.SetParent(root.transform, true);
            ColBoxes(root, null);
            bool signal = false;
            var red = new List<Renderer>(); var yel = new List<Renderer>(); var grn = new List<Renderer>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name.StartsWith("Isik_Kirmizi")) { red.Add(r); signal = true; }
                else if (r.name.StartsWith("Isik_Sari")) { yel.Add(r); signal = true; }
                else if (r.name.StartsWith("Isik_Yesil")) { grn.Add(r); signal = true; }
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                bool dynamicPart = t.name.StartsWith("Isik_");
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, dynamicPart ? 0 : (StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic));
            }
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            if (signal)
            {
                var sl = root.AddComponent<OHSignalLight>();
                sl.red = red.ToArray(); sl.yellow = yel.ToArray(); sl.green = grn.ToArray();
            }
            PrefabUtility.SaveAsPrefabAsset(root, info.prefabPath);
            Object.DestroyImmediate(root);
        }

        // ---------------------------------------------------------------- araç

        static PhysicsMaterial PM(string name, float dyn, float stat, PhysicsMaterialCombine comb)
        {
            string path = $"{MatDir}/{name}.asset";
            var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (!pm) { pm = new PhysicsMaterial(name); AssetDatabase.CreateAsset(pm, path); }
            pm.dynamicFriction = dyn; pm.staticFriction = stat; pm.frictionCombine = comb; pm.bounciness = 0.05f;
            EditorUtility.SetDirty(pm);
            return pm;
        }

        static void BuildVehicle(OHAssetInfo info, GameObject fbx)
        {
            var d = info.json;
            string tip = OHJson.S(OHJson.Get(d, "tip"), "binek");
            bool heavy = tip == "agir";
            bool tractor = tip == "traktor";
            float mass = OHJson.F(OHJson.Get(d, "kutle_kg"), heavy ? 6500 : tractor ? 3200 : 1300);
            var root = new GameObject("OH_" + info.id);
            var model = Instance(fbx, "Model");
            model.transform.SetParent(root.transform, true);
            var bodyPm = PM("OH_AracGovde", 0.35f, 0.4f, PhysicsMaterialCombine.Average);
            var tyrePm = PM("OH_ParkLastik", 0.9f, 1.0f, PhysicsMaterialCombine.Maximum);
            ColBoxes(root, bodyPm);
            var b = RenderBounds(root);

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.04f;
            rb.angularDamping = 0.7f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            Vector3 com = OHJson.V3(OHJson.Get(d, "agirlik_merkezi"), new Vector3(0, 0.45f, b.center.z));
            com.y = Mathf.Min(com.y, heavy ? 0.95f : tractor ? 0.85f : 0.48f);
            rb.centerOfMass = com;
            rb.automaticCenterOfMass = false;
            rb.automaticInertiaTensor = true;

            // tekerlekler
            var wheelDefs = OHJson.A(OHJson.Get(d, "tekerler"));
            string[] order = { "Teker_OnSol", "Teker_OnSag", "Teker_ArkaSol", "Teker_ArkaSag" };
            var vis = new List<Transform>(); var rest = new List<Vector3>(); var restRot = new List<Quaternion>(); var rad = new List<float>(); var st = new List<bool>(); var dr = new List<bool>();
            var wcs = new List<WheelCollider>(); var spheres = new List<SphereCollider>();
            string ceker = OHJson.S(OHJson.Get(d, "ceker"), tractor || heavy ? "arka" : "on");
            float f = heavy ? 1.7f : tractor ? 2.1f : 2.0f;
            foreach (var wname in order)
            {
                var t = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == wname || StripSuffix(x.name) == wname);
                Dictionary<string, object> wd = null;
                if (wheelDefs != null) foreach (var w in wheelDefs) { var o = OHJson.O(w); if (OHJson.S(OHJson.Get(o, "ad", "name")) == wname) wd = o; }
                if (!t) { Log.Add($"{info.id}: {wname} yok"); continue; }
                var mfw = t.GetComponent<MeshFilter>();
                float r = OHJson.F(OHJson.Get(wd, "yaricap"), mfw && mfw.sharedMesh ? mfw.sharedMesh.bounds.extents.y : 0.32f);
                float width = OHJson.F(OHJson.Get(wd, "genislik"), 0.22f);
                bool steer = wd != null ? OHJson.B(OHJson.Get(wd, "direksiyon"), wname.Contains("On")) : wname.Contains("On");
                bool drive = wd != null && OHJson.Get(wd, "ceker") != null ? OHJson.B(OHJson.Get(wd, "ceker")) : (ceker == "on" ? wname.Contains("On") : !wname.Contains("On"));
                Vector3 c = root.transform.InverseTransformPoint(t.position);
                // görselin dinlenme konumu kendi ebeveynine göre
                vis.Add(t); rest.Add(c); restRot.Add(Quaternion.Inverse(root.transform.rotation) * t.rotation); rad.Add(r); st.Add(steer); dr.Add(drive);

                float share = mass / 4f;
                float k = share * Mathf.Pow(2f * Mathf.PI * f, 2f);
                float sag = share * 9.81f / k;
                float sd = Mathf.Clamp(2f * sag + 0.01f, 0.1f, 0.2f);
                var wgo = new GameObject("WC_" + wname);
                wgo.transform.SetParent(root.transform, false);
                wgo.transform.localPosition = c + Vector3.up * (sd * 0.5f - sag);
                var wc = wgo.AddComponent<WheelCollider>();
                wc.radius = r;
                wc.mass = heavy ? 60f : tractor ? (r > 0.6f ? 90f : 35f) : 20f;
                wc.suspensionDistance = sd;
                wc.forceAppPointDistance = 0.12f;
                wc.wheelDampingRate = heavy ? 1.2f : 0.6f;
                var spring = new JointSpring { spring = k, damper = 0.42f * 2f * Mathf.Sqrt(k * share), targetPosition = 0.5f };
                wc.suspensionSpring = spring;
                wc.forwardFriction = new WheelFrictionCurve { extremumSlip = 0.4f, extremumValue = 1f, asymptoteSlip = 0.8f, asymptoteValue = 0.55f, stiffness = 1.6f };
                wc.sidewaysFriction = new WheelFrictionCurve { extremumSlip = 0.22f, extremumValue = 1f, asymptoteSlip = 0.5f, asymptoteValue = 0.8f, stiffness = 2.1f };
                wc.enabled = false;
                wcs.Add(wc);
                var sgo = new GameObject("PS_" + wname);
                sgo.transform.SetParent(root.transform, false);
                sgo.transform.localPosition = c;
                var sc = sgo.AddComponent<SphereCollider>();
                sc.radius = r - 0.005f;
                sc.sharedMaterial = tyrePm;
                spheres.Add(sc);
            }

            var v = root.AddComponent<OHVehicle>();
            v.typeId = info.id;
            v.wheelColliders = wcs.ToArray();
            v.wheelVisuals = vis.ToArray();
            v.wheelRest = rest.ToArray();
            v.wheelRestRot = restRot.ToArray();
            v.wheelRadius = rad.ToArray();
            v.steer = st.ToArray();
            v.drive = dr.ToArray();
            v.parkedWheels = spheres.ToArray();
            v.massKg = mass;
            v.heavy = heavy || tractor;
            if (vis.Count >= 4)
            {
                float zf = (wcs[0].transform.localPosition.z + wcs[1].transform.localPosition.z) * 0.5f;
                float zr = (wcs[2].transform.localPosition.z + wcs[3].transform.localPosition.z) * 0.5f;
                v.wheelBase = Mathf.Abs(zf - zr);
                v.axleMidZ = (zf + zr) * 0.5f;
                v.rearAxleZ = zr;
            }
            v.frontExtent = b.max.z - v.axleMidZ;
            v.rearExtent = v.axleMidZ - b.min.z;
            v.width = b.size.x;
            v.height = b.size.y;
            v.maxSpeedKmh = OHJson.F(OHJson.Get(d, "azami_hiz_kmh"), heavy ? 60 : tractor ? 25 : 70);
            v.accelMax = heavy ? 1.1f : tractor ? 0.8f : tip == "ticari" ? 1.7f : 2.1f;
            v.latAccel = heavy || tractor ? 1.7f : 2.3f;
            v.brakeComfort = heavy ? 2.0f : 2.6f;
            var body = model.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(x => StripSuffix(x.name) == "Govde");
            v.bodyRenderer = body;
            if (body)
            {
                var sm = body.sharedMaterials;
                string paintName = OHJson.S(OHJson.Get(d, "boya_malzemesi"), "AR_Boya");
                string stopName = OHJson.S(OHJson.Get(d, "stop_malzemesi"), "AR_Stop");
                for (int i = 0; i < sm.Length; i++)
                {
                    if (!sm[i]) continue;
                    string n = StripSuffix(sm[i].name);
                    if (n == paintName || (v.paintIndex < 0 && RandomKey.ContainsKey(n))) v.paintIndex = i;
                    if (n == stopName) v.brakeIndex = i;
                }
                if (v.paintIndex >= 0)
                {
                    string pn = StripSuffix(sm[v.paintIndex].name);
                    string key;
                    List<Color> pal;
                    if (RandomKey.TryGetValue(pn, out key) && Palettes.TryGetValue(key, out pal)) v.paintPalette = pal.ToArray();
                    else v.paintPalette = new Color[0];
                }
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;
            PrefabUtility.SaveAsPrefabAsset(root, info.prefabPath);
            Object.DestroyImmediate(root);
            Log.Add($"arac prefab: {info.id} kutle {mass} teker {wcs.Count} boya#{v.paintIndex} stop#{v.brakeIndex}");
        }

        public static void PrepareAll(bool force)
        {
            Log.Clear();
            AssetDatabase.Refresh();
            LoadMaterials();
            LoadManifests();
            BuildPrefabs(force);
        }

        // Raporlar: bu kopyada Kaynak\Raporlar, asıl projede <proje>\Logs\OfisHaritasi
        public static string ReportDir()
        {
            string k = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Kaynak/Raporlar"));
            string d = Directory.Exists(Path.GetDirectoryName(k)) ? k : Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/OfisHaritasi"));
            Directory.CreateDirectory(d);
            return d;
        }

        public static string ShotDir()
        {
            string k = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Kaynak/Onizlemeler/Harita"));
            string d = Directory.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "../../Kaynak"))) ? k : Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/OfisHaritasi/Goruntuler"));
            Directory.CreateDirectory(d);
            return d;
        }

        public static OHAssetInfo Get(string id)
        {
            OHAssetInfo a;
            return Assets.TryGetValue(id, out a) && a.Has ? a : null;
        }

        public static Material Variant(string matName, int i)
        {
            List<Material> l;
            if (Variants.TryGetValue(matName, out l) && l.Count > 0) return l[((i % l.Count) + l.Count) % l.Count];
            return null;
        }
    }
}
