using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Küçük JSON okuyucu: nesne -> Dictionary<string, object>, dizi -> List<object>, sayı -> double.
namespace OfisHaritasi.EditorTools
{
    public static class OHJson
    {
        public static object Parse(string s) { int i = 0; var v = Value(s, ref i); return v; }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{') return Obj(s, ref i);
            if (c == '[') return Arr(s, ref i);
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
        }

        static Dictionary<string, object> Obj(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++;
            while (true)
            {
                Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                string k = Str(s, ref i);
                Ws(s, ref i); i++; // :
                d[k] = Value(s, ref i);
                Ws(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
            }
        }

        static List<object> Arr(string s, ref int i)
        {
            var l = new List<object>();
            i++;
            while (true)
            {
                Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                l.Add(Value(s, ref i));
                Ws(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return l; }
            }
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (s[i] != '"')
            {
                if (s[i] == '\\')
                {
                    i++;
                    char e = s[i];
                    if (e == 'n') sb.Append('\n'); else if (e == 't') sb.Append('\t');
                    else if (e == 'u') { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                    else sb.Append(e);
                }
                else sb.Append(s[i]);
                i++;
            }
            i++;
            return sb.ToString();
        }

        // yardımcılar
        public static Dictionary<string, object> O(object o) { return o as Dictionary<string, object>; }
        public static List<object> A(object o) { return o as List<object>; }
        public static object Get(Dictionary<string, object> d, params string[] keys)
        {
            if (d == null) return null;
            foreach (var k in keys) { object v; if (d.TryGetValue(k, out v)) return v; }
            return null;
        }
        public static float F(object o, float def = 0) { if (o is double) return (float)(double)o; if (o is bool) return (bool)o ? 1 : 0; return def; }
        public static string S(object o, string def = "") { return o as string ?? def; }
        public static bool B(object o, bool def = false) { if (o is bool) return (bool)o; return def; }
        public static float[] Fs(object o)
        {
            var l = A(o); if (l == null) return null;
            var r = new float[l.Count];
            for (int i = 0; i < l.Count; i++) r[i] = F(l[i]);
            return r;
        }
        public static UnityEngine.Vector3 V3(object o, UnityEngine.Vector3 def = default(UnityEngine.Vector3))
        {
            var f = Fs(o); if (f == null || f.Length < 3) return def;
            return new UnityEngine.Vector3(f[0], f[1], f[2]);
        }
    }
}
