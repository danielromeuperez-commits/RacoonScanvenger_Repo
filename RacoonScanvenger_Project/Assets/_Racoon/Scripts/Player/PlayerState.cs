namespace Racoon.Player
{
    /// <summary>
    /// Estados del jugador. El dueño lo escribe y se replica al resto por red.
    /// El Animator lo recibe como parámetro int "State", así que NO cambies los números
    /// una vez montadas las transiciones del Animator.
    /// </summary>
    public enum PlayerState : byte
    {
        // Locomoción (se decide cada frame según el input)
        Idle = 0,
        Walk = 1,
        Run = 2,

        // Acciones (duran un tiempo fijo y bloquean otras acciones)
        Interact = 10,
        SwitchItem = 11,
        Punch = 12,
        UseItem = 13,
    }

    public static class PlayerStateExtensions
    {
        public static bool IsAction(this PlayerState state) => state >= PlayerState.Interact;
    }
}
