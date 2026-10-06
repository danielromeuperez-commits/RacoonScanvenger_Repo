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
        HitStun = 14,   // Golpe ligero (1º y 2º del combo): knockback corto + aturdido sin control
        Dash = 15,
        Knockdown = 16, // Golpe fuerte (3º del combo): derribado, invulnerable hasta levantarse
    }

    public static class PlayerStateExtensions
    {
        public static bool IsAction(this PlayerState state) => state >= PlayerState.Interact;
        /// <summary>Sin control por haber recibido un golpe (ligero o fuerte).</summary>
        public static bool IsStun(this PlayerState state) => state == PlayerState.HitStun || state == PlayerState.Knockdown;
    }
}
