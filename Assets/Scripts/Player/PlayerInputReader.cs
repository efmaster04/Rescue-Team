using UnityEngine;
using UnityEngine.InputSystem;

// Klavye/fare girdisini okur ve oyun nesnelerine komut olarak iletir (girdi ile oyun durumu ayrı).
// InputSystem_Actions.inputactions'a dokunmamak için eylemler burada kodla tanımlanır:
// WASD hareket, fare bakış, Space zıplama, E etkileşim, R yeniden başlat (görev bitince).
// U4: sol tık it / tackle, sağ tık basılı tut fırlat, Q düdük. U4b tanı: basış sayaçları ve bağlama özeti (doğrulama okur).
public class PlayerInputReader : MonoBehaviour
{
    public ThirdPersonPlayer player;
    public PlayerInteraction interaction;
    public OrbitCamera orbitCamera;
    [Tooltip("U4: sol tık it / tackle, sağ tık basılı tut fırlat, Q düdük (boşsa oyuncudan bulunur).")]
    public PlayerCombat combat;

    public const string AttackPath = "<Mouse>/leftButton", ThrowPath = "<Mouse>/rightButton", WhistlePath = "<Keyboard>/q", RunPath = "<Keyboard>/leftShift";

    // U4b tanı
    public int AttackPresses { get; private set; }
    public int AttackAccepted { get; private set; }
    public int WhistlePresses { get; private set; }
    public int ThrowHoldFrames { get; private set; }
    public InputAction AttackAction { get { return attack; } }
    public InputAction ThrowAction { get { return throwHold; } }
    public InputAction WhistleAction { get { return whistle; } }
    public InputAction RunAction { get { return run; } }

    private InputAction move, look, jump, interact, restart, run, attack, throwHold, whistle;

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
    }

    private void OnEnable()
    {
        run.Enable(); move.Enable(); look.Enable(); jump.Enable(); interact.Enable(); restart.Enable();
        attack.Enable(); throwHold.Enable(); whistle.Enable();
    }

    private void OnDisable()
    {
        run.Disable(); move.Disable();
        if (player != null) player.SetRunInput(false); look.Disable(); jump.Disable(); interact.Disable(); restart.Disable();
        attack.Disable(); throwHold.Disable(); whistle.Disable();
        if (combat != null) combat.SetThrowHeld(false);
    }

    private void OnDestroy()
    {
        run.Dispose(); move.Dispose(); look.Dispose(); jump.Dispose(); interact.Dispose(); restart.Dispose();
        attack.Dispose(); throwHold.Dispose(); whistle.Dispose();
    }

    private void Update()
    {
        if (player != null)
        {
            player.SetMoveInput(move.ReadValue<Vector2>());
            player.SetRunInput(run.IsPressed());
            if (jump.WasPressedThisFrame()) player.RequestJump();
        }
        if (orbitCamera != null) orbitCamera.Rotate(look.ReadValue<Vector2>());
        if (interact.WasPressedThisFrame() && interaction != null) interaction.Interact();
        if (interaction != null) interaction.SetInteractHeld(interact.IsPressed()); // basılı tutma (arkadaşı kaldırma)
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
