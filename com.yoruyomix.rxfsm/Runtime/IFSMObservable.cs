using System;
using System.Threading;
using System.Threading.Tasks;

namespace RxFSM
{
    /// <summary>
    /// Restricted view of the FSM for callers that may only observe state changes.
    /// Trigger operations are NOT exposed.
    /// </summary>
    public interface IFSMObservable<TState> where TState : Enum
    {
        TState State { get; }

        // ── EnterState ────────────────────────────────────────────────────────────
        IDisposable EnterState(Action<TState, TState> callback);
        IDisposable EnterState(Action<TState, TState, object> callback);
        IDisposable EnterState(TState targetState, Action<TState, object> callback);
        IDisposable EnterState<TTrigger>(Action<TState, TState, object> callback)
            where TTrigger : struct;
        IDisposable EnterState<TTrigger>(TState targetState, Action<TState, TTrigger> callback)
            where TTrigger : struct;

        // ── ExitState ─────────────────────────────────────────────────────────────
        IDisposable ExitState(Action<TState, TState> callback);
        IDisposable ExitState(Action<TState, TState, object> callback);
        IDisposable ExitState(TState targetState, Action<TState, object> callback);
        IDisposable ExitState<TTrigger>(Action<TState, TState, object> callback)
            where TTrigger : struct;
        IDisposable ExitState<TTrigger>(TState targetState, Action<TState, TTrigger> callback)
            where TTrigger : struct;

        // ── TickState ─────────────────────────────────────────────────────────────
        IDisposable TickState(TState targetState, Action<TState, object> callback);

        // ── EnterStateAsync ───────────────────────────────────────────────────────
        IDisposable EnterStateAsync(
            Func<TState, TState, CancellationToken, Task> callback,
            TransitionOperation policy);
        IDisposable EnterStateAsync(
            Func<TState, TState, object, CancellationToken, Task> callback,
            TransitionOperation policy);
        IDisposable EnterStateAsync(
            TState targetState,
            Func<TState, CancellationToken, Task> callback,
            TransitionOperation policy);
        IDisposable EnterStateAsync<TTrigger>(
            TState targetState,
            Func<TState, TTrigger, CancellationToken, Task> callback,
            TransitionOperation policy)
            where TTrigger : struct;
    }
}
