using UnityEngine;
namespace RescueTeam.Bolum1
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class ThirdPersonPlayer : CarrierActor
    {
        public Transform view;
        public Animator animator;
        public float moveSpeed = 3.5f, jumpHeight = 0.8f;
        public bool InputEnabled = true;
        public float ControlLostUntil { get; private set; }
        CharacterController controller;
        Vector2 move;
        bool jump;
        float vertical;
        Vector3 push;
        void Awake() { controller = GetComponent<CharacterController>(); }
        public void SetInput(Vector2 movement, bool jumpPressed) { move = movement; jump |= jumpPressed; }
        public void KnockAside(Vector3 direction)
        { push = direction.normalized * 5; ControlLostUntil = Time.time + 1; }
        public void Teleport(Vector3 position)
        {
            controller.enabled = false; transform.position = position; controller.enabled = true; vertical = 0;
        }
        void Update()
        {
            Vector3 forward = view ? Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 velocity = (forward * move.y + right * move.x) * moveSpeed;
            if (!InputEnabled || Time.time < ControlLostUntil) velocity = Vector3.zero;
            if (velocity.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(velocity), 12 * Time.deltaTime);
            if (controller.isGrounded && vertical < 0) vertical = -2;
            if (jump && controller.isGrounded && InputEnabled && Time.time >= ControlLostUntil) vertical = Mathf.Sqrt(2 * 20 * jumpHeight);
            jump = false; vertical -= 20 * Time.deltaTime;
            if (controller.enabled) controller.Move((velocity + Vector3.up * vertical + push) * Time.deltaTime);
            push = Vector3.MoveTowards(push, Vector3.zero, 6 * Time.deltaTime);
            if (animator) animator.SetFloat("Speed", velocity.magnitude, 0.1f, Time.deltaTime);
        }
    }
}
