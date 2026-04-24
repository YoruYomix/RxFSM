using System;

namespace RxFSM
{
    /// <summary>
    /// Restricted view of the FSM for callers that may only drive state (trigger, transition).
    /// Observation (EnterState/ExitState/TickState) is NOT exposed.
    /// </summary>
    public interface IFSMObserver<TState> where TState : Enum
    {
        TState State { get; }
        void Trigger<TTrigger>(TTrigger trigger) where TTrigger : struct;
        IDisposable TriggerEveryUpdate<TTrigger>(TTrigger trigger) where TTrigger : struct;
        void Interrupt(IInterrupt interrupt);
        void TransitionTo(TState to);
        void ForceTransitionTo(TState to);
        void Start(TState state, bool fireEnter);
    }
}
