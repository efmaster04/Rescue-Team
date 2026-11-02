using UnityEditor;
using UnityEngine;

namespace CompanySimEditor
{
    static class GeneratedMeshAsset
    {
        public static Mesh Store(Mesh mesh,string path)
        {
            var target=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!target) {AssetDatabase.CreateAsset(mesh,path); return mesh;}
            // Mesh setters invalidate the live GPU buffers. CopySerialized alone can
            // leave an open Editor rendering the previous vertex/index layout.
            target.Clear(false); target.indexFormat=mesh.indexFormat; target.name=mesh.name;
            target.SetVertices(mesh.vertices);
            if(mesh.normals.Length==mesh.vertexCount) target.SetNormals(mesh.normals);
            if(mesh.tangents.Length==mesh.vertexCount) target.SetTangents(mesh.tangents);
            if(mesh.uv.Length==mesh.vertexCount) target.SetUVs(0,mesh.uv);
            if(mesh.uv2.Length==mesh.vertexCount) target.SetUVs(1,mesh.uv2);
            if(mesh.colors32.Length==mesh.vertexCount) target.SetColors(mesh.colors32);
            target.subMeshCount=mesh.subMeshCount;
            for(int sub=0;sub<mesh.subMeshCount;sub++) target.SetIndices(mesh.GetIndices(sub),mesh.GetTopology(sub),sub,false);
            target.bounds=mesh.bounds; target.UploadMeshData(false);
            EditorUtility.SetDirty(target); Object.DestroyImmediate(mesh); return target;
        }
    }
}
