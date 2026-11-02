using UnityEngine;
using System.Linq;

namespace CompanySim
{
    public sealed class UpgradeVisual : MonoBehaviour
    {
        public string upgradeId;
        [HideInInspector] public int storageLayoutVersion;

        public void PrepareLayout()
        {
            if(upgradeId!="storage"||storageLayoutVersion>=3)return;
            var parts=GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.name.StartsWith("G01_",System.StringComparison.Ordinal)).ToArray();
            if(parts.Length==0)return;
            // This runs before activation/batching, including when loading an old company save.
            if(parts.Any(r=>r.isPartOfStaticBatch))return;
            Bounds LocalBounds(MeshRenderer renderer)
            {
                var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
                var matrix=transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
                var b=mesh.bounds;var result=new Bounds(matrix.MultiplyPoint3x4(b.center),Vector3.zero);
                foreach(float x in new[]{-1f,1f})foreach(float y in new[]{-1f,1f})foreach(float z in new[]{-1f,1f})
                    result.Encapsulate(matrix.MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3(x,y,z))));
                return result;
            }
            // G01 contains three complete source bays. Only the first bay is one upgrade.
            var first=parts.Where(r=>LocalBounds(r).center.x<2.6f).ToArray();
            if(first.Length<20)return;
            var bounds=LocalBounds(first[0]);foreach(var r in first.Skip(1))bounds.Encapsulate(LocalBounds(r));
            if(bounds.size.x>2.3f||bounds.size.y>2.5f||bounds.size.z>.9f)return;
            foreach(var r in parts)r.gameObject.SetActive(first.Contains(r));
            // A compact bay fits the solid wall between the office door and the
            // existing plant. Keep the shutter and the main garage aisle clear.
            var scale=new Vector3(.58f,.78f,.85f);var rotation=Quaternion.Euler(0,-90,0);transform.localScale=scale;transform.rotation=rotation;
            transform.position=new Vector3(.50f,.01f,-1.50f)-rotation*Vector3.Scale(new Vector3(bounds.center.x,bounds.min.y,bounds.center.z),scale);
            var collision=GetComponent<BoxCollider>();if(!collision)collision=gameObject.AddComponent<BoxCollider>();
            collision.center=bounds.center;collision.size=bounds.size;
            storageLayoutVersion=3;
        }
    }
}
