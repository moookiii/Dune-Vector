using UnityEngine;

namespace DuneVector
{
    /// <summary>
    /// Marks a streamed desert chunk's terrain collider so ground-bound agents can
    /// tell the dune surface apart from the solid meshes they must not drive through.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DesertTerrainSurface : MonoBehaviour
    {
    }
}
