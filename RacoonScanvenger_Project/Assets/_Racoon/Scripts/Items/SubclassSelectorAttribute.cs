using UnityEngine;

namespace Racoon
{
    /// <summary>
    /// Úsalo junto a [SerializeReference] para mostrar en el inspector un desplegable
    /// con todas las clases concretas que heredan del tipo del campo.
    /// </summary>
    public class SubclassSelectorAttribute : PropertyAttribute { }
}
