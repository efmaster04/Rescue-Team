using System.Collections.Generic;
using UnityEngine;

namespace SelVadisi.EditorBuild
{
    public static partial class SVBuild
    {
        // ------------------------------------------------------------------ floating debris

        class DebrisType
        {
            public string name;
            public float weight, radius, draft, bob = 0.05f, tilt = 5f, spin = 12f;
            public System.Func<System.Random, int, MB> make;
            public int variants = 1;
            public System.Func<System.Random, Vector3> baseEuler = rr => Vector3.zero;
        }

        static MB New() => new MB();

        static List<DebrisType> DebrisTypes()
        {
            Color32 white = H("ECECE5"), grey = H("8E9194"), dark = H("2A2C2E"), chrome = H("C9CDD0");
            Color32[] plastic = { H("2F6DB5"), H("D94436"), H("3E9E57"), H("E8C33A"), H("F0F0EE") };
            Color32[] woods = { H("9C7448"), H("B08A5C"), H("85633F") };
            var list = new List<DebrisType>
            {
                new DebrisType { name = "Buzdolabi", weight = 6, radius = 0.55f, draft = 0.15f, tilt = 4f, spin = 8f, variants = 2, make = (r, v) => Fridge(v == 0 ? white : H("D9DADB"), grey, dark, r),
                    baseEuler = r => new Vector3(Rand(r, -12f, 12f), 0f, Rand(r, -12f, 12f)) },
                new DebrisType { name = "Buzdolabi_Yatik", weight = 4, radius = 0.85f, draft = 0.1f, tilt = 3f, spin = 6f, make = (r, v) => Fridge(white, grey, dark, r),
                    baseEuler = r => new Vector3(90f + Rand(r, -6f, 6f), 0f, 0f) },
                new DebrisType { name = "CamasirMakinesi", weight = 5, radius = 0.45f, draft = 0.1f, make = (r, v) =>
                {
                    var b = New(); b.Box(Vector3.zero, new Vector3(0.6f, 0.85f, 0.6f), white, 0);
                    b.Box(new Vector3(0f, 0.33f, -0.302f), new Vector3(0.58f, 0.15f, 0.01f), H("D5D7D8"), 0);
                    b.Box(new Vector3(-0.18f, 0.33f, -0.31f), new Vector3(0.16f, 0.07f, 0.02f), H("BFC2C4"), 0);
                    b.Push(new Vector3(0.18f, 0.33f, -0.31f), Quaternion.Euler(-90f, 0f, 0f)); b.Cyl(Vector3.zero, 0.04f, 0.03f, 10, grey); b.Pop();
                    b.Push(new Vector3(0f, -0.07f, -0.3f), Quaternion.Euler(-90f, 0f, 0f));
                    b.Cyl(Vector3.zero, 0.23f, 0.03f, 14, chrome); b.Gloss = 0.9f; b.Cyl(new Vector3(0f, 0.03f, 0f), 0.17f, 0.025f, 14, H("33424C")); b.Gloss = 0f;
                    b.Pop();
                    b.Box(new Vector3(0f, -0.4f, -0.302f), new Vector3(0.56f, 0.05f, 0.01f), H("CFD1D2"), 0);
                    return b; }, baseEuler = r => new Vector3(Rand(r, -15f, 15f), 0f, Rand(r, -15f, 15f)) },
                new DebrisType { name = "TahtaKasa", weight = 10, radius = 0.45f, draft = 0.06f, variants = 3, make = (r, v) =>
                {
                    var b = New(); Color32 w = woods[v % woods.Length];
                    b.Box(Vector3.zero, new Vector3(0.66f, 0.46f, 0.46f), H("3A2A1E"), 0);
                    for (int i = -1; i <= 1; i++)
                        for (int s = -1; s <= 1; s += 2)
                        {
                            b.Box(new Vector3(0f, i * 0.16f, s * 0.245f), new Vector3(0.7f, 0.12f, 0.03f), MB.Jitter(w, r, 0.08f), 0);
                            b.Box(new Vector3(s * 0.345f, i * 0.16f, 0f), new Vector3(0.03f, 0.12f, 0.46f), MB.Jitter(w, r, 0.08f), 0);
                        }
                    for (int i = -1; i <= 1; i += 2) for (int j = -1; j <= 1; j += 2) b.Box(new Vector3(i * 0.33f, 0f, j * 0.23f), new Vector3(0.06f, 0.5f, 0.06f), MB.Shade(w, 0.85f), 0);
                    for (int i = -1; i <= 1; i++) b.Box(new Vector3(i * 0.22f, 0.245f, 0f), new Vector3(0.18f, 0.03f, 0.5f), MB.Jitter(w, r, 0.08f), 0);
                    return b; } },
                new DebrisType { name = "Varil", weight = 10, radius = 0.5f, draft = 0.05f, variants = 4, spin = 18f, make = (r, v) =>
                {
                    var b = New(); Color32 c = new[] { H("2F6DB5"), H("2F6DB5"), H("D94436"), H("3E9E57") }[v % 4];
                    b.Taper(new Vector3(-0.45f, 0f, 0f), new Vector3(0.45f, 0f, 0f), 0.29f, 0.29f, 0.29f, 0.29f, 14, c, true, true, MB.Shade(c, 0.9f));
                    foreach (float x in new[] { -0.36f, -0.15f, 0.15f, 0.36f }) b.Taper(new Vector3(x - 0.02f, 0f, 0f), new Vector3(x + 0.02f, 0f, 0f), 0.305f, 0.305f, 0.305f, 0.305f, 14, MB.Shade(c, 0.82f), false, false);
                    b.Push(new Vector3(0.452f, 0.15f, 0f), Quaternion.Euler(0f, 0f, -90f)); b.Cyl(Vector3.zero, 0.04f, 0.03f, 8, MB.Shade(c, 0.7f)); b.Pop();
                    b.Push(new Vector3(0.452f, -0.15f, 0.05f), Quaternion.Euler(0f, 0f, -90f)); b.Cyl(Vector3.zero, 0.03f, 0.03f, 8, MB.Shade(c, 0.7f)); b.Pop();
                    return b; } },
                new DebrisType { name = "PasliVaril", weight = 4, radius = 0.5f, draft = 0.08f, variants = 2, make = (r, v) =>
                {
                    var b = New(); Color32 c = v == 0 ? H("8E3B2A") : H("6B6F73");
                    b.Taper(new Vector3(-0.44f, 0f, 0f), new Vector3(0.44f, 0f, 0f), 0.3f, 0.3f, 0.3f, 0.3f, 14, c, true, true);
                    foreach (float x in new[] { -0.15f, 0.15f }) b.Taper(new Vector3(x - 0.02f, 0f, 0f), new Vector3(x + 0.02f, 0f, 0f), 0.315f, 0.315f, 0.315f, 0.315f, 14, MB.Shade(c, 0.78f), false, false);
                    for (int k = 0; k < 6; k++) b.IcoAO(new Vector3(Rand(r, -0.4f, 0.4f), Rand(r, -0.25f, 0.25f), Rand(r, -1f, 1f) > 0 ? 0.29f : -0.29f), new Vector3(0.08f, 0.06f, 0.02f), H("A0522D"), 0, 0.2f, r, 0f);
                    return b; } },
                new DebrisType { name = "PlastikSandalye", weight = 9, radius = 0.4f, draft = -0.02f, variants = 3, tilt = 8f, spin = 20f, make = (r, v) =>
                {
                    var b = New(); Color32 c = new[] { H("F0F0EE"), H("F0F0EE"), H("3E9E57") }[v % 3];
                    b.Box(Vector3.zero, new Vector3(0.46f, 0.04f, 0.44f), c, 0);
                    b.Push(new Vector3(0f, 0.24f, 0.22f), Quaternion.Euler(-8f, 0f, 0f));
                    b.Box(Vector3.zero, new Vector3(0.46f, 0.46f, 0.035f), c, 0);
                    for (float x = -0.15f; x <= 0.16f; x += 0.1f) b.Box(new Vector3(x, 0.05f, 0f), new Vector3(0.05f, 0.26f, 0.04f), MB.Shade(c, 0.8f), 0);
                    b.Pop();
                    for (int s = -1; s <= 1; s += 2) b.Box(new Vector3(s * 0.23f, 0.13f, 0.02f), new Vector3(0.035f, 0.035f, 0.42f), c, 0);
                    for (int i = -1; i <= 1; i += 2) for (int j = -1; j <= 1; j += 2)
                        b.Taper(new Vector3(i * 0.2f, 0f, j * 0.19f), new Vector3(i * 0.23f, -0.42f, j * 0.22f), 0.025f, 0.03f, 0.02f, 0.025f, 5, c, false, true);
                    return b; }, baseEuler = r => new Vector3(r.NextDouble() < 0.5 ? 180f : 90f, 0f, Rand(r, -20f, 20f)) },
                new DebrisType { name = "TupGaz", weight = 5, radius = 0.35f, draft = 0.0f, spin = 22f, make = (r, v) =>
                {
                    var b = New(); Color32 y = H("E8C12E");
                    b.Taper(new Vector3(-0.3f, 0f, 0f), new Vector3(0.24f, 0f, 0f), 0.16f, 0.16f, 0.16f, 0.16f, 12, y, false, false);
                    b.Taper(new Vector3(0.24f, 0f, 0f), new Vector3(0.33f, 0f, 0f), 0.16f, 0.16f, 0.08f, 0.08f, 12, y, false, true);
                    b.Taper(new Vector3(-0.36f, 0f, 0f), new Vector3(-0.3f, 0f, 0f), 0.15f, 0.15f, 0.16f, 0.16f, 12, MB.Shade(y, 0.8f), true, false);
                    b.Rod(new Vector3(0.33f, 0f, 0f), new Vector3(0.42f, 0f, 0f), 0.035f, 6, chrome);
                    b.Box(new Vector3(0.4f, 0.05f, 0f), new Vector3(0.03f, 0.07f, 0.03f), H("D9473B"), 0);
                    for (int s = -1; s <= 1; s += 2) b.Rod(new Vector3(0.3f, s * 0.08f, 0f), new Vector3(0.45f, s * 0.1f, 0f), 0.015f, 4, MB.Shade(y, 0.85f));
                    return b; } },
                new DebrisType { name = "Lastik", weight = 8, radius = 0.45f, draft = 0.02f, tilt = 3f, make = (r, v) =>
                {
                    var b = New();
                    for (int k = 0; k < 16; k++)
                    {
                        float a0 = k * Mathf.PI / 8f, a1 = (k + 1) * Mathf.PI / 8f;
                        b.Rod(new Vector3(Mathf.Cos(a0) * 0.3f, 0f, Mathf.Sin(a0) * 0.3f), new Vector3(Mathf.Cos(a1) * 0.3f, 0f, Mathf.Sin(a1) * 0.3f), 0.11f, 6, H("232325"));
                        float am = (a0 + a1) * 0.5f;
                        b.Push(new Vector3(Mathf.Cos(am) * 0.41f, 0f, Mathf.Sin(am) * 0.41f), -am * Mathf.Rad2Deg);
                        b.Box(Vector3.zero, new Vector3(0.03f, 0.17f, 0.07f), H("1A1A1C"), 0);
                        b.Pop();
                    }
                    b.Cyl(new Vector3(0f, -0.08f, 0f), 0.21f, 0.16f, 12, H("9A9DA0"), -1f, true, true);
                    for (int k = 0; k < 5; k++) b.Cyl(new Vector3(Mathf.Cos(k * 1.257f) * 0.1f, 0.08f, Mathf.Sin(k * 1.257f) * 0.1f), 0.02f, 0.02f, 6, H("5A5D60"));
                    return b; } },
                new DebrisType { name = "Kutuk", weight = 18, radius = 0.9f, draft = 0.03f, tilt = 2f, spin = 6f, variants = 3, make = (r, v) =>
                {
                    var b = New(); float len = 2.6f + v * 0.9f, rad = 0.17f + v * 0.05f;
                    Color32 bark = MB.Jitter(H("6A4B33"), r, 0.08f);
                    b.Taper(new Vector3(-len * 0.5f, 0f, 0f), new Vector3(len * 0.5f, 0f, 0f), rad, rad, rad * 0.82f, rad * 0.82f, 8, bark, false, false);
                    for (int e = -1; e <= 1; e += 2)
                    {
                        float er = e < 0 ? rad : rad * 0.82f;
                        b.Push(new Vector3(e * len * 0.5f, 0f, 0f), Quaternion.Euler(0f, 0f, -90f * e));
                        b.Cyl(Vector3.zero, er * 0.98f, 0.015f, 8, H("C9A574"));
                        b.Cyl(new Vector3(0f, 0.015f, 0f), er * 0.6f, 0.01f, 8, H("B08C5E"));
                        b.Cyl(new Vector3(0f, 0.025f, 0f), er * 0.25f, 0.008f, 8, H("9C7A50"));
                        b.Pop();
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        float x = Rand(r, -len * 0.35f, len * 0.35f), a = Rand(r, 0f, 6.28f);
                        Vector3 o = new Vector3(x, Mathf.Cos(a) * rad, Mathf.Sin(a) * rad);
                        b.Taper(o * 0.9f, o + new Vector3(Rand(r, -0.1f, 0.3f), Mathf.Cos(a) * 0.35f, Mathf.Sin(a) * 0.35f), 0.06f, 0.06f, 0.025f, 0.025f, 5, bark, false, true);
                    }
                    for (int k = 0; k < 8; k++) b.Box(new Vector3(Rand(r, -len * 0.45f, len * 0.45f), rad * 0.95f, Rand(r, -0.05f, 0.05f)), new Vector3(Rand(r, 0.2f, 0.5f), 0.03f, 0.04f), MB.Shade(bark, 0.75f), 0);
                    return b; } },
                new DebrisType { name = "YaprakliDal", weight = 12, radius = 1.0f, draft = 0.05f, tilt = 4f, make = (r, v) =>
                {
                    var b = New(); Color32 bark = H("6A4B33");
                    b.Taper(new Vector3(-1.4f, 0f, 0f), new Vector3(1.3f, 0.05f, 0.1f), 0.11f, 0.11f, 0.04f, 0.04f, 6, bark, true, true);
                    b.Taper(new Vector3(0f, 0f, 0f), new Vector3(0.8f, 0.25f, 0.75f), 0.06f, 0.06f, 0.025f, 0.025f, 5, bark, false, true);
                    b.Taper(new Vector3(-0.6f, 0f, 0f), new Vector3(-0.2f, 0.2f, -0.7f), 0.05f, 0.05f, 0.02f, 0.02f, 5, bark, false, true);
                    b.IcoAO(new Vector3(1.15f, 0.15f, 0.1f), new Vector3(0.7f, 0.38f, 0.6f), H("5F8A3E"), 1, 0.25f, r, 0.4f);
                    b.IcoAO(new Vector3(0.8f, 0.28f, 0.75f), new Vector3(0.5f, 0.32f, 0.45f), H("6E9A46"), 1, 0.25f, r, 0.4f);
                    b.IcoAO(new Vector3(-0.2f, 0.2f, -0.72f), new Vector3(0.45f, 0.28f, 0.4f), H("7AA04B"), 1, 0.25f, r, 0.4f);
                    return b; } },
                new DebrisType { name = "SamanBalyasi", weight = 6, radius = 0.85f, draft = 0.2f, tilt = 3f, spin = 5f, make = (r, v) =>
                {
                    var b = New();
                    b.Taper(new Vector3(-0.6f, 0f, 0f), new Vector3(0.6f, 0f, 0f), 0.72f, 0.72f, 0.72f, 0.72f, 14, H("D8BC6A"), true, true, H("C4A55A"));
                    for (float x = -0.4f; x <= 0.41f; x += 0.4f) b.Taper(new Vector3(x - 0.015f, 0f, 0f), new Vector3(x + 0.015f, 0f, 0f), 0.73f, 0.73f, 0.73f, 0.73f, 14, H("B9C6D1"), false, false);
                    for (int e = -1; e <= 1; e += 2) { b.Push(new Vector3(e * 0.6f, 0f, 0f), Quaternion.Euler(0f, 0f, -90f * e)); b.Cyl(Vector3.zero, 0.45f, 0.01f, 12, H("B8994F")); b.Cyl(new Vector3(0f, 0.01f, 0f), 0.2f, 0.01f, 10, H("A88A45")); b.Pop(); }
                    return b; } },
                new DebrisType { name = "Palet", weight = 8, radius = 0.7f, draft = 0.0f, tilt = 3f, make = (r, v) =>
                {
                    var b = New(); Color32 w = H("B89A6B");
                    for (int i = -3; i <= 3; i++) b.Box(new Vector3(i * 0.18f, 0.05f, 0f), new Vector3(0.12f, 0.025f, 1.0f), MB.Jitter(w, r, 0.08f), 0);
                    for (int j = -1; j <= 1; j++) { b.Box(new Vector3(0f, -0.03f, j * 0.45f), new Vector3(1.2f, 0.1f, 0.1f), MB.Shade(w, 0.85f), 0); for (int i = -1; i <= 1; i++) b.Box(new Vector3(i * 0.5f, -0.03f, j * 0.45f), new Vector3(0.14f, 0.12f, 0.12f), MB.Shade(w, 0.75f), 0); }
                    return b; } },
                new DebrisType { name = "PlastikKova", weight = 8, radius = 0.35f, draft = 0.02f, variants = 4, spin = 25f, make = (r, v) =>
                {
                    var b = New(); Color32 c = plastic[v % plastic.Length];
                    b.Taper(new Vector3(0f, -0.25f, 0f), new Vector3(0f, 0.25f, 0f), 0.22f, 0.22f, 0.28f, 0.28f, 12, c, true, false);
                    b.Taper(new Vector3(0f, 0.22f, 0f), new Vector3(0f, -0.2f, 0f), 0.265f, 0.265f, 0.2f, 0.2f, 12, MB.Shade(c, 0.6f), false, true);
                    b.Taper(new Vector3(0f, 0.23f, 0f), new Vector3(0f, 0.27f, 0f), 0.295f, 0.295f, 0.295f, 0.295f, 12, MB.Shade(c, 0.9f), false, false);
                    b.Rod(new Vector3(-0.28f, 0.25f, 0f), new Vector3(0f, 0.45f, 0f), 0.012f, 4, grey); b.Rod(new Vector3(0f, 0.45f, 0f), new Vector3(0.28f, 0.25f, 0f), 0.012f, 4, grey);
                    return b; }, baseEuler = r => new Vector3(Rand(r, 60f, 110f), 0f, 0f) },
                new DebrisType { name = "KoliKutu", weight = 7, radius = 0.35f, draft = 0.05f, make = (r, v) =>
                {
                    var b = New(); b.Box(Vector3.zero, new Vector3(0.5f, 0.4f, 0.45f), H("C49A63"), 0);
                    b.Box(new Vector3(0f, 0.2f, 0f), new Vector3(0.09f, 0.012f, 0.46f), H("D9C7A0"), 0);
                    b.Push(new Vector3(0.15f, 0.21f, 0.1f), Quaternion.Euler(0f, 0f, 25f)); b.Box(Vector3.zero, new Vector3(0.22f, 0.01f, 0.2f), H("B58A55"), 0); b.Pop();
                    b.Box(new Vector3(-0.1f, 0f, -0.226f), new Vector3(0.15f, 0.12f, 0.005f), H("2A2C2E"), 0);
                    return b; } },
                new DebrisType { name = "AhsapKapi", weight = 3, radius = 0.9f, draft = 0.0f, tilt = 3f, variants = 2, make = (r, v) =>
                {
                    var b = New(); Color32 c = v == 0 ? H("6B4630") : H("3F5E7A");
                    b.Box(Vector3.zero, new Vector3(0.9f, 0.06f, 2.0f), c, 0);
                    for (int i = -1; i <= 1; i += 2) for (int j = -1; j <= 1; j += 2) b.Box(new Vector3(i * 0.2f, 0.04f, j * 0.48f), new Vector3(0.3f, 0.025f, 0.8f), MB.Shade(c, 1.18f), 0);
                    b.Box(new Vector3(0.36f, 0.07f, 0.05f), new Vector3(0.05f, 0.04f, 0.14f), Brass, 0);
                    return b; } },
                new DebrisType { name = "Yatak", weight = 3, radius = 1.0f, draft = 0.02f, tilt = 2f, make = (r, v) =>
                {
                    var b = New(); b.Box(Vector3.zero, new Vector3(1.9f, 0.22f, 0.9f), H("DCE3EA"), 0);
                    for (float x = -0.8f; x <= 0.81f; x += 0.4f) b.Box(new Vector3(x, 0.112f, 0f), new Vector3(0.12f, 0.006f, 0.9f), H("8FA7C4"), 0);
                    for (float x = -0.85f; x <= 0.86f; x += 0.17f) for (float z = -0.35f; z <= 0.36f; z += 0.17f) b.Box(new Vector3(x, 0.113f, z), new Vector3(0.03f, 0.006f, 0.03f), H("B9C4D2"), 0);
                    b.Taper(new Vector3(-0.95f, 0f, 0f), new Vector3(0.95f, 0f, 0f), 0.02f, 0.11f, 0.02f, 0.11f, 4, H("9FB0C6"), false, false);
                    return b; } },
                new DebrisType { name = "Top", weight = 4, radius = 0.15f, draft = -0.06f, bob = 0.08f, spin = 40f, variants = 2, make = (r, v) =>
                {
                    var b = New(); b.Ico(Vector3.zero, Vector3.one * 0.12f, v == 0 ? H("F2F2F0") : H("E84A2A"), 1, 0f, r, H("2A2C2E"));
                    return b; } },
                new DebrisType { name = "SuDeposu", weight = 3, radius = 0.75f, draft = 0.1f, tilt = 3f, make = (r, v) =>
                {
                    var b = New(); Color32 c = H("2B2D30");
                    b.Taper(new Vector3(-0.7f, 0f, 0f), new Vector3(0.7f, 0f, 0f), 0.45f, 0.45f, 0.45f, 0.45f, 14, c, true, true);
                    foreach (float x in new[] { -0.35f, 0f, 0.35f }) b.Taper(new Vector3(x - 0.03f, 0f, 0f), new Vector3(x + 0.03f, 0f, 0f), 0.47f, 0.47f, 0.47f, 0.47f, 14, MB.Shade(c, 1.3f), false, false);
                    b.Cyl(new Vector3(0f, 0.43f, 0f), 0.14f, 0.08f, 10, MB.Shade(c, 1.4f));
                    return b; } },
                new DebrisType { name = "Kanepe", weight = 2, radius = 1.0f, draft = 0.15f, tilt = 3f, variants = 2, make = (r, v) =>
                {
                    var b = New(); Color32 c = v == 0 ? H("7A5A44") : H("5E6E86");
                    b.Box(new Vector3(0f, -0.05f, 0f), new Vector3(1.8f, 0.35f, 0.85f), MB.Shade(c, 0.9f), 0);
                    b.Box(new Vector3(0f, 0.38f, 0.32f), new Vector3(1.8f, 0.55f, 0.22f), MB.Shade(c, 0.95f), 0);
                    for (int s = -1; s <= 1; s += 2) b.Box(new Vector3(s * 0.83f, 0.25f, 0f), new Vector3(0.18f, 0.32f, 0.86f), MB.Shade(c, 0.85f), 0);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        b.Box(new Vector3(s * 0.37f, 0.2f, -0.06f), new Vector3(0.72f, 0.14f, 0.66f), c, 0);
                        b.Box(new Vector3(s * 0.37f, 0.48f, 0.18f), new Vector3(0.7f, 0.4f, 0.14f), MB.Shade(c, 1.08f), 0);
                    }
                    for (int i = -1; i <= 1; i += 2) for (int j = -1; j <= 1; j += 2) b.Box(new Vector3(i * 0.8f, -0.27f, j * 0.35f), new Vector3(0.06f, 0.1f, 0.06f), dark, 0);
                    return b; } },
                new DebrisType { name = "TersMasa", weight = 3, radius = 0.7f, draft = 0.0f, make = (r, v) =>
                {
                    var b = New(); Color32 w = H("A47B52");
                    b.Box(Vector3.zero, new Vector3(1.2f, 0.05f, 0.8f), w, 0);
                    b.Box(new Vector3(0f, 0.06f, 0f), new Vector3(1.05f, 0.07f, 0.65f), MB.Shade(w, 0.85f), 0);
                    for (int i = -1; i <= 1; i += 2) for (int j = -1; j <= 1; j += 2) b.Taper(new Vector3(i * 0.5f, 0.05f, j * 0.3f), new Vector3(i * 0.5f, 0.75f, j * 0.3f), 0.035f, 0.035f, 0.025f, 0.025f, 6, MB.Shade(w, 0.85f), false, true);
                    return b; } },
                new DebrisType { name = "Tahtalar", weight = 10, radius = 0.9f, draft = 0.0f, tilt = 3f, make = (r, v) =>
                {
                    var b = New();
                    for (int k = 0; k < 4; k++) { b.Push(new Vector3(Rand(r, -0.25f, 0.25f), k * 0.045f, Rand(r, -0.3f, 0.3f)), Rand(r, -25f, 25f)); b.Box(Vector3.zero, new Vector3(0.22f, 0.045f, Rand(r, 1.6f, 2.6f)), MB.Jitter(H("A78B63"), r, 0.1f), 0); b.Pop(); }
                    return b; } },
                new DebrisType { name = "Bidon", weight = 6, radius = 0.3f, draft = 0.03f, variants = 2, spin = 25f, make = (r, v) =>
                {
                    var b = New(); Color32 c = v == 0 ? H("3E7A3A") : H("C8372D");
                    b.Box(Vector3.zero, new Vector3(0.35f, 0.45f, 0.18f), c, 0);
                    b.Box(new Vector3(0f, 0f, 0.092f), new Vector3(0.25f, 0.3f, 0.01f), MB.Shade(c, 0.85f), 0);
                    b.Box(new Vector3(0.06f, 0.26f, 0f), new Vector3(0.18f, 0.06f, 0.06f), c, 0);
                    b.Cyl(new Vector3(-0.11f, 0.22f, 0f), 0.045f, 0.08f, 8, dark);
                    return b; }, baseEuler = r => new Vector3(0f, 0f, Rand(r, 60f, 100f)) },
                new DebrisType { name = "KirikCit", weight = 5, radius = 0.9f, draft = 0.0f, tilt = 3f, make = (r, v) =>
                {
                    var b = New(); Color32 w = H("B08A5C");
                    b.Box(new Vector3(0f, 0f, -0.3f), new Vector3(1.8f, 0.05f, 0.08f), w, 0);
                    b.Box(new Vector3(0f, 0f, 0.3f), new Vector3(1.8f, 0.05f, 0.08f), w, 0);
                    for (float x = -0.8f; x <= 0.81f; x += 0.2f)
                    {
                        float l = Rand(r, 0.9f, 1.15f);
                        b.Box(new Vector3(x, 0.03f, 0f), new Vector3(0.09f, 0.035f, l), MB.Jitter(w, r, 0.08f), 0);
                        b.Push(new Vector3(x, 0.03f, l * 0.5f), Quaternion.Euler(90f, 0f, 0f)); b.Cone(Vector3.zero, 0.065f, 0.1f, 4, MB.Jitter(w, r, 0.08f), Mathf.PI * 0.25f); b.Pop();
                    }
                    return b; } },
                new DebrisType { name = "Televizyon", weight = 3, radius = 0.4f, draft = 0.08f, tilt = 5f, make = (r, v) =>
                {
                    var b = New(); Color32 body = H("4A4C50");
                    b.Box(new Vector3(0f, 0f, 0.05f), new Vector3(0.62f, 0.5f, 0.45f), body, 0);
                    b.Taper(new Vector3(0f, 0f, 0.27f), new Vector3(0f, 0f, 0.45f), 0.25f, 0.2f, 0.14f, 0.12f, 4, MB.Shade(body, 0.85f), false, true);
                    b.Gloss = 0.9f; b.Box(new Vector3(-0.05f, 0.02f, -0.18f), new Vector3(0.44f, 0.36f, 0.02f), H("1E2A30"), 0); b.Gloss = 0f;
                    for (int k = 0; k < 3; k++) b.Box(new Vector3(0.24f, 0.12f - k * 0.08f, -0.18f), new Vector3(0.05f, 0.04f, 0.02f), H("9EA3A6"), 0);
                    return b; }, baseEuler = r => new Vector3(Rand(r, -20f, 20f), 0f, Rand(r, -25f, 25f)) },
                new DebrisType { name = "Komodin", weight = 3, radius = 0.6f, draft = 0.15f, tilt = 4f, make = (r, v) =>
                {
                    var b = New(); Color32 w = H("8A6440");
                    b.Box(Vector3.zero, new Vector3(0.9f, 0.8f, 0.45f), w, 0);
                    for (int k = 0; k < 3; k++)
                    {
                        float y = -0.25f + k * 0.26f;
                        b.Box(new Vector3(0f, y, -0.23f), new Vector3(0.82f, 0.22f, 0.02f), MB.Shade(w, 1.12f), 0);
                        for (int s = -1; s <= 1; s += 2) b.Box(new Vector3(s * 0.22f, y, -0.25f), new Vector3(0.08f, 0.03f, 0.03f), Brass, 0);
                    }
                    b.Box(new Vector3(0f, 0.41f, 0f), new Vector3(0.96f, 0.04f, 0.5f), MB.Shade(w, 0.9f), 0);
                    return b; }, baseEuler = r => new Vector3(r.NextDouble() < 0.5 ? 80f : Rand(r, -10f, 10f), 0f, Rand(r, -10f, 10f)) },
                new DebrisType { name = "OyuncakAyi", weight = 2, radius = 0.25f, draft = 0.0f, bob = 0.07f, spin = 25f, make = (r, v) =>
                {
                    var b = New(); Color32 fur = H("B07A48"), light = H("E2C29A");
                    b.IcoAO(new Vector3(0f, 0f, 0f), new Vector3(0.16f, 0.18f, 0.13f), fur, 1, 0f, r, 0.2f);
                    b.IcoAO(new Vector3(0f, 0.25f, 0f), new Vector3(0.12f, 0.11f, 0.11f), fur, 1, 0f, r, 0.2f);
                    b.IcoAO(new Vector3(0f, 0.23f, 0.09f), new Vector3(0.05f, 0.04f, 0.04f), light, 0, 0f, r, 0f);
                    b.IcoAO(new Vector3(0f, 0.01f, 0.11f), new Vector3(0.09f, 0.11f, 0.03f), light, 0, 0f, r, 0f);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        b.IcoAO(new Vector3(s * 0.09f, 0.34f, 0f), new Vector3(0.04f, 0.04f, 0.025f), fur, 0, 0f, r, 0f);
                        b.Box(new Vector3(s * 0.04f, 0.27f, 0.1f), new Vector3(0.02f, 0.02f, 0.01f), dark, 0);
                        b.IcoAO(new Vector3(s * 0.15f, 0.05f, 0.03f), new Vector3(0.05f, 0.1f, 0.05f), fur, 0, 0f, r, 0f);
                        b.IcoAO(new Vector3(s * 0.08f, -0.17f, 0.06f), new Vector3(0.06f, 0.05f, 0.08f), fur, 0, 0f, r, 0f);
                    }
                    b.Box(new Vector3(0f, 0.235f, 0.13f), new Vector3(0.025f, 0.018f, 0.01f), dark, 0);
                    return b; }, baseEuler = r => new Vector3(-70f, 0f, Rand(r, -20f, 20f)) },
                new DebrisType { name = "CopTorbasi", weight = 6, radius = 0.35f, draft = 0.08f, variants = 2, make = (r, v) =>
                {
                    var b = New(); Color32 c = v == 0 ? H("1E1F22") : H("3A5E3A");
                    b.IcoAO(Vector3.zero, new Vector3(0.3f, 0.26f, 0.28f), c, 1, 0.18f, r, 0.2f, 0.2f);
                    b.Taper(new Vector3(0f, 0.2f, 0f), new Vector3(0.05f, 0.36f, 0f), 0.06f, 0.06f, 0.03f, 0.03f, 5, c, false, true);
                    b.Gloss = 0.6f; b.Ico(new Vector3(0.1f, 0.15f, 0.08f), new Vector3(0.1f, 0.06f, 0.08f), MB.Shade(c, 1.6f), 0, 0f, r); b.Gloss = 0f;
                    return b; } },
                new DebrisType { name = "SogutucuKutu", weight = 3, radius = 0.4f, draft = 0.06f, make = (r, v) =>
                {
                    var b = New();
                    b.Box(new Vector3(0f, -0.05f, 0f), new Vector3(0.6f, 0.3f, 0.38f), H("2F6DB5"), 0);
                    b.Box(new Vector3(0f, 0.14f, 0f), new Vector3(0.62f, 0.1f, 0.4f), H("F0F0EE"), 0);
                    b.Box(new Vector3(0f, 0.21f, 0f), new Vector3(0.3f, 0.04f, 0.06f), H("F0F0EE"), 0);
                    return b; } },
                new DebrisType { name = "SisePaketi", weight = 3, radius = 0.3f, draft = 0.0f, spin = 20f, make = (r, v) =>
                {
                    var b = New();
                    for (int i = 0; i < 3; i++) for (int j = 0; j < 2; j++)
                    {
                        Vector3 c = new Vector3((i - 1) * 0.09f, 0f, (j - 0.5f) * 0.09f);
                        b.Gloss = 0.8f;
                        b.Taper(c + new Vector3(-0.15f, 0f, 0f), c + new Vector3(0.1f, 0f, 0f), 0.042f, 0.042f, 0.042f, 0.042f, 8, H("A9CDEB"), true, false);
                        b.Taper(c + new Vector3(0.1f, 0f, 0f), c + new Vector3(0.16f, 0f, 0f), 0.042f, 0.042f, 0.015f, 0.015f, 8, H("A9CDEB"), false, false);
                        b.Gloss = 0f;
                        b.Taper(c + new Vector3(0.16f, 0f, 0f), c + new Vector3(0.19f, 0f, 0f), 0.017f, 0.017f, 0.017f, 0.017f, 6, H("2F6DB5"), false, true);
                        b.Taper(c + new Vector3(-0.05f, 0f, 0f), c + new Vector3(0.03f, 0f, 0f), 0.044f, 0.044f, 0.044f, 0.044f, 8, H("E84A2A"), false, false);
                    }
                    return b; } },
            };
            return list;
        }

        static MB Fridge(Color32 body, Color32 handle, Color32 dark, System.Random r)
        {
            var b = New();
            b.Box(Vector3.zero, new Vector3(0.72f, 1.75f, 0.68f), body, 0);
            // freezer + fridge doors with a gap line, rounded edges suggested by darker edge strips
            b.Box(new Vector3(0f, 0.36f, -0.342f), new Vector3(0.7f, 0.03f, 0.012f), dark, 0);
            b.Box(new Vector3(0f, 0.6f, -0.35f), new Vector3(0.68f, 0.44f, 0.02f), MB.Shade(body, 1.03f), 0);
            b.Box(new Vector3(0f, -0.25f, -0.35f), new Vector3(0.68f, 1.14f, 0.02f), MB.Shade(body, 1.03f), 0);
            b.Box(new Vector3(0.27f, 0.6f, -0.38f), new Vector3(0.035f, 0.3f, 0.04f), handle, 0);
            b.Box(new Vector3(0.27f, 0.05f, -0.38f), new Vector3(0.035f, 0.4f, 0.04f), handle, 0);
            b.Box(new Vector3(-0.2f, 0.75f, -0.362f), new Vector3(0.14f, 0.04f, 0.005f), H("6B6F73"), 0);
            b.Box(new Vector3(0f, -0.82f, -0.345f), new Vector3(0.62f, 0.08f, 0.015f), H("444648"), 0);
            for (int k = 0; k < 6; k++) b.Box(new Vector3(0f, -0.6f + k * 0.22f, 0.345f), new Vector3(0.6f, 0.025f, 0.02f), H("3A3C3E"), 0);
            for (int i = -1; i <= 1; i += 2) for (int j = -1; j <= 1; j += 2)
                b.Box(new Vector3(i * 0.36f, 0f, j * 0.34f), new Vector3(0.03f, 1.76f, 0.03f), MB.Shade(body, 0.85f), 0);
            return b;
        }

        static void BuildDebris(Transform parent)
        {
            var r = new System.Random(2024);
            var drift = parent.gameObject.AddComponent<DebrisDrift>();
            var types = DebrisTypes();
            float total = 0f;
            foreach (var t in types) total += t.weight;
            var cache = new Dictionary<string, Mesh>();
            int count = 0;
            for (int attempt = 0; attempt < 6000 && count < 280; attempt++)
            {
                // choose a type
                float pick = (float)r.NextDouble() * total;
                DebrisType type = types[0];
                foreach (var t in types) { pick -= t.weight; if (pick <= 0f) { type = t; break; } }
                int variant = r.Next(type.variants);
                string key = type.name + "_" + variant;
                if (!cache.TryGetValue(key, out Mesh mesh))
                {
                    var mr = new System.Random(key.GetHashCode());
                    mesh = SaveMesh(type.make(mr, variant).ToMeshPacked(key), "Esya_" + key);
                    cache[key] = mesh;
                }
                // position: denser through the village, everywhere along the flood
                float z = r.NextDouble() < 0.65 ? Rand(r, -260f, 260f) : Rand(r, -600f, 560f);
                float x = Valley.CenterX(z) + Rand(r, -0.85f, 0.85f) * Valley.HalfWidth(z);
                if (Valley.Depth(x, z) < type.draft + 0.6f) continue;
                bool blocked = false;
                for (int k = 0; k < obstacles.Count && !blocked; k++)
                {
                    var o = obstacles[k];
                    float dx = x - o.x, dz = z - o.z, rr = o.r + type.radius + 0.5f;
                    if (dx * dx + dz * dz < rr * rr) blocked = true;
                }
                if (blocked) continue;

                var go = new GameObject(type.name);
                go.transform.SetParent(parent, false);
                Vector3 be = type.baseEuler(r);
                float yaw = Rand(r, 0f, 360f);
                go.transform.SetPositionAndRotation(new Vector3(x, Valley.WaterLevel(z) - type.draft, z), Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(be));
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var ren = go.AddComponent<MeshRenderer>();
                ren.sharedMaterial = matDynamic;
                ren.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                ren.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                drift.items.Add(new DebrisDrift.Item
                {
                    t = go.transform, radius = type.radius, draft = type.draft, bob = type.bob,
                    tilt = type.tilt, spin = type.spin * Rand(r, 0.6f, 1.4f) * (r.NextDouble() < 0.5 ? -1f : 1f), baseEuler = be
                });
                count++;
            }
        }

        // ------------------------------------------------------------------ flags and hanging sheets

        static void BuildCloth(Transform parent)
        {
            int i = 0;
            foreach (var c in clothSpots)
            {
                MB mb = c.flag ? TurkishFlag(c.h) : Sheet(c.w, c.h, c.color);
                var mesh = SaveMesh(mb.ToMesh("Kumas_" + i), (c.flag ? "Bayrak_" : "Carsaf_") + i);
                var go = MeshObject(c.flag ? "Turk Bayragi" : "Balkondan Sarkan Carsaf", parent, mesh, matDynamic, true);
                go.transform.SetPositionAndRotation(c.pos, Quaternion.Euler(0f, c.yaw, 0f));
                var fw = go.AddComponent<FlagWave>();
                fw.hanging = !c.flag;
                fw.length = c.flag ? c.h * 1.5f : c.h;
                fw.amplitude = c.flag ? 0.2f : (c.h < 1f ? 0.06f : 0.09f);
                if (!c.flag && c.h < 1f) go.name = "Camasir";
                fw.speed = c.flag ? 6.5f : 3.2f;
                fw.waveLength = c.flag ? 1.8f : 1.2f;
                fw.phase = i * 1.37f;
                i++;
            }
        }

        static void ClothGrid(MB b, int nx, int ny, System.Func<float, float, Vector3> P, Color32 col, float zOff)
        {
            int start = b.Count;
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                    b.Vert(P((float)i / nx, (float)j / ny) + new Vector3(0f, 0f, zOff), col);
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = start + j * (nx + 1) + i, c = a + nx + 1;
                    b.Tri(a, c, c + 1); b.Tri(a, c + 1, a + 1);   // one side
                    b.Tri(a, c + 1, c); b.Tri(a, a + 1, c + 1);   // other side
                }
        }

        static MB Sheet(float w, float h, Color32 col)
        {
            var b = new MB();
            ClothGrid(b, 6, 10, (u, v) => new Vector3((u - 0.5f) * w, -v * h, 0f), col, 0f);
            return b;
        }

        static MB TurkishFlag(float G)
        {
            var b = new MB();
            float L = G * 1.5f;
            ClothGrid(b, 18, 10, (u, v) => new Vector3(u * L, -v * G, 0f), H("E30A17"), 0f);
            Color32 white = H("FFFFFF");
            float cy = -G * 0.5f;
            // crescent: outer circle (centre 0.5G, r 0.25G) minus inner circle (centre 0.5625G, r 0.2G)
            for (int side = -1; side <= 1; side += 2)
            {
                float z = side * 0.022f;
                const int N = 28;
                float oa0 = 32.4f * Mathf.Deg2Rad, oa1 = 327.6f * Mathf.Deg2Rad, ia0 = 42f * Mathf.Deg2Rad, ia1 = 318f * Mathf.Deg2Rad;
                Vector3 nrm = new Vector3(0f, 0f, side);
                for (int k = 0; k < N; k++)
                {
                    float t0 = (float)k / N, t1 = (float)(k + 1) / N;
                    Vector3 o0 = new Vector3(0.5f * G + Mathf.Cos(Mathf.Lerp(oa0, oa1, t0)) * 0.25f * G, cy + Mathf.Sin(Mathf.Lerp(oa0, oa1, t0)) * 0.25f * G, z);
                    Vector3 o1 = new Vector3(0.5f * G + Mathf.Cos(Mathf.Lerp(oa0, oa1, t1)) * 0.25f * G, cy + Mathf.Sin(Mathf.Lerp(oa0, oa1, t1)) * 0.25f * G, z);
                    Vector3 i0 = new Vector3(0.5625f * G + Mathf.Cos(Mathf.Lerp(ia0, ia1, t0)) * 0.2f * G, cy + Mathf.Sin(Mathf.Lerp(ia0, ia1, t0)) * 0.2f * G, z);
                    Vector3 i1 = new Vector3(0.5625f * G + Mathf.Cos(Mathf.Lerp(ia0, ia1, t1)) * 0.2f * G, cy + Mathf.Sin(Mathf.Lerp(ia0, ia1, t1)) * 0.2f * G, z);
                    b.Quad(o0, o1, i1, i0, white, nrm);
                }
                // star: circumscribed radius 0.125G, centre 0.896G, one point toward the hoist
                Vector3 sc = new Vector3(0.896f * G, cy, z);
                float R = 0.125f * G, rIn = R * 0.382f;
                for (int k = 0; k < 10; k++)
                {
                    float a0 = Mathf.PI + k * Mathf.PI / 5f, a1 = Mathf.PI + (k + 1) * Mathf.PI / 5f;
                    float r0 = (k % 2 == 0) ? R : rIn, r1 = (k % 2 == 0) ? rIn : R;
                    Vector3 p0 = sc + new Vector3(Mathf.Cos(a0) * r0, Mathf.Sin(a0) * r0, 0f);
                    Vector3 p1 = sc + new Vector3(Mathf.Cos(a1) * r1, Mathf.Sin(a1) * r1, 0f);
                    b.Tri(sc, p0, p1, white, nrm);
                }
            }
            return b;
        }

        // ------------------------------------------------------------------ animated props (beacon, pump)

        static void BuildAnimatedProps(Transform parent)
        {
            // rotating amber beacon at the rescue area
            var bm = new MB();
            bm.Cyl(new Vector3(0f, 0f, 0f), 0.16f, 0.32f, 8, H("2A2C2E"));
            bm.Emit = 2.2f;
            bm.Box(new Vector3(0.16f, 0.16f, 0f), new Vector3(0.1f, 0.24f, 0.22f), H("FFB01F"), 0);
            bm.Box(new Vector3(-0.16f, 0.16f, 0f), new Vector3(0.1f, 0.24f, 0.22f), H("FFB01F"), 0);
            bm.Emit = 0f;
            var beaconMesh = SaveMesh(bm.ToMesh("TepeLambasi"), "TepeLambasi");
            var beacon = MeshObject("Donen Tepe Lambasi", parent, beaconMesh, matDynamic, false);
            beacon.transform.position = beaconPos + Vector3.up * 4.2f;
            var spin = beacon.AddComponent<SimpleMotion>();
            spin.kind = SimpleMotion.Kind.Spin;
            spin.speed = 300f;

            // vibrating motor pump
            var pm = new MB();
            Color32 frame = H("C24A2A"), engine = H("555A5E");
            float w = 0.9f, d = 0.7f, h = 0.75f;
            for (int i = -1; i <= 1; i += 2)
                for (int j = -1; j <= 1; j += 2)
                    pm.Rod(new Vector3(i * w * 0.5f, 0f, j * d * 0.5f), new Vector3(i * w * 0.5f, h, j * d * 0.5f), 0.025f, 4, frame);
            for (int j = -1; j <= 1; j += 2)
            {
                pm.Rod(new Vector3(-w * 0.5f, h, j * d * 0.5f), new Vector3(w * 0.5f, h, j * d * 0.5f), 0.025f, 4, frame);
                pm.Rod(new Vector3(-w * 0.5f, 0.02f, j * d * 0.5f), new Vector3(w * 0.5f, 0.02f, j * d * 0.5f), 0.025f, 4, frame);
            }
            pm.BoxB(-0.1f, 0.05f, 0f, 0.45f, 0.42f, 0.4f, engine);
            pm.BoxB(0.24f, 0.05f, 0f, 0.3f, 0.3f, 0.34f, H("3E78B8"));
            pm.BoxB(-0.1f, 0.47f, 0f, 0.42f, 0.18f, 0.34f, H("D9473B"));
            pm.Rod(new Vector3(-0.32f, 0.3f, 0.12f), new Vector3(-0.5f, 0.55f, 0.12f), 0.05f, 6, H("8A8D90"));
            var pumpMesh = SaveMesh(pm.ToMesh("Motopomp"), "Motopomp");
            var pump = MeshObject("Calisan Motopomp", parent, pumpMesh, matDynamic, true);
            pump.transform.position = pumpPos;
            var shake = pump.AddComponent<SimpleMotion>();
            shake.kind = SimpleMotion.Kind.Shake;
            shake.speed = 45f;
            shake.amplitude = 0.012f;
        }
    }
}
