using System.Collections.Generic;
using System.Linq;
using CompanySim;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CompanySimEditor
{
    // Work only on the van instance and generated meshes. The supplied FBX stays intact.
    static class VehicleBodyRepairBuilder
    {
        const string Folder="Assets/_Project/Art/VehicleMechanics";
        public static void Configure(ServiceVehicle vehicle,VehicleDoor[] doors)
        {
            RepairRearLeaves(vehicle,doors);
            AddInteriorFaces(vehicle);
            AddPillarTrim(vehicle);
        }
        static void RepairRearLeaves(ServiceVehicle vehicle,VehicleDoor[] doors)
        {
            var marker=vehicle.transform.Find("RearLeafGeometryRepaired");
            if(marker && doors.Where(d=>d.name.StartsWith("Rear")).All(d=>d.GetComponentsInChildren<MeshFilter>().Any(f=>f.name.StartsWith("Complete_V21_Repaired")))) return;
            if(marker) Object.DestroyImmediate(marker.gameObject);
            var sources=vehicle.GetComponentsInChildren<MeshFilter>()
                .Where(f=>f.name.StartsWith("Complete_V21")).OrderBy(f=>f.name).ToArray();
            foreach(int side in new[]{-1,1})
            {
                var door=doors.First(d=>d.name=="RearDoor_"+(side<0?"Right":"Left"));
                var pieces=new List<GameObject>();
                for(int i=0;i<sources.Length;i++)
                {
                    // A source object contains a panel on one half and hardware on the
                    // other half. Split actual triangles, not the object's bounds centre.
                    var region=new Bounds(new Vector3(side*.65f,1.55f,2.70f),new Vector3(1.30f,2.50f,.75f));
                    var piece=DoorMeshPartition.Extract(sources[i],region,vehicle.transform,door.transform,"RearLeafSplit_"+i+"_"+side);
                    if(piece) pieces.Add(piece);
                }
                var combines=new List<CombineInstance>(); var materials=new List<Material>();
                foreach(var piece in pieces)
                {
                    var filter=piece.GetComponent<MeshFilter>(); var renderer=piece.GetComponent<MeshRenderer>();
                    for(int sub=0;sub<filter.sharedMesh.subMeshCount;sub++)
                    {
                        if(filter.sharedMesh.GetIndexCount(sub)==0) continue;
                        combines.Add(new CombineInstance {mesh=filter.sharedMesh,subMeshIndex=sub,transform=door.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix});
                        materials.Add(renderer.sharedMaterials[sub]);
                    }
                }
                var mesh=new Mesh {name="RearDoorLeaf_"+side,indexFormat=IndexFormat.UInt32};
                mesh.CombineMeshes(combines.ToArray(),false,true); mesh.RecalculateBounds();
                var go=new GameObject("Complete_V21_Repaired_"+(side<0?"Right":"Left"),typeof(MeshFilter),typeof(MeshRenderer));
                go.transform.SetParent(door.transform,false);
                go.GetComponent<MeshFilter>().sharedMesh=Save(mesh,"RearDoorLeaf_"+side);
                go.GetComponent<MeshRenderer>().sharedMaterials=materials.ToArray();
                foreach(var piece in pieces) Object.DestroyImmediate(piece);
                var collider=door.GetComponent<BoxCollider>();
                collider.center=new Vector3(-side*.50f,1.56f,0); collider.size=new Vector3(1.0f,1.94f,.15f);
            }
            foreach(var source in sources) Object.DestroyImmediate(source.gameObject);
            // The overlapping weather strip belongs to one leaf; it must not stay in
            // the middle of an open load aperture.
            var seal=vehicle.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r=>r.name.Contains("RearDoorCentreSeal"));
            if(seal) seal.transform.SetParent(doors.First(d=>d.name=="RearDoor_Left").transform,true);
            var repairedMarker=new GameObject("RearLeafGeometryRepaired"); repairedMarker.transform.SetParent(vehicle.transform,false);
        }
        static void AddInteriorFaces(ServiceVehicle vehicle)
        {
            if(vehicle.transform.Find("CabinInteriorRepaired")) return;
            var liner=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Van_InnerLiner.mat");
            var sources=vehicle.GetComponentsInChildren<MeshFilter>().Where(f=>
                f.name.StartsWith("Complete_V01 |") || f.name.StartsWith("Complete_V03") ||
                f.name.StartsWith("Complete_V04") || f.name.StartsWith("Complete_V05") ||
                f.name.StartsWith("Complete_V21") || f.name.StartsWith("DoorSurface_Body") ||
                f.name.StartsWith("DoorSurface_RearBody") || f.name.StartsWith("DoorSurface_RearInterior"))
                .OrderBy(f=>f.name).ToArray();
            int number=0;
            foreach(var source in sources)
            {
                var mesh=Object.Instantiate(source.sharedMesh); mesh.name="VanInteriorFace_"+number;
                var normals=mesh.normals; var positions=mesh.vertices;
                for(int i=0;i<positions.Length;i++)
                {
                    float scale=source.transform.TransformVector(normals[i]).magnitude;
                    positions[i]-=normals[i]*(.0015f/Mathf.Max(.0001f,scale)); normals[i]=-normals[i];
                }
                mesh.vertices=positions; mesh.normals=normals;
                var tangents=mesh.tangents;
                for(int i=0;i<tangents.Length;i++) tangents[i].w=-tangents[i].w;
                if(tangents.Length>0) mesh.tangents=tangents;
                for(int sub=0;sub<mesh.subMeshCount;sub++)
                {
                    var triangles=mesh.GetTriangles(sub);
                    for(int i=0;i<triangles.Length;i+=3) {int t=triangles[i+1]; triangles[i+1]=triangles[i+2]; triangles[i+2]=t;}
                    mesh.SetTriangles(triangles,sub);
                }
                mesh.RecalculateBounds();
                var go=new GameObject("CabinInnerFace_"+number,typeof(MeshFilter),typeof(MeshRenderer));
                go.transform.SetParent(source.transform,false);
                go.GetComponent<MeshFilter>().sharedMesh=Save(mesh,"VanInteriorFace_"+number++);
                bool glass=source.name.StartsWith("Complete_V04") || source.name.StartsWith("Complete_V05");
                go.GetComponent<MeshRenderer>().sharedMaterials=glass?source.GetComponent<MeshRenderer>().sharedMaterials:
                    Enumerable.Repeat(liner,mesh.subMeshCount).ToArray();
            }
            var root=new GameObject("CabinInteriorRepaired"); root.transform.SetParent(vehicle.transform,false);
            Panel(root.transform,"CabFootwellFloor",new Vector3(0,.625f,-1.25f),new Vector3(1.78f,.035f,1.35f),liner);
            Panel(root.transform,"CabFirewall",new Vector3(0,1.04f,-1.87f),new Vector3(1.78f,.80f,.035f),liner);
        }
        static void Panel(Transform parent,string name,Vector3 position,Vector3 size,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; go.transform.SetParent(parent,false);
            go.transform.localPosition=position; go.transform.localScale=size;
            go.GetComponent<MeshRenderer>().sharedMaterial=material; Object.DestroyImmediate(go.GetComponent<Collider>());
        }
        static void AddPillarTrim(ServiceVehicle vehicle)
        {
            var root=vehicle.transform.Find("CabinInteriorRepaired");
            var liner=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Van_InnerLiner.mat");
            foreach(int side in new[]{-1,1})
            {
                string name="CabPillarInner_"+side; if(root.Find(name)) continue;
                var bottom=new Vector3(side*.90f,1.54f,-1.79f); var top=new Vector3(side*.90f,2.38f,-1.49f);
                Panel(root,name,(bottom+top)*.5f,new Vector3(.20f,Vector3.Distance(bottom,top),.12f),liner);
                root.Find(name).localRotation=Quaternion.FromToRotation(Vector3.up,top-bottom);
            }
        }
        static Mesh Save(Mesh mesh,string key)
        {
            return GeneratedMeshAsset.Store(mesh,Folder+"/"+key+".asset");
        }
    }
}
