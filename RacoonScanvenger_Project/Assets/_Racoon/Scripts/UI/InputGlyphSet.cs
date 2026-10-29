using System;
using System.Collections.Generic;
using Racoon.Player;
using UnityEngine;

namespace Racoon.UI
{
    /// <summary>
    /// Glifos de los botones (PS5, Xbox y las teclas del PC que usa el juego) en el estilo P5 pastel del HUD.
    ///  - <see cref="actions"/>: qué glifo corresponde a cada acción de PlayerInput.inputactions en cada familia de
    ///    dispositivo. Los avisos («machaca», «pulsa para...») lo piden con <see cref="Get"/>.
    ///  - Las listas de abajo tienen TODOS los botones de cada mando (para menús, tutoriales o pantallas de controles).
    /// Los sprites están en 2D/Sprites/UI/HUD/_Glifos (se generan con un script; el importador del HUD los configura).
    /// Si cambias un binding en PlayerInput.inputactions, cambia aquí su glifo.
    /// </summary>
    [CreateAssetMenu(menuName = "Racoon/UI/Input Glyph Set", fileName = "SO_InputGlyphs")]
    public class InputGlyphSet : ScriptableObject
    {
        [Serializable]
        public class ActionGlyphs
        {
            [Tooltip("Nombre de la acción en PlayerInput.inputactions (Move, Run, Interact, SwitchItem, Use, Dash...).")]
            public string action;
            public Sprite keyboard;
            [Tooltip("Otro control de teclado/ratón para la misma acción (p. ej. clic izquierdo además de F).")]
            public Sprite keyboardAlt;
            public Sprite xbox;
            public Sprite playStation;
        }

        public List<ActionGlyphs> actions = new();

        [Header("Todos los botones")]
        public List<Sprite> playStationButtons = new();
        public List<Sprite> xboxButtons = new();
        public List<Sprite> keyboardKeys = new();

        /// <summary>
        /// Glifo de <paramref name="action"/> para <paramref name="kind"/>. Nintendo y los mandos genéricos usan los de
        /// Xbox (misma posición de botones). Null si la acción no está.
        /// </summary>
        public Sprite Get(string action, InputDeviceKind kind)
        {
            ActionGlyphs entry = Find(action);
            if (entry == null) return null;
            return kind switch
            {
                InputDeviceKind.KeyboardMouse => entry.keyboard != null ? entry.keyboard : entry.keyboardAlt,
                InputDeviceKind.PlayStation => entry.playStation != null ? entry.playStation : entry.xbox,
                _ => entry.xbox != null ? entry.xbox : entry.playStation,
            };
        }

        public ActionGlyphs Find(string action)
        {
            foreach (ActionGlyphs entry in actions)
                if (string.Equals(entry.action, action, StringComparison.OrdinalIgnoreCase)) return entry;
            return null;
        }
    }
}
