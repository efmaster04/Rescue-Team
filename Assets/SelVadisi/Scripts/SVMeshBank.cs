using UnityEngine;

namespace SelVadisi
{
    /// Container asset for the generated meshes. Binary serialization keeps the map's files compact
    /// even though the project uses text serialization.
    [PreferBinarySerialization]
    public class SVMeshBank : ScriptableObject
    {
        public int meshCount;
    }
}
