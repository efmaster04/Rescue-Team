using UnityEngine;
using UnityEngine.InputSystem;

// Klavye/fare girdisini okur ve oyun nesnelerine komut olarak iletir (girdi ile oyun durumu ayrı).
// InputSystem_Actions.inputactions'a dokunmamak için eylemler burada kodla tanımlanır:
// WASD hareket, fare bakış, Space zıplama, E etkileşim, R yeniden başlat (görev bitince).
// U4: sol tık it / tackle, sağ tık basılı tut fırlat, Q düdük. U4b tanı: basış sayaçları ve bağlama özeti (doğrulama okur).
// FP: V birinci şahıs ↔ üçüncü şahıs (OrbitCamera.ToggleMode).
// Ofis-garaj: InputBlocked açıkken (bilgisayar arayüzü / duraklatma menüsü; CompanyPlayerBridge.SetCursor yazar) hareket, kamera,
// etkileşim ve saldırı girdisi durur (hareket sıfırlanır, basılı tutmalar bırakılır); kapanınca geri gelir. E basışı InteractPressed
// olayıyla köprüye de iletilir (bizim PlayerInteraction'ın eylemi yoksa köprü kullanır).
public class PlayerInputReader : MonoBehaviour
{
    public ThirdPersonPlayer player;
    public PlayerInteraction interaction;
    public OrbitCamera orbitCamera;
    [Tooltip("U4: sol tık it / tackle, sağ tık basılı tut fırlat, Q düdük (boşsa oyuncudan bulunur).")]
    public PlayerCombat combat;

    public const string AttackPath = "<Mouse>/leftButton", ThrowPath = "<Mouse>/rightButton", WhistlePath = "<Keyboard>/q", RunPath = "<Keyboard>/leftShift";
    public const string ViewTogglePath = "<Keyboard>/v";

    // U4b tanı
    public int AttackPresses { get; private set; }
    public int AttackAccepted { get; private set; }
    public int WhistlePresses { get; private set; }
    public int ThrowHoldFrames { get; private set; }
    public int ViewTogglePresses { get; private set; }
    public int InteractPresses { get; private set; }
    // Ofis-garaj: UI açıkken oyun girdisi kilitli (bkz. sınıf yorumu)
    public bool InputBlocked { get; set; }
    // E basılı mı (bu karede; kilitliyken false)
    public bool InteractHeld { get; private set; }
    // E'ye basıldı (kilitli değilken; PlayerInteraction.Interact çağrıldıktan sonra)
    public event System.Action InteractPressed;
    public InputAction AttackAction { get { return attack; } }
    public InputAction ThrowAction { get { return throwHold; } }
    public InputAction WhistleAction { get { return whistle; } }
    public InputAction RunAction { get { return run; } }
    public InputAction ViewToggleAction { get { return viewToggle; } }

    private InputAction move, look, jump, interact, restart, run, attack, throwHold, whistle, viewToggle;

    private void Awake()
    {
        if (combat == null && player != null) combat = player.GetComponent<PlayerCombat>();
        attack = new InputAction("Attack", InputActionType.Button, AttackPath);
        throwHold = new InputAction("ThrowHold", InputActionType.Button, ThrowPath);
        whistle = new InputAction("Whistle", InputActionType.Button, WhistlePath);
        run = new InputAction("Run", InputActionType.Button, RunPath);
        run.AddBinding("<Keyboard>/rightShift");
        move = new InputAction("Move", InputActionType.Value);
        move.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
        look = new InputAction("Look", InputActionType.Value, "<Mouse>/delta");
        jump = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
        interact = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
        restart = new InputAction("Restart", InputActionType.Button, "<Keyboard>/r");
        viewToggle = new InputAction("ViewToggle", InputActionType.Button, ViewTogglePath);
    }

    private void OnEnable()
    {
        run.Enable(); move.Enable(); look.Enable(); jump.Enable(); interact.Enable(); restart.Enable();
        attack.Enable(); throwHold.Enable(); whistle.Enable(); viewToggle.Enable();
    }

    private void OnDisable()
    {
        run.Disable(); move.Disable();
        if (player != null) player.SetRunInput(false); look.Disable(); jump.Disable(); interact.Disable(); restart.Disable();
        attack.Disable(); throwHold.Disable(); whistle.Disable(); viewToggle.Disable();
        if (combat != null) combat.SetThrowHeld(false);
    }

    private void OnDestroy()
    {
        run.Dispose(); move.Dispose(); look.Dispose(); jump.Dispose(); interact.Dispose(); restart.Dispose();
        attack.Dispose(); throwHold.Dispose(); whistle.Dispose(); viewToggle.Dispose();
    }

    private void Update()
    {
        if (InputBlocked)
        {
            // UI açık: hareket durur, basılı tutmalar bırakılır, tıklamalar oyuna gitmez
            if (player != null) { player.SetMoveInput(Vector2.zero); player.SetRunInput(false); }
            if (interaction != null) interaction.SetInteractHeld(false);
            InteractHeld = false;
            if (combat == null && player != null) combat = player.GetComponent<PlayerCombat>();
            if (combat != null) combat.CancelAim();   // UI açılırken nişan fırlatmadan iptal edilir
            return;
        }
        if (player != null)
        {
            player.SetMoveInput(move.ReadValue<Vector2>());
            player.SetRunInput(run.IsPressed());
            if (jump.WasPressedThisFrame()) player.RequestJump();
        }
        if (orbitCamera != null) orbitCamera.Rotate(look.ReadValue<Vector2>());
        if (viewToggle.WasPressedThisFrame()) { ViewTogglePresses++; if (orbitCamera != null) orbitCamera.ToggleMode(); } // V: FP ↔ TP
        InteractHeld = interact.IsPressed(); // basış olayından önce: basılı tutma işi (cıvata) ilk karede de "basılı" görsün
        if (interaction != null) interaction.SetInteractHeld(InteractHeld); // basılı tutma (arkadaşı kaldırma)
        if (interact.WasPressedThisFrame())
        {
            InteractPresses++;
            if (interaction != null) interaction.Interact();
            InteractPressed?.Invoke();
        }
        if (restart.WasPressedThisFrame() && MissionDirector.Instance != null) MissionDirector.Instance.RequestRestart();
        // U4: savunma
        if (combat == null && player != null) combat = player.GetComponent<PlayerCombat>();
        if (combat != null)
        {
            if (attack.WasPressedThisFrame()) { AttackPresses++; if (combat.Attack()) AttackAccepted++; }
            bool held = throwHold.IsPressed();
            if (held) ThrowHoldFrames++;
            combat.SetThrowHeld(held);
            if (whistle.WasPressedThisFrame()) { WhistlePresses++; combat.Whistle(); }
        }
    }

    // U4b tanı: eylem → bağlama → etkin mi; aygıt var mı ("fare: var/yok")
    public string BindingSummary()
    {
        string One(InputAction a) { return a == null ? "yok" : a.name + " [" + (a.bindings.Count > 0 ? a.bindings[0].effectivePath : "-") + "] " + (a.enabled ? "etkin" : "kapalı") + (a.controls.Count > 0 ? " → " + a.controls[0].path : " → aygıt yok"); }
        return One(attack) + "; " + One(throwHold) + "; " + One(whistle) + "; " + One(run) + "; fare " + (Mouse.current != null ? "var" : "yok") + ", klavye " + (Keyboard.current != null ? "var" : "yok");
    }
}
