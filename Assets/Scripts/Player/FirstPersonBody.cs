using System.Collections.Generic;
using UnityEngine;

// Birinci şahıs gövdesi (kullanıcı kararı 1, 2026-10-28): kamera başın içindeyken (OrbitCamera.HeadInsideCamera) baş kemiği
// hiddenScale'e küçültülür; tek SkinnedMesh'li Humanoid'de baş noktaya çöker, eller / bacaklar / sedye görünür kalır. Kamera
// baştan çıkınca ölçek geri gelir. Gölge kafalı kalır: aynı mesh ve kemiklerle ikinci bir SkinnedMeshRenderer (yalnız gölge)
// baş yerine ölçeklenmemiş vekil kemikleri kullanır; yalnız baş gizliyken açıktır, üçüncü şahısta hiçbir şey değişmez.
// LateUpdate 400: Animator, CrewAnimation (100) ve StretcherHandIK (300) pozu yazdıktan sonra; el / ayak IK'sı baş ölçeğinden
// etkilenmez (baş kemiği altında yalnız baş vardır). Yerdeyken (ragdoll; başta SphereCollider) ölçek hep 1.
// OrbitCamera hedefinde bu bileşen yoksa çalışma zamanında kendi ekler (sahne elle düzenlenmez).
[DefaultExecutionOrder(400)]
public class FirstPersonBody : MonoBehaviour
{
    [Tooltip("Gizli başın ölçeği (0 tekil matris verir; çok küçük bir değer başı noktaya çöker).")]
    public float hiddenScale = 0.0001f;
    [Tooltip("Baş gizliyken gölge için vekil kemikli ikinci renderer kurulsun mu.")]
    public bool keepHeadShadow = true;

    public bool HeadHidden { get; private set; }
    public Transform Head { get; private set; }
    // Tanı: gölge vekili kuruldu mu ve bu karede açık mı
    public SkinnedMeshRenderer ShadowProxy { get; private set; }

    private Animator animator;
    private ThirdPersonPlayer player;
    private OrbitCamera orbit;
    private Vector3 headScale = Vector3.one;
    private SkinnedMeshRenderer body;
    private readonly List<Transform> headBones = new List<Transform>();  // baş ve altındaki kemikler
    private readonly List<Transform> proxyBones = new List<Transform>(); // aynı sırayla vekilleri
    private bool proxyTried;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        player = GetComponent<ThirdPersonPlayer>();
        if (animator != null && animator.isHuman) Head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (Head != null) headScale = Head.localScale;
    }

    private OrbitCamera Orbit()
    {
        if (orbit != null && orbit.target == transform) return orbit;
        orbit = null;
        if (player != null && player.cameraTransform != null) orbit = player.cameraTransform.GetComponent<OrbitCamera>();
        if (orbit == null && Camera.main != null) orbit = Camera.main.GetComponent<OrbitCamera>();
        if (orbit != null && orbit.target != transform) orbit = null;
        return orbit;
    }

    private void LateUpdate()
    {
        if (Head == null) return;
        var o = Orbit();
        bool hide = o != null && o.enabled && o.HeadInsideCamera && !(player != null && player.IsDown);
        if (hide)
        {
            if (keepHeadShadow && !proxyTried) BuildShadowProxy();
            // Vekiller ölçeklenmemiş baş pozunu kopyalar (Animator bu karede dönüşü yazdı, ölçeğe dokunmaz)
            for (int i = 0; i < headBones.Count; i++)
            {
                var src = headBones[i];
                var dst = proxyBones[i];
                dst.SetLocalPositionAndRotation(src.localPosition, src.localRotation);
                dst.localScale = src == Head ? headScale : src.localScale;
            }
            Head.localScale = headScale * hiddenScale;
            if (ShadowProxy != null) ShadowProxy.enabled = true;
        }
        else
        {
            if (HeadHidden) Head.localScale = headScale;
            if (ShadowProxy != null) ShadowProxy.enabled = false;
        }
        HeadHidden = hide;
    }

    private void OnDisable()
    {
        if (Head != null && HeadHidden) Head.localScale = headScale;
        if (ShadowProxy != null) ShadowProxy.enabled = false;
        HeadHidden = false;
    }

    // Gölge vekili: gövde SkinnedMeshRenderer'ının kopyası (yalnız gölge); baş altındaki kemiklerin yerine aynı hiyerarşide
    // vekil Transform'lar (başın ebeveyni altında, "FP_HeadShadowProxy"). Tembel kurulur: FootGroundingIK gibi Awake'te
    // renderer sayan bileşenler etkilenmez.
    private void BuildShadowProxy()
    {
        proxyTried = true;
        body = null;
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (smr.sharedMesh == null || smr.bones == null) continue;
            bool usesHead = false;
            foreach (var b in smr.bones) if (b == Head) { usesHead = true; break; }
            if (usesHead) { body = smr; break; }
        }
        if (body == null) return;
        var map = new Dictionary<Transform, Transform>();
        Transform proxyRoot = new GameObject("FP_HeadShadowProxy").transform;
        proxyRoot.SetParent(Head.parent, false);
        proxyRoot.SetLocalPositionAndRotation(Head.localPosition, Head.localRotation);
        proxyRoot.localScale = headScale;
        Mirror(Head, proxyRoot, map);
        var bones = body.bones;
        var copy = new Transform[bones.Length];
        for (int i = 0; i < bones.Length; i++) copy[i] = bones[i] != null && map.TryGetValue(bones[i], out Transform p) ? p : bones[i];
        var go = new GameObject("FP_BodyShadow");
        go.transform.SetParent(body.transform.parent, false);
        go.layer = body.gameObject.layer;
        var proxy = go.AddComponent<SkinnedMeshRenderer>();
        proxy.sharedMesh = body.sharedMesh;
        proxy.sharedMaterials = body.sharedMaterials;
        proxy.bones = copy;
        proxy.rootBone = body.rootBone;
        proxy.localBounds = body.localBounds;
        proxy.quality = body.quality;
        proxy.updateWhenOffscreen = body.updateWhenOffscreen;
        proxy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        proxy.enabled = false;
        ShadowProxy = proxy;
    }

    // src altındaki her Transform için dst altında aynı adlı vekil; listeler aynı sırada doldurulur.
    private void Mirror(Transform src, Transform dst, Dictionary<Transform, Transform> map)
    {
        map[src] = dst;
        headBones.Add(src);
        proxyBones.Add(dst);
        for (int i = 0; i < src.childCount; i++)
        {
            var c = src.GetChild(i);
            if (c == dst) continue;
            var d = new GameObject(c.name).transform;
            d.SetParent(dst, false);
            d.SetLocalPositionAndRotation(c.localPosition, c.localRotation);
            d.localScale = c.localScale;
            Mirror(c, d, map);
        }
    }
}
