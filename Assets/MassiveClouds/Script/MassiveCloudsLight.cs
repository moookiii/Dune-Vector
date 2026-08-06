using System;
using UnityEngine;

namespace Mewlist
{
    [Serializable]
    public class MassiveCloudsLight
    {
        public Vector3 Rotation = Vector3.zero;
        public float Intensity = 2f;
        public Color Color = Color.white;

        public Vector3 LightDirection
        {
            get { return Quaternion.Euler(Rotation) * Vector3.forward; }
        }

        public void Synchronize(Light light)
        {
            Rotation = light.transform.rotation.eulerAngles;
            Intensity = light.intensity;
            Color = light.color;
        }

        public void Synchronize(Transform light)
        {
            Rotation = light.rotation.eulerAngles;
        }
    }
}