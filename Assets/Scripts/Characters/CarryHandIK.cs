using UnityEngine;

// Taşıma el IK'sı (kullanıcı isteği 2026-11-05: "eller eşyaya otursun, eşya ele bağlı olsun"). StretcherHandIK deseninin tek
// taşıyıcılı eşyalara (ICarriedProp: koni, değerli eşya, ofis-garaj eşyası) genellemesi. Eşya hesaplanmış bir noktaya değil
// GERÇEK avuçlara göre konur; hiçbir durumda elden kopmaz:
//  - Hafif: sağ el klip pozunda kalır; eşya tepesinden (tutma noktası) sağ avuca asılır, dışa-öne eğik sarkar. Eşya yere girecekse
//    el yere pay bırakacak kadar yukarı kaldırılır. Birinci şahısta el göğüs önüne gelir (aşağı bakınca eşya görünür).
//  - Orta / ağır: eşyanın genişliği omuz açıklığına yakınsa avuçlar yan yüzlerde (alt çeyrek), genişse alt yüzde; eller klipteki gibi
//    omuz genişliğinde kalır (±0.17'de ön kol gövdeye giriyordu). Arka yüz gövde önüne dayanır, tepe çene altında kalır.
//  Chibi kol zinciri kısa (0.177 + 0.14 m): hedef omuzdan maximumReach'ten uzaksa hedef erişim küresine çekilir.
// Sıra: Animator → katmanlar → StretcherHandIK (300) → bu (310) → FirstPersonBody (400). Eşyaların kendi yedek konumlaması
// (TrafficCone / LootItem / CompanyPlayerBridge.LateUpdate) IK sürüyorsa (Drives) atlanır.
// Nişan ve atışta (PlayerCombat.ThrowPose) sürmez: eşya eski gibi sağ ele gider ve fırlatılır. Sedye / sırtta hasta / kangal kapsam dışı.
// Yumrukta eller klibe döner; orta / ağır eşya gövdeye göre yerinde kalır, sol el üstünde (sağ el yumruk atar).
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
    public float lightTilt = 22f;
    [Tooltip("Tutma noktası (eşyanın tepesi) avucun bu kadar altında (m): yumruk eşyayı sarmış görünür.")]
    public float lightGripInset = 0.02f;
    [Tooltip("Birinci şahısta hafif eşyayı tutan avuç (kök uzayında sağ, yukarı, ileri; m).")]
    public Vector3 lightFirstPersonPalm = new Vector3(0.22f, 0.82f, 0.30f);
    [Header("Orta / ağır (iki el)")]
    [Tooltip("Gövde ön yüzü, kökten ileri (m): eşyanın arka yüzü bu düzleme dayanır (CharacterController yarıçapı 0.25).")]
    public float bodyFront = 0.23f;
    [Tooltip("En dar el aralığının yarısı (m): daha içeride ön kol gövdeye girer.")]
    public float minHandSpread = 0.185f;
    [Tooltip("Eşya yarı genişliği bu sınırı (m) aşmıyorsa avuçlar yan yüzlerde, aşıyorsa alt yüzde.")]
    public float sideGripMaxHalfWidth = 0.27f;
    [Tooltip("Bundan yüksek eşya (m) genişliği ne olursa olsun yanından tutulur: alttan tutulsa kısa kollar onu görüşe kaldırırdı.")]
    public float underGripMaxHeight = 0.36f;
    [Tooltip("Yan tutuşta avuçların en geniş yarı açıklığı (m; chibi kol erişimi).")]
    public float sideGripMaxSpread = 0.30f;
    [Tooltip("Yan tutuşta avucun yüksekliği: eşya merkezinin bu oran × boy altında.")]
    public float sideGripDrop = 0.05f;
    [Tooltip("Eşya avuçların bu kadar üstünde başlar (m; alt tutuş).")]
    public float palmLift = 0.01f;
    [Tooltip("Orta sınıfta eşya merkezinin yükseklik aralığı (kök üstü, m): uzun eşya yüzü kapatmasın diye aşağı iner.")]
    public Vector2 mediumCenterHeight = new Vector2(0.60f, 0.84f);
    [Tooltip("Ağır sınıfta eşya merkezinin yükseklik aralığı (kök üstü, m).")]
    public Vector2 heavyCenterHeight = new Vector2(0.56f, 0.80f);
    [Tooltip("Eşya tepesinin kökten en yüksek yeri (m; çene hizası). Geçerse merkez aşağı iner.")]
    public float maxTopHeight = 0.97f;
    [Tooltip("Ayakta kalça yüksekliği (m): eşya yükseklik aralığı kalçayla birlikte iner / çıkar (eğilme, zıplama, yürüyüş).")]
    public float standingHipHeight = 0.445f;
    [Tooltip("Birinci şahısta (kamera karışımıyla) iki elde eşya bu kadar alçalır (m): görüş merkezini kapatmaz.")]
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
    public bool SideGrip { get; private set; }
    public Vector3 LastCenter { get; private set; }
    public Quaternion LastRotation { get; private set; } = Quaternion.identity;
    public Vector3 LastSize { get; private set; }

    private Animator animator;
    private ThirdPersonPlayer player;
    private PlayerCombat combat;
    private CharacterController controller;
    private Vector3 homeLocal;                // son döngü karesinde eşya merkezi (kök uzayında): eylem sırasında yerinde kalır
    private bool homeKnown;

    private void Awake()
    {
        player = GetComponent<ThirdPersonPlayer>();
        animator = player != null && player.animator != null ? player.animator : GetComponent<Animator>();
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
        if (combat != null && combat.ThrowPose) return null;
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

    // PickUp / PutDown: eller klipteki gibi eşyaya gidip gelir, eşya avuçları izler.
    private bool FollowsClipHands()
    {
        var st = animator.GetCurrentAnimatorStateInfo(0);
        if (st.IsName(CrewAnimation.StatePickUp) || st.IsName(CrewAnimation.StatePutDown)) return true;
        if (!animator.IsInTransition(0)) return false;
        var next = animator.GetNextAnimatorStateInfo(0);
        return next.IsName(CrewAnimation.StatePickUp) || next.IsName(CrewAnimation.StatePutDown);
    }

    // Karar Update'te (310: ThirdPersonPlayer, PlayerCombat ve PlayerInteraction'dan sonra): eşyaların LateUpdate'i (60) aynı karede görür.
    private void Update()
    {
        var prop = CurrentProp();
        bool ready = prop != null && animator != null && animator.enabled && animator.isHuman && animator.runtimeAnimatorController != null;
        var previous = Prop;
        Prop = ready ? prop : null;
        if (!ReferenceEquals(previous, Prop)) homeKnown = false;
        Class = Prop != null ? Prop.CarryClass : CarryClass.None;
    }

    private void LateUpdate()
    {
        ClampedHands = 0;
        MaximumShoulderDistance = 0f;
        float dt = Time.deltaTime;
        if (Prop == null || Class == CarryClass.None) { Weight = Mathf.MoveTowards(Weight, 0f, dt / Mathf.Max(0.01f, blendSeconds)); return; }
        bool loop = InCarryLoop();
        Weight = Mathf.MoveTowards(Weight, loop ? 1f : 0f, dt / Mathf.Max(0.01f, blendSeconds));
        Vector3 size = Prop.HeldSize;
        LastSize = size;
        if (Class == CarryClass.Light) SolveLight(size);
        else SolveTwoHand(size, Class == CarryClass.Heavy, loop);
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
        Vector3 up = Vector3.up, fwd = Forward(), right = Vector3.Cross(up, fwd), root = transform.position;
        float fp = OrbitCamera.FirstPersonBlendFor(transform);
        // FP: taban öne uzanır, aşağı bakınca eşya görünür
        float tilt = Mathf.Deg2Rad * Mathf.Lerp(lightTilt, 42f, fp);
        Vector3 itemUp = (up * Mathf.Cos(tilt) - right * Mathf.Sin(tilt) * Mathf.Lerp(0.8f, 0.25f, fp) - fwd * Mathf.Sin(tilt) * Mathf.Lerp(0.6f, 1f, fp)).normalized;
        Quaternion rot = Quaternion.FromToRotation(up, itemUp) * Quaternion.LookRotation(fwd, up);
        float hang = Mathf.Max(0f, size.y - lightGripInset);
        Vector3 palm0 = Palm(HumanBodyBones.RightHand);
        // Yere pay: tabanın yüksekliği = avuç − sarkan boy × itemUp.y
        float needPalmY = root.y + lightGroundClearance + hang * itemUp.y;
        Vector3 target = palm0;
        if (palm0.y < needPalmY)
        {
            float raise = needPalmY - palm0.y;
            target = palm0 + up * raise + right * raise * 0.25f; // yukarı ve hafif dışa: dirsek geriye, önkol öne
        }
        if (fp > 0f)
        {
            float crouch = player != null ? player.CrouchBodyDrop : 0f;
            Vector3 fpPalm = root + right * lightFirstPersonPalm.x + up * (lightFirstPersonPalm.y - crouch) + fwd * lightFirstPersonPalm.z;
            target = Vector3.Lerp(target, fpPalm, fp);
        }
        Solve(false, target, Weight);
        Vector3 palm = Palm(HumanBodyBones.RightHand);
        Vector3 center = palm - itemUp * (size.y * 0.5f - lightGripInset);
        LastCenter = center; LastRotation = rot;
        Prop.PlaceHeld(center, rot);
        // Gövde teması yalnız alt yarıda ölçülür: tepesi avuçta, kolun yanında (koni orada dar)
        BodyOverlap = ComputeBodyOverlap(center - itemUp * (size.y * 0.25f), rot, new Vector3(size.x, size.y * 0.5f, size.z));
        GripErrorLeft = 0f;
        GripErrorRight = Vector3.Distance(palm, center + itemUp * (size.y * 0.5f - lightGripInset));
    }

    // ---- orta / ağır: iki avuç eşyada, eşya gövdeye dayalı ----
    private void SolveTwoHand(Vector3 size, bool heavy, bool loop)
    {
        Vector3 up = Vector3.up, fwd = Forward(), right = Vector3.Cross(up, fwd), root = transform.position;
        Quaternion flat = Quaternion.LookRotation(fwd, up);
        float halfW = Mathf.Max(0.04f, size.x * 0.5f), height = Mathf.Max(0.02f, size.y), depth = Mathf.Max(0.02f, size.z);
        bool side = halfW <= sideGripMaxHalfWidth || height > underGripMaxHeight;
        SideGrip = side;
        float gripBelowCenter = side ? height * sideGripDrop : height * 0.5f + palmLift;
        Vector3 l0 = Palm(HumanBodyBones.LeftHand), r0 = Palm(HumanBodyBones.RightHand);
        Vector3 mid0 = (l0 + r0) * 0.5f;

        if (!loop && !FollowsClipHands() && homeKnown)
        {
            // Yumruk, darbe tepkisi: eşya gövdeye göre yerinde, sol el üstünde; sağ el klibi oynar
            Vector3 home = transform.TransformPoint(homeLocal);
            float hxHome = side ? Mathf.Clamp(halfW + 0.005f, minHandSpread, sideGripMaxSpread) : Mathf.Abs(Vector3.Dot(l0 - r0, right)) * 0.5f;
            Solve(true, home - right * hxHome - up * gripBelowCenter, 1f);
            Place(home, flat, size);
            GripErrorLeft = SurfaceDistance(Palm(HumanBodyBones.LeftHand), home, flat, size);
            GripErrorRight = 0f;
            return;
        }

        float spread = Mathf.Abs(Vector3.Dot(l0 - r0, right)) * 0.5f;
        float hx = side ? Mathf.Clamp(halfW + 0.005f, minHandSpread, sideGripMaxSpread) : Mathf.Max(minHandSpread, spread);
        float sx0 = Vector3.Dot(mid0 - root, right);
        // Avuç ileri: eşyanın ortası, klip konumundan çok uzaklaşmaz (erişim küresi ileride keser)
        float z0 = Vector3.Dot(mid0 - root, fwd);
        float hz = Mathf.Clamp(bodyFront + depth * 0.5f, z0 - 0.05f, z0 + 0.01f);
        // Yükseklik: eşya merkezi sınıf aralığında, tepesi çene altında; klipteki yaylanma korunur (eşya canlı sallanır)
        Vector2 range = heavy ? heavyCenterHeight : mediumCenterHeight;
        // FP: eller eşyanın üst kenarında, eşya aşağıda sarkar: aşağı bakınca ekran merkezini kapatmaz (kullanıcı isteği 2026-11-05)
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        float bodyDrop = hips != null ? standingHipHeight - (hips.position.y - root.y) : 0f; // eğilme, yürüyüş: +; zıplama: −
        float fp = OrbitCamera.FirstPersonBlendFor(transform);
        gripBelowCenter = Mathf.Lerp(gripBelowCenter, -(height * 0.5f - 0.03f), fp);
        float y0 = mid0.y - root.y;
        float top = Mathf.Min(range.y, maxTopHeight - height * 0.5f);
        float bottomLimit = Mathf.Lerp(Mathf.Min(range.x, top), -10f, fp);
        float centerY = Mathf.Clamp(y0 + gripBelowCenter, bottomLimit - bodyDrop, top - bodyDrop) - firstPersonDrop * fp;
        float hy = centerY - gripBelowCenter;
        Vector3 lt = root + fwd * hz + up * (hy + l0.y - mid0.y) + right * (sx0 - hx);
        Vector3 rt = root + fwd * hz + up * (hy + r0.y - mid0.y) + right * (sx0 + hx);
        Solve(true, lt, Weight);
        Solve(false, rt, Weight);
        Vector3 l = Palm(HumanBodyBones.LeftHand), r = Palm(HumanBodyBones.RightHand);
        Vector3 mid = (l + r) * 0.5f;
        // Eşya gerçek avuçlara göre: arka yüz gövde önünden geriye geçmez; önkol öne uzanırsa eşya da gider
        float midZ = Vector3.Dot(mid - root, fwd);
        float cz = bodyFront + depth * 0.5f + Mathf.Max(0f, midZ - hz);
        float cy = mid.y - root.y + gripBelowCenter;
        float cx = Vector3.Dot(mid - root, right);
        Vector3 center = root + fwd * cz + up * cy + right * cx;
        // İki avucun yükseklik farkı eşyayı hafif yatırır (yürüyüşte canlı durur). Cross(fwd, sağ) = yukarı.
        Vector3 across = r - l;
        across -= fwd * Vector3.Dot(across, fwd);
        Quaternion rot = across.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fwd, Vector3.Cross(fwd, across.normalized)) : flat;
        Place(center, rot, size);
        if (loop && Weight >= 0.99f) { homeLocal = transform.InverseTransformPoint(center); homeKnown = true; }
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
