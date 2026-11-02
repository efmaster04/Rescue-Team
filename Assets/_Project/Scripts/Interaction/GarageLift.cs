using System.Collections.Generic;
using UnityEngine;

namespace CompanySim
{
    public sealed class GarageLift : MonoBehaviour
    {
        public ServiceVehicle vehicle;
        public Transform[] movingParts;
        public VehicleDoor[] doors;
        public ModuleStation moduleStation;
        public float maximumHeight=.85f,speed=.28f;
        public float Height { get; private set; }
        public float CarriageHeight { get; private set; }
        public bool IsLowered => !IsMoving && CarriageHeight<.005f;
        public bool IsMoving => direction!=0;
        public bool IsBlocked { get; private set; }
        public string BlockingReason { get; private set; }
        int direction;
        Vector3 vehicleBase;
        BoxCollider exteriorCarrier;
        Vector3[] partBases;
        readonly HashSet<Transform> movable=new HashSet<Transform>();
        readonly Collider[] overlaps=new Collider[80];
        public bool Owns(Transform part) => movable.Contains(part);
        void Awake()
        {
            vehicleBase=vehicle.transform.position; partBases=new Vector3[movingParts.Length];
            if(vehicle.manualModules) exteriorCarrier=vehicle.manualModules.carrierCollision;
            for(int i=0;i<movingParts.Length;i++) { partBases[i]=movingParts[i].position; movable.Add(movingParts[i]); }
        }
        public bool RequestMotion(int requested,PlayerInteractor player)
        {
            requested=requested>0?1:-1;
            if(direction==requested) { Stop(); return true; }
            if(!CanStart(requested,out var reason)) {Notify(player,reason); return false;}
            direction=requested; IsBlocked=false; BlockingReason=null; return true;
        }
        public bool CanStart(int requested,out string reason)
        {
            reason=null;
            if(!vehicle || (vehicle.manualModules && !vehicle.manualModules.Initialized)) reason="Lift hazırlanıyor";
            else if(moduleStation && moduleStation.Busy) reason="Modül işleminin bitmesini bekleyin";
            // An empty carrier is safe. Only a package actually resting on the
            // carrier needs its fasteners and connection completed.
            else if(vehicle.manualModules && vehicle.manualModules.Current && !vehicle.manualModules.IsSecured) reason="Takılı modülün bağlantılarını tamamlayın";
            else
            {
                foreach(var door in doors) if(door && !door.IsClosed) {reason="Aracın tüm kapılarını kapatın"; break;}
                if(reason==null && requested>0 && CarriageHeight>=maximumHeight+.34f-.001f) reason="Lift üst konumda";
                if(reason==null && requested<0 && CarriageHeight<=.001f) reason="Lift zeminde";
            }
            return reason==null;
        }
        void Notify(PlayerInteractor player,string value) { if(player && player.ui) player.ui.Toast(value); }
        void FixedUpdate()
        {
            if(direction==0) return;
            float carriage=Mathf.Clamp(CarriageHeight+direction*speed*Time.fixedDeltaTime,0,maximumHeight+.34f);
            float next=Mathf.Max(0,carriage-.34f);
            var center=vehicleBase+Vector3.up*(next+1.35f)+vehicle.transform.forward*-.12f;
            BlockingReason=null;
            IsBlocked=Obstructed(center,new Vector3(1.02f,1.36f,2.93f),vehicle.transform.rotation);
            // Wheels protrude beyond the body only at the axles. A single wide
            // box falsely included the safe operator space beside the control panel.
            for(int axle=0;axle<2 && !IsBlocked;axle++) for(int side=-1;side<=1 && !IsBlocked;side+=2)
            {
                var wheel=vehicleBase+vehicle.transform.rotation*new Vector3(side*.94f,next+.42f,axle==0?-1.96f:1.78f);
                IsBlocked=Obstructed(wheel,new Vector3(.30f,.43f,.43f),vehicle.transform.rotation);
            }
            if(!IsBlocked && exteriorCarrier)
            {
                // Check the exterior carrier separately. Enlarging the whole van sweep
                // also covers the operator standing at the front lift controls.
                var bounds=exteriorCarrier.bounds;
                IsBlocked=Obstructed(bounds.center+Vector3.up*(next-Height),bounds.extents+Vector3.one*.10f,Quaternion.identity);
            }
            if(!IsBlocked && vehicle.rearStepCollision)
            {
                var bounds=vehicle.rearStepCollision.bounds;
                IsBlocked=Obstructed(bounds.center+Vector3.up*(next-Height),bounds.extents+Vector3.one*.04f,Quaternion.identity);
            }
            if(IsBlocked) return;
            CarriageHeight=carriage; Height=next; vehicle.transform.position=vehicleBase+Vector3.up*Height;
            for(int i=0;i<movingParts.Length;i++) movingParts[i].position=partBases[i]+Vector3.up*CarriageHeight;
            if(CarriageHeight<=.001f || CarriageHeight>=maximumHeight+.34f-.001f) Stop();
        }
        bool Obstructed(Vector3 center,Vector3 half,Quaternion rotation)
        {
            int count=Physics.OverlapBoxNonAlloc(center,half,overlaps,rotation,~0,QueryTriggerInteraction.Ignore);
            if(count==overlaps.Length) {BlockingReason="Hareket alanını boşaltın"; return true;}
            for(int i=0;i<count;i++)
            {
                var obstacle=overlaps[i]; if(obstacle.transform.IsChildOf(vehicle.transform) || Owns(obstacle.transform)) continue;
                if(obstacle is CharacterController) {BlockingReason="Aracın hareket alanından çıkın"; return true;}
                var item=obstacle.GetComponentInParent<CarryableItem>();
                if(item) {BlockingReason=item.DisplayName+" hareket alanında"; return true;}
            }
            return false;
        }
        public void Stop() { direction=0; IsBlocked=false; BlockingReason=null; }
    }
}
