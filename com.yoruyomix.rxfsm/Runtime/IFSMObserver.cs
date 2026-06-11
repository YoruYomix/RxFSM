using System;

namespace RxFSM
{
    /// <summary>
    /// Common read-only view of the current state. Shared base of the driver and
    /// observer views so <see cref="IFSM{TState}"/> exposes a single, unambiguous State.
    /// </summary>
    public interface IFSMState<TState> where TState : Enum
    {
        TState State { get; }
    }

    /// <summary>
    /// Restricted view of the FSM for callers that may only drive state (trigger, transition).
    /// Observation (EnterState/ExitState/TickState) is NOT exposed.
    /// </summary>
    public interface IFSMObserver<TState> : IFSMState<TState> where TState : Enum
    {
        void Trigger<TTrigger>(TTrigger trigger) where TTrigger : struct;
        IDisposable TriggerEveryUpdate<TTrigger>(TTrigger trigger) where TTrigger : struct;
        void Interrupt(IInterrupt interrupt);
        void TransitionTo(TState to);
        void ForceTransitionTo(TState to);
    }
}
