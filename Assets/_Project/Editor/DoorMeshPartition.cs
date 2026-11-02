using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CompanySimEditor
{
    // Split existing paint/trim triangles at the authored door opening. At rest the
    // two meshes cover exactly the source surface; the supplied FBX is never edited.
    static class DoorMeshPartition
    {
        struct Vertex
        {
            public Vector3 p,q,n; public Vector2 uv;
            public static Vertex Lerp(Vertex a,Vertex b,float t) => new Vertex {p=Vector3.Lerp(a.p,b.p,t),q=Vector3.Lerp(a.q,b.q,t),n=Vector3.Lerp(a.n,b.n,t).normalized,uv=Vector2.Lerp(a.uv,b.uv,t)};
        }
        sealed class Output
        {
            public readonly List<Vector3> positions=new List<Vector3>(),normals=new List<Vector3>();
            public readonly List<Vector2> uvs=new List<Vector2>();
            public readonly List<int>[] triangles;
            public Output(int count) {triangles=new List<int>[count]; for(int i=0;i<count;i++) triangles[i]=new List<int>();}
            public void Add(List<Vertex> polygon,int sub)
            {
                for(int j=1;j+1<polygon.Count;j++)
                {
                    var a=polygon[0]; var b=polygon[j]; var c=polygon[j+1];
                    if(Vector3.Cross(b.p-a.p,c.p-a.p).sqrMagnitude<1e-14f) continue;
                    foreach(var v in new[]{a,b,c}) {triangles[sub].Add(positions.Count); positions.Add(v.p); normals.Add(v.n); uvs.Add(v.uv);}
                }
            }
            public Mesh Mesh(string name)
            {
                var mesh=new Mesh {name=name,indexFormat=positions.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
                mesh.SetVertices(positions); mesh.SetNormals(normals); mesh.SetUVs(0,uvs); mesh.subMeshCount=triangles.Length;
                for(int i=0;i<triangles.Length;i++) mesh.SetTriangles(triangles[i],i);
                mesh.RecalculateBounds(); return mesh;
            }
        }
        static List<Vertex> Clip(List<Vertex> input,Plane plane,bool inside)
        {
            var result=new List<Vertex>(); if(input.Count==0) return result;
            var previous=input[input.Count-1]; float old=plane.GetDistanceToPoint(previous.q); bool oldInside=inside?old>=0:old<=0;
            foreach(var current in input)
            {
                float distance=plane.GetDistanceToPoint(current.q); bool currentInside=inside?distance>=0:distance<=0;
                if(currentInside!=oldInside) result.Add(Vertex.Lerp(previous,current,old/(old-distance)));
                if(currentInside) result.Add(current);
                previous=current; old=distance; oldInside=currentInside;
            }
            return result;
        }
        public static GameObject Extract(MeshFilter source,Bounds region,Transform regionSpace,Transform door,string key,bool facingZOnly=false)
        {
            var mesh=source.sharedMesh; var vertices=mesh.vertices; var normals=mesh.normals; var uvs=mesh.uv;
            var inside=new Output(mesh.subMeshCount); var outside=new Output(mesh.subMeshCount);
            var planes=new[]{new Plane(Vector3.right,region.min),new Plane(Vector3.left,region.max),
                new Plane(Vector3.up,region.min),new Plane(Vector3.down,region.max),new Plane(Vector3.forward,region.min),new Plane(Vector3.back,region.max)};
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                var indices=mesh.GetTriangles(sub);
                for(int i=0;i<indices.Length;i+=3)
                {
                    var polygon=new List<Vertex>();
                    for(int j=0;j<3;j++) {int index=indices[i+j]; polygon.Add(new Vertex {p=vertices[index],q=regionSpace.InverseTransformPoint(source.transform.TransformPoint(vertices[index])),n=normals.Length>index?normals[index]:Vector3.up,uv=uvs.Length>index?uvs[index]:Vector2.zero});}
                    if(facingZOnly && Mathf.Abs(Vector3.Cross(polygon[1].q-polygon[0].q,polygon[2].q-polygon[0].q).normalized.z)<.8f) {outside.Add(polygon,sub); continue;}
                    foreach(var plane in planes)
                    {
                        outside.Add(Clip(polygon,plane,false),sub); polygon=Clip(polygon,plane,true);
                        if(polygon.Count<3) break;
                    }
                    inside.Add(polygon,sub);
                }
            }
            if(inside.positions.Count==0) return null;
            const string folder="Assets/_Project/Art/VehicleMechanics";
            System.IO.Directory.CreateDirectory(folder);
            var rest=outside.Mesh(key+"_Fixed"); var moving=inside.Mesh(key+"_Moving");
            Save(rest,folder+"/"+key+"_Fixed.asset"); Save(moving,folder+"/"+key+"_Moving.asset"); source.sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(folder+"/"+key+"_Fixed.asset");
            var go=new GameObject("DoorSurface_"+key,typeof(MeshFilter),typeof(MeshRenderer));
            go.transform.SetParent(source.transform.parent,false); go.transform.localPosition=source.transform.localPosition;
            go.transform.localRotation=source.transform.localRotation; go.transform.localScale=source.transform.localScale;
            go.GetComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(folder+"/"+key+"_Moving.asset");
            go.GetComponent<MeshRenderer>().sharedMaterials=source.GetComponent<MeshRenderer>().sharedMaterials;
            go.transform.SetParent(door,true); return go;
        }
        static void Save(Mesh mesh,string path)
        {
            GeneratedMeshAsset.Store(mesh,path);
        }
    }
}
