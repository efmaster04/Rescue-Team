using System.Collections.Generic;
using UnityEngine;

// U3: güvenli yer (teslim noktası) — çekici kasasının arka ucunda, yerden erişilebilir; zeminde hafif bir halka işareti.
// Oyuncu elindeki değerli eşyayla ≤ interactRange yakın ve yüzü dönükken "E: Güvenli yere koy" → LootRegistry.Secure.
// (Durakta bekleyen ambulansın arka kapısı da güvenli yerdir; onu PlayerInteraction ambulanstan okur.)
public class SafeDropPoint : MonoBehaviour
{
    public static readonly List<SafeDropPoint> All = new List<SafeDropPoint>();

    [Tooltip("Zemin halkası (görsel; vurgu hedefi).")]
    public Transform marker;
    [Tooltip("Teslim menzili: oyuncu kökünden halka merkezine yatay uzaklık (m).")]
    public float interactRange = 1.5f;

    public int Deliveries { get; private set; }

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    public Vector3 Center { get { return transform.position; } }
    public Transform HighlightTarget { get { return marker != null ? marker : transform; } }

    public bool InRange(Vector3 p)
    {
        Vector3 d = Center - p;
        float dy = d.y;
        d.y = 0f;
        return d.magnitude <= interactRange && Mathf.Abs(dy) <= 1.5f;
    }

    public void CountDelivery() { Deliveries++; }
}
