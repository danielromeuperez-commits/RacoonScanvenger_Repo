using System;
using Racoon.Player;

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
    }
}
