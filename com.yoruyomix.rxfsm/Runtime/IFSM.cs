using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RxFSM
{
    // ── Non-generic interface used by the nested-FSM system ──────────────────────

    public interface IFSM : IDisposable
    {
        Enum CurrentState { get; }
        void Evaluate(object trigger);
        IReadOnlyList<Enum> GetActiveStateHierarchy();

        // Lifecycle notifications from parent
        void OnLeavingActivePath(object parentTrigger);   // phases 1-3 for this subtree
        void OnEnteringActivePath(object parentTrigger);  // phases 5-6 for this subtree

        // Deactivate propagation
        void IncrementDeactivate();
        void DecrementDeactivate();
    }

    // ── TransitionFilter ────────────────────────────────────────────────────────

    public readonly struct TransitionContext
    {
        public Enum From { get; }
        public Enum To   { get; }

        public TransitionContext(Enum from, Enum to) { From = from; To = to; }
    }

    /// <summary>
    /// Middleware that intercepts a matched transition before it executes.
    /// Call <c>next()</c> to allow the transition. Omit to block it.
    /// Note: trigger is passed as boxed object; cast as needed inside Invoke.
    /// </summary>
    public interface ITransitionFilter
    {
        ValueTask Invoke(object trigger, TransitionContext context,
                         Func<ValueTask> next, CancellationToken ct);
    }

    // ── Interrupt ────────────────────────────────────────────────────────────────

    public interface IInterrupt
    {
        ValueTask InvokeAsync(Enum currentState, CancellationToken ct);
    }

    // ── Generic full-access interface ────────────────────────────────────────────

    /// <summary>
    /// Full interface combining observer (drive) + observable (watch) + lifecycle.
    /// <c>FSM&lt;TState&gt;</c> implements this; use it for DI, testing, or mocking.
    /// </summary>
    public interface IFSM<TState> : IFSMObserver<TState>, IFSMObservable<TState>, IDisposable
        where TState : Enum
    {
        Action<Exception, object, CallbackType> OnError { get; set; }
        IDisposable Connect<TTrigger>(
            Action<Action<TTrigger>> subscribe,
            Action<Action<TTrigger>> unsubscribe)
            where TTrigger : struct;
        IDisposable Deactivate();
        IReadOnlyList<Enum> GetActiveStateHierarchy();
        void AddTo(GameObject gameObject);
    }
}
