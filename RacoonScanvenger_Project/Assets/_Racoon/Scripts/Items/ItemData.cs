using Racoon.Feedback;
using Racoon.Player;
using UnityEngine;

namespace Racoon.Items
{
    /// <summary>
    /// Definición de un objeto (estilo Mario Kart). Todos los objetos usan este mismo SO:
    /// lo que hace cada uno se elige en el desplegable "Effect" (ver ItemEffect).
    /// </summary>
    [CreateAssetMenu(menuName = "Racoon/Items/Item Data", fileName = "ITEM_New")]
    public class ItemData : ScriptableObject
    {
        public string displayName;
        public Sprite icon;
        [Tooltip("Modelo que se ve en la mano cuando está equipado (opcional). Si es un prefab de red " +
                 "(p. ej. PF_BananaTrap), PlayerItemHandler le quita física, colliders y scripts: solo queda la malla.")]
        public GameObject heldVisualPrefab;

        [Tooltip("Color del objeto: destello al pararse la ruleta del HUD.")]
        public Color rarityColor = Color.white;

        [Header("Reparto en las Item Box")]
        [Tooltip("Probabilidad relativa de salir para quien va PRIMERO en puntos.")]
        [Min(0f)] public float boxWeightLeader = 1f;
        [Tooltip("Probabilidad relativa de salir para quien va ÚLTIMO en puntos. Entre medias se interpola.")]
        [Min(0f)] public float boxWeightLast = 1f;

        [Tooltip("Qué hace el objeto al usarse.")]
        [SerializeReference, SubclassSelector] ItemEffect effect;

        [Header("Sonido (pendiente: arrastra los clips cuando los haya)")]
        [Tooltip("SFX al USAR este objeto. Lo oyen todos, sale del jugador. " +
                 "Plátano: «fiu» de lanzamiento. Bebida energética: lata que se abre «pssst» + trago.")]
        public SfxCue useSfx = new();

        public ItemEffect Effect => effect;

        /// <summary>Si ahora se puede usar (ver ItemEffect.CanUse). En el dueño y en el servidor.</summary>
        public bool CanUse(PlayerController user) => effect == null || effect.CanUse(user);

        /// <summary>Peso en las Item Box según la posición: 0 = va primero, 1 = va último.</summary>
        public float BoxWeight(float positionFromLeader) =>
            Mathf.Lerp(boxWeightLeader, boxWeightLast, Mathf.Clamp01(positionFromLeader));

        /// <summary>
        /// Se ejecuta SOLO en el servidor cuando el jugador usa el objeto.
        /// <paramref name="dropBehind"/>: lo ha soltado hacia atrás (stick hacia atrás mientras apuntaba).
        /// </summary>
        public void ServerUse(PlayerController user, bool dropBehind = false)
        {
            if (effect == null)
            {
                Debug.Log($"{user.name} ha usado {displayName} (sin efecto)");
                return;
            }

            effect.ServerUse(user, this, dropBehind);
        }
    }
}
