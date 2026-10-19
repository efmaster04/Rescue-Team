using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// U4 (kullanıcı kararı 2026-10-19): minibüs bagaj kapısı İLK açıldığında tavan bagajındaki 2 valiz (SM_Minivan_RackSuitcase_01/02)
// tavandan kayıp minibüsün arkasına yuvarlanır (basit parabol animasyonu; fizik yok, deterministik), yere oturur ve U3 valiz
// kuralıyla LootItem olur (valiz 20 ₺ + sabit tohumlu içerik); LootRegistry toplamı yeniden hesaplar (%50 eşiği değişir).
// Minibüs hiyerarşisine valizler dışında dokunulmaz; valizler düşünce minibüsten ayrılır.
public class MinivanRoofLuggage : MonoBehaviour
{
    public MinivanTailgate tailgate;
    [Tooltip("Tavan bagajındaki valiz parçaları.")]
    public Transform[] suitcases = new Transform[0];
    public LootValues values;
    [Tooltip("İçerik tohumu: yerdeki valizler 0..N−1 kullanır; tavan valizleri bundan başlar.")]
    public int contentsSeedBase = 5;
    [Tooltip("Uçuş süresi (sn), tepe yüksekliği (m), arka tampondan geriye uzaklık (m), iki valiz arası yan açıklık (m).")]
    public float arcSeconds = 1.2f, arcHeight = 1.0f, behind = 2.6f, sideSpacing = 1.0f;

    public bool Dropped { get; private set; }
    public int Landed { get; private set; }
    public readonly List<LootItem> Items = new List<LootItem>();
    public event Action<LootItem> ItemLanded;

    private void Awake()
    {
        if (tailgate == null) tailgate = GetComponent<MinivanTailgate>();
    }

    private void Update()
    {
        if (!Dropped && tailgate != null && tailgate.IsOpen) StartDrop();
    }

    public void StartDrop()
    {
        if (Dropped) return;
        Dropped = true;
        for (int i = 0; i < suitcases.Length; i++)
            if (suitcases[i] != null) StartCoroutine(Fall(suitcases[i], i));
    }

    private IEnumerator Fall(Transform s, int index)
    {
        Vector3 vanUp = transform.up;
        s.SetParent(null, true);
        Vector3 startPos = s.position;
        Quaternion startRot = s.rotation;
        // İniş yeri: arka noktanın gerisinde, yanlara açılmış; zemin raycast'le (minibüs ve hastalar hariç)
        Vector3 back = tailgate != null ? tailgate.RearDirection : -transform.forward;
        Vector3 side = Vector3.Cross(Vector3.up, back);
        Vector3 rear = tailgate != null && tailgate.rearPoint != null ? tailgate.rearPoint.position : transform.position;
        Vector3 at = rear + back * (behind + index * 0.7f) + side * ((index % 2 == 0 ? -1f : 1f) * sideSpacing * 0.5f);
        Vector3 ground = at;
        bool found = false;
        foreach (var h in Physics.RaycastAll(at + Vector3.up * 3f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.transform.IsChildOf(transform) || h.transform == s || h.transform.IsChildOf(s) || GroundProbe.IsMovingGameObject(h.collider)) continue;
            if (!found || h.point.y > ground.y) { ground = h.point; found = true; }
        }
        if (!found && GroundProbe.Find(rear + back * 1.2f + Vector3.up * 2f, 8f, out RaycastHit g)) ground = g.point;
        // Son duruş: tavandaki "yukarı" ekseni dünya yukarısına, hafif rastgele yaw
        Quaternion level = Quaternion.FromToRotation(vanUp, Vector3.up) * startRot;
        Quaternion finalRot = Quaternion.AngleAxis(index == 0 ? -22f : 31f, Vector3.up) * level;
        // Tabanın yere oturması: son pozdaki sınırların alt kenarı
        s.SetPositionAndRotation(ground, finalRot);
        Bounds b = Bounds(s);
        Vector3 finalPos = ground + Vector3.up * (ground.y - b.min.y + 0.005f) + (s.position - b.center) * 0f;
        finalPos = new Vector3(ground.x + (s.position.x - b.center.x), finalPos.y, ground.z + (s.position.z - b.center.z));
        s.SetPositionAndRotation(startPos, startRot);
        Vector3 tumbleAxis = side;
        for (float t = 0f; t < arcSeconds; t += Time.deltaTime)
        {
            float u = Mathf.Clamp01(t / arcSeconds);
            Vector3 p = Vector3.Lerp(startPos, finalPos, u) + Vector3.up * (arcHeight * 4f * u * (1f - u));
            Quaternion r = Quaternion.AngleAxis(360f * u, tumbleAxis) * Quaternion.Slerp(startRot, finalRot, u);
            s.SetPositionAndRotation(p, r);
            yield return null;
        }
        s.SetPositionAndRotation(finalPos, finalRot);
        // Collider (parçada yok) ve LootItem
        if (s.GetComponentInChildren<Collider>() == null)
        {
            var mf = s.GetComponentInChildren<MeshFilter>();
            (mf != null ? mf.gameObject : s.gameObject).AddComponent<BoxCollider>();
        }
        Physics.SyncTransforms();
        var item = s.gameObject.AddComponent<LootItem>();
        item.displayName = "Valiz";
        item.value = values != null ? values.suitcaseValue : 20;
        item.kind = LootKind.Luggage;
        item.carryable = true;
        item.contents = values != null ? values.Roll(contentsSeedBase + index) : new List<LootValues.Entry>();
        Items.Add(item);
        Landed++;
        if (LootRegistry.Instance != null) LootRegistry.Instance.ComputeTotal();
        ItemLanded?.Invoke(item);
    }

    private static Bounds Bounds(Transform t)
    {
        var rs = t.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(t.position, Vector3.one * 0.3f);
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}
