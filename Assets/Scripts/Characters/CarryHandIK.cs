using UnityEngine;

// Taşıma el IK'sı (kullanıcı isteği 2026-11-05: "eller eşyaya otursun, eşya ele bağlı olsun"). StretcherHandIK deseninin tek
// taşıyıcılı eşyalara (ICarriedProp: koni, değerli eşya, ofis-garaj eşyası) genellemesi:
//  - Hafif: sağ el klip pozunda kalır; eşya tepesinden (tutma noktası) sağ avuca asılır, dışa-öne eğik sarkar. Eşya yere girecekse
//    el yere pay bırakacak kadar yukarı kaldırılır (kısa koni, uzun bidon: poz boyuta uyar).
//  - Orta / ağır: iki avuç eşyanın ALT yüzüne kaydırılır (±yarı genişlik, derinliğe göre ileri); eşya avuçların üstüne, arka yüzü
//    gövde önüne (bodyFront) dayalı konur. Uzun eşyada eller alçalır (merkez yüksekliği sınıf aralığında kalır).
//  Chibi kol zinciri kısa (0.177 + 0.14 m): hedef omuzdan maximumReach'ten uzaksa hedef erişim küresine çekilir (sedye IK'sındaki
//  "vazgeç" yerine); eşya her zaman GERÇEK avuçlara göre konduğundan hiçbir durumda elden kopmaz.
// Sıra: Animator → Crouch katmanı → StretcherHandIK (300) → bu (310) → FirstPersonBody (400). Eşyayı burada konumlar; eşyaların
// kendi yedek konumlaması (TrafficCone / LootItem / CompanyPlayerBridge.LateUpdate) IK sürüyorsa (Drives) atlanır.
// Nişan alırken (PlayerCombat.Aiming) sürmez: eşya eski gibi sağ ele gider ve fırlatılır. Sedye / sırtta hasta / kangal kapsam dışı.
// Birinci şahısta (OrbitCamera.Blend) iki elde eşya biraz alçalır, görüş merkezini kapatmaz; ayrı FP kol sistemi yoktur.
// ThirdPersonPlayer Awake'te kendi ekler; sahne / prefab dosyası değişmez.
[DefaultExecutionOrder(310)]
public sealed class CarryHandIK : MonoBehaviour
{
    public const float PalmOffset = 0.05f;  // avuç = el kemiği + kemik yönünde 5 cm (clips_meta palm_offset_hand_local_m)

    [Tooltip("IK açılma / kapanma süresi (sn).")]
    public float blendSeconds = 0.2f;
    [Tooltip("Omuz (üst kol) → el hedefi en çok bu kadar (m); daha uzak hedef bu küreye çekilir.")]
    public float maximumReach = 0.305f;
    [Header("Hafif (tek el)")]
    [Tooltip("Sarkan eşyanın tabanı ile zemin arasındaki pay (m).")]
    public float lightGroundClearance = 0.06f;
    [Tooltip("Sarkan eşyanın dışa-öne eğimi (°): tabanı kalçadan uzaklaşır.")]
    public float lightTilt = 28f;
    [Tooltip("Tutma noktası (eşyanın tepesi) avucun bu kadar altında (m): yumruk eşyayı sarmış görünür.")]
    public float lightGripInset = 0.02f;
    [Header("Orta / ağır (iki el)")]
    [Tooltip("Gövde ön yüzü, kökten ileri (m): eşyanın arka yüzü bu düzleme dayanır (CharacterController yarıçapı 0.25).")]
    public float bodyFront = 0.23f;
    [Tooltip("Avuçlar eşyanın alt kenarından bu kadar içeride (m).")]
    public float edgeInset = 0.035f;
    [Tooltip("Eşya avuçların bu kadar üstünde başlar (m).")]
    public float palmLift = 0.01f;
    [Tooltip("Orta sınıfta eşya merkezinin yükseklik aralığı (kök üstü, m): uzun eşya yüzü kapatmasın diye eller alçalır.")]
    public Vector2 mediumCenterHeight = new Vector2(0.62f, 0.88f);
    [Tooltip("Ağır sınıfta eşya merkezinin yükseklik aralığı (kök üstü, m).")]
    public Vector2 heavyCenterHeight = new Vector2(0.52f, 0.76f);
    [Tooltip("Birinci şahısta (kamera karışımıyla) iki elde eşya bu kadar alçalır (m).")]
    public float firstPersonDrop = 0.10f;

    // Tanı (doğrulama okur)
    public float Weight { get; private set; }
    public ICarriedProp Prop { get; private set; }          // bu karede sürülen eşya (yoksa null)
    public CarryClass Class { get; private set; }
    public int ClampedHands { get; private set; }           // son karede erişim küresine çekilen el sayısı
    public float MaximumShoulderDistance { get; private set; }
    public float GripErrorLeft { get; private set; }        // avuç → eşya yüzeyi (m); hafifte sağ el tutma noktası
    public float GripErrorRight { get; private set; }
    public float BodyOverlap { get; private set; }          // eşya kutusunun gövde kapsülüne girme derinliği (m, yatay)
    public Vector3 LastCenter { get; private set; }
    public Quaternion LastRotation { get; private set; } = Quaternion.identity;
    public Vector3 LastSize { get; private set; }

    private Animator animator;
    private ThirdPersonPlayer player;
    private PlayerCombat combat;
    private CharacterController controller;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        player = GetComponent<ThirdPersonPlayer>();
        combat = GetComponent<PlayerCombat>();
        controller = GetComponent<CharacterController>();
    }

    // Bu taşıyıcıdaki IK verilen eşyayı sürüyor mu (eşyalar kendi yedek konumlamasını atlamak için sorar).
    public static bool Drives(Component holder, ICarriedProp prop)
    {
        if (holder == null || prop == null) return false;
        var ik = holder.GetComponent<CarryHandIK>();
        return ik != null && ik.enabled && ReferenceEquals(ik.Prop, prop);
    }

    private ICarriedProp CurrentProp()
    {
        if (player == null || player.IsDown || player.Rope != null) return null;
        if (player.HeldStretcher != null || player.Piggyback != null || player.HeldCoil != null) return null;
        if (combat != null && combat.Aiming) return null;
        if (player.HeldCone != null) return player.HeldCone;
        if (player.HeldValuable != null) return player.HeldValuable;
        return player.HeldProp as ICarriedProp;
    }

    private bool InCarryLoop()
    {
        var st = animator.GetCurrentAnimatorStateInfo(0);
        if (IsCarryLoop(st)) return true;
        return animator.IsInTransition(0) && IsCarryLoop(animator.GetNextAnimatorStateInfo(0));
    }

    private static bool IsCarryLoop(AnimatorStateInfo st)
    {
        return st.IsName(CrewAnimation.StateCarryCone) || st.IsName(CrewAnimation.StateCarryTwoHand) || st.IsName(CrewAnimation.StateCarryHeavy);
    }

    // Karar Update'te (310: ThirdPersonPlayer, PlayerCombat ve PlayerInteraction'dan sonra): eşyaların LateUpdate'i (60) aynı karede görür.
    private void Update()
    {
        var prop = CurrentProp();
        bool ready = prop != null && animator != null && animator.enabled && animator.isHuman && animator.runtimeAnimatorController != null;
        Prop = ready ? prop : null;
        Class = Prop != null ? Prop.CarryClass : CarryClass.None;
    }

    private void LateUpdate()
    {
        ClampedHands = 0;
        MaximumShoulderDistance = 0f;
        float dt = Time.deltaTime;
        if (Prop == null) { Weight = Mathf.MoveTowards(Weight, 0f, dt / Mathf.Max(0.01f, blendSeconds)); return; }
        bool loop = InCarryLoop();
        Weight = Mathf.MoveTowards(Weight, loop ? 1f : 0f, dt / Mathf.Max(0.01f, blendSeconds));
        Vector3 size = Prop.HeldSize;
        LastSize = size;
        if (Class == CarryClass.Light) SolveLight(size);
        else SolveTwoHand(size, Class == CarryClass.Heavy);
    }

    private Vector3 Palm(HumanBodyBones bone)
    {
        var hand = animator.GetBoneTransform(bone);
        return hand.position + hand.up * PalmOffset;
    }

    private Vector3 Forward() { Vector3 f = transform.forward; f.y = 0f; return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward; }

    // ---- hafif: eşya sağ avuca asılı ----
    private void SolveLight(Vector3 size)
    {
        Vector3 up = Vector3.up, fwd = Forward(), right = Vector3.Cross(up, fwd);
        float tilt = Mathf.Deg2Rad * lightTilt;
        Vector3 itemUp = (up * Mathf.Cos(tilt) - right * Mathf.Sin(tilt) * 0.8f - fwd * Mathf.Sin(tilt) * 0.6f).normalized;
        Quaternion rot = Quaternion.FromToRotation(up, itemUp) * Quaternion.LookRotation(fwd, up);
        float hang = Mathf.Max(0f, size.y - lightGripInset);
        Vector3 palm0 = Palm(HumanBodyBones.RightHand);
        // Yere pay: tabanın yüksekliği = avuç − sarkan boy × itemUp.y
        float groundY = transform.position.y;
        float needPalmY = groundY + lightGroundClearance + hang * itemUp.y;
        Vector3 target = palm0;
        if (palm0.y < needPalmY)
        {
            float raise = needPalmY - palm0.y;
            target = palm0 + up * raise + right * raise * 0.25f; // yukarı ve hafif dışa: dirsek geriye, önkol öne
        }
        Solve(false, target, Weight);
        Vector3 palm = Palm(HumanBodyBones.RightHand);
        Vector3 center = palm - itemUp * (size.y * 0.5f - lightGripInset);
        Place(center, rot, size);
        GripErrorLeft = 0f;
        GripErrorRight = Vector3.Distance(palm, center + itemUp * (size.y * 0.5f - lightGripInset));
    }

    // ---- orta / ağır: iki avuç eşyanın altında, eşya gövdeye dayalı ----
    private void SolveTwoHand(Vector3 size, bool heavy)
    {
        Vector3 up = Vector3.up, fwd = Forward(), right = Vector3.Cross(up, fwd);
        Vector3 l0 = Palm(HumanBodyBones.LeftHand), r0 = Palm(HumanBodyBones.RightHand);
        Vector3 root = transform.position;
        Vector3 mid0 = (l0 + r0) * 0.5f;
        float spacing0 = Mathf.Abs(Vector3.Dot(l0 - r0, right));
        float halfW = Mathf.Max(0.04f, size.x * 0.5f), height = Mathf.Max(0.02f, size.y), depth = Mathf.Max(0.02f, size.z);
        // Avuç x: klip aralığı ile eşya genişliği içinden küçüğü (eller eşyanın altında kalır)
        float hx = Mathf.Max(0.05f, Mathf.Min(spacing0 * 0.5f, halfW - edgeInset));
        // Avuç ileri: eşyanın arka yüzü gövde önünde, avuçlar ön-alt kenarda (erişim küresi ileride keser)
        float z0 = Vector3.Dot(mid0 - root, fwd);
        float hz = Mathf.Clamp(bodyFront + depth - edgeInset, z0 - 0.03f, z0 + 0.12f);
        // Avuç yükseklik: eşya merkezi sınıf aralığında kalsın (uzun eşya yüzü kapatmaz, kısa eşya bel altına inmez)
        Vector2 range = heavy ? heavyCenterHeight : mediumCenterHeight;
        float fp = OrbitCamera.FirstPersonBlendFor(transform);
        float y0 = mid0.y - root.y;
        float centerY = Mathf.Clamp(y0 + palmLift + height * 0.5f, range.x, range.y) - firstPersonDrop * fp;
        float hy = centerY - palmLift - height * 0.5f;
        Vector3 lt = root + fwd * hz + up * hy - right * hx;
        Vector3 rt = root + fwd * hz + up * hy + right * hx;
        Solve(true, lt, Weight);
        Solve(false, rt, Weight);
        Vector3 l = Palm(HumanBodyBones.LeftHand), r = Palm(HumanBodyBones.RightHand);
        Vector3 mid = (l + r) * 0.5f;
        // Eşya: avuçların üstünde; ileri-geri: avuçlar ön-alt kenarın edgeInset içinde, ama arka yüz gövde önünden geriye geçmez
        float midZ = Vector3.Dot(mid - root, fwd);
        float cz = Mathf.Max(midZ - depth * 0.5f + edgeInset, bodyFront + depth * 0.5f);
        float cy = mid.y - root.y + palmLift + height * 0.5f;
        float cx = Vector3.Dot(mid - root, right);
        Vector3 center = root + fwd * cz + up * cy + right * cx;
        Quaternion rot = Quaternion.LookRotation(fwd, up);
        Place(center, rot, size);
        GripErrorLeft = SurfaceDistance(l, center, rot, size);
        GripErrorRight = SurfaceDistance(r, center, rot, size);
    }

    private void Place(Vector3 center, Quaternion rot, Vector3 size)
    {
        LastCenter = center; LastRotation = rot;
        Prop.PlaceHeld(center, rot);
        BodyOverlap = ComputeBodyOverlap(center, rot, size);
    }

    // Avuç → eşya kutusu yüzeyi (kutu dışındaysa en yakın noktaya uzaklık, içindeyse 0)
    private static float SurfaceDistance(Vector3 p, Vector3 center, Quaternion rot, Vector3 size)
    {
        Vector3 local = Quaternion.Inverse(rot) * (p - center);
        Vector3 e = size * 0.5f;
        Vector3 c = new Vector3(Mathf.Clamp(local.x, -e.x, e.x), Mathf.Clamp(local.y, -e.y, e.y), Mathf.Clamp(local.z, -e.z, e.z));
        return Vector3.Distance(local, c);
    }

    // Eşya kutusunun gövde kapsülüne (CharacterController) yatay girme derinliği: kapsül eksenindeki en yakın nokta → kutu
    private float ComputeBodyOverlap(Vector3 center, Quaternion rot, Vector3 size)
    {
        if (controller == null) return 0f;
        float radius = controller.radius;
        Vector3 axisBase = transform.position + transform.TransformVector(controller.center);
        float half = Mathf.Max(0f, controller.height * 0.5f - radius);
        float y = Mathf.Clamp(center.y, axisBase.y - half, axisBase.y + half);
        Vector3 a = new Vector3(axisBase.x, y, axisBase.z);
        Vector3 local = Quaternion.Inverse(rot) * (a - center);
        Vector3 e = size * 0.5f;
        Vector3 c = new Vector3(Mathf.Clamp(local.x, -e.x, e.x), Mathf.Clamp(local.y, -e.y, e.y), Mathf.Clamp(local.z, -e.z, e.z));
        Vector3 cw = center + rot * c;
        Vector3 d = cw - a; d.y = 0f;
        return Mathf.Max(0f, radius - d.magnitude);
    }

    // İki kemikli analitik çözüm (StretcherHandIK ile aynı): dirsek mevcut bükülme düzleminde kalır, el kemiğinin dünya dönüşü
    // korunur. Hedef omuzdan maximumReach'ten uzaksa erişim küresine çekilir (ClampedHands sayar).
    private void Solve(bool left, Vector3 palmTarget, float weight)
    {
        if (weight <= 0f) return;
        var upper = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        var lower = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        if (upper == null || lower == null || hand == null) return;
        Quaternion handRotation = hand.rotation;
        Vector3 target = palmTarget - hand.up * PalmOffset;
        Vector3 delta = target - upper.position;
        float distance = delta.magnitude;
        MaximumShoulderDistance = Mathf.Max(MaximumShoulderDistance, distance);
        float a = Vector3.Distance(upper.position, lower.position), b = Vector3.Distance(lower.position, hand.position);
        float reach = Mathf.Min(maximumReach, a + b - 0.001f);
        if (distance < 0.001f) return;
        if (distance > reach)
        {
            ClampedHands++;
            delta = delta / distance * reach;
            distance = reach;
            target = upper.position + delta;
        }
        Vector3 direction = delta / distance;
        Vector3 bend = Vector3.ProjectOnPlane(lower.position - upper.position, direction);
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(-transform.forward, direction);
        bend.Normalize();
        float along = (a * a + distance * distance - b * b) / (2f * distance);
        Vector3 elbow = upper.position + direction * along + bend * Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
        Quaternion upperStart = upper.rotation, lowerStart = lower.rotation;
        upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, elbow - upper.position) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, target - lower.position) * lower.rotation;
        Quaternion upperSolved = upper.rotation, lowerSolved = lower.rotation;
        upper.rotation = Quaternion.Slerp(upperStart, upperSolved, weight);
        lower.rotation = Quaternion.Slerp(lowerStart, lowerSolved, weight);
        hand.rotation = handRotation;
    }
}
