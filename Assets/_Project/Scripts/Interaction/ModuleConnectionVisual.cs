using UnityEngine;
using UnityEngine.Rendering;

namespace CompanySim
{
    public sealed class ModuleConnectionVisual : MonoBehaviour
    {
        public ManualModuleSystem system;
        public Material cableMaterial,plugMaterial;
        public Transform connector;
        public bool Connected { get; private set; }
        public Vector3 EndPoint => cable?transform.TransformPoint(cable.GetPosition(cable.positionCount-1)):transform.position;
        LineRenderer cable;
        Transform plug;
        void Start()
        {
            var go=new GameObject("ModuleServiceCable"); go.transform.SetParent(transform,false); cable=go.AddComponent<LineRenderer>();
            cable.sharedMaterial=cableMaterial; cable.useWorldSpace=false; cable.positionCount=12;
            cable.startWidth=cable.endWidth=.014f; cable.numCapVertices=3; cable.numCornerVertices=2;
            cable.generateLightingData=true; cable.shadowCastingMode=ShadowCastingMode.On; cable.receiveShadows=true;
            var head=GameObject.CreatePrimitive(PrimitiveType.Cylinder); head.name="ServiceCablePlug"; plug=head.transform; plug.SetParent(transform,false);
            plug.localRotation=Quaternion.Euler(90,0,0); plug.localScale=new Vector3(.035f,.030f,.035f);
            head.GetComponent<Renderer>().sharedMaterial=plugMaterial; Destroy(head.GetComponent<Collider>());
            system.Changed+=Refresh; Refresh();
        }
        void OnDestroy() {if(system) system.Changed-=Refresh;}
        void Refresh()
        {
            if(!cable) return;
            Connected=system.Current && system.Current.record.connected;
            var start=connector?transform.InverseTransformPoint(connector.position+connector.forward*.025f):new Vector3(-.43f,1.96f,.465f);
            var port=system.Current?system.Current.transform.Find("ServicePort"):null;
            var end=Connected && port?transform.InverseTransformPoint(port.position):start+new Vector3(.08f,-.20f,.06f);
            var control=(start+end)*.5f+new Vector3(0,-.12f,.10f);
            for(int i=0;i<cable.positionCount;i++)
            {
                float t=i/(float)(cable.positionCount-1),u=1-t;
                cable.SetPosition(i,u*u*start+2*u*t*control+t*t*end);
            }
            plug.localPosition=end; plug.localRotation=Quaternion.Euler(90,0,0);
        }
    }
}
