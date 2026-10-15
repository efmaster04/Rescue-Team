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

    private InputAction move, look, jump, interact, restart;

    private void Awake()
    {
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
        move.Enable(); look.Enable(); jump.Enable(); interact.Enable(); restart.Enable();
    }

    private void OnDisable()
    {
        move.Disable(); look.Disable(); jump.Disable(); interact.Disable(); restart.Disable();
    }

    private void OnDestroy()
    {
        move.Dispose(); look.Dispose(); jump.Dispose(); interact.Dispose(); restart.Dispose();
    }

    private void Update()
    {
        if (player != null)
        {
            player.SetMoveInput(move.ReadValue<Vector2>());
            if (jump.WasPressedThisFrame()) player.RequestJump();
        }
        if (orbitCamera != null) orbitCamera.Rotate(look.ReadValue<Vector2>());
        if (interact.WasPressedThisFrame() && interaction != null) interaction.Interact();
        if (restart.WasPressedThisFrame() && MissionDirector.Instance != null) MissionDirector.Instance.RequestRestart();
    }
}
