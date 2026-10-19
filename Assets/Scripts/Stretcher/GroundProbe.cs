using UnityEngine;

// Zemin bulma: bir noktanın altındaki yürünebilir yüzeyi raycast'le bulur.
// Hareketli oyun objeleri (taşıyıcılar, sedyeler, hastalar, ambulans) ve dinamik cisimler (koni, ragdoll) zemin sayılmaz.
public static class GroundProbe
{
    private static readonly RaycastHit[] hits = new RaycastHit[32];

    // from: ışının başladığı nokta (aranan zeminin üstünde olmalı). maxDistance: aşağı doğru en fazla.
    public static bool Find(Vector3 from, float maxDistance, out RaycastHit ground)
    {
        int n = Physics.RaycastNonAlloc(from, Vector3.down, hits, maxDistance, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        ground = default;
        for (int i = 0; i < n; i++)
        {
            var h = hits[i];
            if (h.distance >= best || IsMovingGameObject(h.collider)) continue;
            best = h.distance;
            ground = h;
        }
        return best < float.MaxValue;
    }

    // E4: a→b doğru parçasını statik bir collider kesiyor mu?
    public static bool LineBlocked(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < 1e-4f) return false;
        int n = Physics.RaycastNonAlloc(a, d / len, hits, len, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (!IsMovingGameObject(hits[i].collider)) return true;
        return false;
    }

    public static bool IsMovingGameObject(Collider c)
    {
        if (c is CharacterController) return true;
        var rb = c.attachedRigidbody;
        if (rb != null && !rb.isKinematic) return true;
        return c.GetComponentInParent<StretcherController>() != null
            || c.GetComponentInParent<Patient>() != null
            || c.GetComponentInParent<AmbulanceController>() != null
            || c.GetComponentInParent<LaneFollower>() != null      // trafik araçları
            || c.GetComponentInParent<PlayerKnockdown>() != null   // düşmüş oyuncunun ragdoll'u
            || c.GetComponentInParent<StretcherHelperBot>() != null // düşmüş botun ragdoll'u
            || c.GetComponentInParent<Thief>() != null;             // U4: hırsız gövdesi ve ragdoll'u
    }
}
