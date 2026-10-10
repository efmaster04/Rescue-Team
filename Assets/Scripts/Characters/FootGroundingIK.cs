using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ayakları zemine "yapıştırır": animasyon botları zemine gömse bile ayak tabanını zemin yüzeyine oturtur.
///
/// NEDEN: Karakterin botları Mixamo iskeletine göre çok büyük olduğu için, yürüme/koşma kliplerinde
/// topuk ve burun zemine giriyor (ve bot gereğinden fazla öne eğik duruyor). Modele/kliplere dokunmadan,
/// oyun çalışırken her kare ayağı raycast ile bulunan zemine göre düzeltiyoruz.
/// (IK = "ayağın gitmesi gereken yeri söyle, bacak ona uysun".)
///
/// KURULUM: Animator'ın bulunduğu objeye eklenir. Animator Controller'da Base Layer'ın
/// "IK Pass" kutusu açık olmalı; yoksa OnAnimatorIK hiç çağrılmaz.
///
/// NASIL ÇALIŞIR:
///  Awake'te (animasyon henüz çalışmadan, karakter bağ pozundayken) botun "düz basmış" halini kaydederiz:
///  bileğin tabandan yüksekliği ve taban örnek noktaları. Sabit sayı yok; model değişirse kendiliğinden uyar.
///  Her kare:
///   1) Her ayağın altına ışın at; tabanın en alçak noktasının zemine göre yüksekliğini (clearance) ölç.
///   2) Taban zemine yakınsa (destek fazı) IK ağırlığı 1, havadaysa (salınım fazı) 0 olur; arası yumuşak geçiş.
///   3) Tüm vücut, destek ayağının bileğinin gitmesi gereken mesafe kadar kayar (iki ayak havadaysa kayma 0'a döner).
///      Neden tüm vücut: klibin salınan ayağı da aynı oranda gömülü; sadece destek ayağını kaldırırsak
///      salınan ayak yere yapışır. Neden bilek kadar: vücut ve bilek aynı miktarda kayınca kalça-bilek mesafesi,
///      yani bacağın gerilmesi, animasyondakiyle aynı kalır; ayak zemine hep yetişir.
///   4) Destek fazında ayak "düz basmış" haline (bağ pozundaki dönüş + zemin eğimi) çekilir;
///      salınımda yalnızca zemine girmesi engellenir, animasyonun kaldırışı aynen korunur.
/// </summary>
[RequireComponent(typeof(Animator))]
public class FootGroundingIK : MonoBehaviour
{
    [Header("Zemin")]
    [SerializeField, Tooltip("Hangi katmanlar zemin sayılsın? Karakterin kendi collider'ı varsa onu hariç tut.")]
    private LayerMask groundMask = ~0;

    [SerializeField, Tooltip("Işın, ayağın animasyondaki konumunun kaç metre ÜSTÜNDEN başlasın? Rampada ayak zeminin içindeyse ışının zemini kaçırmaması için gerekli.")]
    private float rayStartHeight = 0.5f;

    [SerializeField, Tooltip("Işın, ayağın kaç metre ALTINA kadar zemin arasın? Bundan derin boşlukta IK devreye girmez (uçurum kenarı).")]
    private float rayDownDistance = 0.6f;

    [SerializeField, Range(0f, 60f), Tooltip("Zemin eğimi bu dereceden dikse yüzey zemin sayılmaz, düz zemin gibi davranılır (duvar/basamak kenarı).")]
    private float maxGroundAngle = 45f;

    [Header("Ayak")]
    [SerializeField, Tooltip("Bileğin tabandan yüksekliği (metre). 0 = bağ pozundan otomatik al (önerilen). Model değişince kendiliğinden yeniden hesaplanır. Elle girersen o değer kullanılır.")]
    private float ankleHeightOverride = 0f;

    [SerializeField, Tooltip("Bağ pozunda zeminden bu kadar (metre) yüksekliğe kadar olan köşeler 'taban' sayılır (burun ucu/topuk bulmak için). Yalnız FBX Read/Write açıksa kullanılır.")]
    private float soleDetectHeight = 0.015f;

    [SerializeField, Tooltip("Taban boyutu: bileğin önünde botun ucu kaç metre ileride? Yalnız mesh okunamıyorsa (FBX Read/Write kapalı) kullanılır. Model değişince FootGroundingMeasure.SoleExtents ile yeniden ölç.")]
    private float soleToeForward = 0.156f;

    [SerializeField, Tooltip("Taban boyutu: bileğin arkasında topuk kaç metre geride? (Mesh okunamıyorsa kullanılır.)")]
    private float soleHeelBack = 0.097f;

    [SerializeField, Tooltip("Taban boyutu: bileğin iki yanında taban yarım genişliği (metre). (Mesh okunamıyorsa kullanılır.)")]
    private float soleHalfWidth = 0.086f;

    [SerializeField, Tooltip("Taban boyutu: taban izinin merkezi bileğin kaç metre DIŞINDA? (Bot bilekle ortalı olmayabilir. Mesh okunamıyorsa kullanılır.)")]
    private float soleCenterOffset = 0.031f;

    [SerializeField, Tooltip("Tabanla zemin arasında bırakılacak güvenlik payı (metre). Botun zemine hafifçe gömülmemesi için 0.005 gibi küçük bir değer verilebilir (örnek noktalar mesh köşelerinden biraz seyrek olduğu için 0.01 öneriliyor); negatif değer botu gömer.")]
    private float soleLift = 0.01f;

    [SerializeField, Tooltip("Ayağın tabanı, iki ayağın en alçağından bu kadar (metre) veya daha az yüksekteyse IK tam ağırlıkta (ayak 'yere basıyor'). En alçak ayak için zemine göre de aynı eşik geçerli.")]
    private float fullWeightClearance = 0.01f;

    [SerializeField, Tooltip("Ayağın tabanı en alçak ayaktan (veya en alçak ayak zeminden) bu kadar (metre) yüksekteyken IK tamamen kapanır (salınım fazı: ayak yere yapışmaz). Tam ve sıfır arası yumuşak geçiş olur. Klibin ayak kaldırışı küçükse düşür.")]
    private float zeroWeightClearance = 0.05f;

    [SerializeField, Tooltip("Ayağı en fazla bu kadar (metre) yukarı çeker. Tuhaf sıçramaları engeller.")]
    private float maxRaise = 0.25f;

    [SerializeField, Tooltip("Ayağı en fazla bu kadar (metre) aşağı çeker.")]
    private float maxLower = 0.25f;

    [SerializeField, Range(0f, 1f), Tooltip("Ayağı düz basmış (zemin eğimine paralel) hale getirme oranı. 0 = animasyondaki ayak eğimi aynen kalır.")]
    private float flattenWeight = 1f;

    [Header("Gövde (pelvis)")]
    [SerializeField, Tooltip("Gövde en fazla bu kadar (metre) yukarı kayabilir. Klip botları ne kadar gömüyorsa o kadar gerekir (koşuda en çok).")]
    private float maxBodyRaise = 0.25f;

    [SerializeField, Tooltip("Gövde en fazla bu kadar (metre) aşağı kayabilir (iniş, yokuş aşağı).")]
    private float maxBodyDrop = 0.25f;

    [SerializeField, Tooltip("Gövdenin AŞAĞI dönüş yumuşaklığı (saniye; yukarı kayma anidir). Küçük = hızlı, büyük = yavaş ve pürüzsüz.")]
    private float bodySmoothTime = 0.05f;

    [SerializeField, Range(0f, 1f), Tooltip("Geri besleme gücü: IK sonrası kalan gömülmenin her karede ne kadarı düzeltilsin. 0 = geri besleme kapalı.")]
    private float residualGain = 0.8f;

    [SerializeField, Tooltip("Geri beslemenin ayağa ekleyebileceği en fazla yükseklik (metre).")]
    private float maxResidual = 0.1f;

    [Header("Genel")]
    [SerializeField, Range(0f, 1f), Tooltip("Tüm IK'nın genel gücü. 0 = kapalı (animasyon olduğu gibi).")]
    private float masterWeight = 1f;

    // Bağ pozundan kaydedilen, ayak başına referans bilgisi.
    private class FootRig
    {
        public AvatarIKGoal goal;
        public Transform bone;
        public Transform toeBone;
        public HumanBodyBones toeId;
        public Quaternion restToeLocalRotation; // bağ pozundaki parmak (toe) yerel dönüşü
        public Quaternion restRotationInRoot;   // düz basmış ayağın karakter köküne göre dönüşü
        public Transform lowerLeg;              // alt bacak kemiği: ayağın doğal yönünü o belirler
        public Quaternion restFootInLeg;        // bağ pozunda ayağın alt bacağa göre dönüşü
        public Vector3 restToeDirLocal;         // bağ pozunda bilekten toe'ya yön (ayak kemiği yerelinde)
        public float restHeadingYaw;            // bağ pozunda bilekten toe'ya yatay yönün karakter önüne göre açısı
        public Transform[] soleBones;           // taban örnek noktalarının bağlı olduğu kemikler
        public Vector3[] soleLocals;            // taban örnek noktaları (kendi kemiklerinin yerelinde)
        public bool goalCalibrated;             // IK hedef-kemik dönüş farkı hesaplandı mı
        public Quaternion goalToBone;           // kemik dönüşü = IK hedef dönüşü * goalToBone (avatara özgü sabit)
        public float residual;                  // IK sonrası ölçülen kalan gömülmeyi gidermek için eklenen yükseklik (metre)
        public float lastWeight;                // bu karedeki destek ağırlığı (LateUpdate geri bildirimi için)
        public float ankleHeight;               // bilek tabandan kaç metre yukarıda
    }

    // Bir ayağın animasyondaki durumu ve altındaki zemin (raycast sonucu).
    private struct FootProbe
    {
        public bool hasGround;
        public Vector3 animPos;      // animasyonun koyduğu bilek konumu
        public Quaternion animRot;   // animasyonun koyduğu ayak kemiği dönüşü
        public Vector3 point;        // zemin vuruş noktası
        public Vector3 normal;       // zemin yüzey normali
        public float clearance;      // tabanın en alçak noktasının zeminden yüksekliği (negatif = gömülü)
        public float weight;         // destek ağırlığı 0..1 (clearance'tan)
        public float ankleLift;      // düz basmak için bileğin zemin normali boyunca gitmesi gereken mesafe (+ yukarı)
    }

    private Animator animator;
    private FootRig leftFoot, rightFoot;
    private float bodyOffset;          // şu an uygulanan gövde kayması (+ yukarı)
    private float bodyOffsetVelocity;  // SmoothDamp için iç değişken

    private void Awake()
    {
        animator = GetComponent<Animator>();
        leftFoot = BuildRig(AvatarIKGoal.LeftFoot, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, HumanBodyBones.LeftLowerLeg);
        rightFoot = BuildRig(AvatarIKGoal.RightFoot, HumanBodyBones.RightFoot, HumanBodyBones.RightToes, HumanBodyBones.RightLowerLeg);
    }

    // Karakter bağ pozundayken (kök zeminde, taban düz) ayağın referans değerlerini kaydeder.
    private FootRig BuildRig(AvatarIKGoal goal, HumanBodyBones footId, HumanBodyBones toeId, HumanBodyBones legId)
    {
        Transform foot = animator.GetBoneTransform(footId);
        Transform toe = animator.GetBoneTransform(toeId);
        Transform leg = animator.GetBoneTransform(legId);
        if (foot == null || toe == null || leg == null) return null;

        float ankleHeight = ankleHeightOverride > 0f
            ? ankleHeightOverride
            : Vector3.Dot(foot.position - transform.position, transform.up);

        // Taban örnek noktaları. Önce yedek olarak bilek ve toe kemiğinin tabana izdüşümü (bağ pozunda taban düz zemindedir).
        var soleBones = new List<Transform>();
        var soleLocals = new List<Vector3>();
        soleBones.Add(foot); soleLocals.Add(foot.InverseTransformPoint(foot.position - transform.up * ankleHeight));
        float toeHeight = Vector3.Dot(toe.position - transform.position, transform.up);
        soleBones.Add(toe); soleLocals.Add(toe.InverseTransformPoint(toe.position - transform.up * toeHeight));

        int before = soleBones.Count;
        AddSoleExtremes(foot, toe, soleBones, soleLocals);
        if (soleBones.Count == before)
        {
            // Mesh okunamadı: tabanın boyutunu Inspector'daki değerlerden al (bağ pozunda, bileğe göre).
            Vector3 groundAnkle = foot.position - transform.up * ankleHeight;
            Vector3 fwd = transform.forward, right = transform.right;
            // Taban izi bir elips gibi kabul edilir (bot ucu ve topuk yuvarlak). Elips çevresinde 12 nokta alırız:
            // bot yana yatınca/öne eğilince en alçak nokta bu çevrede olur. Merkez bilekten biraz dışa kaymıştır.
            float side = Vector3.Dot(foot.position - transform.position, transform.right) >= 0f ? 1f : -1f; // + sağ, - sol ayak
            Vector3 center = groundAnkle + transform.right * (side * soleCenterOffset);
            float centerFwd = (soleToeForward - soleHeelBack) * 0.5f;
            float semiLength = (soleToeForward + soleHeelBack) * 0.5f;
            float toeBoneFwd = Vector3.Dot(toe.position - foot.position, transform.forward); // bundan ilerisi toe kemiğine bağlanır
            for (int k = 0; k < 12; k++)
            {
                float angle = k * Mathf.PI * 2f / 12f;
                float f = centerFwd + semiLength * Mathf.Cos(angle);
                Vector3 p = center + transform.forward * f + transform.right * (soleHalfWidth * Mathf.Sin(angle));
                Transform owner = f > toeBoneFwd ? toe : foot;
                soleBones.Add(owner); soleLocals.Add(owner.InverseTransformPoint(p));
            }
        }

        return new FootRig
        {
            goal = goal,
            bone = foot,
            toeBone = toe,
            toeId = toeId,
            restToeLocalRotation = toe.localRotation,
            restRotationInRoot = Quaternion.Inverse(transform.rotation) * foot.rotation,
            restHeadingYaw = HeadingYaw(toe.position - foot.position),
            lowerLeg = leg,
            restFootInLeg = Quaternion.Inverse(leg.rotation) * foot.rotation,
            restToeDirLocal = foot.InverseTransformDirection(toe.position - foot.position),
            soleBones = soleBones.ToArray(),
            soleLocals = soleLocals.ToArray(),
            ankleHeight = ankleHeight
        };
    }

    // Bir yönün yatay izdüşümünün, karakterin önüne göre açısı (derece, yukarı eksen etrafında).
    private float HeadingYaw(Vector3 direction)
    {
        Vector3 dir = Vector3.ProjectOnPlane(direction, transform.up);
        return dir.sqrMagnitude > 1e-8f ? Vector3.SignedAngle(transform.forward, dir, transform.up) : 0f;
    }

    // Mesh okunabiliyorsa, bu ayağa bağlı ve bağ pozunda tabana yakın köşelerin uç noktalarını (öne, arkaya, sola, sağa)
    // örnek nokta olarak ekler. Neden: botlar çok büyük; burun ucu veya topuk, kemik noktalarından çok daha aşağıya gömülür.
    private void AddSoleExtremes(Transform foot, Transform toe, List<Transform> bones, List<Vector3> locals)
    {
        var best = new float[4] { float.MinValue, float.MinValue, float.MinValue, float.MinValue }; // ön, arka, sol, sağ
        var bestBone = new Transform[4];
        var bestWorld = new Vector3[4];
        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            Mesh mesh = smr.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue; // okunamıyorsa yedek noktalarla devam
            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            Transform[] smrBones = smr.bones;
            if (weights.Length != vertices.Length) continue;
            for (int i = 0; i < vertices.Length; i++)
            {
                Transform b = smrBones[weights[i].boneIndex0];
                if (b != foot && b != toe) continue;
                Vector3 world = smr.transform.TransformPoint(vertices[i]); // bağ pozunda köşenin dünya konumu
                Vector3 rel = world - transform.position;
                if (Vector3.Dot(rel, transform.up) > soleDetectHeight) continue; // sadece tabana yakın köşeler
                float f = Vector3.Dot(rel, transform.forward), r = Vector3.Dot(rel, transform.right);
                float[] score = { f, -f, -r, r };
                for (int k = 0; k < 4; k++)
                    if (score[k] > best[k]) { best[k] = score[k]; bestBone[k] = b; bestWorld[k] = world; }
            }
        }
        for (int k = 0; k < 4; k++)
            if (bestBone[k] != null) { bones.Add(bestBone[k]); locals.Add(bestBone[k].InverseTransformPoint(bestWorld[k])); }
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (animator == null || layerIndex != 0) return; // yalnız Base Layer; iki kez çalışmasın

        FootProbe left = Probe(leftFoot);
        FootProbe right = Probe(rightFoot);

        // 0) Destek ağırlıkları. Klipte iki ayak da zemine gömülü olabilir; bu yüzden "yere yakın mı" sorusunu
        // mutlak değil, iki ayağın en alçağına göre sorarız: en alçak ayak (destek) 1, ondan belirgin yüksek ayak (salınım) 0.
        // Ayrıca en alçak ayağın kendisi de zemine yakın olmalı; değilse (koşuda uçuş) iki ayak da 0 olur.
        float lowestFoot = float.MaxValue;
        if (left.hasGround) lowestFoot = Mathf.Min(lowestFoot, left.clearance);
        if (right.hasGround) lowestFoot = Mathf.Min(lowestFoot, right.clearance);
        if (lowestFoot < float.MaxValue)
        {
            float contact = ContactWeight(lowestFoot);
            left.weight = left.hasGround ? ContactWeight(left.clearance - lowestFoot) * contact * masterWeight : 0f;
            right.weight = right.hasGround ? ContactWeight(right.clearance - lowestFoot) * contact * masterWeight : 0f;
        }

        // 1) Gövde: destek ayaklarının bilek kaymasının ağırlıklı ortalaması kadar TÜM vücut kayar.
        float weightSum = left.weight + right.weight;
        float target = 0f;
        if (weightSum > 0.0001f)
        {
            float average = (left.ankleLift * left.weight + right.ankleLift * right.weight) / weightSum;
            // İki ayak da havadaysa (koşuda uçuş) kayma yumuşakça 0'a döner.
            target = Mathf.Clamp(average * Mathf.Min(weightSum, 1f), -maxBodyDrop, maxBodyRaise) * masterWeight;
        }
        // Yukarı kayma ANİDEN olur (taban zemine girerken gecikmesin); aşağı dönüş yumuşak olur (titreme olmasın).
        if (target > bodyOffset) { bodyOffset = target; bodyOffsetVelocity = 0f; }
        else bodyOffset = Mathf.SmoothDamp(bodyOffset, target, ref bodyOffsetVelocity, bodySmoothTime);
        if (Mathf.Abs(bodyOffset) > 0.0001f)
            animator.bodyPosition += Vector3.up * bodyOffset;

        // 2) Ayaklar: gövde kaymasını hesaba katarak her ayağı çöz.
        SolveFoot(leftFoot, left);
        SolveFoot(rightFoot, right);
    }

    // IK uygulandıktan SONRA (LateUpdate) tabanın gerçek yüksekliğini ölçeriz. Ayak bileği kıvrım sınırı yüzünden hedef dönüşe
    // tam ulaşamazsa (ör. koşuda dışa dönük ayak) taban hâlâ zemine girebilir. Kalan gömülmeyi bir sonraki karede
    // ayağın hedef yüksekliğine ekleyerek gideririz (geri besleme).
    private void LateUpdate()
    {
        if (animator == null) return;
        ApplyResidualFeedback(leftFoot);
        ApplyResidualFeedback(rightFoot);
    }

    private void ApplyResidualFeedback(FootRig rig)
    {
        if (rig == null) return;
        FootProbe post = Probe(rig);
        if (post.hasGround && rig.lastWeight > 0.1f)
            rig.residual = Mathf.Clamp(rig.residual - residualGain * post.clearance, -maxResidual, maxResidual); // destek: iki yöne
        else if (post.hasGround && post.clearance < 0f)
            rig.residual = Mathf.Clamp(rig.residual - residualGain * post.clearance, 0f, maxResidual); // salınım: yalnız yukarı (zemine girmesin)
        else
            rig.residual *= 0.8f; // sorun yoksa geri beslemeyi yavaşça unut
    }

    // Yükseklikten ağırlık: eşikten alçaksa 1, yüksekse 0, arası yumuşak geçiş (ayak aniden sıçramasın).
    private float ContactWeight(float height)
    {
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fullWeightClearance, zeroWeightClearance, height));
    }

    // Ayağın altına ışın atar ve tabanın zemine göre en alçak noktasını ölçer.
    private FootProbe Probe(FootRig rig)
    {
        var probe = new FootProbe();
        if (rig == null) return probe;
        probe.animPos = rig.bone.position;
        probe.animRot = rig.bone.rotation;

        // Işın: ayağın biraz üstünden aşağı bak.
        Vector3 origin = probe.animPos + Vector3.up * rayStartHeight;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayStartHeight + rayDownDistance, groundMask, QueryTriggerInteraction.Ignore))
            return probe; // zemin yok (boşluk): IK devre dışı kalır, animasyon çalışır

        probe.hasGround = true;
        probe.point = hit.point;
        probe.normal = Vector3.Angle(Vector3.up, hit.normal) > maxGroundAngle ? Vector3.up : hit.normal; // çok dik yüzeyi zemin sayma

        // Animasyondaki tabanın en alçak örnek noktası (topuk, burun ucu, kenarlar), zemin yüzeyine göre.
        float lowest = float.MaxValue;
        for (int i = 0; i < rig.soleBones.Length; i++)
            lowest = Mathf.Min(lowest, Vector3.Dot(rig.soleBones[i].TransformPoint(rig.soleLocals[i]) - probe.point, probe.normal));
        probe.clearance = lowest - soleLift;


        // Düz basmış ayakta bilek zeminden (ankleHeight + pay) yüksekte olmalı; animasyondaki bilek şu kadar farklı.
        float currentHeight = Vector3.Dot(probe.animPos - probe.point, probe.normal);
        probe.ankleLift = Mathf.Clamp(rig.ankleHeight + soleLift - currentHeight, -maxLower, maxRaise);
        return probe;
    }

    // Tek ayağın IK hedefini ayarlar.
    private void SolveFoot(FootRig rig, FootProbe probe)
    {
        if (rig == null) return;
        AvatarIKGoal goal = rig.goal;
        if (!probe.hasGround)
        {
            animator.SetIKPositionWeight(goal, 0f);
            animator.SetIKRotationWeight(goal, 0f);
            return;
        }

        Vector3 normal = probe.normal;

        // Gövde kaydığı için ayağın animasyondaki konumu da aynı kadar kaymış olacak.
        Vector3 shiftedPos = probe.animPos + Vector3.up * bodyOffset;
        float clearance = probe.clearance + bodyOffset * normal.y;

        float weight = probe.weight;

        // Konum 1 (salınım): animasyondaki konum; sadece zemine giriyorsa (kalan gömülme) yukarı it.
        float penetration = Mathf.Clamp(-clearance, 0f, maxRaise);
        Vector3 swingPos = shiftedPos + normal * (penetration + Mathf.Max(rig.residual, 0f));

        // Konum 2 (destek): düz basmış ayağın bileği zemin normali boyunca (ankleHeight + pay) yukarıda.
        // Yatay konum animasyondan gelir, ayak zeminde kaymaz.
        float flatHeight = rig.ankleHeight + soleLift;
        float currentHeight = Vector3.Dot(shiftedPos - probe.point, normal);
        float lift = Mathf.Clamp(flatHeight - currentHeight, -maxLower, maxRaise);
        Vector3 flatPos = shiftedPos + normal * (lift + rig.residual);

        Vector3 targetPos = Vector3.Lerp(swingPos, flatPos, weight);

        // Düz basmış ayağın hedef dönüşü (KEMİK çerçevesinde): bağ pozundaki dönüş, animasyonun yönüne (yaw)
        // çevrilip zemin eğimine hizalanmış hali.
        Quaternion restWorld = transform.rotation * rig.restRotationInRoot;
        // Yön (yaw): klipte ayaklar çok dışa dönük olabilir; insan ayağı ise bileğinden yanlara fazla kıvrılamaz.
        // Bu yüzden ayağın yönünü ayak kemiğinden değil, alt bacağın doğal (bilek nötr) duruşundan okuruz.
        // Böylece düz basmak için bileğin kıvıramayacağı bir yana yatma istemeyiz.
        Quaternion naturalFoot = rig.lowerLeg.rotation * rig.restFootInLeg;
        float yaw = HeadingYaw(naturalFoot * rig.restToeDirLocal) - rig.restHeadingYaw;
        Quaternion flatBoneRot = Quaternion.FromToRotation(transform.up, normal) * Quaternion.AngleAxis(yaw, transform.up) * restWorld;

        // Humanoid IK hedef dönüşü kemik dönüşüyle birebir aynı çerçevede değildir; aradaki fark avatara özgü bir sabittir.
        // İlk karede (henüz biz hedef atamadan) animasyonun IK hedefi ile kemik dönüşünden bu farkı bir kez hesaplarız.
        // (Sonraki karelerde GetIKRotation bizim atadığımız değeri döndürebilir, o yüzden tekrar hesaplamıyoruz.)
        if (!rig.goalCalibrated)
        {
            rig.goalToBone = Quaternion.Inverse(animator.GetIKRotation(goal)) * probe.animRot;
            rig.goalCalibrated = true;
        }
        Quaternion flatRot = flatBoneRot * Quaternion.Inverse(rig.goalToBone);

        rig.lastWeight = weight;
        float rotWeight = weight * flattenWeight;
        animator.SetIKPositionWeight(goal, masterWeight);
        animator.SetIKPosition(goal, targetPos);
        animator.SetIKRotationWeight(goal, rotWeight);
        animator.SetIKRotation(goal, flatRot);

        // Parmak (toe) kemiği: animasyon botun ucunu kıvırıyorsa taban düz durmaz; destek fazında bağ pozundaki düz haline çek.
        animator.SetBoneLocalRotation(rig.toeId, Quaternion.Slerp(rig.toeBone.localRotation, rig.restToeLocalRotation, rotWeight));
    }
}
