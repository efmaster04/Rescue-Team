using UnityEngine;

namespace CompanySim
{
    [RequireComponent(typeof(BoxCollider),typeof(Rigidbody))]
    public sealed class VehicleDoor : InteractableBehaviour
    {
        public string displayName="Araç kapısı";
        public float openAngle=75,speed=95;
        public ServiceVehicle vehicle;
        public GarageLift lift;
        public BoxCollider accessoryCollision;
        public bool IsOpen { get; private set; }
        public bool IsBlocked { get; private set; }
        public string BlockingObstacle { get; private set; }
        public float CurrentAngle { get; private set; }
        public bool IsClosed => !IsOpen && Mathf.Abs(CurrentAngle)<.1f;
        Quaternion closedRotation;
        Rigidbody body;
        BoxCollider leaf;
        BoxCollider[] movingShapes;
        readonly Collider[] overlaps=new Collider[48];
        void Awake()
        {
            body=GetComponent<Rigidbody>(); leaf=GetComponent<BoxCollider>(); closedRotation=transform.localRotation;
            body.isKinematic=true; body.useGravity=false; body.interpolation=RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
            movingShapes=accessoryCollision?new[]{leaf,accessoryCollision}:new[]{leaf};
        }
        Quaternion Rotation(float angle) => transform.parent.rotation*closedRotation*Quaternion.Euler(0,angle,0);
        void FixedUpdate()
        {
            float target=IsOpen?openAngle:0;
            if(Mathf.Abs(CurrentAngle-target)<.01f) { IsBlocked=false; return; }
            float next=Mathf.MoveTowards(CurrentAngle,target,speed*Time.fixedDeltaTime);
            int steps=Mathf.Max(1,Mathf.CeilToInt(Mathf.Abs(next-CurrentAngle)));
            float start=CurrentAngle; IsBlocked=false; BlockingObstacle=null;
            for(int i=1;i<=steps;i++)
            {
                float candidate=Mathf.Lerp(start,next,i/(float)steps);
                var rotation=Rotation(candidate);
                bool blocked=false;
                foreach(var shape in movingShapes)
                {
                    if(!shape || !shape.enabled) continue;
                    PoseAt(shape,rotation,out var origin,out var shapeRotation);
                    PoseAt(shape,Rotation(CurrentAngle),out var oldOrigin,out var oldRotation);
                    var center=origin+shapeRotation*Vector3.Scale(shape.center,shape.transform.lossyScale);
                    int count=Physics.OverlapBoxNonAlloc(center,Vector3.Scale(shape.size,shape.transform.lossyScale)*.5f+Vector3.one*.015f,overlaps,shapeRotation,~0,QueryTriggerInteraction.Ignore);
                    blocked=count==overlaps.Length;
                    for(int j=0;j<count && !blocked;j++)
                    {
                        var obstacle=overlaps[j]; if(obstacle.transform.IsChildOf(vehicle.transform)) continue;
                        if(obstacle is CharacterController controller)
                        {
                            float depthNow=CapsuleDepth(controller,shape,origin,shapeRotation),depthBefore=CapsuleDepth(controller,shape,oldOrigin,oldRotation);
                            if(depthNow>0 && !(depthBefore>0 && depthNow<depthBefore-.0001f)) blocked=true;
                            if(blocked) BlockingObstacle=obstacle.name;
                            continue;
                        }
                        if(!Physics.ComputePenetration(shape,origin,shapeRotation,obstacle,obstacle.transform.position,obstacle.transform.rotation,out _,out float depth)) continue;
                        bool retreat=Physics.ComputePenetration(shape,oldOrigin,oldRotation,obstacle,obstacle.transform.position,obstacle.transform.rotation,out _,out float oldDepth) && depth<oldDepth-.0001f;
                        if(!retreat) {blocked=true; BlockingObstacle=obstacle.name;}
                    }
                    if(blocked) break;
                }
                if(blocked) { IsBlocked=true; break; }
                CurrentAngle=candidate;
            }
            body.MoveRotation(Rotation(CurrentAngle));
        }
        void PoseAt(BoxCollider shape,Quaternion rootRotation,out Vector3 origin,out Quaternion rotation)
        {
            var inverse=Quaternion.Inverse(transform.rotation);
            origin=body.position+rootRotation*(inverse*(shape.transform.position-transform.position));
            rotation=rootRotation*inverse*shape.transform.rotation;
        }
        static float CapsuleDepth(CharacterController controller,BoxCollider shape,Vector3 origin,Quaternion rotation)
        {
            var scale=controller.transform.lossyScale;
            float radius=controller.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z))+controller.skinWidth*.5f;
            float segment=Mathf.Max(0,controller.height*Mathf.Abs(scale.y)*.5f-radius);
            var local=Quaternion.Inverse(rotation)*(controller.transform.TransformPoint(controller.center)-origin)-Vector3.Scale(shape.center,shape.transform.lossyScale);
            var half=Vector3.Scale(shape.size,shape.transform.lossyScale)*.5f+Vector3.one*.015f;
            float dx=Mathf.Max(0,Mathf.Abs(local.x)-half.x),dz=Mathf.Max(0,Mathf.Abs(local.z)-half.z),dy=Mathf.Max(0,Mathf.Abs(local.y)-segment-half.y);
            return radius-Mathf.Sqrt(dx*dx+dy*dy+dz*dz);
        }
        public override string Prompt(PlayerInteractor player)
        {
            if(lift && !lift.IsLowered) return "Kapı için lifti indirin";
            return "E — "+displayName+(IsOpen?" kapat":" aç")+(IsBlocked?" · Kapının önünü boşaltın":"");
        }
        public override void Interact(PlayerInteractor player)
        {
            if(lift && !lift.IsLowered) { if(player) player.ui.Toast("Kapıları kullanmadan önce lifti indirin."); return; }
            IsOpen=!IsOpen;
        }
    }
}
