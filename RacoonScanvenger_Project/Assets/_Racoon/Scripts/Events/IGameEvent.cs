/// <summary>
/// Un evento de partida que lanza el EventManager (solo en el servidor). Se puede lanzar varias veces por partida
/// según la secuencia del EventManager.
/// </summary>
public interface IGameEvent
{
    /// <summary>Se ha lanzado al menos una vez en esta partida.</summary>
    bool HasTriggered { get; }

    /// <summary>Sigue en marcha (o no puede ir a más): el EventManager no lo vuelve a lanzar mientras tanto.</summary>
    bool IsBusy { get; }

    void TriggerEventManually();
}
