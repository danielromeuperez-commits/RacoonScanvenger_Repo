using UnityEngine;

namespace Racoon.Gameplay
{
    /// <summary>
    /// Puntos de aparición de la escena. El host (clientId 0) usa el primero y el cliente (1) el segundo.
    /// </summary>
    public class PlayerSpawnPoints : MonoBehaviour
    {
        [SerializeField] Transform[] points;

        static PlayerSpawnPoints instance;

        void Awake() => instance = this;

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        public static bool TryGetPose(ulong clientId, out Pose pose)
        {
            if (instance == null || instance.points == null || instance.points.Length == 0)
            {
                pose = default;
                return false;
            }

            Transform point = instance.points[(int)(clientId % (ulong)instance.points.Length)];
            pose = new Pose(point.position, point.rotation);
            return true;
        }

        void OnDrawGizmos()
        {
            if (points == null) return;
            Gizmos.color = Color.green;
            foreach (Transform point in points)
            {
                if (point == null) continue;
                Gizmos.DrawWireSphere(point.position, 0.5f);
                Gizmos.DrawRay(point.position, point.forward);
            }
        }
    }
}
