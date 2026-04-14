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

        // ── ExitState ─────────────────────────────────────────────────────────────
        IDisposable ExitState(Action<TState, TState> callback);
        IDisposable ExitState(Action<TState, TState, object> callback);

        // ── TickState ─────────────────────────────────────────────────────────────
        IDisposable TickState(TState targetState, Action<TState, object> callback);

        // ── Async overloads ───────────────────────────────────────────────────────
        IDisposable EnterStateAsync(
            Func<TState, TState, CancellationToken, Task> callback,
            AsyncOperation policy);

        IDisposable EnterStateAsync(
            TState targetState,
            Func<TState, CancellationToken, Task> callback,
            AsyncOperation policy);
    }
}
