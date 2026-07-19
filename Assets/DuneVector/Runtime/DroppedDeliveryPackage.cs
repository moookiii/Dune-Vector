using UnityEngine;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DroppedDeliveryPackage : MonoBehaviour
    {
        private DesertWorldStreamer _world;
        private BoxCollider _collider;
        private bool _initialized;
        private float _groundContactOffset;

        public static void Release(
            Transform package,
            Transform worldParent,
            DesertWorldStreamer world,
            DeliveryTuning settings,
            Vector3 carrierVelocity)
        {
            if (package == null || world == null || settings == null)
            {
                return;
            }

            package.SetParent(worldParent, true);
            DroppedDeliveryPackage droppedPackage = package.gameObject.AddComponent<DroppedDeliveryPackage>();
            droppedPackage.Initialize(world, settings, carrierVelocity);
        }

        private void Initialize(DesertWorldStreamer world, DeliveryTuning settings, Vector3 carrierVelocity)
        {
            _world = world;
            _groundContactOffset = settings.PackageDropGroundContactOffset;

            _collider = gameObject.AddComponent<BoxCollider>();
            _collider.size = settings.PackageDropColliderSize;
            _collider.isTrigger = true;

            Rigidbody body = gameObject.AddComponent<Rigidbody>();
            body.mass = settings.PackageDropMass;
            body.linearVelocity = carrierVelocity * settings.PackageDropInheritedVelocityMultiplier;
            body.angularVelocity = settings.PackageDropAngularVelocity;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            _world.WorldShifted += HandleWorldShift;
            _initialized = true;
        }

        private void FixedUpdate()
        {
            if (!_initialized || _world == null || _collider == null)
            {
                return;
            }

            Vector3 position = transform.position;
            double logicalX = _world.OriginOffsetX + position.x;
            double logicalZ = _world.OriginOffsetZ + position.z;
            float groundHeight = (float)_world.HeightField.SampleHeight(logicalX, logicalZ);
            float halfHeight = _collider.size.y * Mathf.Abs(transform.lossyScale.y) * 0.5f;
            if (position.y - halfHeight <= groundHeight + _groundContactOffset)
            {
                Destroy(gameObject);
            }
        }

        private void HandleWorldShift(Vector3 shift)
        {
            transform.position += shift;
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.WorldShifted -= HandleWorldShift;
            }
        }
    }
}
