using System;
using System.Collections.Generic;
using UnityEngine;

// U3: değer taşıyan sahne eşyası. Üç tür:
//  - Luggage (valiz/çanta prop'u): tek elle taşınır (Carry_OneHand_Walk, koni kuralı), güvenli yere konunca valiz + içindekiler
//    tek tek para yazar.
//  - PatientItem (hastanın değerli eşyası, koyu kutu yer tutucu): hastanın kökünde, karnında durur; hasta yerde ya da sedyedeyken
//    alınır. Hasta eşyasıyla ambulansa binerse eşya kendiliğinden güvende sayılır.
//  - Equipment (sedye, halat kangalı, koni; sağlık çantası sahnede yoksa yok): taşınmaz, para kazandırmaz; çalınırsa − yazılır.
// Çalınma ve güvenlik: LootRegistry.ReportStolen / Secure. Güvenli yere konmuş eşya çalınamaz (IsSecured).
public enum LootKind { Luggage, PatientItem, Equipment }

[DefaultExecutionOrder(60)] // taşıyıcı hareket ettikten sonra elde konumlanır (TrafficCone ile aynı)
public class LootItem : MonoBehaviour
{
    public static readonly List<LootItem> All = new List<LootItem>();

    public enum LootState { AtStart, Held, Dropped, Secured, Stolen }

    [Tooltip("Ekranda görünen ad (\"Valiz\", \"Kamera\", \"Sedye\").")]
    public string displayName = "Eşya";
    [Tooltip("Eşyanın kendi değeri (₺).")]
    public int value;
    public LootKind kind = LootKind.Luggage;
    [Tooltip("Valiz içindekiler (ad + değer); güvenli yere konunca tek tek yazılır.")]
    public List<LootValues.Entry> contents = new List<LootValues.Entry>();
    [Tooltip("Taşınabilir mi (valiz, hasta eşyası).")]
    public bool carryable = true;
    [Tooltip("Hasta eşyasıysa sahibi.")]
    public Patient patient;
    [Tooltip("Alma menzili: oyuncu kökünden eşya merkezine yatay uzaklık (m).")]
    public float interactRange = 1.5f;

    public LootState State { get; private set; } = LootState.AtStart;
    public ThirdPersonPlayer Holder { get; private set; }
    public bool IsHeld { get { return Holder != null; } }
    public bool IsSecured { get { return State == LootState.Secured; } }
    public bool IsStolen { get { return State == LootState.Stolen; } }
    // Toplam değer: kendi + içindekiler (çalınan çubuğu ve güvenli yer bunu sayar)
    public int TotalValue
    {
        get
        {
            int t = value;
            foreach (var c in contents) if (c != null) t += c.value;
            return t;
        }
    }
    // Alınabilir mi: taşınabilir, güvende/çalınmış değil, elde değil; hasta eşyası yalnız hasta yerde ya da sedyedeyken
    public bool CanPickUp
    {
        get
        {
            if (!carryable || State == LootState.Secured || State == LootState.Stolen || Holder != null || !gameObject.activeInHierarchy) return false;
            if (patient != null && State == LootState.AtStart)
                return patient.State == PatientState.LyingGround || patient.State == PatientState.OnStretcher;
            return true;
        }
    }

    private Collider[] colliders;
    private Transform startParent;
    private Vector3 startLocalPosition;
    private Quaternion startLocalRotation;
    private Quaternion startRotation;
    private float bottomOffset;
    private bool startKnown;

    private void Awake()
    {
        colliders = GetComponentsInChildren<Collider>(true);
        RememberStart();
    }

    private void RememberStart()
    {
        if (startKnown) return;
        startKnown = true;
        startParent = transform.parent;
        startLocalPosition = transform.localPosition;
        startLocalRotation = transform.localRotation;
        startRotation = transform.rotation;
        // Pivotun tabandan yüksekliği: valiz dik durur (dünya sınırlarından); hasta kutusu yatan hastaya bağlı (yerel sınırlardan)
        var r = GetComponentInChildren<Renderer>(true);
        if (kind == LootKind.PatientItem || r == null) bottomOffset = r != null ? r.localBounds.extents.y * transform.lossyScale.y : 0.03f;
        else bottomOffset = Mathf.Max(0f, transform.position.y - WorldBounds.min.y);
    }

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        if (patient != null) patient.StateChanged += OnPatientState;
    }

    private void OnDisable()
    {
        All.Remove(this);
        if (patient != null) patient.StateChanged -= OnPatientState;
    }

    // Hasta eşyasıyla ambulansa bindi: eşya güvende (para yazılır)
    private void OnPatientState(Patient p)
    {
        if (State != LootState.AtStart || Holder != null) return;
        if (p.State == PatientState.InAmbulance && LootRegistry.Instance != null) LootRegistry.Instance.Secure(this);
    }

    public Bounds WorldBounds
    {
        get
        {
            var rs = GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(transform.position, Vector3.one * 0.1f);
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }
    public Vector3 Center { get { return WorldBounds.center; } }

    // Oyuncu yakın ve yüzü dönük mü (U3 yüz kuralı: PlayerInteraction'ın açısıyla)
    public bool InRange(Vector3 p)
    {
        Vector3 d = Center - p;
        float dy = d.y;
        d.y = 0f;
        return d.magnitude <= interactRange && Mathf.Abs(dy) <= 1.8f;
    }

    // ---- taşıma (yalnız PlayerInteraction ve ThirdPersonPlayer çağırır) ----
    public void PickUp(ThirdPersonPlayer by)
    {
        Holder = by;
        State = LootState.Held;
        transform.SetParent(null, true);
        foreach (var c in colliders) if (c != null) c.enabled = false;
        FollowHolder();
    }

    // Verilen zemine bırakılır (dik; yatay dönüşü yaw).
    public void Drop(Vector3 groundPoint, float yaw)
    {
        Holder = null;
        State = LootState.Dropped;
        Quaternion rot = kind == LootKind.PatientItem ? Quaternion.Euler(0f, yaw, 0f) : Quaternion.AngleAxis(yaw - startRotation.eulerAngles.y, Vector3.up) * startRotation;
        transform.SetPositionAndRotation(groundPoint + Vector3.up * (bottomOffset + 0.005f), rot);
        foreach (var c in colliders) if (c != null) c.enabled = true;
        Physics.SyncTransforms();
    }

    // Elden düşer (çarpılma / can 0 / yeniden doğma): taşıyanın önündeki zemine.
    public void DropNear(Vector3 from, Vector3 forward)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
        forward.Normalize();
        Vector3 at = from + forward * 0.6f;
        if (!GroundProbe.Find(at + Vector3.up * 1.5f, 5f, out RaycastHit g)) { at = from; if (!GroundProbe.Find(at + Vector3.up * 1.5f, 5f, out g)) g.point = from; }
        Drop(g.point, Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg);
    }

    // Güvenli yere kondu: kaybolur (kasaya/ambulansa yerleşmiş sayılır).
    public void Secure()
    {
        Holder = null;
        State = LootState.Secured;
        gameObject.SetActive(false);
    }

    // Çalındı (U4: hırsız taşıyacak; şimdilik kaybolur).
    public void MarkStolen()
    {
        Holder = null;
        State = LootState.Stolen;
        // Taşınabilir eşya kaybolur (U4: hırsız taşıyacak); ekipman (sedye, kangal, koni) sahnede kalır, yalnız değeri sayılır
        if (carryable) gameObject.SetActive(false);
    }

    // Test: başlangıç durumuna döner.
    public void ResetToStart()
    {
        Holder = null;
        State = LootState.AtStart;
        gameObject.SetActive(true);
        transform.SetParent(startParent, false);
        transform.SetLocalPositionAndRotation(startLocalPosition, startLocalRotation);
        foreach (var c in colliders) if (c != null) c.enabled = true;
        Physics.SyncTransforms();
    }

    // Elde: tek elle, taşıyanın sağ önünde bel hizasında (koni gibi; Carry_OneHand_Walk).
    private void FollowHolder()
    {
        if (Holder == null) return;
        Transform r = Holder.transform;
        Vector3 fwd = r.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        Quaternion rot = kind == LootKind.PatientItem ? Quaternion.Euler(0f, yaw, 0f) : Quaternion.AngleAxis(yaw - startRotation.eulerAngles.y, Vector3.up) * startRotation;
        Vector3 basePos = r.position + fwd * 0.38f + right * 0.28f + Vector3.up * 0.42f;
        transform.SetPositionAndRotation(basePos, rot);
    }

    private void LateUpdate()
    {
        if (Holder != null) FollowHolder();
    }
}
