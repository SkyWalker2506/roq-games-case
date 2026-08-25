using UnityEngine;

namespace Case4
{
    /// <summary>Identity marker for a whole rigidbody cube in the Buca stack.</summary>
    [DisallowMultipleComponent]
    public sealed class BucaStackBlock : MonoBehaviour
    {
        [System.NonSerialized] public GreenBlockShatter owner;
        [System.NonSerialized] public int index;
    }
}
