using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEditor;

public static partial class Bolum1AnimVerify
{
    private static GameObject TestBox(string name, Vector3 at, Vector3 size, Quaternion rotation)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetPositionAndRotation(at, rotation);
        go.transform.localScale = size;
        Physics.SyncTransforms();
        return go;
    }

    private static IEnumerator PhaseCorrections()
    {
        var bot = Bot;
        bot.assistEnabled = false;
        // Sahnedeki ilk yerleşim: her iki sap gerçekten yerden tutulabiliyor.
        Vector3 center = (Stretcher(0).transform.position + Stretcher(1).transform.position) * .5f;
        var truck = GameObject.Find(Bolum1SceneBuilder.TowTruckName);
        for (int i = 0; i < 2; i++)
        {
            var s = Stretcher(i);
            var box = s.GetComponent<BoxCollider>();
            bool grounded = GroundProbe.Find(s.transform.position + Vector3.up, 2f, out var g) && Mathf.Abs(s.transform.position.y - g.point.y) < .08f;
            var overlaps = Physics.OverlapBox(box.bounds.center, box.size * .48f, s.transform.rotation, ~0, QueryTriggerInteraction.Ignore)
                .Where(c => !c.transform.IsChildOf(s.transform)).Select(c => c.name).ToArray();
            TeleportPlayerToHandle(s, true);
            yield return Seconds(.2f);
            Interaction.Interact();
            bool grabbed = Player.HeldStretcher == s;
            if (grabbed) Interaction.Interact();
            Check("(düzeltme 8) sedye yerde, engelden ayrı, E ile erişilir " + i, grounded && grabbed && overlaps.Length == 0,
                "zemin " + grounded + ", tutuldu " + grabbed + ", çakışma " + string.Join(",", overlaps));
        }
        ParkCarriersOffLane();
        Vector3 rear = (center - truck.transform.position).normalized;
        yield return Capture("duzeltme_sedye_yeri", center + rear * 6f + Vector3.up * 4.5f + Vector3.Cross(Vector3.up, rear) * 4f,
            Vector3.Lerp(center, truck.transform.position, .35f), 55f);

        Vector3 origin = new Vector3(300f, 0f, 300f);
        var floor = TestBox("CorrectionsFloor", origin + Vector3.down * .5f, new Vector3(80f, 1f, 80f), Quaternion.identity);
        Transform oldCamera = Player.cameraTransform;
        Player.cameraTransform = null;
        Player.Teleport(origin, Quaternion.identity);
        yield return Seconds(.3f);
        Player.SetRunInput(true);
        Player.SetMoveInput(Vector2.up);
        yield return Seconds(.4f);
        Vector3 p0 = Player.transform.position;
        float t0 = Time.time;
        yield return Seconds(1f);
        float run = Flat(Player.transform.position - p0).magnitude / (Time.time - t0);
        bool running = Player.animator.GetCurrentAnimatorClipInfo(0).Any(c => c.clip.name.Contains("Running") && c.weight > .9f);
        Player.SetMoveInput(Vector2.zero);
        Check("(düzeltme 1) Shift koşusu 3.2 ± .05 ve Running", Mathf.Abs(run - 3.2f) <= .05f && running, run.ToString("F3") + " m/sn; Running " + running);

        var cone = Cone(3);
        Vector3 coneAt = cone.BasePoint;
        Player.HeldCone = cone;
        cone.PickUp(Player);
        yield return Seconds(1.3f);
        Player.SetMoveInput(Vector2.up);
        yield return Seconds(.5f);
        var meter = new FootSlideMeter(Player.animator, Player.transform);
        t0 = Time.time;
        while (Time.time - t0 < 2f) { meter.Sample(); yield return null; }
        var slides = meter.Slides();
        Check("(düzeltme 1) koniyle koşu, hıza uyumlu Carry_OneHand_Walk", PlayerAnim.InState(CrewAnimation.StateCarryCone)
            && Mathf.Abs(Player.HorizontalVelocity.magnitude - 3.2f) <= .05f && slides.Count > 1 && slides.Max() < .06f,
            Player.HorizontalVelocity.magnitude.ToString("F3") + " m/sn; kayma " + (slides.Count > 0 ? (slides.Max() * 100f).ToString("F2") : "ölçülemedi") + " cm/adım");
        Player.SetMoveInput(Vector2.zero);
        Player.HeldCone = null;
        cone.PlaceAt(coneAt, 0f);

        var s0 = Stretcher(0);
        var savedS = s0.transform.position;
        var savedR = s0.transform.rotation;
        s0.Teleport(origin, Quaternion.identity);
        Player.Teleport(origin - Vector3.forward * 1.6f, Quaternion.identity);
        s0.Grab(Player, false);
        bot.assistEnabled = true;
        bot.Assist(Player, s0, false);
        yield return Seconds(.6f);
        Player.SetMoveInput(Vector2.down);
        yield return Seconds(.4f);
        p0 = Player.transform.position;
        Vector3 b0 = bot.transform.position;
        t0 = Time.time;
        yield return Seconds(1f);
        float carry = Flat(Player.transform.position - p0).magnitude / (Time.time - t0);
        float botSpeed = Flat(bot.transform.position - b0).magnitude / (Time.time - t0);
        Check("(düzeltme 1) Shift + sedye 1.4, bot aynı hız", Mathf.Abs(carry - 1.4f) <= .05f && Mathf.Abs(botSpeed - carry) <= .05f,
            "oyuncu " + carry.ToString("F3") + ", bot " + botSpeed.ToString("F3"));
        Player.SetMoveInput(Vector2.zero);
        bot.ReleaseHeld(); s0.Release(Player);
        yield return Seconds(.6f);
        s0.Teleport(savedS, savedR);
        bot.assistEnabled = false;
        Player.SetRunInput(false);

        // Hasta kopyası: asıl hastaların yeri ve durumu testten etkilenmez.
        var patientGo = UnityEngine.Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1AmbulanceBuilder.PatientPrefabPath("Tourist")));
        var patient = patientGo.GetComponent<Patient>();
        patient.LieOnGround(origin + Vector3.forward * 3f, Vector3.forward, Vector3.up);
        yield return Seconds(.2f);
        var body = patient.GetComponent<NpcBody>();
        body.Refresh();
        Vector3 startPatient = patient.transform.position;
        Player.Teleport(origin + Vector3.forward * 1.3f, Quaternion.identity);
        Player.SetMoveInput(Vector2.up);
        t0 = Time.time;
        float maxY = Player.transform.position.y;
        while (Time.time - t0 < 4f) { maxY = Mathf.Max(maxY, Player.transform.position.y); yield return null; }
        float pushed = Flat(patient.transform.position - startPatient).magnitude;
        bool blocked = Player.transform.position.z < body.Body.bounds.min.z && maxY < .15f;
        Check("(düzeltme 2) hasta katı, üstüne çıkılmaz; itme ≤1 m", blocked && pushed > .02f && pushed <= 1f,
            "blok " + blocked + ", yükselme " + maxY.ToString("F3") + ", itme " + pushed.ToString("F3"));
        Player.SetMoveInput(Vector2.zero);
        // Kenar güvenliği: z=340'ta biten zeminde beden önünden 1 m mesafe korunur.
        patient.LieOnGround(origin + Vector3.forward * 38.1f, Vector3.forward, Vector3.up);
        body.Refresh();
        startPatient = patient.transform.position;
        Player.Teleport(origin + Vector3.forward * 36.4f, Quaternion.identity);
        Player.SetMoveInput(Vector2.up);
        yield return Seconds(3f);
        float edge = origin.z + 40f - body.Body.bounds.max.z;
        Check("(düzeltme 2) kenarda hasta itmesi kesilir", edge >= .99f && Flat(patient.transform.position - startPatient).magnitude <= 1f,
            "beden–kenar " + edge.ToString("F3") + " m");
        Player.SetMoveInput(Vector2.zero);
        UnityEngine.Object.Destroy(patientGo);
        bot.transform.position = origin + Vector3.forward * 3f;
        Player.Teleport(origin + Vector3.forward, Quaternion.identity);
        Player.SetMoveInput(Vector2.up);
        Vector3 botStart = bot.transform.position;
        maxY = 0f; t0 = Time.time;
        while (Time.time - t0 < 2f) { maxY = Mathf.Max(maxY, Player.transform.position.y); yield return null; }
        Check("(düzeltme 2) bot katı, üstüne çıkılmaz, sendeler", !bot.IsDown && Player.HasControl && maxY < .15f
            && Player.transform.position.z < bot.transform.position.z - .5f && bot.transform.position.z > botStart.z + .02f,
            "yükselme " + maxY.ToString("F3") + ", bot itme " + Vector3.Distance(botStart, bot.transform.position).ToString("F3"));
        Player.SetMoveInput(Vector2.zero);
        ParkBot();

        // Gerçek 60° collider: on saniye boyunca her kare zıplama isteği.
        var slope = TestBox("CorrectionsSlope60", origin + new Vector3(0f, 4.330127f, 5.5f), new Vector3(8f, .2f, 10f), Quaternion.Euler(-60f, 0f, 0f));
        Player.Teleport(origin + Vector3.forward * 2.55f, Quaternion.identity);
        yield return Seconds(.3f);
        float y0 = Player.transform.position.y;
        Player.SetMoveInput(Vector2.up);
        t0 = Time.time;
        while (Time.time - t0 < 10f) { Player.RequestJump(); yield return null; }
        Player.SetMoveInput(Vector2.zero);
        yield return Seconds(.7f);
        float gain = Player.transform.position.y - y0;
        Check("(düzeltme 5) 60° yamaca 10 sn zıplama ≤ .5 m", gain <= .5f && !Player.IsDown, "kalıcı yükselme " + gain.ToString("F3") + " m");
        UnityEngine.Object.Destroy(slope);
        yield return null;

        // Yüksek bir platformdan yürüyerek ayrıl: kayıtlı son basış 4 m.
        var platform = TestBox("CorrectionsDrop4", origin + new Vector3(0f, 3.5f, -5f), new Vector3(4f, 1f, 2f), Quaternion.identity);
        Player.Teleport(origin + new Vector3(0f, 4.02f, -5f), Quaternion.identity);
        yield return Seconds(.4f);
        Player.SetMoveInput(Vector2.up);
        yield return Until(() => Player.IsDown, 5f, "4 m düşüş ragdoll");
        Check("(düzeltme 6) >3 m iniş ragdoll", Player.IsDown && Player.knockdown.ragdollBodies.All(b => !b.isKinematic),
            "son basıştan düşüş " + Player.LastLandingDrop.ToString("F3") + " m");
        Player.RespawnAt(origin, Quaternion.identity);
        UnityEngine.Object.Destroy(platform);
        var ramp = TestBox("CorrectionsRamp20", origin + new Vector3(0f, 2.736f, -10f), new Vector3(5f, .2f, 16f), Quaternion.Euler(-20f, 0f, 0f));
        Vector3 top = origin + new Vector3(0f, 0f, -3.2f);
        GroundProbe.Find(top + Vector3.up * 10f, 15f, out var topHit);
        Player.Teleport(topHit.point + Vector3.up * .02f, Quaternion.identity);
        yield return Seconds(.3f);
        Player.SetMoveInput(Vector2.down);
        yield return Seconds(7f);
        Player.SetMoveInput(Vector2.zero);
        Check("(düzeltme 6) 20° rampada >3 m yürüyerek iniş ragdoll değil", !Player.IsDown && topHit.point.y - Player.transform.position.y > 3f,
            "iniş " + (topHit.point.y - Player.transform.position.y).ToString("F3") + " m, ragdoll " + Player.IsDown);
        UnityEngine.Object.Destroy(ramp);
        Player.cameraTransform = oldCamera;
        ParkCarriersOffLane();
        UnityEngine.Object.Destroy(floor);
        yield return PhaseStoppedVehicle();

        var lane = Traffic.eastBound;
        float laneS = lane.Length * .45f;
        Vector3 roadPoint = lane.PointAt(laneS);
        Vector3 along = Flat(lane.TangentAt(laneS)).normalized;
        Player.Teleport(roadPoint + Vector3.up * .02f, Quaternion.LookRotation(along));
        Player.cameraTransform = null;
        Player.SetRunInput(true);
        Player.SetMoveInput(new Vector2(along.x, along.z));
        yield return Seconds(.5f);
        Vector3 atRun = Player.transform.position;
        yield return Capture("duzeltme_kosma", atRun + Vector3.Cross(Vector3.up, along) * 3f - along * 2f + Vector3.up * 1.6f,
            atRun + Vector3.up * .75f, 50f);
        Player.SetMoveInput(Vector2.zero);
        Player.SetRunInput(false);
        Player.cameraTransform = oldCamera;
        ParkCarriersOffLane();
    }

    private static IEnumerator PhaseStoppedVehicle()
    {
        var v = Traffic.ForceSpawn(true, TrafficVehicleType.Sedan);
        var lane = Traffic.eastBound;
        v.enabled = false; // Hızı ve pozu test belirler; gerçek gövde ve temas kodu çalışır.
        float s = lane.Length * .45f;
        var contact = typeof(TrafficVehicle).GetMethod("HitContacts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Transform cam = Player.cameraTransform;
        Player.cameraTransform = null;
        foreach (float speed in new[] { 0f, .5f, 1f, 1.1f })
        {
            v.Follower.Place(lane, s, speed);
            Vector3 forward = Flat(v.transform.forward).normalized;
            Vector3 front = v.transform.TransformPoint(v.bodyCenter + Vector3.forward * v.bodySize.z * .5f);
            front.y = v.transform.position.y + .02f;
            Player.RespawnAt(front + forward * .5f, Quaternion.LookRotation(-forward));
            Player.SetMoveInput(new Vector2(-forward.x, -forward.z));
            float t = Time.time;
            while (Time.time - t < 1f && !Player.IsDown) { contact.Invoke(v, null); yield return null; }
            bool down = Player.IsDown;
            float separation = Vector3.Dot(Player.transform.position - front, forward);
            Check("(düzeltme 4) ön tampon " + speed + " m/sn", down == (speed > 1f) && (down || separation >= .20f),
                "düştü " + down + ", tampon uzaklığı " + separation.ToString("F3") + " m, yaklaşma " + v.ApproachSpeed(Player.transform.position));
            Player.SetMoveInput(Vector2.zero);
        }
        Player.RespawnAt(parkPosition, Quaternion.identity);
        var testPatientGo = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Bolum1AmbulanceBuilder.PatientPrefabPath("Tourist")));
        var testPatient = testPatientGo.GetComponent<Patient>();
        foreach (float speed in new[] { 0f, 1.1f })
        {
            v.Follower.Place(lane, s, speed);
            Vector3 front = v.transform.TransformPoint(v.bodyCenter + Vector3.forward * (v.bodySize.z * .5f + .2f));
            front.y = v.transform.position.y;
            Bot.transform.position = front;
            testPatient.LieOnGround(front, Vector3.right, Vector3.up);
            Physics.SyncTransforms();
            contact.Invoke(v, null);
            bool expected = speed > 1f;
            Check("(düzeltme 4) bot/hasta " + speed + " m/sn", Bot.IsDown == expected && (testPatient.State == PatientState.Ragdoll) == expected,
                "bot " + Bot.IsDown + ", hasta " + testPatient.State);
            if (!expected) yield return null;
        }
        UnityEngine.Object.Destroy(testPatientGo);
        Player.cameraTransform = cam;
        Traffic.ClearVehicles();
        yield return Seconds(Bot.getUpDelay + 2f);
        ParkBot();
    }
}
