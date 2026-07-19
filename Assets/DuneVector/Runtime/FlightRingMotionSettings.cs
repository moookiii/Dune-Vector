using UnityEngine;

namespace DuneVector
{
    [CreateAssetMenu(fileName = "Flight Ring Motion Settings", menuName = "Dune Vector/Flight Ring Motion Settings")]
    public sealed class FlightRingMotionSettings : ScriptableObject
    {
        [Header("Flight Mode Height Offset")]
        [Min(0f)] public float MinimumHeightOffset;
        [Min(0f)] public float MaximumHeightOffset;
        [Min(0f)] public float HeightSharpness;

        private static FlightRingMotionSettings _instance;

        public static FlightRingMotionSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<FlightRingMotionSettings>("Flight Ring Motion Settings");
                }

                return _instance;
            }
        }
    }
}
