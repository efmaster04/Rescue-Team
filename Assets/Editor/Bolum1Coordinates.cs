using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

// Blender → Unity koordinat dönüşümü ve Bölüm 1 JSON dosyalarının okunması.
//
// Blender: sağ el, X doğu, Y dağ yönü, Z yukarı (markers.json, minivan_meta.json).
// Unity:   sol el, Y yukarı.
// FBX'ler Blender'dan axis_forward=-Z, axis_up=Y ile çıkarıldı, Unity'de Bake Axis Conversion KAPALI import edildi.
// Bu durumda Blender noktası (x, y, z) Unity'de (-x, z, -y) olur. Modül kökleri Unity'de (0,0,0)'a düşer,
// kökte yalnızca eksen dönüşümü kalır. Bolum1Verify bu dönüşümü raycast ile doğrular (patika ve yol yüzeyi).
public static class Bolum1Coordinates
{
    public const string MarkersPath = "Assets/Models/Environment/Bolum1/markers.json";
    public const string MinivanMetaPath = "Assets/Models/Vehicles/Minivan/minivan_meta.json";
    public const string TowTruckMetaPath = "Assets/Models/Vehicles/TowTruck/towtruck_meta.json";

    // Dönüşümün tek kaynağı: Blender dünya noktası → Unity dünya noktası.
    public static Vector3 ToUnity(Vector3 blender)
    {
        return new Vector3(-blender.x, blender.z, -blender.y);
    }

    // Blender'daki bir dönüş (quaternion) → Unity dünyasındaki aynı dönüş.
    // Eksen ToUnity ile taşınır; el yönü değiştiği için açı ters döner. Sonuç: (x, -z, y, w).
    public static Quaternion ToUnity(Quaternion blender)
    {
        return new Quaternion(blender.x, -blender.z, blender.y, blender.w);
    }

    // Blender Euler (XYZ sırası, derece) → Blender quaternion.
    public static Quaternion BlenderEulerXYZ(Vector3 degrees)
    {
        // Blender'da sağ el; XYZ sırasında önce X, sonra Y, sonra Z uygulanır: q = qz * qy * qx.
        Quaternion qx = BlenderAxisAngle(new Vector3(1, 0, 0), degrees.x);
        Quaternion qy = BlenderAxisAngle(new Vector3(0, 1, 0), degrees.y);
        Quaternion qz = BlenderAxisAngle(new Vector3(0, 0, 1), degrees.z);
        return qz * qy * qx;
    }

    private static Quaternion BlenderAxisAngle(Vector3 axis, float degrees)
    {
        // Quaternion çarpımı el yönünden bağımsızdır; bileşenleri doğrudan yazıyoruz.
        float half = degrees * Mathf.Deg2Rad * 0.5f;
        float s = Mathf.Sin(half);
        return new Quaternion(axis.x * s, axis.y * s, axis.z * s, Mathf.Cos(half));
    }

    // FBX'ten gelen bir objeyi Blender'daki yerine koyar.
    // importRotation: prefabın kendi kök dönüşü (eksen dönüşümü); onu korumazsak model yan yatar.
    public static void Place(Transform t, Vector3 blenderPosition, Quaternion blenderRotation, Quaternion importRotation)
    {
        t.position = ToUnity(blenderPosition);
        t.rotation = ToUnity(blenderRotation) * importRotation;
    }

    // ---- JSON yardımcıları ----

    public static Dictionary<string, object> LoadJson(string assetPath)
    {
        var text = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
        if (text == null)
            throw new Exception("JSON bulunamadı: " + assetPath);
        return (Dictionary<string, object>)MiniJson.Parse(text.text);
    }

    public static Dictionary<string, object> Obj(object o, string key)
    {
        return (Dictionary<string, object>)((Dictionary<string, object>)o)[key];
    }

    public static bool Has(object o, string key)
    {
        return o is Dictionary<string, object> d && d.ContainsKey(key);
    }

    public static Vector3 Vec3(object o)
    {
        var list = (List<object>)o;
        return new Vector3(Convert.ToSingle(list[0]), Convert.ToSingle(list[1]), Convert.ToSingle(list[2]));
    }

    public static Vector3 Vec3(object o, string key)
    {
        return Vec3(((Dictionary<string, object>)o)[key]);
    }

    // [w, x, y, z] → Unity Quaternion(x, y, z, w) (hâlâ Blender uzayında).
    public static Quaternion QuatWxyz(object o)
    {
        var list = (List<object>)o;
        return new Quaternion(Convert.ToSingle(list[1]), Convert.ToSingle(list[2]), Convert.ToSingle(list[3]), Convert.ToSingle(list[0]));
    }

    // Küçük JSON okuyucu: nesne → Dictionary, dizi → List, sayı → double. Harici paket gerekmesin diye.
    private static class MiniJson
    {
        public static object Parse(string json)
        {
            int i = 0;
            object value = ReadValue(json, ref i);
            return value;
        }

        private static void SkipWhite(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static object ReadValue(string s, ref int i)
        {
            SkipWhite(s, ref i);
            char c = s[i];
            if (c == '{') return ReadObject(s, ref i);
            if (c == '[') return ReadArray(s, ref i);
            if (c == '"') return ReadString(s, ref i);
            if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
            if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
            if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        private static Dictionary<string, object> ReadObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++; // {
            SkipWhite(s, ref i);
            if (s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWhite(s, ref i);
                string key = ReadString(s, ref i);
                SkipWhite(s, ref i);
                i++; // :
                d[key] = ReadValue(s, ref i);
                SkipWhite(s, ref i);
                if (s[i] == ',') { i++; continue; }
                i++; // }
                return d;
            }
        }

        private static List<object> ReadArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // [
            SkipWhite(s, ref i);
            if (s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ReadValue(s, ref i));
                SkipWhite(s, ref i);
                if (s[i] == ',') { i++; continue; }
                i++; // ]
                return list;
            }
        }

        private static string ReadString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // "
            while (s[i] != '"')
            {
                if (s[i] == '\\')
                {
                    i++;
                    char e = s[i];
                    if (e == 'u') { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                    else if (e == 'n') sb.Append('\n');
                    else if (e == 't') sb.Append('\t');
                    else sb.Append(e);
                }
                else sb.Append(s[i]);
                i++;
            }
            i++; // "
            return sb.ToString();
        }
    }
}
