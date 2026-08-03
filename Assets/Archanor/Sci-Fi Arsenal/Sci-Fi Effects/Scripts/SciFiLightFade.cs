using UnityEngine;

namespace SciFiArsenal
{
    public class SciFiLightFade : MonoBehaviour
    {
        [Header("Seconds to dim the light")]
        public float life = 0.2f;
        public bool killAfterLife = true;

        private Light li;
        private float initIntensity;

        private void Start()
        {
            li = GetComponent<Light>();
            if (li != null)
            {
                initIntensity = li.intensity;
            }
            else
            {
                Debug.LogWarning($"No light object found on {gameObject.name}", this);
            }
        }

        private void Update()
        {
            if (li == null)
            {
                return;
            }

            li.intensity -= initIntensity * (Time.deltaTime / life);
            if (killAfterLife && li.intensity <= 0f)
            {
                Destroy(gameObject);
            }
        }
    }
}
