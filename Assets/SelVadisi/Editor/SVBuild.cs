using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SelVadisi.EditorBuild
{
    /// Builds the large flooded valley map ("Sel Vadisi - Büyük Harita") procedurally:
    /// terrain, flowing flood water, village, nature, props, floating debris, people, rain and cameras.
    public static partial class SVBuild
    {
        public const string Root = "Assets/SelVadisi";
        public const string Gen = Root + "/Generated";
        public const string ScenePath = Root + "/SelVadisi_BuyukHarita.unity";

        static Material matStatic, matDynamic, matPeople, matWater, matFall, matRain, matSky;
        static Texture2D texNoise, texNormal, texFlow;
        static int meshCounter;

        // Oyunun içinde (InHostGame) düz harita (FlyCamera; oyuncu, görev ve masa yok) görev sahnesinin üstüne yazılmamalı.
        // Rescue Team bu kancayı bağlar (SelVadisiAutoSetup): Tools menüsü de haritayı görev kurulumuyla birlikte üretir.
        public static System.Action HostRebuild;

        [MenuItem("Tools/Sel Vadisi/Büyük Haritayı Oluştur", priority = 0)]
        public static void BuildMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog("Sel Vadisi", "Önce Play modundan çıkın.", "Tamam"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (InHostGame && HostRebuild != null) { HostRebuild(); return; }
            Build();
            EditorUtility.DisplayDialog("Sel Vadisi", "Büyük harita oluşturuldu:\n" + ScenePath + "\n\nPlay'e basıp WASD + sağ tık ile gezebilir, 1-6 tuşlarıyla kamera noktalarına geçebilir, C ile sinematik tur başlatabilirsin.", "Tamam");
        }

        [MenuItem("Tools/Sel Vadisi/Sahneyi Aç", priority = 1)]
        public static void OpenScene()
        {
            if (!File.Exists(ScenePath)) { BuildMenu(); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        public static void Build()
        {
            // Play modunda üretim Generated klasörünü silip sonra hata verirdi: hiçbir şeye dokunmadan dur
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Sel Vadisi: harita Play modunda üretilemez; önce Play modundan çıkın.");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            meshCounter = 0;
            banks.Clear();
            try
            {
                EditorUtility.DisplayProgressBar("Sel Vadisi", "Hazırlanıyor", 0f);
                if (AssetDatabase.IsValidFolder(Gen)) AssetDatabase.DeleteAsset(Gen);
                AssetDatabase.CreateFolder(Root, "Generated");

                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                EditorUtility.DisplayProgressBar("Sel Vadisi", "Dokular", 0.03f);
                MakeTextures();

                EditorUtility.DisplayProgressBar("Sel Vadisi", "Yerleşim planı", 0.06f);
                Plan();

                var root = new GameObject("SEL VADISI - Buyuk Harita (1040 x 1520 m)");
                var field = new GameObject("Akinti Alani (Flood Field)").AddComponent<FloodField>();
                field.transform.SetParent(root.transform);
                field.obstacles = new List<FlowObstacle>(obstacles);
                Valley.SetObstacles(obstacles);

                EditorUtility.DisplayProgressBar("Sel Vadisi", "Akıntı haritası", 0.1f);
                texFlow = BakeFlow();
                MakeMaterials();

                AssetDatabase.StartAssetEditing();
                try
                {
                    EditorUtility.DisplayProgressBar("Sel Vadisi", "Arazi", 0.18f);
                    BuildTerrain(Group(root, "Arazi"));
                    EditorUtility.DisplayProgressBar("Sel Vadisi", "Sel suyu", 0.3f);
                    BuildWater(Group(root, "Sel Suyu ve Akinti"));

                    var structures = new ChunkSet("Yapilar");
                    var details = new ChunkSet("Detaylar");
                    var nature = new ChunkSet("Doga");
                    EditorUtility.DisplayProgressBar("Sel Vadisi", "Evler", 0.4f);
                    BuildHouses(structures, details);
                    EditorUtility.DisplayProgressBar("Sel Vadisi", "Köprü, rampa, otopark, cami, okul", 0.5f);
                    BuildLandmarks(structures, details);
                    BuildRoads(details);
                    EditorUtility.DisplayProgressBar("Sel Vadisi", "Elektrik hatları ve detaylar", 0.58f);
                    BuildProps(details);
                    EditorUtility.DisplayProgressBar("Sel Vadisi", "Orman, kayalar, çimen", 0.66f);
                    BuildNature(nature);

                    EmitChunks(structures, Group(root, "Yapilar (Evler, Kopru, Cami, Okul)"), matStatic, true);
                    EmitChunks(details, Group(root, "Detaylar (Direkler, Teller, Kurtarma Alani)"), matStatic, false);
                    EmitChunks(nature, Group(root, "Doga (Orman, Kaya, Cimen)"), matStatic, false);

                    EditorUtility.DisplayProgressBar("Sel Vadisi", "Yüzen eşyalar", 0.8f);
                    BuildDebris(Group(root, "Yuzen Esyalar (akintiyla suruklenir)"));
                    EditorUtility.DisplayProgressBar("Sel Vadisi", "İnsanlar", 0.86f);
                    BuildPeople(Group(root, "Insanlar (animasyonlu)"));
                    BuildCloth(Group(root, "Bayraklar ve Carsaflar"));
                    BuildAnimatedProps(Group(root, "Animasyonlu Detaylar"));
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                EditorUtility.DisplayProgressBar("Sel Vadisi", "Işık, yağmur ve kameralar", 0.93f);
                SetupAtmosphere(root);

                foreach (var bk in banks.Values) EditorUtility.SetDirty(bk);
                if (InHostGame && !File.Exists(ScenePath) && !File.Exists(ScenePath + ".meta"))
                    File.WriteAllText(ScenePath + ".meta", "fileFormatVersion: 2\nguid: " + HostSceneGuid + "\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
                EditorSceneManager.SaveScene(scene, ScenePath);
                // standalone map project: the map is the first scene; inside a host game it is appended (the office stays first)
                var list = new List<EditorBuildSettingsScene>();
                if (!InHostGame) list.Add(new EditorBuildSettingsScene(ScenePath, true));
                foreach (var s in EditorBuildSettings.scenes) if (s.path != ScenePath) list.Add(s);
                if (InHostGame) list.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = list.ToArray();
                AssetDatabase.SaveAssets();
                Debug.Log($"[Sel Vadisi] Built in {sw.Elapsed.TotalSeconds:0.0}s, meshes: {meshCounter}, houses: {houses.Count}, people: {personSpots.Count}, obstacles: {obstacles.Count}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ------------------------------------------------------------------ helpers

        /// True when the map lives inside the Rescue Team game project (its office scene exists).
        public static bool InHostGame => System.IO.File.Exists("Assets/_Project/Scenes/OfficeGarage.unity");

        /// Inside the host game the generated scene always gets this GUID, so the Build Settings entry committed to the
        /// game's repository matches on every machine (the scene itself is generated locally and is not committed).
        public const string HostSceneGuid = "4053244eb14461e41ab839b5fee56537";

        static Transform Group(GameObject root, string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(root.transform, false);
            return g.transform;
        }

        static readonly Dictionary<string, SVMeshBank> banks = new Dictionary<string, SVMeshBank>();

        static string BankFor(string name)
        {
            if (name.StartsWith("Arazi")) return "Arazi";
            if (name.StartsWith("Sel_") || name.StartsWith("Dere")) return "Su";
            if (name.StartsWith("Yapilar")) return "Yapilar";
            if (name.StartsWith("Detaylar")) return "Detaylar";
            if (name.StartsWith("Doga")) return "Doga";
            if (name.StartsWith("Insan") || name.StartsWith("Semsiye")) return "Insanlar";
            return "Esyalar";
        }

        /// Stores the mesh as a sub-asset of a binary mesh bank (much smaller than text .asset files).
        static Mesh SaveMesh(Mesh mesh, string name)
        {
            string safe = name.Replace(' ', '_').Replace('/', '_');
            mesh.name = $"{safe}_{meshCounter++:000}";
            string bank = BankFor(safe);
            if (!banks.TryGetValue(bank, out SVMeshBank b))
            {
                b = ScriptableObject.CreateInstance<SVMeshBank>();
                b.name = "SV_Meshes_" + bank;
                AssetDatabase.CreateAsset(b, $"{Gen}/SV_Meshes_{bank}.asset");
                banks[bank] = b;
            }
            AssetDatabase.AddObjectToAsset(mesh, b);
            b.meshCount++;
            return mesh;
        }

        static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material mat, bool castShadows = true, bool collider = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        static void EmitChunks(ChunkSet set, Transform parent, Material mat, bool collider)
        {
            foreach (var kv in set.All)
            {
                if (kv.Value.Count == 0) continue;
                int cx = (int)(kv.Key >> 32), cz = (int)(kv.Key & 0xffffffff);
                string name = $"{set.Name}_{cx}_{cz}";
                var mesh = SaveMesh(kv.Value.ToMeshPacked(name), name);
                MeshObject(name, parent, mesh, mat, true, collider);
            }
        }

        static float Rand(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
        static T Pick<T>(System.Random r, T[] arr) => arr[r.Next(arr.Length)];
        static Color32 H(string hex) => MB.Hex(hex);

        // ------------------------------------------------------------------ textures & materials

        static void MakeTextures()
        {
            texNoise = SVNoise.MakeNoiseTexture(256);
            AssetDatabase.CreateAsset(texNoise, Gen + "/SV_Noise.asset");
            texNormal = SVNoise.MakeNormalTexture(256);
            AssetDatabase.CreateAsset(texNormal, Gen + "/SV_WaveNormals.asset");
        }

        static Texture2D BakeFlow()
        {
            const int W = 520, Hh = 760;
            var tex = new Texture2D(W, Hh, TextureFormat.RGBAHalf, false, true)
            {
                name = "SV_FlowField", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            var cols = new Color[W * Hh];
            float sx = (Valley.MaxX - Valley.MinX) / W, sz = (Valley.MaxZ - Valley.MinZ) / Hh;
            for (int j = 0; j < Hh; j++)
            {
                float z = Valley.MinZ + (j + 0.5f) * sz;
                float cx = Valley.CenterX(z), hw = Valley.HalfWidth(z);
                for (int i = 0; i < W; i++)
                {
                    float x = Valley.MinX + (i + 0.5f) * sx;
                    if (Mathf.Abs(x - cx) > hw + 40f) { cols[j * W + i] = new Color(0, 0, 0, -5f); continue; }
                    float depth = Valley.Depth(x, z);
                    if (depth < -1.5f) { cols[j * W + i] = new Color(0, 0, 0, depth); continue; }
                    Vector2 v = Valley.Flow(x, z);
                    if (depth < 0.6f) v *= Mathf.Clamp01((depth + 1.5f) / 2.1f);
                    float foam = Valley.Foam(x, z, depth, v);
                    cols[j * W + i] = new Color(v.x, v.y, foam, depth);
                }
            }
            tex.SetPixels(cols);
            tex.Apply(false, false);
            AssetDatabase.CreateAsset(tex, Gen + "/SV_FlowField.asset");
            return tex;
        }

        static readonly Color AmbSky = new Color(0.66f, 0.71f, 0.77f);
        static readonly Color AmbGround = new Color(0.47f, 0.44f, 0.38f);

        static Material NewMat(string shader, string name)
        {
            var sh = Shader.Find(shader);
            if (sh == null) throw new System.Exception("Shader not found: " + shader);
            var m = new Material(sh) { name = name };
            AssetDatabase.CreateAsset(m, $"{Gen}/{name}.mat");
            return m;
        }

        static void MakeMaterials()
        {
            matStatic = NewMat("SelVadisi/VertexLit", "SV_Static");
            matStatic.SetFloat("_WaterA", Valley.WaterBase);
            matStatic.SetFloat("_WaterB", Valley.WaterSlope);
            matStatic.SetFloat("_Wind", 0.35f);
            matStatic.SetFloat("_Wet", 1f);
            matStatic.SetColor("_AmbSky", AmbSky);
            matStatic.SetColor("_AmbGround", AmbGround);

            matDynamic = NewMat("SelVadisi/VertexLit", "SV_Dynamic");
            matDynamic.SetFloat("_WaterA", Valley.WaterBase);
            matDynamic.SetFloat("_WaterB", Valley.WaterSlope);
            matDynamic.SetFloat("_Wind", 0f);
            matDynamic.SetFloat("_Wet", 0f);
            matDynamic.SetColor("_AmbSky", AmbSky);
            matDynamic.SetColor("_AmbGround", AmbGround);

            matPeople = NewMat("SelVadisi/VertexLit", "SV_People");
            matPeople.SetFloat("_WaterA", Valley.WaterBase);
            matPeople.SetFloat("_WaterB", Valley.WaterSlope);
            matPeople.SetFloat("_Wind", 0.12f);
            matPeople.SetFloat("_Wet", 0f);
            matPeople.SetFloat("_SmoothNormals", 1f);
            matPeople.SetFloat("_Wrap", 0.42f);
            matPeople.SetFloat("_Rim", 0.3f);
            matPeople.SetColor("_AmbSky", AmbSky);
            matPeople.SetColor("_AmbGround", AmbGround);

            matWater = NewMat("SelVadisi/FloodWater", "SV_FloodWater");
            matWater.SetTexture("_FlowTex", texFlow);
            matWater.SetTexture("_NoiseTex", texNoise);
            matWater.SetTexture("_NormalTex", texNormal);
            matWater.SetVector("_FlowRect", new Vector4(Valley.MinX, Valley.MinZ, Valley.MaxX - Valley.MinX, Valley.MaxZ - Valley.MinZ));
            matWater.SetFloat("_WaterA", Valley.WaterBase);
            matWater.SetFloat("_WaterB", Valley.WaterSlope);
            matWater.SetFloat("_WaveAmp", Valley.WaveAmp);
            matWater.SetColor("_AmbSky", AmbSky);
            matWater.SetColor("_AmbGround", AmbGround);

            matFall = NewMat("SelVadisi/Waterfall", "SV_Waterfall");
            matFall.SetTexture("_NoiseTex", texNoise);
            matFall.SetColor("_AmbSky", AmbSky);
            matFall.SetColor("_AmbGround", AmbGround);

            matRain = NewMat("SelVadisi/Rain", "SV_Rain");
            matSky = NewMat("SelVadisi/Sky", "SV_Sky");
            matSky.SetTexture("_NoiseTex", texNoise);
        }

        // ------------------------------------------------------------------ light, fog, rain, cameras

        static readonly Color FogColor = new Color(0.70f, 0.74f, 0.76f);

        static void SetupAtmosphere(GameObject root)
        {
            RenderSettings.skybox = matSky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.67f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.52f, 0.55f, 0.53f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.35f, 0.31f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 260f;
            RenderSettings.fogEndDistance = 1900f;
            RenderSettings.fogColor = FogColor;

            var sunGo = new GameObject("Gunes (bulutlu ogleden sonra)");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.87f);
            sun.intensity = 1.45f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.82f;
            sun.shadowBias = 0.04f;
            sun.shadowNormalBias = 0.35f;
            sunGo.transform.rotation = Quaternion.Euler(44f, 38f, 0f);
            RenderSettings.sun = sun;

            // Inside a host game (Rescue Team) the URP asset is shared with the office: the mission scene widens shadows at runtime instead.
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (rp != null && !InHostGame) { rp.shadowDistance = 340f; EditorUtility.SetDirty(rp); }

            // post processing
            var volGo = new GameObject("Atmosfer (Post Process)");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, Gen + "/SV_Atmosphere.asset");
            var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.Neutral);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.1f); ca.contrast.Override(12f); ca.saturation.Override(5f);
            var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(0.35f); bloom.threshold.Override(1.05f); bloom.scatter.Override(0.6f);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.22f); vig.smoothness.Override(0.45f);
            var wb = profile.Add<WhiteBalance>(true); wb.temperature.Override(-4f);
            vol.sharedProfile = profile;
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);

            // lightning
            var lightning = new GameObject("Simsek").AddComponent<LightningFlash>();
            lightning.sun = sun;

            // camera
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 2600f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.allowHDR = true;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            camGo.AddComponent<AudioListener>();
            var fly = camGo.AddComponent<FlyCamera>();
            fly.shots = Shots();
            fly.orbitCenter = new Vector3(Valley.CenterX(0f) - 10f, 6f, 0f);
            camGo.transform.SetPositionAndRotation(fly.shots[0].position, Quaternion.Euler(fly.shots[0].euler));

            BuildRain(camGo.transform.position);
            DynamicGI.UpdateEnvironment();
        }

        static FlyCamera.Shot Shot(string name, Vector3 pos, Vector3 target)
        {
            var e = Quaternion.LookRotation(target - pos).eulerAngles;
            return new FlyCamera.Shot { name = name, position = pos, euler = e };
        }

        public static FlyCamera.Shot[] Shots()
        {
            float cx0 = Valley.CenterX(0f);
            Vector3 padC = new Vector3((Valley.PadX0 + Valley.PadX1) * 0.5f, Valley.PadY, (Valley.PadZ0 + Valley.PadZ1) * 0.5f);
            Vector3 ramp = (Valley.RampTop + Valley.RampBottom) * 0.5f;
            Vector3 fallEnd = Valley.Stream[Valley.Stream.Length - 1];
            Vector3 fallMid = Valley.Stream[Valley.Stream.Length / 2];
            return new[]
            {
                Shot("Köy - çatıdakiler ve rampa", new Vector3(cx0 + 10f, 62f, -78f), new Vector3(cx0 - 52f, 4f, 52f)),
                Shot("Kurtarma alanı", Valley.PadEdgeX > 0f ? padC : new Vector3(Valley.PadEdgeX - 33f, Valley.TerrainHeight(Valley.PadEdgeX - 33f, Valley.PadZ0 - 8f) + 7.5f, Valley.PadZ0 - 8f), new Vector3(Valley.CenterX(-150f) + 25f, 4f, -150f)),
                Shot("Bütün vadi", new Vector3(Valley.CenterX(-640f) + 40f, 390f, -720f), new Vector3(cx0, 0f, 60f)),
                Shot("Köprü ve kanyon", new Vector3(Valley.CenterX(330f) + 40f, 26f, 318f), new Vector3(Valley.CenterX(410f), 5f, 420f)),
                Shot("Otopark ve taş rampa", Valley.RampBottom + new Vector3(30f, 19f, -34f), ramp + new Vector3(-6f, 2f, 6f)),
                Shot("Şelale", new Vector3(fallEnd.x - 46f, 22f, fallEnd.z - 52f), fallMid + new Vector3(0f, -4f, 0f)),
                Shot("Taşkın ovası (seralar)", new Vector3(Valley.CenterX(-380f) + 60f, 48f, -330f), new Vector3(Valley.CenterX(-500f) - 60f, 3f, -520f)),
            };
        }

        static void BuildRain(Vector3 camPos)
        {
            var go = new GameObject("Yagmur");
            go.transform.position = camPos + Vector3.up * 24f;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 5f;
            main.startLifetime = 1.6f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
            main.startColor = new Color(1f, 1f, 1f, 1f);
            main.maxParticles = 14000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var em = ps.emission;
            em.rateOverTime = 7000f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(110f, 1f, 110f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(2.2f, 3.2f);
            vel.y = new ParticleSystem.MinMaxCurve(-26f, -21f);
            vel.z = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.045f;
            r.lengthScale = 1.2f;
            r.sharedMaterial = matRain;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.AddComponent<RainFollow>();
            ps.Play();
        }

        // ------------------------------------------------------------------ batch entry points (verification)

        public static void BatchBuildAndCapture()
        {
            string outDir = System.Environment.GetEnvironmentVariable("SV_SHOTS");
            if (string.IsNullOrEmpty(outDir)) outDir = Path.GetFullPath("WorldSource/Shots");
            Directory.CreateDirectory(outDir);
            try
            {
                Build();
                CaptureAll(outDir);
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Sel Vadisi] FAILED: " + e);
                EditorApplication.Exit(1);
            }
        }

        public static void BatchCaptureOnly()
        {
            string outDir = System.Environment.GetEnvironmentVariable("SV_SHOTS");
            if (string.IsNullOrEmpty(outDir)) outDir = Path.GetFullPath("WorldSource/Shots");
            Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene(ScenePath);
            CaptureAll(outDir);
            EditorApplication.Exit(0);
        }

        public static void CaptureAll(string outDir)
        {
            ShaderUtil.allowAsyncCompilation = false;
            var cam = Camera.main;
            cam.fieldOfView = 50f;
            var shots = Shots();
            for (int i = 0; i < shots.Length; i++)
            {
                cam.transform.SetPositionAndRotation(shots[i].position, Quaternion.Euler(shots[i].euler));
                Capture(cam, Path.Combine(outDir, $"shot_{i + 1}.png"), 1600, 900);
            }
        }

        static void Capture(Camera cam, string file, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
    }
}
