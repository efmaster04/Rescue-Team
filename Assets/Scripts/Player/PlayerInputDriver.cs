using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
namespace RescueTeam.Bolum1
{
    public sealed class PlayerInputDriver : MonoBehaviour
    {
        public ThirdPersonPlayer player;
        public PlayerInteraction interaction;
        public OrbitCamera orbit;
        InputAction move, look, jump, interact, restart;
        void Awake()
        {
            move = new InputAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            look = new InputAction("Look", binding: "<Mouse>/delta");
            jump = new InputAction("Jump", binding: "<Keyboard>/space");
            interact = new InputAction("Interact", binding: "<Keyboard>/e");
            restart = new InputAction("Restart", binding: "<Keyboard>/r");
        }
        void OnEnable() { move.Enable(); look.Enable(); jump.Enable(); interact.Enable(); restart.Enable(); }
        void OnDisable() { move.Disable(); look.Disable(); jump.Disable(); interact.Disable(); restart.Disable(); }
        void OnDestroy() { move.Dispose(); look.Dispose(); jump.Dispose(); interact.Dispose(); restart.Dispose(); }
        void Update()
        {
            player.SetInput(move.ReadValue<Vector2>(), jump.WasPressedThisFrame());
            if (orbit) orbit.Rotate(look.ReadValue<Vector2>());
            if (interact.WasPressedThisFrame() && player.InputEnabled) interaction.Interact();
            if (restart.WasPressedThisFrame() && MissionDirector.Instance && MissionDirector.Instance.IsFinished)
                SceneManager.LoadScene(SceneManager.GetActiveScene().path);
        }
    }
}
