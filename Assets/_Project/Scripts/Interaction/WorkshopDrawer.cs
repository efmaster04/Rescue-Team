using UnityEngine;

namespace CompanySim
{
    [RequireComponent(typeof(BoxCollider),typeof(Rigidbody))]
    public sealed class WorkshopDrawer : InteractableBehaviour
    {
        public float travel=.36f;
        public bool IsOpen { get; private set; }
        public bool IsBlocked { get; private set; }
        public float Extension { get; private set; }
        Vector3 closedPosition;
        Rigidbody body;
        BoxCollider box;
        AudioFeedback feedback;
        float velocity;
        readonly Collider[] hits=new Collider[40];
        void Awake()
        {
            box=GetComponent<BoxCollider>(); body=GetComponent<Rigidbody>(); closedPosition=body.position;
            body.isKinematic=true; body.useGravity=false; body.interpolation=RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
        }
        void Start() { feedback=FindAnyObjectByType<AudioFeedback>(); }
        void FixedUpdate()
        {
            float target=IsOpen?travel:0;
            if(Mathf.Abs(target-Extension)<.0005f) { Extension=target; velocity=0; IsBlocked=false; return; }
            float next=Mathf.SmoothDamp(Extension,target,ref velocity,.10f,.65f,Time.fixedDeltaTime);
            var position=closedPosition-transform.right*next;
            var rotation=body.rotation;
            var center=position+rotation*Vector3.Scale(box.center,transform.lossyScale);
            // CharacterController's query shape is slightly smaller than its
            // visible capsule. Gather generously, then use the precise capsule
            // distance below to stop the tray before physical contact.
            int count=Physics.OverlapBoxNonAlloc(center,Vector3.Scale(box.size,transform.lossyScale)*.5f+
                new Vector3(.12f,.01f,.12f),hits,rotation,~0,QueryTriggerInteraction.Ignore);
            IsBlocked=count==hits.Length;
            for(int i=0;i<count;i++)
            {
                var hit=hits[i]; if(hit.transform.IsChildOf(transform)) continue;
                if(!(hit is CharacterController) && !hit.attachedRigidbody) continue;
                if(hit is CharacterController controller)
                {
                    float depth=CapsuleDepth(controller,position);
                    if(depth<=0 || (CapsuleDepth(controller,body.position)>depth+.0001f)) continue;
                    IsBlocked=true; break;
                }
                if(!Physics.ComputePenetration(box,position,rotation,hit,hit.transform.position,hit.transform.rotation,out _,out float depthNow)) continue;
                bool overlaps=Physics.ComputePenetration(box,body.position,rotation,hit,hit.transform.position,hit.transform.rotation,out _,out float depthBefore);
                if(overlaps && depthNow<depthBefore-.0001f) continue;
                IsBlocked=true; break;
            }
            if(IsBlocked) { velocity=0; return; }
            Extension=next; body.MovePosition(position);
        }
        float CapsuleDepth(CharacterController controller,Vector3 position)
        {
            var scale=controller.transform.lossyScale;
            float radius=controller.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z))+controller.skinWidth*.5f;
            float segment=Mathf.Max(0,controller.height*Mathf.Abs(scale.y)*.5f-radius);
            var local=Quaternion.Inverse(body.rotation)*(controller.transform.TransformPoint(controller.center)-position)
                -Vector3.Scale(box.center,transform.lossyScale);
            var half=Vector3.Scale(box.size,transform.lossyScale)*.5f+new Vector3(.015f,0,.005f);
            var delta=new Vector3(Mathf.Max(0,Mathf.Abs(local.x)-half.x),Mathf.Max(0,Mathf.Abs(local.y)-segment-half.y),Mathf.Max(0,Mathf.Abs(local.z)-half.z));
            return radius-delta.magnitude;
        }
        public override string Prompt(PlayerInteractor player)
            => (IsOpen?"E — Çekmeceyi kapat":"E — Çekmeceyi aç")+(IsBlocked?"  •  Önü dolu":"");
        public override void Interact(PlayerInteractor player)
        {
            IsOpen=!IsOpen;
            if(feedback) feedback.Drawer(transform.position);
        }
    }
}
