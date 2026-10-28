public interface IGameEvent
{
    bool HasTriggered { get; }
    void TriggerEventManually();
}