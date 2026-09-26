using System.Collections.Generic;
using UnityEngine;

namespace Racoon.Gameplay
{
    /// <summary>
    /// Puntos de aparición de la escena. El servidor reserva uno libre para cada jugador que entra
    /// y lo libera cuando se va, así nunca aparecen dos jugadores en el mismo punto.
    /// </summary>
    public class PlayerSpawnPoints : MonoBehaviour
    {
        public enum SelectionMode
        {
            InOrder, // Host -> primer punto, siguiente jugador -> segundo...
            Random,
        }

        [SerializeField] Transform[] points;
        [SerializeField] SelectionMode selection = SelectionMode.InOrder;

        static PlayerSpawnPoints instance;
        readonly Dictionary<ulong, int> reservedByClient = new();

        void Awake() => instance = this;

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        /// <summary>
        /// Solo servidor. Devuelve el punto reservado para ese cliente, o null si están todos ocupados.
        /// Si no hay PlayerSpawnPoints en la escena, usa el origen y avisa.
        /// </summary>
        public static Pose? Reserve(ulong clientId)
        {
            if (instance == null || instance.points == null || instance.points.Length == 0)
            {
                Debug.LogWarning("No hay PlayerSpawnPoints con puntos en la escena: el jugador aparece en el origen.");
                return new Pose(Vector3.zero, Quaternion.identity);
            }
            return instance.ReserveInternal(clientId);
        }

        /// <summary>Solo servidor. Libera el punto del cliente que se ha ido.</summary>
        public static void Release(ulong clientId)
        {
            if (instance != null) instance.reservedByClient.Remove(clientId);
        }

        public static void ReleaseAll()
        {
            if (instance != null) instance.reservedByClient.Clear();
        }

        Pose? ReserveInternal(ulong clientId)
        {
            if (!reservedByClient.TryGetValue(clientId, out int index))
            {
                var free = new List<int>();
                for (int i = 0; i < points.Length; i++)
                    if (points[i] != null && !reservedByClient.ContainsValue(i)) free.Add(i);

                if (free.Count == 0) return null;
                index = selection == SelectionMode.Random ? free[Random.Range(0, free.Count)] : free[0];
                reservedByClient[clientId] = index;
            }

            Transform point = points[index];
            return new Pose(point.position, point.rotation);
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
