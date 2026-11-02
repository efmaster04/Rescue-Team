using UnityEngine;

namespace CompanySim
{
    [RequireComponent(typeof(BoxCollider),typeof(Rigidbody))]
    public sealed class OfficeDoor : InteractableBehaviour
    {
        public float openAngle=95, speed=160;
        public bool IsOpen { get; private set; }
        public bool IsBlocked { get; private set; }
        public float CurrentAngle { get; private set; }
        float swingAngle,velocity;
        bool latchPending;
        Quaternion closedRotation;
        Rigidbody body;
        BoxCollider leaf;
        Vector3 clearanceSize;
        AudioFeedback feedback;
        readonly Collider[] overlaps=new Collider[48];

        void Awake()
        {
            closedRotation=transform.localRotation;
            swingAngle=openAngle;
            leaf=GetComponent<BoxCollider>(); body=GetComponent<Rigidbody>();
            body.isKinematic=true; body.useGravity=false;
            body.interpolation=RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
            clearanceSize=leaf.size+new Vector3(.06f,0,.06f);
        }
        void Start() { feedback=FindAnyObjectByType<AudioFeedback>(); }
        void FixedUpdate()
        {
            float target=IsOpen?swingAngle:0;
            if(Mathf.Abs(CurrentAngle-target)<.025f)
            {
                CurrentAngle=target; velocity=0; IsBlocked=false;
                body.MoveRotation(WorldRotation(target));
                if(latchPending && !IsOpen)
                {
                    latchPending=false;
                    if(feedback) feedback.Door(false,transform.position+Vector3.up);
                }
                return;
            }
            float next=Mathf.SmoothDamp(CurrentAngle,target,ref velocity,.12f,speed,Time.fixedDeltaTime);
            int steps=Mathf.Max(1,Mathf.CeilToInt(Mathf.Abs(next-CurrentAngle)/1.25f));
            float start=CurrentAngle; IsBlocked=false;
            for(int step=1;step<=steps;step++)
            {
                float candidate=Mathf.Lerp(start,next,(float)step/steps);
                if(Obstructed(CurrentAngle,candidate)) { IsBlocked=true; velocity=0; break; }
                CurrentAngle=candidate;
            }
            body.MoveRotation(WorldRotation(CurrentAngle));
        }
        Quaternion WorldRotation(float angle)
        {
            var local=closedRotation*Quaternion.Euler(0,angle,0);
            return transform.parent?transform.parent.rotation*local:local;
        }
        bool Obstructed(float previous,float candidate)
        {
            var rotation=WorldRotation(candidate);
            var center=body.position+rotation*Vector3.Scale(leaf.center,transform.lossyScale);
            int count=Physics.OverlapBoxNonAlloc(center,Vector3.Scale(clearanceSize,transform.lossyScale)*.5f,
                overlaps,rotation,~0,QueryTriggerInteraction.Ignore);
            if(count==overlaps.Length) return true;
            for(int i=0;i<count;i++)
            {
                var obstacle=overlaps[i];
                if(obstacle.transform.IsChildOf(transform)) continue;
                // Fixed masonry stays outside the authored swing. Players and
                // movable objects can newly enter the door's swept volume.
                if(!(obstacle is CharacterController) && !obstacle.attachedRigidbody) continue;
                if(obstacle is CharacterController controller)
                {
                    float penetration=CapsuleDepth(controller,rotation);
                    if(penetration<=0) continue;
                    float oldPenetration=CapsuleDepth(controller,WorldRotation(previous));
                    if(oldPenetration>0 && penetration<oldPenetration-.0001f) continue;
                    return true;
                }
                if(!Physics.ComputePenetration(leaf,body.position,rotation,obstacle,
                    obstacle.transform.position,obstacle.transform.rotation,out _,out float depth)) continue;
                bool wasOverlapping=Physics.ComputePenetration(leaf,body.position,WorldRotation(previous),obstacle,
                    obstacle.transform.position,obstacle.transform.rotation,out _,out float oldDepth);
                // Allow a leaf to retreat from an already overlapping obstacle.
                if(wasOverlapping && depth<oldDepth-.0001f) continue;
                return true;
            }
            return false;
        }
        float CapsuleDepth(CharacterController controller,Quaternion rotation)
        {
            // Upright capsules against a yawing OBB, including the rounded ends.
            // This remains reliable when the controller is stationary or just
            // re-enabled, unlike querying penetration on a disabled helper shape.
            var scale=controller.transform.lossyScale;
            float radius=controller.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z))+controller.skinWidth*.5f;
            float segment=Mathf.Max(0,controller.height*Mathf.Abs(scale.y)*.5f-radius);
            var local=Quaternion.Inverse(rotation)*(controller.transform.TransformPoint(controller.center)-body.position)
                -Vector3.Scale(leaf.center,transform.lossyScale);
            var half=Vector3.Scale(clearanceSize,transform.lossyScale)*.5f;
            float dx=Mathf.Max(0,Mathf.Abs(local.x)-half.x);
            float dz=Mathf.Max(0,Mathf.Abs(local.z)-half.z);
            float dy=Mathf.Max(0,Mathf.Abs(local.y)-segment-half.y);
            return radius-Mathf.Sqrt(dx*dx+dy*dy+dz*dz);
        }
        public override string Prompt(PlayerInteractor player)
        {
            string action=IsOpen?"E — Kapıyı kapat":"E — Kapıyı aç";
            return IsBlocked?action+"  •  Kapı önü dolu":action;
        }
        public override void Interact(PlayerInteractor player)
        {
            if(!IsOpen && Mathf.Abs(CurrentAngle)<1 && player)
            {
                var radial=WorldRotation(0)*Vector3.Scale(leaf.center,transform.lossyScale); radial.y=0;
                var tangent=Vector3.Cross(Vector3.up,radial);
                var offset=player.transform.position-(transform.position+radial);
                // Pick the swing away from the person, from either room.
                swingAngle=Mathf.Abs(openAngle)*(Vector3.Dot(tangent,offset)>0?-1:1);
            }
            IsOpen=!IsOpen; latchPending=!IsOpen;
            if(IsOpen && feedback) feedback.Door(true,transform.position+Vector3.up);
        }
    }
}
