using System;
using System.Collections.Generic;

namespace RxFSM
{
    public enum CallbackType { EnterState, ExitState, TickState, EnterStateAsync, UnhandledTrigger }

    public sealed partial class FSM<TState> : IDisposable where TState : Enum
    {
        private TState _current;
        private readonly List<EventTransition<TState>> _transitions;
        private bool _disposed;

        // Reentrancy guard (Decision #2)
        private bool _evaluating;
        private readonly Queue<object> _pendingTriggers;

        // Phase 1 test hook — kept so Phase1Tester passes in T2.18 regression.
        internal Action<TState, TState, object> _testTransitionHook;

        // Central bucket for all FSMLoop registrations.
        private readonly FSMCompositeDisposable _loopDisposables = new FSMCompositeDisposable();

        // Phase 3
        internal int _deactivateCount;

        public TState State => _current;
        public Action<Exception, object, CallbackType> OnError    { get; set; }
        public Action                                  OnDisposed { get; set; }

        internal FSM(TState initialState, List<EventTransition<TState>> transitions)
        {
            _current = initialState;
            _transitions = transitions;
            _pendingTriggers = new Queue<object>();
        }

        public void Trigger<TTrigger>(TTrigger trigger) where TTrigger : struct
            => Evaluate(trigger);

        // Root entry (Trigger / IFSM.Evaluate). Fires OnUnhandledTrigger when a
        // trigger produces no transition anywhere in the active hierarchy.
        internal void Evaluate(object trigger)
        {
            if (_disposed || trigger == null || _deactivateCount > 0) return;

            if (_evaluating)
            {
                _pendingTriggers.Enqueue(trigger);
                return;
            }

            _evaluating = true;
            try
            {
                if (!ProcessEvaluate(trigger))
                    FireUnhandled(trigger);

                while (_pendingTriggers.Count > 0)
                {
                    var pending = _pendingTriggers.Dequeue();
                    if (!ProcessEvaluate(pending))
                        FireUnhandled(pending);
                }
            }
            finally
            {
                _evaluating = false;
            }
        }

        // Child-propagation entry. Returns whether the trigger was handled
        // (transition occurred) somewhere in this subtree. Never fires
        // OnUnhandledTrigger — only the root decides "unhandled".
        internal bool TryEvaluate(object trigger)
        {
            if (_disposed || trigger == null || _deactivateCount > 0) return false;

            if (_evaluating)
            {
                // Deferred to this FSM's own queue — cannot report a result
                // synchronously, so treat as not-yet-handled for bubbling.
                _pendingTriggers.Enqueue(trigger);
                return false;
            }

            _evaluating = true;
            try
            {
                bool handled = ProcessEvaluate(trigger);
                while (_pendingTriggers.Count > 0)
                    ProcessEvaluate(_pendingTriggers.Dequeue());
                return handled;
            }
            finally
            {
                _evaluating = false;
            }
        }

        // Returns true if the trigger caused (or launched an async) transition in
        // this layer or in the active child subtree.
        private bool ProcessEvaluate(object trigger)
        {
            bool transitioned = false;

            foreach (var t in _transitions)
            {
                if (!t.Match(_current, trigger)) continue;
                if (!t.IsForce && IsGuardBlocking(_current, trigger)) break;
                if (t.IsForce) ResetGuardsForState(_current);

                // Phase 4: run filter pipeline (IsForce bypasses all filters)
                if (!t.IsForce && (_globalFilters != null || t.LocalFilters != null))
                {
                    var prev   = _current;
                    bool? sync = RunFilterPipeline(t, prev, trigger);
                    if (sync == null)
                    {
                        // Async pipeline launched — stop trying further rules
                        transitioned = true; // prevent child propagation while filter runs
                        break;
                    }
                    if (sync == false) continue; // filter blocked → try next rule
                    // sync == true → ExecuteTransitionCore already ran inside the pipeline
                    transitioned = true;
                    break;
                }

                // No filters (or IsForce) — execute directly
                var p = _current;
                ExecuteTransitionCore(p, t.To, trigger);
                transitioned = true;
                break;
            }

            // Propagate trigger to active child if no transition occurred at this layer.
            // The child reports back whether it (or its own subtree) handled it.
            if (!transitioned && _children != null &&
                _children.TryGetValue(_current, out var activeChild))
                return activeChild.TryEvaluate(trigger);

            return transitioned;
        }

        public void TransitionTo(TState to)
        {
            if (_disposed) return;
            if (_deactivateCount > 0) return;

            CancelAllFilterPipelines();
            CancelInterrupt();

            var prev = _current;
            FireExit(prev, to, null);

            IFSM leavingChild = null;
            _children?.TryGetValue(prev, out leavingChild);
            leavingChild?.OnLeavingActivePath(null);

            _current = to;
            FireEnter(to, prev, null);

            IFSM enteringChild = null;
            _children?.TryGetValue(to, out enteringChild);
            enteringChild?.OnEnteringActivePath(null);
        }

        public void ForceTransitionTo(TState to)
        {
            if (_disposed) return;
            if (_deactivateCount > 0) return;

            CancelAllFilterPipelines();
            CancelInterrupt();
            ResetGuardsForState(_current);

            var prev = _current;
            FireExit(prev, to, null);

            IFSM leavingChild = null;
            _children?.TryGetValue(prev, out leavingChild);
            leavingChild?.OnLeavingActivePath(null);

            _current = to;
            FireEnter(to, prev, null);

            IFSM enteringChild = null;
            _children?.TryGetValue(to, out enteringChild);
            enteringChild?.OnEnteringActivePath(null);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CancelAllFilterPipelines();
            CancelInterrupt();
            _transitions.Clear();
            _pendingTriggers.Clear();
            DisposeCallbacks();
            DisposeGuards();
            DisposeAsync();
            DisposeFilters();
            DisposeChildren();
            _loopDisposables.Dispose();
            OnDisposed?.Invoke();
        }

        private void SafeInvoke(Action action, object trg, CallbackType ct)
        {
            try { action(); }
            catch (Exception ex) { OnError?.Invoke(ex, trg, ct); }
        }
    }
}
