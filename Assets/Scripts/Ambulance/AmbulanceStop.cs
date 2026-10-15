using UnityEngine;

// Ambulans durağı işareti (kullanıcı kararı 2). Sahnede taşınabilir: ambulansın kök pivotu
// (tekerlek dikdörtgeninin merkezi) bu objenin şeride izdüşümünde durur.
public class AmbulanceStop : MonoBehaviour
{
    [Tooltip("Gizmo için ambulans boyu (m) ve kökten arka tampona uzaklık (m).")]
    public float vehicleLength = 6f;
    public float rearOverhang = 3f;

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.5f, 1f, 0.6f);
        Gizmos.matrix = transform.localToWorldMatrix;
        float front = vehicleLength - rearOverhang;
        Gizmos.DrawWireCube(new Vector3(0f, 1.3f, (front - rearOverhang) * 0.5f), new Vector3(2f, 2.6f, vehicleLength));
        Gizmos.DrawLine(new Vector3(0f, 0f, -rearOverhang), new Vector3(0f, 0f, -rearOverhang - 1.2f)); // yükleme noktası yönü
    }
}
