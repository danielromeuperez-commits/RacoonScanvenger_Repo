using System;
using Racoon.Player;
using UnityEngine;

namespace Racoon.Items
{
    /// <summary>
    /// Lo que hace un objeto al usarse. Para crear un objeto nuevo:
    /// 1. Crea una clase que herede de ItemEffect y márcala con [Serializable].
    /// 2. Añade sus parámetros como campos serializados y sobrescribe ServerUse.
    /// Aparecerá automáticamente en el desplegable "Effect" de cualquier ItemData.
    /// </summary>
    [Serializable]
    public abstract class ItemEffect
    {
        /// <summary>
        /// Se ejecuta SOLO en el servidor cuando el jugador usa el objeto.
        /// Aquí va la lógica real del objeto (spawnear un proyectil, aplicar un buff...).
        /// </summary>
        public abstract void ServerUse(PlayerController user, ItemData item);

        /// <summary>
        /// Igual, sabiendo si lo ha soltado hacia atrás (stick hacia atrás mientras apuntaba).
        /// Por defecto da igual: los objetos que no se lanzan no lo sobrescriben.
        /// </summary>
        public virtual void ServerUse(PlayerController user, ItemData item, bool dropBehind) => ServerUse(user, item);

        /// <summary>Igual, lanzándolo a un punto elegido con Apuntar. Por defecto el punto da igual.</summary>
        public virtual void ServerUseAt(PlayerController user, ItemData item, Vector3 target) => ServerUse(user, item);

        /// <summary>
        /// Metros alrededor del jugador hasta los que Apuntar puede mover el punto de caída.
        /// 0 = este objeto no se apunta (Apuntar no hace nada y el stick sigue moviendo al jugador).
        /// </summary>
        public virtual float MaxAimRange => 0f;

        /// <summary>
        /// Si ahora se puede usar. Lo comprueban el dueño (al pulsar Usar: si no, no apunta y avisa con
        /// PlayerController.ItemUseRefused) y el servidor (no lo gasta). Por defecto, siempre.
        /// </summary>
        public virtual bool CanUse(PlayerController user) => true;

        /// <summary>
        /// Para el arco de apuntado (solo visual, en el dueño): desde dónde y con qué velocidad saldría el objeto.
        /// false = no se lanza (no se dibuja arco).
        /// </summary>
        public virtual bool TryGetThrow(PlayerController user, bool dropBehind, out Vector3 spawnPosition, out Vector3 velocity)
        {
            spawnPosition = velocity = Vector3.zero;
            return false;
        }

        /// <summary>
        /// Igual que <see cref="TryGetThrow"/>, para que caiga en <paramref name="target"/> (se ajusta al alcance).
        /// Lo usan el servidor y el arco de apuntado: siempre coinciden.
        /// </summary>
        public virtual bool TryGetThrowAt(PlayerController user, Vector3 target, out Vector3 spawnPosition, out Vector3 velocity)
        {
            spawnPosition = velocity = Vector3.zero;
            return false;
        }
    }
}
