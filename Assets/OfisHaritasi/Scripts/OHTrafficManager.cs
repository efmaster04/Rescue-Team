using System;
using System.Collections.Generic;
using UnityEngine;

// Ofis haritası trafiği: araçları doğurur, her fizik adımında sürer, uzak/yakın kipini seçer,
// tünele giren aracı başka bir tünelden çıkarır, devrilen ya da sıkışan aracı görünmezken toplar.
namespace OfisHaritasi
{
    public class OHTrafficManager : MonoBehaviour
    {
        public OHTrafficNetwork network;
        public OHVehicle[] prefabs = new OHVehicle[0];
        public float[] weights = new float[0];
        public int[] maxPerType = new int[0];
        public int movingCount = 42;
        public int seed = 20261106;
        [Tooltip("Oyuncu bu mesafeye girince araç fizik kipine geçer (m).")]
        public float physicsEnter = 130f;
        [Tooltip("Oyuncu bu mesafeden uzaklaşınca araç ray kipine döner (m).")]
        public float physicsExit = 175f;
        public bool spawnOnStart = true;
        public int audibleCars = 6;
        public Transform vehicleRoot;

        [NonSerialized] public bool hasFocusOverride;
        [NonSerialized] public Vector3 focusOverride;
        [NonSerialized] public readonly List<OHVehicle> vehicles = new List<OHVehicle>();
        [NonSerialized] public readonly List<string> log = new List<string>();
        [NonSerialized] public int teleportCount, failCount, honkCount;

        System.Random rng;
        Collider[] playerOnly;
        int nextId = 1;
        readonly List<OHVehicle> teleportQueue = new List<OHVehicle>();
        readonly Dictionary<OHVehicle, AudioSource> engines = new Dictionary<OHVehicle, AudioSource>();
        float audioTimer;
        AudioClip engineClip, hornClip;
        Transform cachedFocus;

        void Start()
        {
            if (!network) network = GetComponent<OHTrafficNetwork>();
            network.Init();
            if (spawnOnStart && vehicles.Count == 0) SpawnAll();
        }

        void FixedUpdate() { Step(Time.fixedDeltaTime); }

        public float Rand() { if (rng == null) rng = new System.Random(seed); return (float)rng.NextDouble(); }

        // ---------------------------------------------------------------- doğurma

        public void SpawnAll()
        {
            network.Init();
            if (rng == null) rng = new System.Random(seed);
            if (!vehicleRoot) { var go = new GameObject("Trafik_Araclari"); go.transform.SetParent(transform, false); vehicleRoot = go.transform; }
            var counts = new int[prefabs.Length];
            var candidates = new List<int>();
            float total = 0;
            foreach (var l in network.lanes)
                if (!l.IsConnector && !l.sink && !l.source && l.length > 30f) { candidates.Add(l.id); total += l.length * l.weight; }
            Vector3 focus = Focus();
            int made = 0, guard = 0;
            while (made < movingCount && guard++ < movingCount * 40)
            {
                int type = PickType(counts);
                if (type < 0) break;
                // şerit seç
                float r = Rand() * total, acc = 0;
                int lane = candidates[0];
                foreach (int c in candidates) { acc += network.lanes[c].length * network.lanes[c].weight; if (acc >= r) { lane = c; break; } }
                var L = network.lanes[lane];
                float s = 8f + Rand() * Mathf.Max(1f, L.length - 22f);
                if ((L.PointAt(s) - focus).sqrMagnitude < 35f * 35f) continue;
                if (!SpotFree(lane, s, 15f)) continue;
                var v = Create(type, lane, s);
                counts[type]++;
                made++;
                v.RegisterOccupancy();
            }
            log.Add("dogan arac: " + made);
        }

        int PickType(int[] counts)
        {
            float total = 0;
            for (int i = 0; i < prefabs.Length; i++) if (prefabs[i] && (maxPerType.Length <= i || maxPerType[i] <= 0 || counts[i] < maxPerType[i])) total += weights.Length > i ? weights[i] : 1f;
            if (total <= 0) return -1;
            float r = Rand() * total, acc = 0;
            for (int i = 0; i < prefabs.Length; i++)
            {
                if (!prefabs[i] || !(maxPerType.Length <= i || maxPerType[i] <= 0 || counts[i] < maxPerType[i])) continue;
                acc += weights.Length > i ? weights[i] : 1f;
                if (acc >= r) return i;
            }
            return prefabs.Length - 1;
        }

        bool SpotFree(int lane, float s, float clear)
        {
            foreach (var o in vehicles)
            {
                if (o.route.Count == 0) continue;
                if (o.route[0] == lane && Mathf.Abs(o.s - s) < clear) return false;
                if (o.prevLane == lane && Mathf.Abs(network.lanes[lane].length + o.s - s) < clear) return false;
            }
            return true;
        }

        OHVehicle Create(int type, int lane, float s)
        {
            var v = Instantiate(prefabs[type], vehicleRoot);
            v.name = prefabs[type].name + "_" + nextId;
            v.manager = this;
            v.id = nextId++;
            if (v.paintPalette != null && v.paintPalette.Length > 0) v.SetPaint(v.paintPalette[(int)(Rand() * v.paintPalette.Length) % v.paintPalette.Length]);
            v.SetMode(OHDriveMode.Rail);
            v.PlaceOnRoute(lane, s, network.lanes[lane].speedLimit * (0.45f + 0.3f * Rand()));
            vehicles.Add(v);
            if (playerOnly == null)
            {
                var root = GetComponentInParent<OHMapRoot>();
                playerOnly = root ? root.playerOnly : new Collider[0];
            }
            if (playerOnly.Length > 0)
                foreach (var c in v.GetComponentsInChildren<Collider>(true))
                    foreach (var w in playerOnly) if (w) Physics.IgnoreCollision(c, w, true);
            var src = v.gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false; src.loop = true; src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 3f; src.maxDistance = 55f; src.dopplerLevel = 0.4f; src.volume = 0; src.enabled = false;
            engines[v] = src;
            return v;
        }

        public int PickNext(OHLane lane)
        {
            if (lane.next.Length == 1) return lane.next[0];
            float total = 0;
            foreach (int n in lane.next) total += network.lanes[n].weight;
            float r = Rand() * total, acc = 0;
            foreach (int n in lane.next) { acc += network.lanes[n].weight; if (acc >= r) return n; }
            return lane.next[lane.next.Length - 1];
        }

        // ---------------------------------------------------------------- adım

        public Vector3 Focus()
        {
            if (hasFocusOverride) return focusOverride;
            var cam = Camera.main;
            if (cam && cam.isActiveAndEnabled) return cam.transform.position;
            if (!cachedFocus)
            {
                var cc = FindAnyObjectByType<CharacterController>();
                if (cc) cachedFocus = cc.transform;
            }
            return cachedFocus ? cachedFocus.position : Vector3.zero;
        }

        public float Clock { get; private set; }
        public float LastDt { get; private set; } = 0.02f;

        public void Step(float dt)
        {
            if (!network) return;
            Clock += dt; LastDt = dt;
            network.Init();
            network.TickSignals(dt);
            network.ClearOccupancy();
            for (int i = 0; i < vehicles.Count; i++) vehicles[i].RegisterOccupancy();
            Vector3 focus = Focus();
            float enter2 = physicsEnter * physicsEnter, exit2 = physicsExit * physicsExit;
            for (int i = 0; i < vehicles.Count; i++)
            {
                var v = vehicles[i];
                float d2 = (v.transform.position - focus).sqrMagnitude;
                if (v.mode == OHDriveMode.Rail && d2 < enter2 && v.odometer > v.unstickOdo) v.SetMode(OHDriveMode.Physics);
                else if (v.mode == OHDriveMode.Physics && d2 > exit2 && v.CanSwitchToRail()) { v.SetMode(OHDriveMode.Rail); v.PoseOnRail(0); }
                v.Tick(dt, focus);
                if (v.mode == OHDriveMode.Wreck)
                {
                    bool unseen = !AnyVisible(v) && d2 > 45f * 45f;
                    if ((v.wreckTimer > 20f && unseen) || v.wreckTimer > 90f) RequestTeleport(v);
                }
            }
            if (teleportQueue.Count > 0)
            {
                for (int i = teleportQueue.Count - 1; i >= 0; i--) if (Teleport(teleportQueue[i])) teleportQueue.RemoveAt(i);
            }
            audioTimer -= dt;
            if (audioTimer <= 0) { audioTimer = 0.25f; UpdateAudio(focus); }
        }

        static bool AnyVisible(OHVehicle v)
        {
            return v.bodyRenderer && v.bodyRenderer.isVisible;
        }

        public void RequestTeleport(OHVehicle v)
        {
            if (!teleportQueue.Contains(v)) teleportQueue.Add(v);
        }

        bool Teleport(OHVehicle v)
        {
            if (network.sources.Length == 0) return false;
            int start = (int)(Rand() * network.sources.Length);
            for (int k = 0; k < network.sources.Length; k++)
            {
                int src = network.sources[(start + k) % network.sources.Length];
                bool clear = true;
                foreach (var o in network.occupancy[src])
                {
                    float rear; if (o != v && o.RearOn(src, out rear) && rear < 26f) { clear = false; break; }
                }
                if (!clear) continue;
                network.UnreserveAll(v);
                v.SetMode(OHDriveMode.Rail);
                if (v.paintPalette != null && v.paintPalette.Length > 1) v.SetPaint(v.paintPalette[(int)(Rand() * v.paintPalette.Length) % v.paintPalette.Length]);
                v.PlaceOnRoute(src, 6f, 7f);
                v.teleports++;
                teleportCount++;
                return true;
            }
            return false;
        }

        public void OnVehicleFailed(OHVehicle v, string reason)
        {
            failCount++;
            var L = network.lanes[v.route[0]];
            log.Add($"{Clock:F1}s {v.name}: {reason} @ {v.transform.position:F1} serit {L.id}{(L.IsConnector ? " (kavsak " + L.junction + " donus " + L.turn + ")" : "")} s {v.s:F1}/{L.length:F1} yanal {v.lateral:F2} hiz {v.speed:F1} ivme {v.cmdAccel:F1} bekleme '{v.waitReason}' engel {(v.blockedBy ? v.blockedBy.name : "-")} up {v.transform.up.y:F2}");
            v.BecomeWreck();
        }

        public void Honk(OHVehicle v)
        {
            honkCount++;
            if (hornClip == null) hornClip = OHSynth.Horn(v.heavy);
            AudioSource src;
            if (engines.TryGetValue(v, out src) && src)
            {
                if (!src.enabled) src.enabled = true;
                src.PlayOneShot(hornClip, 0.8f);
            }
        }

        void UpdateAudio(Vector3 focus)
        {
            if (engineClip == null) engineClip = OHSynth.Engine();
            // en yakın N araç
            var near = new List<KeyValuePair<float, OHVehicle>>();
            foreach (var v in vehicles)
            {
                if (v.mode == OHDriveMode.Parked) continue;
                float d = (v.transform.position - focus).sqrMagnitude;
                if (d < 70f * 70f) near.Add(new KeyValuePair<float, OHVehicle>(d, v));
            }
            near.Sort((a, b) => a.Key.CompareTo(b.Key));
            var keep = new HashSet<OHVehicle>();
            for (int i = 0; i < near.Count && i < audibleCars; i++) keep.Add(near[i].Value);
            foreach (var kv in engines)
            {
                var v = kv.Key; var src = kv.Value;
                if (!v || !src) continue;
                bool on = keep.Contains(v) && v.mode != OHDriveMode.Wreck;
                if (on)
                {
                    if (!src.enabled) src.enabled = true;
                    if (src.clip != engineClip) src.clip = engineClip;
                    if (!src.isPlaying) { src.time = Rand() * 0.9f; src.Play(); }
                    float sp = Mathf.Max(0, v.speed);
                    src.pitch = v.heavy ? 0.55f + sp / 24f : 0.72f + sp / 17f;
                    src.volume = (v.heavy ? 0.42f : 0.30f) + Mathf.Clamp01(v.cmdAccel) * 0.12f;
                }
                else if (src.enabled && src.isPlaying) src.Stop();
            }
        }

        // doğrulama için
        public int CountMode(OHDriveMode m) { int n = 0; foreach (var v in vehicles) if (v.mode == m) n++; return n; }
    }
}
