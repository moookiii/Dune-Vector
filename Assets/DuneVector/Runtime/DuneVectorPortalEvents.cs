using System;
using UnityEngine;

namespace DuneVector
{
    public readonly struct DuneVectorPortalCrossing
    {
        public Vector3 Position { get; }
        public Vector3 TravelDirection { get; }

        public DuneVectorPortalCrossing(Vector3 position, Vector3 travelDirection)
        {
            Position = position;
            TravelDirection = travelDirection.sqrMagnitude > Mathf.Epsilon
                ? travelDirection.normalized
                : Vector3.forward;
        }
    }

    public static class DuneVectorPortalEvents
    {
        public static event Action<DuneVectorPortalCrossing> PlayerCrossed;

        public static void NotifyPlayerCrossed(
            Vector3 portalPosition,
            Vector3 portalForward,
            DroneCharacterController player)
        {
            Vector3 travelDirection = player != null && player.Motor != null
                ? player.Motor.Velocity
                : Vector3.zero;
            if (travelDirection.sqrMagnitude <= Mathf.Epsilon && player != null)
            {
                travelDirection = player.AimDirection;
            }
            if (travelDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                travelDirection = portalForward;
            }

            PlayerCrossed?.Invoke(new DuneVectorPortalCrossing(
                portalPosition,
                travelDirection));
        }
    }
}
