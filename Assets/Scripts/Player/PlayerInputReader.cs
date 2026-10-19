using UnityEngine;
using UnityEngine.InputSystem;

// Klavye/fare girdisini okur ve oyun nesnelerine komut olarak iletir (girdi ile oyun durumu ayrı).
// InputSystem_Actions.inputactions'a dokunmamak için eylemler burada kodla tanımlanır:
// WASD hareket, fare bakış, Space zıplama, E etkileşim, R yeniden başlat (görev bitince).
public class PlayerInputReader : MonoBehaviour
{
    public ThirdPersonPlayer player;
    public PlayerInteraction interaction;
    public OrbitCamera orbitCamera;
    [Tooltip("U4: sol tık it / tackle, sağ tık basılı tut fırlat, Q düdük (boşsa oyuncudan bulunur).")]
    public PlayerCombat combat;

    private InputAction move, look, jump, interact, restart, run, attack, throwHold, whistle;

    private void Awake()
    {
        if (combat == null && player != null) combat = player.GetComponent<PlayerCombat>();
        attack = new InputAction("Attack", InputActionType.Button, "<Mouse>/leftButton");
        throwHold = new InputAction("ThrowHold", InputActionType.Button, "<Mouse>/rightButton");
        whistle = new InputAction("Whistle", InputActionType.Button, "<Keyboard>/q");
        run = new InputAction("Run", InputActionType.Button, "<Keyboard>/leftShift");
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
            if (attack.WasPressedThisFrame()) combat.Attack();
            combat.SetThrowHeld(throwHold.IsPressed());
            if (whistle.WasPressedThisFrame()) combat.Whistle();
        }
    }
}
