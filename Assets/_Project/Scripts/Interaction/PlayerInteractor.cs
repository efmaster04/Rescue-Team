using UnityEngine;
using UnityEngine.InputSystem;

namespace CompanySim
{
    public abstract class InteractableBehaviour : MonoBehaviour
    {
        public abstract string Prompt(PlayerInteractor player);
        public abstract void Interact(PlayerInteractor player);
    }

    // Arkadaşın basit FPS kontrolcüsü. Ofis-garaj sahnesinde artık kullanılmaz: bizim karakterde bundan türeyen CompanyPlayerBridge
    // durur (ComputerUI.player ve CompanyRuntime bu tipi beklediğinden tip korunur). Türetilebilmesi için üyeler sanal.
    [RequireComponent(typeof(CharacterController))]
    public class PlayerInteractor : MonoBehaviour
    {
        public const float BodyHeight=1.72f, BodyRadius=.28f, EyeHeight=1.57f;
        public Camera viewCamera;
        public ComputerUI ui;
        public float walkSpeed = 3.2f, runSpeed = 5.0f, lookSensitivity = .11f;
        public float interactionDistance = 2.4f, gravity = -20f;
        public Key interactKey = Key.E, dropKey = Key.F, rotateKey = Key.R;
        public bool InputLocked { get; set; }
        public CarryableItem HeldItem { get; protected set; }
        CharacterController controller;
        protected Transform holdPoint;
        float pitch, verticalVelocity;
        Vector3 horizontalVelocity;
        Vector2 lookDelta;
        float stride,stepDistance;
        readonly RaycastHit[] reachHits=new RaycastHit[32];
        readonly Collider[] placementHits=new Collider[32];
        public bool carrySway=true;
        public InteractableBehaviour FocusedTarget { get; protected set; }
        public Transform HoldPoint => holdPoint;
        // E basılı mı (ModuleFastener cıvata sıkma/sökme okur). Köprü bizim girdi okuyucusundan verir.
        public virtual bool InteractHeld => Keyboard.current != null && Keyboard.current[interactKey].isPressed;

        protected virtual void Awake()
        {
            controller = GetComponent<CharacterController>();
            holdPoint = new GameObject("Equipment_HoldPoint").transform;
            holdPoint.SetParent(viewCamera.transform, false);
            holdPoint.localPosition = new Vector3(.32f, -.29f, .82f);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        protected virtual void Start()
        {
            foreach (var item in FindObjectsByType<CarryableItem>(FindObjectsSortMode.None))
                item.IgnorePlayerCollision();
        }

        protected virtual void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;
            if (InputLocked) return;

            Vector2 delta = mouse.delta.ReadValue() * lookSensitivity;
            lookDelta=delta;
            transform.Rotate(0, delta.x, 0);
            pitch = Mathf.Clamp(pitch - delta.y, -82, 82);
            viewCamera.transform.localRotation = Quaternion.Euler(pitch, 0, 0);

            Vector2 move = Vector2.zero;
            if (kb[Key.W].isPressed) move.y++;
            if (kb[Key.S].isPressed) move.y--;
            if (kb[Key.D].isPressed) move.x++;
            if (kb[Key.A].isPressed) move.x--;
            float speed = kb[Key.LeftShift].isPressed ? runSpeed : walkSpeed;
            Vector3 desired = transform.TransformDirection(new Vector3(move.x, 0, move.y).normalized) * speed;
            horizontalVelocity=Vector3.MoveTowards(horizontalVelocity,desired,(move==Vector2.zero?22:15)*Time.deltaTime);
            verticalVelocity = controller.isGrounded ? -.8f : verticalVelocity + gravity * Time.deltaTime;
            var before=transform.position;
            controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * Time.deltaTime);
            var displacement=transform.position-before; displacement.y=0;
            stride+=displacement.magnitude*7;
            stepDistance+=controller.isGrounded?displacement.magnitude:0;
            if(stepDistance>(speed>walkSpeed?1.45f:1.10f))
            {
                stepDistance=0;
                if(ui.audioFeedback) ui.audioFeedback.Step();
            }

            var target=FindTarget(); FocusedTarget=target;
            ui.SetFocus(target!=null);
            var targetPrompt=target?target.Prompt(this):"";
            ui.SetPrompt(targetPrompt);
            if (kb[interactKey].wasPressedThisFrame && target) target.Interact(this);
            if (kb[dropKey].wasPressedThisFrame && HeldItem) DropHeld();
            if (kb[rotateKey].wasPressedThisFrame && HeldItem)
            {
                HeldItem.transform.Rotate(Vector3.up,90,Space.World); HeldItem.CenterInHand();
            }
            if (kb[Key.Tab].wasPressedThisFrame) ui.TogglePreparation();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (kb[Key.F9].wasPressedThisFrame) ui.Runtime.DevCompleteFieldJob();
#endif
        }
        public virtual InteractableBehaviour FindTarget()
        {
            int count=Physics.SphereCastNonAlloc(viewCamera.transform.position,.025f,viewCamera.transform.forward,
                reachHits,interactionDistance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide);
            float nearest=float.PositiveInfinity; InteractableBehaviour target=null;
            for(int i=0;i<count;i++)
            {
                var hit=reachHits[i];
                if(hit.collider.transform.IsChildOf(transform) || hit.distance>=nearest) continue;
                nearest=hit.distance; target=hit.collider.GetComponentInParent<InteractableBehaviour>();
            }
            return target;
        }
        protected virtual void LateUpdate()
        {
            if(!HeldItem) return;
            float size=HeldItem.HalfSize.magnitude;
            var position=new Vector3(.32f,-.29f,Mathf.Clamp(.75f+size*.55f,.85f,1.22f));
            if(HeldItem.definitionId=="toolbox" && FocusedTarget is ModuleFastener)
            {position.x=.50f; position.y=-.62f;}
            int hits=Physics.SphereCastNonAlloc(viewCamera.transform.position,Mathf.Clamp(size,.12f,.7f),
                viewCamera.transform.forward,reachHits,position.z,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<hits;i++)
                if(!reachHits[i].collider.transform.IsChildOf(transform) && reachHits[i].distance<position.z)
                {
                    position.z=Mathf.Max(.04f,reachHits[i].distance-.03f);
                    position.y=-.48f;
                }
            var rotation=Quaternion.identity;
            if(carrySway && !InputLocked)
            {
                position+=new Vector3(-Mathf.Clamp(lookDelta.x,-2,2)*.007f,Mathf.Sin(stride)*.005f,0);
                rotation=Quaternion.Euler(Mathf.Clamp(lookDelta.y,-2,2)*.8f,0,-Mathf.Sin(stride*.5f)*.65f);
            }
            float blend=1-Mathf.Exp(-12*Time.deltaTime);
            holdPoint.localPosition=Vector3.Lerp(holdPoint.localPosition,position,blend);
            holdPoint.localRotation=Quaternion.Slerp(holdPoint.localRotation,rotation,blend);
            HeldItem.UpdateHandVisibility(viewCamera);
        }

        public virtual void Hold(CarryableItem item)
        {
            if (HeldItem) return;
            HeldItem = item;
            item.AttachToHand(holdPoint);
            ui.SetCarriedItem(item);
        }

        public virtual void DropHeld()
        {
            if (!HeldItem) return;
            if(!TryGetDropPosition(HeldItem,out var position))
            {
                ui.Toast("Eşyayı bırakmak için biraz boşluk gerekiyor."); return;
            }
            var item = HeldItem; HeldItem = null;
            item.Drop(position);
            ui.SetCarriedItem(null);
            ui.Runtime.WorldChanged();
        }
        public virtual bool TryGetDropPosition(CarryableItem item,out Vector3 position)
        {
            position=Vector3.zero;
            float radius=Mathf.Clamp(item.HalfSize.magnitude,.12f,.7f), distance=1.25f;
            int count=Physics.SphereCastNonAlloc(viewCamera.transform.position,radius,viewCamera.transform.forward,
                reachHits,distance,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)
                if(!reachHits[i].collider.transform.IsChildOf(transform)) distance=Mathf.Min(distance,reachHits[i].distance-.06f);
            for(float reach=distance;reach>=.35f;reach-=.15f)
            {
                var candidate=viewCamera.transform.position+viewCamera.transform.forward*reach-Vector3.up*.12f-item.CenterOffset;
                count=Physics.OverlapBoxNonAlloc(candidate+item.CenterOffset,item.HalfSize+Vector3.one*.025f,
                    placementHits,item.transform.rotation,~0,QueryTriggerInteraction.Ignore);
                bool clear=count<placementHits.Length;
                for(int i=0;i<count;i++)
                    if(!placementHits[i].transform.IsChildOf(transform) && !placementHits[i].transform.IsChildOf(item.transform)) clear=false;
                if(clear) {position=candidate; return true;}
            }
            return false;
        }

        public virtual CarryableItem TakeHeld()
        {
            var item = HeldItem; HeldItem = null;
            ui.SetCarriedItem(null);
            return item;
        }

        public virtual void SetCursor(bool free)
        {
            InputLocked = free;
            horizontalVelocity=Vector3.zero; lookDelta=Vector2.zero;
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }
    }
}
