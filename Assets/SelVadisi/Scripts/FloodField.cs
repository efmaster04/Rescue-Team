using System.Collections.Generic;
using UnityEngine;

namespace SelVadisi
{
    /// Holds the obstacles (houses, poles, piers, trees ...) that bend the flood current
    /// and hands them to <see cref="Valley"/> so floating debris steers around them.
    [ExecuteAlways]
    public class FloodField : MonoBehaviour
    {
        public List<FlowObstacle> obstacles = new List<FlowObstacle>();

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        public void Apply() => Valley.SetObstacles(obstacles);

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.6f);
            foreach (var o in obstacles)
            {
                float y = Valley.WaterLevel(o.z);
                Gizmos.DrawWireSphere(new Vector3(o.x, y, o.z), o.r);
            }
        }
    }
}
