using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanySim;
using UnityEngine;

namespace CompanySimEditor
{
    static class VehicleInteriorValidation
    {
        static void Check(bool value,string message) {if(!value) throw new Exception("Vehicle interior: "+message);}
        public static void CheckRearOwnership(ServiceVehicle vehicle)
        {
            int vertices=0;
            foreach(var door in vehicle.doors.Where(d=>d.name.StartsWith("Rear")))
            {
                int side=door.name.EndsWith("Right")?-1:1;
                var leaf=door.GetComponentsInChildren<MeshFilter>().Single(f=>f.name.StartsWith("Complete_V21"));
                foreach(var point in leaf.sharedMesh.vertices)
                {
                    var local=vehicle.transform.InverseTransformPoint(leaf.transform.TransformPoint(point));
                    Check(local.x*side>=-.002f,"panel and latch geometry must belong to the same half: "+door.name+" point "+local);
                    vertices++;
                }
                Check(leaf.sharedMesh.vertexCount>30,"original panel and hardware are retained on "+door.name);
            }
            File.AppendAllText("Logs/vehicle_exterior_fix_checks.txt","PASS: "+vertices+" rear panel/hardware vertices belong to their own door half.\n");
        }
        public static void CheckInterior(ServiceVehicle vehicle)
        {
            var temporary=new GameObject("Temporary_InteriorMeshQueries");
            var colliders=new List<MeshCollider>(); bool old=Physics.queriesHitBackfaces;
            try
            {
                Physics.queriesHitBackfaces=false;
                foreach(var filter in vehicle.GetComponentsInChildren<MeshFilter>())
                {
                    if(!filter.sharedMesh || filter.sharedMesh.vertexCount==0 || !filter.GetComponent<MeshRenderer>().enabled) continue;
                    var go=new GameObject(filter.name); go.layer=2; go.transform.SetParent(temporary.transform,false);
                    go.transform.SetPositionAndRotation(filter.transform.position,filter.transform.rotation); go.transform.localScale=filter.transform.lossyScale;
                    var mesh=go.AddComponent<MeshCollider>(); mesh.sharedMesh=filter.sharedMesh; colliders.Add(mesh);
                }
                Physics.SyncTransforms(); int tested=0;
                // Both cargo sides, roof, floor and cab partition must close from within.
                foreach(float z in new[]{-.30f,.25f,.80f,1.35f,1.90f,2.30f})
                    foreach(float y in new[]{.85f,1.20f,1.60f,2.05f,2.35f})
                        foreach(float side in new[]{-1f,1f})
                            Sample(vehicle,colliders,new Vector3(0,y,z),Vector3.right*side,1.06f,"cargo side",ref tested);
                foreach(float z in new[]{-.30f,.5f,1.3f,2.15f}) foreach(float x in new[]{-.65f,0,.65f})
                {
                    Sample(vehicle,colliders,new Vector3(x,1.75f,z),Vector3.up,1.01f,"cargo roof",ref tested);
                    Sample(vehicle,colliders,new Vector3(x,1.4f,z),Vector3.down,1.0f,"cargo floor",ref tested);
                }
                foreach(float x in new[]{-.70f,0,.70f}) foreach(float y in new[]{.85f,1.45f,2.20f})
                    Sample(vehicle,colliders,new Vector3(x,y,.10f),Vector3.back,.87f,"cab partition",ref tested);
                // The front doors remain closed for these inward-facing shell samples.
                foreach(float y in new[]{.90f,1.25f,1.80f,2.10f}) foreach(float side in new[]{-1f,1f})
                    Sample(vehicle,colliders,new Vector3(0,y,-1.10f),Vector3.right*side,1.08f,"opposite cab door/window",ref tested);
                foreach(float x in new[]{-.65f,0,.65f})
                {
                    Sample(vehicle,colliders,new Vector3(x,1.5f,-1.20f),Vector3.down,1.0f,"cab footwell",ref tested);
                    Sample(vehicle,colliders,new Vector3(x,1.9f,-1.20f),Vector3.up,.70f,"cab roof",ref tested);
                    Sample(vehicle,colliders,new Vector3(x,1.90f,-1.25f),Vector3.back,.85f,"windscreen interior",ref tested);
                }
                foreach(float side in new[]{-1f,1f}) foreach(float y in new[]{1.60f,1.78f,1.96f,2.14f,2.30f})
                {
                    var point=new Vector3(0,y,-1.10f);
                    var corner=new Vector3(side*.90f,y,Mathf.Lerp(-1.79f,-1.49f,(y-1.54f)/.84f));
                    Sample(vehicle,colliders,point,(corner-point).normalized,(corner-point).magnitude+.10f,"windscreen pillar return",ref tested);
                }
                File.AppendAllText("Logs/vehicle_exterior_fix_checks.txt","PASS: "+tested+" inward mesh ray samples hit opaque vehicle surfaces with backface hits disabled.\n");
            }
            finally {Physics.queriesHitBackfaces=old; temporary.SetActive(false); UnityEngine.Object.Destroy(temporary);}
        }
        static void Sample(ServiceVehicle vehicle,List<MeshCollider> colliders,Vector3 point,Vector3 direction,float distance,string name,ref int tested)
        {
            var ray=new Ray(vehicle.transform.TransformPoint(point),vehicle.transform.TransformDirection(direction));
            RaycastHit nearest=default; float closest=distance;
            foreach(var mesh in colliders) if(mesh.Raycast(ray,out var hit,closest)) {nearest=hit; closest=hit.distance;}
            Check(nearest.collider,name+" is opaque from inside at "+point+" toward "+direction);
            Check(Vector3.Dot(nearest.normal,ray.direction)<-.2f,name+" faces the interior"); tested++;
        }
        public static void Capture(ServiceVehicle vehicle,string name,Vector3 localPosition,Vector3 localTarget)
        {
            var go=new GameObject("Temporary_VehicleRepairCamera"); var camera=go.AddComponent<Camera>();
            camera.transform.position=vehicle.transform.TransformPoint(localPosition); camera.transform.LookAt(vehicle.transform.TransformPoint(localTarget));
            camera.fieldOfView=65; camera.nearClipPlane=.025f;
            ProjectValidation.CaptureCamera(camera,"Documentation/"+name+".png"); UnityEngine.Object.Destroy(go);
        }
    }
}
