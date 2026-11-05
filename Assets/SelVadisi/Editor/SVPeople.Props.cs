using System.Collections.Generic;
using UnityEngine;
using PropKind = SelVadisi.PropKind;

namespace SelVadisi.EditorBuild
{
    /// Hand-held props (separate objects driven by the animator) and worn accessories (skinned into the body).
    /// Hand frame at rest: -Y toward the fingers, palm normal -side*X, thumb side +Z. Prop meshes have their origin at the grip.
    public static partial class SVBuild
    {
        static Mesh PhoneMesh(int side)
        {
            var b = new MB();
            b.Gloss = 0.7f;
            b.Box(Vector3.zero, new Vector3(0.009f, 0.15f, 0.075f), H("1A1B1E"), 0);
            b.Gloss = 0.2f; b.Emit = 0.6f;
            b.Box(new Vector3(-side * 0.0048f, 0.002f, 0f), new Vector3(0.0012f, 0.13f, 0.066f), H("9FC6E8"), 0);
            b.Emit = 0f; b.Gloss = 0.5f;
            b.Box(new Vector3(side * 0.0055f, -0.055f, 0.02f), new Vector3(0.003f, 0.022f, 0.022f), H("2A2C2E"), 0);
            return b.ToMesh("Telefon_" + (side > 0 ? "R" : "L"));
        }

        static Mesh RadioMesh(int side)
        {
            var b = new MB();
            b.Gloss = 0.3f;
            b.Box(Vector3.zero, new Vector3(0.035f, 0.11f, 0.055f), H("1E1F21"), 0);
            b.Box(new Vector3(-side * 0.018f, 0.012f, 0f), new Vector3(0.002f, 0.04f, 0.04f), H("3A3C40"), 0);
            b.Rod(new Vector3(0f, -0.05f, 0.015f), new Vector3(0f, -0.17f, 0.015f), 0.004f, 4, H("1A1B1E"));
            b.Emit = 1.0f;
            b.Box(new Vector3(-side * 0.012f, -0.045f, -0.018f), new Vector3(0.008f, 0.008f, 0.008f), H("FF4A3A"), 0);
            b.Emit = 0f;
            return b.ToMesh("Telsiz_" + (side > 0 ? "R" : "L"));
        }

        static Mesh CaneMesh(int side, float length)
        {
            var b = new MB();
            Color32 wood = H("5A3E2A");
            b.Gloss = 0.25f;
            // shaft from the palm down along -side*X (the palm faces the ground in the cane pose)
            b.Rod(new Vector3(0f, 0f, 0f), new Vector3(-side * length, 0f, 0f), 0.012f, 6, wood);
            b.Rod(new Vector3(-side * length, 0f, 0f), new Vector3(-side * (length + 0.02f), 0f, 0f), 0.014f, 6, H("1E1F22"));
            // crook above the grip, curling toward the fingers
            var pts = new List<Vector3>();
            for (int i = 0; i <= 6; i++)
            {
                float a = i / 6f * Mathf.PI;
                pts.Add(new Vector3(side * (0.02f + 0.05f * Mathf.Sin(a)), -0.05f + 0.05f * Mathf.Cos(a), 0f));
            }
            b.Tube(pts, 0.012f, 6, wood);
            b.Gloss = 0f;
            return b.ToMesh("Baston_" + (side > 0 ? "R" : "L"));
        }

        static Mesh TeaMesh()
        {
            var b = new MB();
            // tulip glass (origin at the waist where it is held), saucer below
            b.Gloss = 0.8f;
            var rings = new List<MB.Ring>
            {
                Ring(new Vector3(0f, -0.04f, 0f), Vector3.right, Vector3.forward, 0.022f, 0.022f, H("9A3A1A"), MB.Bone(0)),
                Ring(new Vector3(0f, 0.00f, 0f), Vector3.right, Vector3.forward, 0.016f, 0.016f, H("9A3A1A"), MB.Bone(0)),
                Ring(new Vector3(0f, 0.035f, 0f), Vector3.right, Vector3.forward, 0.024f, 0.024f, H("A8441E"), MB.Bone(0)),
                Ring(new Vector3(0f, 0.045f, 0f), Vector3.right, Vector3.forward, 0.025f, 0.025f, H("D9E6EE"), MB.Bone(0)),
            };
            for (int i = 0; i < rings.Count; i++) { var r = rings[i]; r.gloss = 0.85f; rings[i] = r; }
            b.Loft(rings, 10, true, true);
            b.Gloss = 0.5f;
            b.Cyl(new Vector3(0f, -0.048f, 0f), 0.045f, 0.006f, 12, H("F2F0EA"), -1f, true, true);
            b.Gloss = 0f;
            return b.ToMesh("CayBardagi");
        }

        static Mesh UmbrellaMesh2(Color32 col, float shaft, bool clear)
        {
            var b = new MB();
            Color32 alt = clear ? col : (col.r + col.g + col.b < 150 ? H("2A2D31") : MB.Shade(col, 0.88f));
            const int N = 10;
            const float R = 0.52f;
            float top = shaft;
            Vector3 apex = new Vector3(0f, top + 0.24f, 0f);
            b.Gloss = clear ? 0.9f : 0.45f;
            if (clear) b.Emit = 0.05f;
            for (int k = 0; k < N; k++)
            {
                float a0 = k * Mathf.PI * 2f / N, a1 = (k + 1) * Mathf.PI * 2f / N, am = (a0 + a1) * 0.5f;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * R, top, Mathf.Sin(a0) * R), p1 = new Vector3(Mathf.Cos(a1) * R, top, Mathf.Sin(a1) * R);
                Vector3 mid = new Vector3(Mathf.Cos(am) * R * 0.94f, top + 0.03f, Mathf.Sin(am) * R * 0.94f);
                Vector3 inner0 = new Vector3(Mathf.Cos(a0) * R * 0.55f, top + 0.17f, Mathf.Sin(a0) * R * 0.55f);
                Vector3 inner1 = new Vector3(Mathf.Cos(a1) * R * 0.55f, top + 0.17f, Mathf.Sin(a1) * R * 0.55f);
                Color32 c = (k & 1) == 0 ? col : alt;
                Vector3 outN = new Vector3(Mathf.Cos(am), 1.2f, Mathf.Sin(am));
                b.Tri(p0, inner0, mid, c, outN); b.Tri(mid, inner0, inner1, c, outN); b.Tri(mid, inner1, p1, c, outN);
                b.Tri(inner0, apex, inner1, c, Vector3.up);
                Color32 under = MB.Shade(c, 0.55f);
                b.Tri(p0, mid, inner0, under, -outN); b.Tri(mid, inner1, inner0, under, -outN); b.Tri(mid, p1, inner1, under, -outN);
                b.Tri(inner0, inner1, apex, under, Vector3.down);
                b.Emit = 0f;
                b.Box(p0 + Vector3.down * 0.01f, new Vector3(0.012f, 0.02f, 0.012f), H("2A2C2E"), 0);
            }
            b.Gloss = 0.3f;
            b.Rod(new Vector3(0f, 0f, 0f), new Vector3(0f, top + 0.3f, 0f), 0.009f, 6, H("2A2C2E"));
            var hook = new List<Vector3>();
            for (int i = 0; i <= 5; i++) { float a = i / 5f * Mathf.PI; hook.Add(new Vector3(0f, -0.02f - 0.05f * Mathf.Sin(a), 0.05f - 0.05f * Mathf.Cos(a))); }
            b.Tube(hook, 0.013f, 6, H("6B4A33"));
            b.Gloss = 0f;
            return b.ToMesh("Semsiye");
        }

        static Mesh ClothMesh(Color32 col, int side)
        {
            var b = new MB();
            const int NU = 4, NV = 3;
            float w = 0.6f, h = 0.36f;
            // held at one corner; hangs along the hand's +Y, spans +Z; wind weight grows toward the free edges
            for (int face = 0; face < 2; face++)
            {
                int start = b.Count;
                for (int j = 0; j <= NV; j++)
                    for (int i = 0; i <= NU; i++)
                    {
                        float u = i / (float)NU, v = j / (float)NV;
                        b.Wind = Mathf.Clamp01(0.8f * Mathf.Max(u, v * 0.9f));
                        b.Vert(new Vector3(side * 0.01f * Mathf.Sin(u * 7f), v * h, u * w), face == 0 ? col : MB.Shade(col, 0.8f));
                    }
                b.Wind = 0f;
                for (int j = 0; j < NV; j++)
                    for (int i = 0; i < NU; i++)
                    {
                        int a = start + j * (NU + 1) + i, c = a + NU + 1;
                        if (face == 0) { b.Tri(a, c, a + 1); b.Tri(a + 1, c, c + 1); }
                        else { b.Tri(a, a + 1, c); b.Tri(a + 1, c + 1, c); }
                    }
            }
            return b.ToMesh("Havlu");
        }

        static Mesh BeadsMesh(System.Random r)
        {
            var b = new MB();
            Color32 c = Pk(r, "C98A2E", "1E1F22", "8E1E24", "3AA6A0");
            b.Gloss = 0.6f;
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                Vector3 p = new Vector3(0.008f * Mathf.Sin(a), -0.02f - 0.06f * (1f - Mathf.Cos(a)) * 0.5f - 0.01f * Mathf.Sin(a) * Mathf.Sin(a), 0.025f * Mathf.Sin(a));
                b.Ico(p, Vector3.one * 0.0065f, c, 0, 0f, r);
            }
            b.Cone(new Vector3(0f, -0.14f, 0f), 0.008f, 0.025f, 5, MB.Shade(c, 0.8f));
            b.Gloss = 0f;
            return b.ToMesh("Tespih");
        }

        static Mesh BabyMesh(System.Random r, Color32 skin)
        {
            var b = new MB();
            Color32 bl = Pk(r, "E9C9D6", "BFD7EA", "F2EFE6", "E8DFA8");
            // bundle along +X, head at +X
            b.IcoAO(new Vector3(-0.04f, 0f, 0f), new Vector3(0.22f, 0.09f, 0.11f), bl, 1, 0.04f, r, 0.25f, 0.08f);
            b.IcoAO(new Vector3(0.16f, 0.01f, 0f), new Vector3(0.068f, 0.066f, 0.07f), MB.Shade(bl, 0.95f), 1, 0.03f, r, 0.2f, 0.08f);
            b.Ico(new Vector3(0.175f, 0.03f, 0.03f), new Vector3(0.046f, 0.05f, 0.04f), skin, 1, 0f, r);
            for (int s = -1; s <= 1; s += 2) b.Box(new Vector3(0.18f + s * 0.012f, 0.045f, 0.066f), new Vector3(0.006f, 0.003f, 0.003f), H("2A211D"), 0);
            b.Box(new Vector3(0.0f, 0.09f, 0f), new Vector3(0.05f, 0.02f, 0.2f), MB.Shade(bl, 0.88f), 0);
            return b.ToMesh("Bebek");
        }

        static Mesh BoxMesh(System.Random r)
        {
            var b = new MB();
            Color32 c = Pk(r, "2F6FB5", "C49A63", "E8B923", "3A7A3A");
            b.Box(Vector3.zero, new Vector3(0.45f, 0.30f, 0.35f), c, 0);
            b.Box(new Vector3(0f, 0.151f, 0f), new Vector3(0.08f, 0.004f, 0.36f), MB.Shade(c, 1.25f), 0);
            for (int s = -1; s <= 1; s += 2) b.Box(new Vector3(s * 0.226f, 0.08f, 0f), new Vector3(0.004f, 0.04f, 0.12f), MB.Shade(c, 0.6f), 0);
            return b.ToMesh("Kasa");
        }

        static Mesh SeatMesh(bool chair, System.Random r, float h)
        {
            var b = new MB();
            if (chair)
            {
                Color32 c = H("EDEDE8");
                b.Gloss = 0.3f;
                b.Box(new Vector3(0f, h - 0.02f, 0f), new Vector3(0.44f, 0.04f, 0.42f), c, 0);
                for (int i = -1; i <= 1; i += 2) for (int j = -1; j <= 1; j += 2)
                        b.Rod(new Vector3(i * 0.19f, h - 0.03f, j * 0.18f), new Vector3(i * 0.21f, 0f, j * 0.2f), 0.018f, 4, c);
                b.Box(new Vector3(0f, h + 0.22f, -0.2f), new Vector3(0.42f, 0.36f, 0.03f), c, 0);
                b.Gloss = 0f;
            }
            else
            {
                Color32 c = Pk(r, "E8B923", "3A7A3A", "2F6DB5");
                b.Box(new Vector3(0f, h * 0.5f, 0f), new Vector3(0.5f, h, 0.35f), c, MB.NY);
                b.Box(new Vector3(0f, h * 0.5f, 0.176f), new Vector3(0.3f, 0.08f, 0.004f), MB.Shade(c, 0.6f), 0);
            }
            return b.ToMesh(chair ? "Sandalye" : "Kasa_Oturak");
        }

        // ------------------------------------------------------------------ worn accessories (skinned, rest pose)

        static void BuildWorn(MB mb, CastMember m, BodyParams p, System.Random r)
        {
            if (m.backpack)
            {
                mb.Weight = MB.Bone(SVSkel.Chest);
                Color32 c = m.packCol;
                float zc = -(p.chestZb + 0.075f), yc = (p.yChest + p.yWaist) * 0.5f + 0.05f;
                var rings = new List<MB.Ring>();
                for (int i = 0; i <= 4; i++)
                {
                    float t = i / 4f;
                    float y = Mathf.Lerp(yc - 0.19f, yc + 0.19f, t);
                    float wdt = 0.13f + 0.03f * Mathf.Sin(t * Mathf.PI);
                    rings.Add(Ring(new Vector3(0f, y, zc), Vector3.right, Vector3.forward, wdt * Mathf.Min(1f, p.H / 1.6f), 0.065f, c, MB.Bone(SVSkel.Chest), 3f));
                }
                mb.Loft(rings, 12, true, true);
                mb.Box(new Vector3(0f, yc - 0.07f, zc - 0.07f), new Vector3(0.2f, 0.14f, 0.04f), MB.Shade(c, 0.85f), 0);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 a = new Vector3(s * 0.08f, p.yNeck - 0.03f, -p.chestZb * 0.5f);
                    Vector3 f1 = new Vector3(s * 0.1f, p.yArmpit, p.chestZf + 0.012f);
                    Vector3 f2 = new Vector3(s * 0.11f, p.yChest - 0.08f, p.chestZf * 0.9f);
                    mb.Tube(new List<Vector3> { new Vector3(s * 0.09f, yc + 0.17f, zc + 0.04f), a + new Vector3(0f, 0.02f, 0f), f1, f2 }, 0.011f, 4, MB.Shade(c, 0.7f));
                }
            }
            if (m.shoulderBag)
            {
                Color32 c = Pk(r, "5A3A2A", "2B2B2B", "7A2E3A");
                int s = m.domSide;
                mb.Weight = MB.Bone(SVSkel.Pelvis);
                Vector3 bag = new Vector3(s * (p.hipRx + 0.05f), p.yHip + 0.05f, 0.02f);
                mb.Box(bag, new Vector3(0.08f, 0.18f, 0.26f), c, 0);
                mb.Box(bag + new Vector3(s * 0.041f, 0.05f, 0f), new Vector3(0.004f, 0.08f, 0.25f), MB.Shade(c, 0.85f), 0);
                mb.Weight = MB.Bone(SVSkel.Chest);
                mb.Tube(new List<Vector3> { new Vector3(-s * 0.1f, p.yNeck - 0.02f, 0.02f), new Vector3(0f, p.yChest, p.chestZf + 0.02f), new Vector3(s * 0.12f, p.yWaist + 0.02f, p.waistZf + 0.02f), bag + new Vector3(0f, 0.08f, 0.1f) }, 0.009f, 4, MB.Shade(c, 0.8f));
            }
        }
    }
}
