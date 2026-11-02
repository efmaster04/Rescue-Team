using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanySim;
using UnityEngine;

namespace CompanySimEditor
{
    public static class VehicleVisualValidation
    {
        static void Check(bool condition,string message) { if(!condition) throw new Exception("Vehicle shell: "+message); }
        public static void Run(ServiceVehicle vehicle)
        {
            CaptureViews();
            var renderers=vehicle.GetComponentsInChildren<MeshRenderer>();
            Check(renderers.All(x=>!x.name.StartsWith("RestoredCargoSide")),"no mirrored overlay on the complete body");
            Check(renderers.Count(x=>x.name.Contains("V11"))==2 && renderers.Count(x=>x.name.Contains("V21"))==2,
                "both original headlamp assemblies and both complete loading doors are present");
            Check(renderers.All(x=>x.sharedMaterials.All(m=>m && m.GetFloat("_Surface")==0 && m.GetFloat("_Cull")==2)),
                "opaque depth-writing materials use the repaired outward faces");
            var seats=vehicle.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.StartsWith("Complete_V18")).ToArray();
            Check(seats.Length==2 && seats.All(f=>f.sharedMesh.vertices.All(p=>vehicle.transform.InverseTransformPoint(f.transform.TransformPoint(p)).z<-.70f)),
                "both upholstered seat backs remain ahead of the cargo bulkhead");
            var interior=vehicle.transform.Find("CabinAndCargoDetails");
            Check(interior && interior.GetComponentsInChildren<MeshRenderer>().Length==1 &&
                interior.GetComponentsInChildren<Collider>().Length==0 && interior.GetComponentsInChildren<Light>().Length==0,
                "combined cabin and cargo details introduce no walking obstacles or realtime lights");
            var temporary=new GameObject("Temporary_VanGeometryQueries");
            var colliders=new List<MeshCollider>(); var names=new Dictionary<Collider,string>();
            bool backfaces=Physics.queriesHitBackfaces;
            try
            {
                Physics.queriesHitBackfaces=false;
                foreach(var filter in vehicle.GetComponentsInChildren<MeshFilter>())
                {
                    if(!filter.sharedMesh) continue;
                    var go=new GameObject(filter.name); go.layer=2; go.transform.SetParent(temporary.transform,false);
                    go.transform.SetPositionAndRotation(filter.transform.position,filter.transform.rotation); go.transform.localScale=filter.transform.lossyScale;
                    var collider=go.AddComponent<MeshCollider>(); collider.sharedMesh=filter.sharedMesh;
                    colliders.Add(collider); names[collider]=filter.name;
                }
                Physics.SyncTransforms(); int tested=0;
                var sideShell=colliders.Where(x=>names[x].Contains("V01") || names[x].Contains("V20")).ToList();
                var roofShell=colliders.Where(x=>names[x].Contains("V01") || names[x].Contains("V19")).ToList();
                var rearShell=colliders.Where(x=>names[x].Contains("V21") || names[x].Contains("CentreSeal")).ToList();
                foreach(float side in new[]{-1f,1f})
                    for(int longitudinal=0;longitudinal<9;longitudinal++) for(int height=0;height<6;height++)
                    {
                        float z=Mathf.Lerp(-.42f,2.48f,longitudinal/8f), y=Mathf.Lerp(.84f,2.38f,height/5f);
                        var ray=new Ray(vehicle.transform.TransformPoint(new Vector3(side*3,y,z)),vehicle.transform.TransformDirection(Vector3.left*side));
                        var hit=Nearest(sideShell,ray);
                        var local=vehicle.transform.InverseTransformPoint(hit.point);
                        Check(hit.collider && Mathf.Abs(local.x)>.96f && (names[hit.collider].Contains("V01") || names[hit.collider].Contains("V20")),
                            "exterior cargo side is opaque, side="+side+", sample="+new Vector2(z,y)+", hit="+(hit.collider?names[hit.collider]:"none")+", point="+local);
                        Check(Vector3.Dot(hit.normal,vehicle.transform.right*side)>.7f,"cargo side faces outward"); tested++;
                    }
                for(int across=0;across<7;across++) for(int length=0;length<9;length++)
                {
                    float x=Mathf.Lerp(-.8f,.8f,across/6f),z=Mathf.Lerp(-.4f,2.48f,length/8f);
                    var hit=Nearest(roofShell,new Ray(vehicle.transform.TransformPoint(new Vector3(x,4,z)),Vector3.down));
                    Check(hit.collider && vehicle.transform.InverseTransformPoint(hit.point).y>2.58f && hit.normal.y>.7f,"roof has an outward upper surface"); tested++;
                }
                foreach(float side in new[]{-1f,1f}) for(int length=0;length<7;length++)
                {
                    float z=Mathf.Lerp(-.45f,2.45f,length/6f);
                    var origin=vehicle.transform.TransformPoint(new Vector3(side*1.5f,2.67f,z));
                    var target=vehicle.transform.TransformPoint(new Vector3(side*.95f,2.58f,z));
                    var hit=Nearest(sideShell,new Ray(origin,(target-origin).normalized));
                    Check(hit.collider && Mathf.Abs(vehicle.transform.InverseTransformPoint(hit.point).x)>.92f,
                        "upper cargo shoulder has a physical closing face, side="+side+", z="+z); tested++;
                }
                foreach(float x in new[]{-.80f,-.50f,-.20f,0f,.20f,.50f,.80f}) for(int height=0;height<6;height++)
                {
                    var hit=Nearest(rearShell,new Ray(vehicle.transform.TransformPoint(new Vector3(x,Mathf.Lerp(.72f,2.42f,height/5f),4)),Vector3.back));
                    Check(hit.collider && vehicle.transform.InverseTransformPoint(hit.point).z>2.64f,
                        "loading doors and centre seal close the rear, x="+x+", height="+height); tested++;
                }
                File.AppendAllText("Logs/office_garage_validation.txt","PASS: complete original van, "+tested+" exterior mesh ray samples across both cargo walls, roof and loading doors; outward faces, opaque glass/body, original lamps/latches, no mirrored body overlays\n");
            }
            finally
            {
                Physics.queriesHitBackfaces=backfaces; temporary.SetActive(false); UnityEngine.Object.Destroy(temporary);
            }
            var control=GameObject.Find("ModuleStation_ControlPanel");
            Check(control && !control.GetComponent<BoxCollider>().enabled,"module desktop no longer intercepts the lift controls");
            Physics.SyncTransforms();
            Check(Physics.Raycast(new Vector3(6.26f,1.555f,-1.1f),Vector3.forward,out var controlHit,2,~0,QueryTriggerInteraction.Collide) &&
                controlHit.collider.GetComponent<LiftButton>(),"real lift raise button can be reached with E");
            var workshop=GameObject.Find("11_WorkshopInterior");
            Check(workshop.GetComponentsInChildren<MeshRenderer>().Count(x=>x.name.StartsWith("CabinetCarcass_"))==4 &&
                workshop.GetComponentsInChildren<MeshRenderer>().Count(x=>x.name.StartsWith("Drawer_"))==16,"four cabinet bodies and sixteen drawer fronts");
            var oldControls=GameObject.Find("01_Environment_OfficeGarage").GetComponentsInChildren<MeshRenderer>()
                .Where(x=>x.name.StartsWith("T30")).ToArray();
            Check(oldControls.Length>=28 && oldControls.All(x=>!x.enabled),"superseded floor stand, emergency cap and status lamp are hidden together");
            Check(GameObject.Find("LiftControlHousing").GetComponent<BoxCollider>(),"new control housing has physical collision");
        }
        static void CaptureViews()
        {
            Capture(new Vector3(7.9f,2.30f,-4.20f),new Vector3(4.65f,1.30f,-.15f),"Documentation/VehicleCompleteFront.png",65);
            Capture(new Vector3(1.25f,1.80f,1.05f),new Vector3(4.65f,1.45f,.90f),"Documentation/VehicleCompleteLeft.png",70);
            Capture(new Vector3(8.70f,1.80f,1.30f),new Vector3(4.65f,1.45f,.95f),"Documentation/VehicleCompleteRight.png",70);
            Capture(new Vector3(4.65f,1.80f,4.10f),new Vector3(4.65f,1.46f,2.40f),"Documentation/VehicleCompleteRear.png",68);
            Capture(new Vector3(4.65f,3.65f,-1.10f),new Vector3(4.65f,2.25f,.80f),"Documentation/VehicleCompleteRoof.png",76);
            Capture(new Vector3(1.9f,2.10f,-4.20f),new Vector3(6.8f,1.38f,1.0f),"Documentation/WorkshopOverhaulWide.png",74);
            Capture(new Vector3(7.35f,1.67f,-1.3f),new Vector3(8.45f,.93f,.70f),"Documentation/WorkshopCabinets.png",72);
        }
        static RaycastHit Nearest(List<MeshCollider> colliders,Ray ray)
        {
            RaycastHit closest=default; float distance=100;
            foreach(var collider in colliders)
                if(collider.Raycast(ray,out var hit,distance)) { closest=hit; distance=hit.distance; }
            return closest;
        }
        static void Capture(Vector3 position,Vector3 target,string path,float fov)
        {
            var go=new GameObject("Temporary_VanVisualCamera"); var camera=go.AddComponent<Camera>();
            camera.transform.position=position; camera.transform.LookAt(target); camera.fieldOfView=fov; camera.nearClipPlane=.03f;
            ProjectValidation.CaptureCamera(camera,path); UnityEngine.Object.Destroy(go);
        }
    }
}
