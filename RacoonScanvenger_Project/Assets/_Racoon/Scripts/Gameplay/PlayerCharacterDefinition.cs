using Unity.Netcode;
using UnityEngine;

namespace Racoon.Gameplay
{
    /// <summary>
    /// Un personaje seleccionable: lo que se ve en la selección y el prefab de red que se spawnea.
    /// </summary>
    [CreateAssetMenu(menuName = "Racoon/Players/Character Definition", fileName = "CHAR_New")]
    public class PlayerCharacterDefinition : ScriptableObject
    {
        public string displayName;
        public Sprite portrait;
        [Tooltip("Prefab del jugador (con NetworkObject). Debe estar en la Network Prefabs List del NetworkManager.")]
        public NetworkObject prefab;
    }
}
