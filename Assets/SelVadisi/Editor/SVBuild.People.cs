using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Beh = SelVadisi.Beh;
using Role = SelVadisi.Role;
using PropKind = SelVadisi.PropKind;

namespace SelVadisi.EditorBuild
{
    /// Assembles the villagers and rescue workers: cast -> body + head mesh -> skeleton (A-pose bind) -> props -> animator data.
    public static partial class SVBuild
    {
        static MB.Ring Ring(Vector3 c, Vector3 u, Vector3 v, float rx, float rz, Color32 col, BoneWeight w, float n = 2f)
            => new MB.Ring { c = c, u = u, v = v, rx = rx, rz = rz, col = col, w = w, n = n };

        /// Head size relative to the body table; 1 = the anatomy preset's head height.
        const float HeadSizeK = 0.97f;

        static void BuildPeople(Transform parent)
        {
            var anim = parent.gameObject.AddComponent<PeopleAnimator>();
            var r = new System.Random(99);
            var cast = CastPeople(r);
            var propMeshes = new Dictionary<string, Mesh>();
            Mesh Shared(string key, System.Func<Mesh> make)
            {
                if (!propMeshes.TryGetValue(key, out var m)) { m = SaveMesh(make(), key); propMeshes[key] = m; }
                return m;
            }

            // group-local partner indices -> global person indices
            var firstOfGroup = new Dictionary<int, int>();
            var groupMembers = new Dictionary<int, List<int>>();
            for (int i = 0; i < cast.Count; i++)
            {
                int g = cast[i].spot.group;
                if (g < 0) continue;
                if (!groupMembers.TryGetValue(g, out var l)) groupMembers[g] = l = new List<int>();
                l.Add(i);
            }

            for (int idx = 0; idx < cast.Count; idx++)
            {
                var m = cast[idx];
                var pr = new System.Random(1000 + idx * 7919);
                var p = m.body;
                bool skirt = false, trousers = false;
                foreach (var g in m.outfit.garments)
                {
                    if (g.kind == GarmentKind.Skirt || g.kind == GarmentKind.CoatTail || g.kind == GarmentKind.Salvar) skirt = true;
                    if (g.kind == GarmentKind.Trousers || g.kind == GarmentKind.Jeans || g.kind == GarmentKind.Salvar) trousers = true;
                }

                // ---- mesh: head first (it tells the body where the neck must end), then body and worn items
                var mb = new MB { W = new List<BoneWeight>() };
                float headScale = HeadSizeK * p.headH / m.head.RefHeight;
                var hi = SVHead.Build(mb, m.head, p.H, 0f, headScale);
                p.neckTopY = hi.neckTopCentre.y; p.neckTopZ = hi.neckTopCentre.z; p.neckTopRx = hi.neckTopRx; p.neckTopRz = hi.neckTopRz;
                var br = SVBody.Build(mb, p, m.outfit);
                BuildWorn(mb, m, p, pr);
                string label = m.arch.ToString();
                var mesh = SkinnedMesh(mb, $"{label}_{idx}");

                // ---- skeleton: rest positions, A-pose bind matrices, then back to identity
                var root = new GameObject($"{label}_{m.spot.loc}_{idx:000}_{Hex(m.outer)}");
                root.transform.SetParent(parent, false);
                var bones = new Transform[SVSkel.Count];
                var bonePos = br.bonePos;
                bonePos[SVSkel.Eyes] = hi.eyesBone; bonePos[SVSkel.Mouth] = hi.mouthBone; bonePos[SVSkel.Head] = hi.headBone;
                bones[0] = root.transform;
                var restLocal = new Vector3[SVSkel.Count];
                for (int b = 1; b < SVSkel.Count; b++)
                {
                    var t = new GameObject(SVSkel.Names[b]).transform;
                    t.SetParent(bones[SVSkel.Parent[b]], false);
                    restLocal[b] = bonePos[b] - bonePos[SVSkel.Parent[b]];
                    t.localPosition = restLocal[b];
                    bones[b] = t;
                }
                var model = new GameObject("Model");
                model.transform.SetParent(root.transform, false);
                for (int b = 1; b < SVSkel.Count; b++) bones[b].localRotation = SVBody.BindLocalRotation(b);
                var bind = new Matrix4x4[SVSkel.Count];
                for (int b = 0; b < SVSkel.Count; b++) bind[b] = bones[b].worldToLocalMatrix * model.transform.localToWorldMatrix;
                for (int b = 1; b < SVSkel.Count; b++) bones[b].localRotation = Quaternion.identity;
                mesh.bindposes = bind;
                SaveMesh(mesh, $"Insan_{label}_{idx}");
                var smr = model.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                smr.bones = bones;
                smr.rootBone = bones[SVSkel.Root];
                smr.quality = SkinQuality.Bone2;
                smr.sharedMaterial = matPeople;
                smr.localBounds = new Bounds(new Vector3(0f, p.H * 0.5f + 0.1f, 0f), new Vector3(2.2f * p.H, 1.5f * p.H, 2.2f * p.H));
                smr.lightProbeUsage = LightProbeUsage.Off;
                smr.reflectionProbeUsage = ReflectionProbeUsage.Off;

                // ---- props held in the hands or carried at the chest
                var props = new List<PropRef>();
                int sD = m.domSide, sO = -sD;
                float lh = p.Lh * p.hand;
                PropRef Prop(PropKind kind, Mesh pm, int bone, Vector3 local, Quaternion rot, bool always, int side)
                {
                    var go = new GameObject("Esya_" + kind);
                    go.transform.SetParent(bones[bone], false);
                    go.transform.localPosition = local;
                    go.transform.localRotation = rot;
                    go.AddComponent<MeshFilter>().sharedMesh = pm;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = matPeople;
                    var rref = new PropRef { t = go.transform, kind = kind, side = side, always = always };
                    props.Add(rref);
                    return rref;
                }
                Vector3 Palm(int s, float inset) => new Vector3(-s * inset, -0.42f * lh, 0f);
                foreach (var k in m.props)
                {
                    switch (k)
                    {
                        case PropKind.Phone: Prop(k, Shared("Telefon_" + sD, () => PhoneMesh(sD)), SVSkel.Hand(sD), Palm(sD, 0.022f), Quaternion.identity, false, sD); break;
                        case PropKind.Radio: Prop(k, Shared("Telsiz_" + sD, () => RadioMesh(sD)), SVSkel.Hand(sD), Palm(sD, 0.03f), Quaternion.identity, false, sD); break;
                        case PropKind.Cane: Prop(k, Shared("Baston_" + sD + "_" + Mathf.RoundToInt(p.H * 20f), () => CaneMesh(sD, 0.86f * p.H / 1.75f)), SVSkel.Hand(sD), Palm(sD, 0.02f), Quaternion.identity, false, sD); break;
                        case PropKind.Tea: Prop(k, Shared("Cay", TeaMesh), SVSkel.Hand(sD), Palm(sD, 0.035f), Quaternion.identity, false, sD); break;
                        case PropKind.Beads: Prop(k, Shared("Tespih_" + (idx % 4), () => BeadsMesh(new System.Random(idx % 4))), SVSkel.Hand(sD), new Vector3(-sD * 0.015f, -0.55f * lh, 0f), Quaternion.identity, true, sD); break;
                        case PropKind.Cloth: Prop(k, Shared("Havlu_" + Hex(m.clothCol) + sD, () => ClothMesh(m.clothCol, sD)), SVSkel.Hand(sD), Palm(sD, 0.01f), Quaternion.identity, false, sD); break;
                        case PropKind.Baby:
                        {
                            Vector3 axis = new Vector3(sO, 0.35f, 0.15f).normalized;
                            Vector3 at = new Vector3(sO * 0.04f, -0.17f, 0.20f) * (p.H / 1.75f);
                            at = new Vector3(at.x, at.y, p.chestZf + 0.08f);
                            Prop(k, Shared("Bebek_" + (idx % 5), () => BabyMesh(new System.Random(idx % 5), m.outfit.skin)), SVSkel.Chest, at, Quaternion.FromToRotation(Vector3.right, axis), false, sO);
                            break;
                        }
                        case PropKind.Box: Prop(k, Shared("Kasa_" + (idx % 3), () => BoxMesh(new System.Random(idx % 3))), SVSkel.Chest, new Vector3(0f, -0.27f * p.H / 1.75f, 0.30f * p.H / 1.75f), Quaternion.identity, false, 0); break;
                    }
                }
                if (m.umbrella)
                {
                    int u = m.umbrellaSide;
                    float shaft = Mathf.Max(0.6f, (p.H + 0.17f) - (p.yChest - 0.23f * p.H / 1.75f));
                    Prop(PropKind.Umbrella, Shared("Semsiye_" + Hex(m.umbrellaCol) + Mathf.RoundToInt(shaft * 20f), () => UmbrellaMesh2(m.umbrellaCol, shaft, Hex(m.umbrellaCol) == "D8E4EA")), SVSkel.Hand(u), Palm(u, 0.02f), Quaternion.identity, true, u);
                }
                foreach (var pref in props) if (!pref.always) pref.t.gameObject.SetActive(false);

                // seat props on roofs
                if (m.seatProp)
                {
                    var seatGo = new GameObject("Oturak");
                    seatGo.transform.SetParent(parent, false);
                    seatGo.transform.SetPositionAndRotation(m.seat, Quaternion.Euler(0f, m.yaw, 0f));
                    seatGo.AddComponent<MeshFilter>().sharedMesh = Shared(m.seatH > 0.4f ? "Sandalye" : "KasaOturak_" + (idx % 3), () => SeatMesh(m.seatH > 0.4f, new System.Random(idx % 3), m.seatH));
                    seatGo.AddComponent<MeshRenderer>().sharedMaterial = matDynamic;
                }

                // ---- animator data
                root.transform.SetPositionAndRotation(m.hasSeat ? m.seat : m.pos, Quaternion.Euler(0f, m.yaw, 0f));
                var person = new Person
                {
                    root = root.transform, bones = bones, restPos = restLocal, smr = smr, id = idx, seed = 17 + idx * 131,
                    role = m.role, domSide = m.domSide, female = m.female,
                    s = p.H / 1.75f, hs = HeadSizeK * p.headH / 0.26f,
                    skirt = skirt, pockets = trousers && !skirt, umbrella = m.umbrella, umbrellaSide = m.umbrellaSide,
                    pool = m.pool, poolWeight = m.poolW,
                    group = m.spot.group, partner = -1, adultInPair = m.adultInPair,
                    lmSternum = br.sternum, lmBelly = br.belly, lmSmallOfBack = br.smallOfBack,
                    lmHipSideL = br.hipSideL, lmHipSideR = br.hipSideR, lmPocketL = br.pocketL, lmPocketR = br.pocketR,
                    lmMouth = hi.mouth, lmEarL = hi.earL, lmEarR = hi.earR, lmCheekL = hi.cheekL, lmCheekR = hi.cheekR, lmForehead = hi.forehead, lmCrown = hi.crown,
                    waistRx = p.waistRx, waistRz = Mathf.Max(p.waistZf, p.waistZb), chestRz = Mathf.Max(p.chestZf, p.chestZb),
                    hasSeat = m.hasSeat, seatPos = m.seat, seatFwd = m.seatFwd, seatH = m.seatH, sitStyle = m.sitStyle,
                    hasRail = m.spot.hasRail, railA = m.spot.railA, railB = m.spot.railB, railN = m.spot.railN, railTop = m.spot.railTop, bridgeRail = m.spot.bridge,
                    hasKnob = m.spot.hasKnob, knob = m.spot.knob, lane = m.spot.lane,
                    helpSource = m.spot.help, waterPoint = m.spot.water, pointTarget = m.spot.loc == SpotLoc.Roof || m.spot.loc == SpotLoc.Balcony ? m.spot.help : m.spot.water,
                    props = props.ToArray(),
                };
                if (m.partner >= 0 && groupMembers.TryGetValue(m.spot.group, out var gm) && m.partner < gm.Count) person.partner = gm[m.partner];
                Habits(person, m, p, pr);
                anim.people.Add(person);
            }
            // pose everyone for the edit-mode scene (partners exist now)
            // twice: pair contacts (hugs, an arm round a shoulder, a child on a leg) read the partner's posed bones
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < anim.people.Count; i++) anim.Bake(i, 3f + i * 0.37f);
            Debug.Log($"[Sel Vadisi] People: {cast.Count} ({CountBy(cast)})");
        }

        static string CountBy(List<CastMember> cast)
        {
            var d = new SortedDictionary<string, int>();
            foreach (var c in cast) { string k = c.arch.ToString(); d[k] = (d.TryGetValue(k, out int v) ? v : 0) + 1; }
            var sb = new System.Text.StringBuilder();
            foreach (var kv in d) sb.Append(kv.Key).Append(' ').Append(kv.Value).Append(", ");
            return sb.ToString();
        }

        /// Per-person animation habits (anim spec 2.0 table).
        static void Habits(Person p, CastMember m, BodyParams b, System.Random r)
        {
            switch (m.role)
            {
                case Role.Child: p.tempo = Rand(r, 1.2f, 1.4f); p.amp = Rand(r, 1.1f, 1.3f); p.maxElev = 180f; p.slouch = Rand(r, -2f, 2f); p.shiftPeriod = Rand(r, 2f, 5f); p.gazeDwell = Rand(r, 1f, 2.5f); p.blinkInterval = Rand(r, 4f, 8f); p.startEnergy = Rand(r, 0.6f, 1f); break;
                case Role.Teen: p.tempo = Rand(r, 1.05f, 1.25f); p.amp = Rand(r, 1f, 1.2f); p.maxElev = 180f; p.slouch = Rand(r, 0f, 6f); p.shiftPeriod = Rand(r, 3f, 7f); p.gazeDwell = Rand(r, 1.5f, 3.5f); p.blinkInterval = Rand(r, 3f, 6f); break;
                case Role.Woman: p.tempo = Rand(r, 0.95f, 1.15f); p.amp = Rand(r, 0.85f, 1.05f); p.maxElev = 175f; p.slouch = Rand(r, 0f, 6f); p.shiftPeriod = Rand(r, 4f, 9f); p.gazeDwell = Rand(r, 2f, 5f); p.blinkInterval = Rand(r, 2.5f, 5f); break;
                case Role.ElderMan: case Role.ElderWoman: p.tempo = Rand(r, 0.65f, 0.85f); p.amp = Rand(r, 0.55f, 0.75f); p.maxElev = 145f; p.slouch = Mathf.Max(m.slouch, Rand(r, 8f, 18f)); p.shiftPeriod = Rand(r, 6f, 14f); p.gazeDwell = Rand(r, 3f, 7f); p.blinkInterval = Rand(r, 3f, 6f); p.round = Rand(r, 6f, 12f); break;
                case Role.Worker: p.tempo = Rand(r, 1f, 1.15f); p.amp = Rand(r, 0.9f, 1.1f); p.maxElev = 178f; p.slouch = Rand(r, 0f, 4f); p.shiftPeriod = Rand(r, 5f, 10f); p.gazeDwell = Rand(r, 1.5f, 4f); p.blinkInterval = Rand(r, 3f, 5f); p.worry = -0.3f; break;
                default: p.tempo = Rand(r, 0.9f, 1.1f); p.amp = Rand(r, 0.9f, 1.1f); p.maxElev = 178f; p.slouch = Rand(r, 2f, 8f); p.shiftPeriod = Rand(r, 4f, 10f); p.gazeDwell = Rand(r, 2f, 5f); p.blinkInterval = Rand(r, 2.5f, 5f); break;
            }
            if (m.role != Role.ElderMan && m.role != Role.ElderWoman) p.round = Rand(r, 0f, 8f);
            if (m.role != Role.Worker) p.worry = Rand(r, 0.2f, 0.6f);
            p.habitPitch = Rand(r, -3f, 4f); p.habitRoll = Rand(r, -4f, 4f);
            float heavy = Mathf.Clamp01((b.waistRx - 0.13f) / 0.07f);
            p.stance = b.hipX + 0.025f + Rand(r, 0f, 0.04f) * (0.5f + heavy);
            p.toeOut = Rand(r, 6f, 14f);
            p.hipClear = Mathf.Asin(Mathf.Clamp((b.hipRx + 0.045f - b.ghX) / Mathf.Max(0.3f, b.Lu + b.Lf), -1f, 1f)) * Mathf.Rad2Deg + 2f;
            p.hipClear = Mathf.Clamp(p.hipClear, 3f, 14f);
            p.energy = p.startEnergy = m.spot.loc == SpotLoc.Roof ? Rand(r, 0.6f, 1f) : (m.spot.loc == SpotLoc.Balcony ? Rand(r, 0.5f, 0.9f) : Rand(r, 0.2f, 0.5f));
            p.restStyle = PickRest(m, r);
        }

        static int PickRest(CastMember m, System.Random r)
        {
            // Hang, Pockets, BehindBack, ArmsCrossed, ClaspedFront, HandsOnHips, OnePocket+Hang (anim spec 2.1 table)
            float[] w;
            switch (m.role)
            {
                case Role.Woman: w = new float[] { 20, 5, 2, 18, 30, 10, 15 }; break;
                case Role.ElderMan: w = new float[] { 10, 10, 40, 12, 8, 0, 20 }; break;
                case Role.ElderWoman: w = new float[] { 15, 0, 10, 15, 35, 5, 20 }; break;
                case Role.Teen: w = new float[] { 25, 35, 0, 20, 0, 10, 10 }; break;
                case Role.Child: w = new float[] { 45, 15, 0, 10, 10, 5, 15 }; break;
                case Role.Worker: w = new float[] { 15, 10, 5, 20, 0, 35, 15 }; break;
                default: w = new float[] { 20, 25, 8, 20, 5, 12, 10 }; break;
            }
            bool pockets = false;
            foreach (var g in m.outfit.garments) if (g.kind == GarmentKind.Trousers || g.kind == GarmentKind.Jeans) pockets = true;
            if (!pockets) { w[4] += w[1] + w[6]; w[1] = 0f; w[6] = 0f; }
            if (m.blanket) return 3;
            float tot = 0f; foreach (var x in w) tot += x;
            float u = (float)r.NextDouble() * tot;
            for (int i = 0; i < w.Length; i++) { u -= w[i]; if (u <= 0f) return i; }
            return 0;
        }

        /// Skinned mesh with welded smooth normals and compact 2-bone weights.
        static Mesh SkinnedMesh(MB mb, string name)
        {
            int n = mb.V.Count;
            var mesh = new Mesh { name = name, indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(mb.V);
            mesh.SetNormals(SVBody.WeldedNormals(mb.V, mb.T));
            mesh.SetColors(mb.C);
            mesh.SetUVs(0, mb.U);
            mesh.SetTriangles(mb.T, 0, true);
            var bpv = new NativeArray<byte>(n, Allocator.Temp);
            var list = new List<BoneWeight1>(n * 2);
            for (int i = 0; i < n; i++)
            {
                var w = mb.W[i];
                float w0 = w.weight0, w1 = w.weight1;
                int b0 = w.boneIndex0, b1 = w.boneIndex1;
                if (w1 > w0) { (w0, w1) = (w1, w0); (b0, b1) = (b1, b0); }
                if (w1 > 0.001f && b1 != b0)
                {
                    float s = w0 + w1;
                    list.Add(new BoneWeight1 { boneIndex = b0, weight = w0 / s });
                    list.Add(new BoneWeight1 { boneIndex = b1, weight = w1 / s });
                    bpv[i] = 2;
                }
                else { list.Add(new BoneWeight1 { boneIndex = b0, weight = 1f }); bpv[i] = 1; }
            }
            var arr = new NativeArray<BoneWeight1>(list.ToArray(), Allocator.Temp);
            mesh.SetBoneWeights(bpv, arr);
            bpv.Dispose(); arr.Dispose();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
