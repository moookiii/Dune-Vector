using System;
using UnityEngine;

namespace Mewlist
{
    [Serializable]
    public class MassiveCloudsAmbient
    {
#if UNITY_2018_1_OR_NEWER
        [ColorUsage(false, true), SerializeField]
#else
        [ColorUsage(false, true, 0f, 8f, 0.125f, 3f), SerializeField]
#endif
        private Color skyColor = Color.blue;
#if UNITY_2018_1_OR_NEWER
        [ColorUsage(false, true), SerializeField]
#else
        [ColorUsage(false, true, 0f, 8f, 0.125f, 3f), SerializeField]
#endif
        private Color equatorColor = Color.cyan;
#if UNITY_2018_1_OR_NEWER
        [ColorUsage(false, true), SerializeField]
#else
        [ColorUsage(false, true, 0f, 8f, 0.125f, 3f), SerializeField]
#endif
        private Color groundColor = Color.gray;
        [SerializeField] private float luminanceFix = 0f;

        private readonly Color[] colors = new Color[3] {Color.blue, Color.cyan, Color.gray};

        private Color Fix(Color col)
        {
            var factor = Mathf.Pow(2, -luminanceFix);
            return col / factor;
        }

        public Color SkyColor
        {
            get { return Fix(skyColor); }
            set { skyColor = value; }
        }

        public Color EquatorColor
        {
            get { return Fix(equatorColor); }
            set { equatorColor = value; }
        }

        public Color GroundColor
        {
            get { return Fix(groundColor); }
            set { groundColor = value; }
        }

        public Color[] ToArray()
        {
            colors[0] = skyColor;
            colors[1] = equatorColor;
            colors[2] = groundColor;
            return colors;
        }
    }
}