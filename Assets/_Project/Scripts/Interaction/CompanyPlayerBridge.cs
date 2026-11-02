using UnityEngine;
using UnityEngine.InputSystem;

namespace CompanySim
{
    // Ofis-garaj köprüsü (kullanıcı kararı: her yerde bizim karakter). Bizim ekip karakterinde (ThirdPersonPlayer + OrbitCamera +
    // PlayerInputReader + PlayerInteraction + PlayerCombat) durur ve arkadaşın etkileşim dünyasına "PlayerInteractor" olarak görünür:
    // InteractableBehaviour türevleri, ComputerUI, CompanyRuntime hiç değişmeden bu nesneyle çalışır (tip korunur, davranış ezilir).
    //  - Hedef bulma: kameranın merkez ışını (üçüncü şahısta ışın oyuncunun pivotundan itibaren sayılır; kendi gövdesi yok sayılır).
    //  - Tuşlar bizim düzen: E al / bırak / etkileşim (PlayerInputReader.InteractPressed; bizim PlayerInteraction'ın eylemi yoksa),
    //    E basılı tut = cıvata (InteractHeld), sağ tık fırlat (PlayerCombat → IHeldProp), Tab hazırlık paneli. F ve R kullanılmaz.
    //  - Taşıma: eşya oyuncu kökünün sağ önünde göğüs hizasında (üçüncü şahıs) ↔ kameranın sağ altında (birinci şahıs), Blend ile karışır.
    //    Nişan alırken sağ el kemiğinde (LootItem deseni).
    //  - UI (bilgisayar / duraklatma) açılınca ComputerUI SetCursor(true) çağırır: girdi okuyucusu kilitlenir, imleç serbest; kapanınca geri.
    //  - Fırlatılan CarryableItem sert çarpınca dayanıklılığı düşer (CarryableItem.ImpactDamageSpeed / ImpactDamage).
    public sealed class CompanyPlayerBridge : PlayerInteractor, IHeldProp
    {
        [Tooltip("Bizim oyuncu (boşsa aynı nesneden bulunur).")]
        public ThirdPersonPlayer player;
        public PlayerInputReader inputReader;
        public PlayerInteraction interaction;
        public PlayerCombat combat;
        public OrbitCamera orbitCamera;
        [Tooltip("Üçüncü şahıs taşıma noktası: oyuncu kök uzayında (sağ, yukarı, ileri; eşya yarı boyu eklenir).")]
        public Vector3 holdLocalThirdPerson = new Vector3(0.22f, 0.95f, 0.30f);
        [Tooltip("Birinci şahıs taşıma noktası: kamera uzayında (arkadaşın FPS değeri).")]
        public Vector3 holdLocalFirstPerson = new Vector3(0.32f, -0.29f, 0.85f);
        [Tooltip("Etkileşim ışını: üçüncü şahısta oyuncu pivotundan, birinci şahısta gözden bu kadar ileri (m).")]
        public float reach = 2.4f;

        // Tanı (doğrulama okur)
        public int Interactions { get; private set; }
        public int Holds { get; private set; }
        public int Drops { get; private set; }
        public int Throws { get; private set; }
        public string LastPrompt { get; private set; } = "";
        public bool Locked => InputLocked || (ui != null && (ui.IsOpen || ui.IsPaused));

        readonly RaycastHit[] rayHits = new RaycastHit[32];
        readonly Collider[] overlapHits = new Collider[32];
        bool holding; // HeldItem yok edildiyse (CompanyRuntime yeniden kurdu) durumu temizlemek için

        protected override void Awake()
        {
            if (player == null) player = GetComponent<ThirdPersonPlayer>();
            if (inputReader == null) inputReader = GetComponent<PlayerInputReader>();
            if (interaction == null) interaction = GetComponent<PlayerInteraction>();
            if (combat == null) combat = GetComponent<PlayerCombat>();
            if (viewCamera == null) viewCamera = Camera.main;
            if (orbitCamera == null && viewCamera != null) orbitCamera = viewCamera.GetComponent<OrbitCamera>();
            if (ui == null) ui = FindAnyObjectByType<ComputerUI>();
            // Taşıma noktası oyuncunun altında (kameranın değil): konumu her karede LateUpdate verir
            holdPoint = new GameObject("Equipment_HoldPoint").transform;
            holdPoint.SetParent(transform, false);
            holdPoint.localPosition = holdLocalThirdPerson;
            if (orbitCamera != null) lookSensitivity = orbitCamera.sensitivity;
        }

        void OnEnable() { if (inputReader != null) inputReader.InteractPressed += OnInteractPressed; }
        void OnDisable() { if (inputReader != null) inputReader.InteractPressed -= OnInteractPressed; }

        protected override void Start()
        {
            base.Start(); // eşyalar CharacterController ile çarpışmaz
            if (ui != null && ui.player != this) ui.player = this;
        }

        public override bool InteractHeld => inputReader != null ? inputReader.InteractHeld : base.InteractHeld;

        protected override void Update()
        {
            // Elindeki eşya yok edildiyse (CompanyRuntime yükleme sonrası yeniden kurar) durumu temizle
            if (holding && HeldItem == null) ClearHeld();
            // Duraklatma menüsündeki hassasiyet ayarı bizim kameraya
            if (orbitCamera != null) orbitCamera.sensitivity = lookSensitivity;
            // UI durumu ile girdi kilidi tutarlı kalsın (SetCursor dışından açılan pencereler için)
            bool locked = Locked;
            if (inputReader != null && inputReader.InputBlocked != locked) inputReader.InputBlocked = locked;
            if (locked) return;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb[Key.Tab].wasPressedThisFrame && ui != null) ui.TogglePreparation();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (kb[Key.F9].wasPressedThisFrame && ui != null && ui.Runtime != null) ui.Runtime.DevCompleteFieldJob();
#endif
            }

            var target = FindTarget();
            FocusedTarget = target;
            if (ui == null) return;
            ui.SetFocus(target != null);
            string prompt = target != null ? target.Prompt(this) : "";
            // Bizim sistemin ipucu varsa (sedye, koni, eşya…) o öncelikli; yoksa elde eşya varken boşluğa "bırak"
            if (interaction != null && !string.IsNullOrEmpty(interaction.CurrentHint)) prompt = interaction.CurrentHint;
            else if (string.IsNullOrEmpty(prompt) && HeldItem != null && target == null) prompt = "E — " + HeldItem.DisplayName + " bırak";
            LastPrompt = prompt;
            ui.SetPrompt(prompt);
        }

        // E basıldı (PlayerInputReader; PlayerInteraction.Interact zaten çağrıldı). Bizim eylem yoksa arkadaşın hedefi, o da yoksa bırak.
        void OnInteractPressed()
        {
            if (Locked || player == null || !player.HasControl) return;
            if (interaction != null && !string.IsNullOrEmpty(interaction.CurrentHint)) return;
            var target = FindTarget();
            FocusedTarget = target;
            if (target != null) { Interactions++; target.Interact(this); }
            else if (HeldItem != null) DropHeld();
        }

        // Kameranın merkez ışını. Üçüncü şahısta kamera gövdenin arkasındadır: ışın oyuncu pivotunun ışın üzerindeki izdüşümünden
        // başlar (kamera–gövde arası sayılmaz), birinci şahısta gözden. Kendi gövdesi (ragdoll, kapsül) ve elindeki eşya yok sayılır.
        public override InteractableBehaviour FindTarget()
        {
            if (viewCamera == null) return null;
            Vector3 origin = RayOrigin(out Vector3 forward);
            int count = Physics.SphereCastNonAlloc(origin, .025f, forward, rayHits, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            float nearest = float.PositiveInfinity; InteractableBehaviour target = null;
            for (int i = 0; i < count; i++)
            {
                var hit = rayHits[i];
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
                nearest = hit.distance; target = hit.collider.GetComponentInParent<InteractableBehaviour>();
            }
            return target;
        }

        Vector3 RayOrigin(out Vector3 forward)
        {
            forward = viewCamera.transform.forward;
            Vector3 camPos = viewCamera.transform.position;
            float pivotHeight = orbitCamera != null ? orbitCamera.pivotHeight : 1.3f;
            Vector3 pivot = transform.position + Vector3.up * pivotHeight;
            float along = Mathf.Max(0f, Vector3.Dot(pivot - camPos, forward));
            return camPos + forward * along;
        }

        // Taşıma noktası: TP kök uzayında sağ-ön göğüs hizası (eşya yarı boyu kadar ileri), FP kamera uzayında; Blend ile karışır.
        // Nişan alırken (PlayerCombat.Aiming) sağ elde (LootItem.FollowHolder ile aynı).
        protected override void LateUpdate()
        {
            if (HeldItem == null) return;
            Vector3 half = HeldItem.HalfSize;
            Quaternion rot = Quaternion.LookRotation(Flat(transform.forward), Vector3.up);
            Vector3 position;
            if (combat != null && combat.Aiming && player != null && player.animator != null && player.animator.isHuman
                && player.animator.GetBoneTransform(HumanBodyBones.RightHand) is Transform hand)
            {
                position = hand.position + hand.up * 0.05f + hand.forward * 0.06f;
            }
            else
            {
                Vector3 tp = transform.TransformPoint(holdLocalThirdPerson + new Vector3(half.x * 0.3f, 0f, half.z));
                float blend = orbitCamera != null ? orbitCamera.Blend : 0f;
                if (blend > 0f && viewCamera != null)
                {
                    Vector3 fpLocal = holdLocalFirstPerson; fpLocal.z = Mathf.Clamp(fpLocal.z + half.magnitude * 0.55f - 0.1f, 0.85f, 1.22f);
                    Vector3 fp = viewCamera.transform.TransformPoint(fpLocal);
                    position = Vector3.Lerp(tp, fp, blend);
                    rot = Quaternion.Slerp(rot, Quaternion.Euler(0f, viewCamera.transform.eulerAngles.y, 0f), blend);
                }
                else position = tp;
            }
            holdPoint.SetPositionAndRotation(position, rot);
            HeldItem.UpdateHandVisibility(viewCamera);
        }

        public override void Hold(CarryableItem item)
        {
            if (HeldItem != null || item == null) return;
            HeldItem = item; holding = true; Holds++;
            item.AttachToHand(holdPoint);
            if (player != null) player.HeldProp = this;
            if (ui != null) ui.SetCarriedItem(item);
        }

        public override void DropHeld()
        {
            if (HeldItem == null) return;
            if (!TryGetDropPosition(HeldItem, out var position))
            {
                if (ui != null) ui.Toast("Eşyayı bırakmak için biraz boşluk gerekiyor.");
                return;
            }
            var item = HeldItem; ClearHeld(); Drops++;
            item.Drop(position);
            if (ui != null && ui.Runtime != null) ui.Runtime.WorldChanged();
        }

        // Bırakma yeri: oyuncunun göğsünden kameranın yatay bakış yönüne (yoksa gövde yönü) 1.25 m'ye kadar; engel varsa kısalır, sonra
        // yakına doğru boş yer aranır (arkadaşın algoritması; başlangıç kamera değil gövde, çünkü üçüncü şahısta kamera arkadadır).
        public override bool TryGetDropPosition(CarryableItem item, out Vector3 position)
        {
            position = Vector3.zero;
            Vector3 dir = viewCamera != null ? Flat(viewCamera.transform.forward) : Flat(transform.forward);
            if (dir.sqrMagnitude < 1e-4f) dir = Flat(transform.forward);
            dir.Normalize();
            Vector3 origin = transform.position + Vector3.up * 0.9f;
            float radius = Mathf.Clamp(item.HalfSize.magnitude, .12f, .7f), distance = 1.25f;
            int count = Physics.SphereCastNonAlloc(origin, radius, dir, rayHits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!rayHits[i].collider.transform.IsChildOf(transform)) distance = Mathf.Min(distance, rayHits[i].distance - .06f);
            for (float r = distance; r >= .35f; r -= .15f)
            {
                var candidate = origin + dir * r - Vector3.up * .12f - item.CenterOffset;
                count = Physics.OverlapBoxNonAlloc(candidate + item.CenterOffset, item.HalfSize + Vector3.one * .025f, overlapHits, item.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
                bool clear = count < overlapHits.Length;
                for (int i = 0; i < count; i++)
                    if (!overlapHits[i].transform.IsChildOf(transform) && !overlapHits[i].transform.IsChildOf(item.transform)) clear = false;
                if (clear) { position = candidate; return true; }
            }
            return false;
        }

        public override CarryableItem TakeHeld()
        {
            var item = HeldItem;
            ClearHeld();
            return item;
        }

        void ClearHeld()
        {
            HeldItem = null; holding = false;
            if (player != null && ReferenceEquals(player.HeldProp, this)) player.HeldProp = null;
            if (ui != null) ui.SetCarriedItem(null);
        }

        // UI açıldı / kapandı (ComputerUI.Open/Close, PauseMenu.SetPaused): girdi kilidi ve imleç
        public override void SetCursor(bool free)
        {
            InputLocked = free;
            if (inputReader != null) inputReader.InputBlocked = free;
            if (player != null && free) player.SetMoveInput(Vector2.zero);
            if (Application.isBatchMode) return;
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }

        // ---- IHeldProp: PlayerCombat fırlatır, ThirdPersonPlayer düşürür ----
        public Vector3 Center => HeldItem != null ? HeldItem.transform.position + HeldItem.CenterOffset : holdPoint.position;

        public void ThrowFrom(ThirdPersonPlayer by, Vector3 velocity)
        {
            var item = HeldItem;
            if (item == null) { ClearHeld(); return; }
            Vector3 position = item.transform.position;
            Quaternion rotation = item.transform.rotation;
            ClearHeld(); Throws++;
            item.Throw(position, rotation, velocity);
        }

        public void DropFrom(ThirdPersonPlayer by)
        {
            var item = HeldItem;
            if (item == null) { ClearHeld(); return; }
            if (!TryGetDropPosition(item, out var position)) position = transform.position + Flat(transform.forward).normalized * 0.5f + Vector3.up * 0.5f;
            ClearHeld(); Drops++;
            item.Drop(position);
            if (ui != null && ui.Runtime != null) ui.Runtime.WorldChanged();
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v : Vector3.forward; }
    }
}
