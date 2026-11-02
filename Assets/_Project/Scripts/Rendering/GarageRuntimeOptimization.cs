using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CompanySim
{
    public sealed class GarageRuntimeOptimization : MonoBehaviour
    {
        public GarageLift lift;
        public bool autoCombine=true;
        public int CombinedRendererCount { get; private set; }
        public bool Ready { get; private set; }
        IEnumerator Start()
        {
            // Finish dynamic door/lift initialization before selecting immovable meshes.
            yield return null;
#if UNITY_EDITOR
            if(UnityEditor.SessionState.GetBool("CompanyGarageBenchmark",false)) yield break;
#endif
            if(autoCombine) Combine();
        }
        public void Combine()
        {
            if(Ready) return;
            var candidates=new List<GameObject>();
            foreach(var renderer in FindObjectsByType<MeshRenderer>())
            {
                if(!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.isPartOfStaticBatch) continue;
                if(renderer.GetComponentInParent<InteractableBehaviour>() || renderer.GetComponentInParent<ServiceVehicle>()) continue;
                if(lift && lift.Owns(renderer.transform)) continue;
                var filter=renderer.GetComponent<MeshFilter>();
                if(!filter || !filter.sharedMesh || !filter.sharedMesh.isReadable || filter.sharedMesh.vertexCount==0) continue;
                candidates.Add(renderer.gameObject);
            }
            if(candidates.Count>0) StaticBatchingUtility.Combine(candidates.ToArray(),gameObject);
            foreach(var go in candidates) if(go.GetComponent<MeshRenderer>().isPartOfStaticBatch) CombinedRendererCount++;
            Ready=true;
        }
    }
}
