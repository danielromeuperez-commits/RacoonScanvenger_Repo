using System.Collections.Generic;
using UnityEngine;

namespace Racoon.Gameplay
{
    /// <summary>
    /// Lista de personajes seleccionables. Por red solo viaja el índice, así que host y cliente
    /// deben usar el mismo asset con el mismo orden.
    /// </summary>
    [CreateAssetMenu(menuName = "Racoon/Players/Character Roster", fileName = "CharacterRoster")]
    public class PlayerCharacterRoster : ScriptableObject
    {
        [SerializeField] List<PlayerCharacterDefinition> characters = new();

        public int Count => characters.Count;
        public IReadOnlyList<PlayerCharacterDefinition> Characters => characters;

        public PlayerCharacterDefinition Get(int index) =>
            index >= 0 && index < characters.Count ? characters[index] : null;
    }
}
