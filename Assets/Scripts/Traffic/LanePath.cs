using System;
using UnityEngine;

// Trafik şeridi: markers.json'daki polyline'ın Unity dünya koordinatına çevrilmiş hâli.
// Şerit üzerindeki her nokta "yol uzunluğu" s (metre, şeridin başından itibaren) ile adreslenir.
// Bütün trafik (şimdilik yalnız ambulans) bu sınıfı LaneFollower üzerinden kullanır.
public class LanePath : MonoBehaviour
{
    [Tooltip("Şerit orta çizgisinin noktaları (Unity dünya koordinatı, yol yüzeyinde). Builder markers.json'dan doldurur.")]
    public Vector3[] points = new Vector3[0];

    [Tooltip("Şerit genişliği (m). Ambulansın 'önümde biri var mı' kontrolü bunu kullanır.")]
    public float width = 4f;

    [Tooltip("Yön (teğet) hesaplanırken s'nin bu kadar önüne ve arkasına bakılır; köşelerdeki ani dönüşü yumuşatır.")]
    public float tangentWindow = 1.5f;

    // cumulative[i] = şeridin başından i. noktaya kadar olan yol uzunluğu
    private float[] cumulative;

    public float Length
    {
        get
        {
            EnsureCache();
            return cumulative.Length > 0 ? cumulative[cumulative.Length - 1] : 0f;
        }
    }

    // Noktalar değişince (editörde) önbelleği yeniden kur.
    public void Rebuild()
    {
        cumulative = null;
        EnsureCache();
    }

    private void EnsureCache()
    {
        if (cumulative != null && cumulative.Length == points.Length)
            return;
        cumulative = new float[points.Length];
        for (int i = 1; i < points.Length; i++)
            cumulative[i] = cumulative[i - 1] + Vector3.Distance(points[i - 1], points[i]);
    }

    // s mesafesindeki nokta (polyline üzerinde doğrusal ara değer; yükseklik de polyline'dan gelir).
    public Vector3 PointAt(float s)
    {
        EnsureCache();
        if (points.Length == 0) return transform.position;
        if (points.Length == 1) return points[0];
        s = Mathf.Clamp(s, 0f, Length);
        int i = SegmentIndex(s);
        float segLength = cumulative[i + 1] - cumulative[i];
        float t = segLength > 1e-6f ? (s - cumulative[i]) / segLength : 0f;
        return Vector3.LerpUnclamped(points[i], points[i + 1], t);
    }

    // s mesafesindeki yön: s-w ile s+w arasındaki kiriş. Köşelerde yumuşak döner, eğimi (yokuş) de içerir.
    public Vector3 TangentAt(float s)
    {
        EnsureCache();
        if (points.Length < 2) return Vector3.forward;
        float w = Mathf.Max(0.05f, tangentWindow);
        Vector3 a = PointAt(Mathf.Max(0f, s - w));
        Vector3 b = PointAt(Mathf.Min(Length, s + w));
        Vector3 dir = b - a;
        if (dir.sqrMagnitude < 1e-8f)
        {
            int i = SegmentIndex(Mathf.Clamp(s, 0f, Length));
            dir = points[i + 1] - points[i];
        }
        return dir.normalized;
    }

    // Şeridin sağ tarafı (yatay). Sağdan trafik: EastBound'da sağ = vadi tarafı.
    public Vector3 RightAt(float s)
    {
        Vector3 t = TangentAt(s);
        t.y = 0f;
        if (t.sqrMagnitude < 1e-8f) return Vector3.right;
        return Vector3.Cross(Vector3.up, t.normalized);
    }

    // Bir noktanın şeride izdüşümü (yatay düzlemde en yakın nokta).
    // lateral: şerit orta çizgisine yatay uzaklık, işaretli (+ = sağ). closest: şerit üzerindeki en yakın nokta.
    public float Project(Vector3 p, out float lateral, out Vector3 closest)
    {
        EnsureCache();
        lateral = 0f;
        closest = PointAt(0f);
        if (points.Length < 2) return 0f;
        float bestSqr = float.MaxValue, bestS = 0f;
        Vector2 q = new Vector2(p.x, p.z);
        for (int i = 0; i + 1 < points.Length; i++)
        {
            Vector2 a = new Vector2(points[i].x, points[i].z);
            Vector2 b = new Vector2(points[i + 1].x, points[i + 1].z);
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude) : 0f;
            float d = (a + ab * t - q).sqrMagnitude;
            if (d < bestSqr)
            {
                bestSqr = d;
                bestS = cumulative[i] + (cumulative[i + 1] - cumulative[i]) * t;
            }
        }
        closest = PointAt(bestS);
        Vector3 offset = p - closest;
        offset.y = 0f;
        lateral = Vector3.Dot(offset, RightAt(bestS));
        return bestS;
    }

    public float Project(Vector3 p)
    {
        return Project(p, out _, out _);
    }

    // s'yi içeren parçanın başlangıç indeksi (ikili arama).
    private int SegmentIndex(float s)
    {
        int idx = Array.BinarySearch(cumulative, s);
        if (idx < 0) idx = ~idx - 1;
        return Mathf.Clamp(idx, 0, points.Length - 2);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        for (int i = 1; i < points.Length; i++)
            Gizmos.DrawLine(points[i - 1], points[i]);
    }
}
