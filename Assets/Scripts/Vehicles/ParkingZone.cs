using System.Collections.Generic;
using UnityEngine;

// Sürüş girişi (kullanıcı kararı 2026-11-04): banketteki park alanı. Bu objenin pozu = sahnedeki park pozu (minibüsün
// kurucuda oturduğu yer: TowTruckPlacement); halfExtents yerel X (genişlik) ve Z (uzunluk) yarı ölçüleri.
// "İyi park" = minibüsün ayak izi (gövde köşeleri) tamamen alanın içinde VE hiçbir şeridin şeridine (±width/2) girmiyor.
// Görsel çerçeve (yere boyalı şerit) kurucuda (Bolum1DriveBuilder) ayrı bir mesh olarak çizilir; bu bileşen yalnız hesaptır.
public class ParkingZone : MonoBehaviour
{
    [Tooltip("Alanın yarı ölçüleri (yerel; x genişlik, z uzunluk).")]
    public Vector3 halfExtents = new Vector3(2f, 1f, 4.5f);
    [Tooltip("Trafik şeritleri: minibüs hiçbirine girmemeli.")]
    public LanePath[] lanes = new LanePath[0];
    [Tooltip("Şerit kenarına ek pay (m): şerit yarı genişliği + bu kadar içeride kalan nokta şeritte sayılır.")]
    public float laneMargin = 0f;

    public bool LastInside { get; private set; }
    public bool LastOutOfLanes { get; private set; }
    public string LastReason { get; private set; } = "";

    public bool Contains(Vector3 world)
    {
        Vector3 l = transform.InverseTransformPoint(world);
        return Mathf.Abs(l.x) <= halfExtents.x && Mathf.Abs(l.z) <= halfExtents.z;
    }

    public bool FullyInside(IEnumerable<Vector3> points)
    {
        foreach (var p in points) if (!Contains(p)) return false;
        return true;
    }

    // Örnek noktaların hiçbiri hiçbir şeridin şeridine girmiyor (aynı kotta: |Δy| ≤ 3 m).
    public bool OutOfLanes(IEnumerable<Vector3> points, out string reason)
    {
        reason = "";
        foreach (var lane in lanes)
        {
            if (lane == null) continue;
            float half = lane.width * 0.5f + laneMargin;
            foreach (var p in points)
            {
                lane.Project(p, out float lateral, out Vector3 on);
                if (Mathf.Abs(p.y - on.y) > 3f) continue;
                if (Mathf.Abs(lateral) < half) { reason = lane.name + " şeridinde (yanal " + lateral.ToString("F2") + " m)"; return false; }
            }
        }
        return true;
    }

    // Minibüsün ayak izi için değerlendirme; sonuç özellikleri test ve HUD için saklanır.
    public bool Evaluate(IEnumerable<Vector3> footprint)
    {
        var list = new List<Vector3>(footprint);
        LastInside = FullyInside(list);
        LastOutOfLanes = OutOfLanes(list, out string why);
        LastReason = !LastInside ? "minibüs park alanının dışında" : !LastOutOfLanes ? why : "iyi park";
        return LastInside && LastOutOfLanes;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(halfExtents.x * 2f, 0.1f, halfExtents.z * 2f));
    }
}
