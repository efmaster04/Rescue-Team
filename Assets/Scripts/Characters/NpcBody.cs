using System.Collections.Generic;
using UnityEngine;

// Animasyon sırasında kapalı ragdoll collider'larının yerine gerçek beden hacmi.
// Oyuncunun yatay süpürmesi düşük bedeni basamak saymaz; itme hiçbir zaman ragdoll kuralını değiştirmez.
[DefaultExecutionOrder(150)]
public class NpcBody : MonoBehaviour
{
    private static readonly List<NpcBody> all = new List<NpcBody>();
    public float pushSpeed = 0.45f;
    public float maxPatientPush = 0.75f;
    public float edgeClearance = 1f;
    public BoxCollider Body { get; private set; }
    private Patient patient;
    private StretcherHelperBot bot;
    private Vector3 pushOrigin;
    private bool wasActive;
    private float lastStagger = -10f;
    private Vector3 half;

    private bool Solid => patient != null ? patient.State == PatientState.LyingGround
        : bot != null && !bot.IsDown;

    private void Awake()
    {
        patient = GetComponent<Patient>();
        bot = GetComponent<StretcherHelperBot>();
        var go = new GameObject("BodyContact");
        go.transform.SetParent(transform, false);
        Body = go.AddComponent<BoxCollider>();
        Refresh();
    }
    private void OnEnable() { all.Add(this); }
    private void OnDisable() { all.Remove(this); if (Body != null) Body.enabled = false; }
    private void LateUpdate() { Refresh(); }
    public void ResetPushOrigin() { pushOrigin = transform.position; }

    public void Refresh()
    {
        bool active = Solid;
        Body.enabled = active;
        if (!active) { wasActive = false; return; }
        if (!wasActive) pushOrigin = transform.position;
        wasActive = true;
        Vector3 feet = patient != null ? patient.Position - Vector3.up * patient.hipsToBack : transform.position;
        Vector3 forward = Vector3.ProjectOnPlane(patient != null ? transform.up : transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
        half = patient != null ? new Vector3(.34f, .23f, .78f) : new Vector3(.30f, .75f, .30f);
        Body.transform.SetPositionAndRotation(feet + Vector3.up * half.y, Quaternion.LookRotation(forward));
        Body.size = half * 2f;
    }

    // Bedenin çevresi ve ilave 1 m zemine basmalı. Kenar, dik yüzey veya >35 cm basamak itmeye izin vermez.
    public bool HasSafeGround(Vector3 delta)
    {
        Vector3 center = Body.bounds.center + delta;
        float groundY = Body.bounds.min.y + delta.y;
        for (int i = 0; i < 16; i++)
        {
            float a = i * Mathf.PI / 8f;
            Vector3 local = new Vector3(Mathf.Cos(a) * (half.x + edgeClearance + .05f), 0f,
                Mathf.Sin(a) * (half.z + edgeClearance + .05f));
            Vector3 probe = center + Body.transform.rotation * local;
            probe.y = groundY + .4f;
            if (!GroundProbe.Find(probe, .75f, out var hit) || hit.normal.y < .707f) return false;
        }
        return true;
    }

    private void Push(Vector3 direction, float distance)
    {
        if (!Solid || (bot != null && (bot.HeldStretcher != null || bot.Pulling))) return; // halatı çeken bot itilmez (D2)
        Vector3 delta = direction * Mathf.Min(distance, pushSpeed * Time.deltaTime);
        if (patient != null)
        {
            Vector3 total = Vector3.ProjectOnPlane(transform.position + delta - pushOrigin, Vector3.up);
            if (total.magnitude > maxPatientPush || !HasSafeGround(delta)) return;
        }
        Vector3 basePoint = Body.bounds.center + delta;
        basePoint.y = Body.bounds.min.y;
        if (!GroundProbe.Find(basePoint + Vector3.up * .4f, .75f, out var ground)) return;
        delta.y = ground.point.y - Body.bounds.min.y;
        // Araç, sedye veya diğer bedenlerin içine itilemez.
        foreach (var c in Physics.OverlapBox(Body.bounds.center + delta, half * .95f, Body.transform.rotation, ~0, QueryTriggerInteraction.Ignore))
            if (!c.transform.IsChildOf(transform) && !(c is CharacterController) && GroundProbe.IsMovingGameObject(c)) return;
        transform.position += delta;
        if (bot != null && bot.crewAnimation != null && Time.time - lastStagger > .8f)
        {
            if (bot.animator != null) bot.animator.SetTrigger(CrewAnimation.TriggerStartled);
            lastStagger = Time.time;
        }
        Refresh();
        Physics.SyncTransforms();
    }

    public static Vector3 RestrictMotion(ThirdPersonPlayer player, Vector3 motion, float radius)
    {
        motion.y = 0f;
        foreach (var body in all)
        {
            body.Refresh();
            if (!body.Solid || motion.sqrMagnitude < 1e-10f) continue;
            var t = body.Body.transform;
            // Havada da bedenin üstüne adım atılamaz; başka kattaki beden engel değildir.
            float feetY = player.transform.position.y;
            if (feetY > body.Body.bounds.max.y + player.jumpHeight + .2f || feetY + 1.5f < body.Body.bounds.min.y) continue;
            Vector3 p = t.InverseTransformPoint(player.transform.position);
            Vector3 d = t.InverseTransformDirection(motion);
            Vector3 h = body.half + new Vector3(radius + .025f, 0f, radius + .025f);
            float enter = 0f, leave = 1f;
            bool hit = true;
            foreach (int axis in new[] { 0, 2 })
            {
                if (Mathf.Abs(d[axis]) < .000001f) { if (Mathf.Abs(p[axis]) > h[axis]) hit = false; continue; }
                float a = (-h[axis] - p[axis]) / d[axis], b = (h[axis] - p[axis]) / d[axis];
                enter = Mathf.Max(enter, Mathf.Min(a, b));
                leave = Mathf.Min(leave, Mathf.Max(a, b));
            }
            if (!hit || enter > leave || leave < 0f || enter > 1f) continue;
            // Başlangıçta temas varsa bedenden uzaklaşmaya izin ver.
            if (enter == 0f && p.x * d.x + p.z * d.z >= 0f) continue;
            body.Push(motion.normalized, motion.magnitude);
            motion *= Mathf.Max(0f, enter - .001f);
        }
        return motion;
    }
}
