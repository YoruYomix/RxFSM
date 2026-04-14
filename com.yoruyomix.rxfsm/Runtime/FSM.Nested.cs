using System;
using System.Collections.Generic;

namespace RxFSM
{
    /// <summary>
    /// Implements IFSM on FSM&lt;TState&gt; and provides nested-FSM runtime support.
    ///
    /// Null sentinel: TState is an enum (value type), so we use default(TState) to
    /// signal "parent-caused" exits (next=default) and "history" entries (prev=default).
    /// Testers check .Equals(default(TState)) instead of == null.
    /// </summary>
    public sealed partial class FSM<TState> : IFSM
    {
        // ── Child registry ────────────────────────────────────────────────────────

        private Dictionary<TState, IFSM> _children;

        internal void RegisterChild(TState parentState, IFSM child)
        {
            _children ??= new Dictionary<TState, IFSM>();
            _children[parentState] = child;
        }

        // ── IFSM explicit implementation ──────────────────────────────────────────

        Enum IFSM.CurrentState => _current;

        void IFSM.Evaluate(object trigger) => Evaluate(trigger);

        void IFSM.IncrementDeactivate() => _deactivateCount++;

        void IFSM.DecrementDeactivate()
        {
            if (_deactivateCount > 0) _deactivateCount--;
        }

        // ── GetActiveStateHierarchy ───────────────────────────────────────────────

        public IReadOnlyList<Enum> GetActiveStateHierarchy()
        {
            var list = new List<Enum> { _current };
            if (_children != null && _children.TryGetValue(_current, out var child))
                list.AddRange(child.GetActiveStateHierarchy());
            return list;
        }

        // ── OnLeavingActivePath (Phases 1–3 for this subtree) ─────────────────────

        void IFSM.OnLeavingActivePath(object parentTrigger)
        {
            // Phase 2: cancel guards, async tasks, interrupt for current state
            ResetGuardsForState(_current);
            CancelAllAsyncCts();
            CancelInterrupt();

            // Phase 3: fire ExitState callbacks — next=default signals parent-caused exit (top-down)
            FireExit(_current, default, parentTrigger);

            // Recurse into active child
            if (_children != null && _children.TryGetValue(_current, out var child))
                child.OnLeavingActivePath(parentTrigger);
        }

        // ── OnEnteringActivePath (Phases 5–6 for this subtree) ────────────────────

        void IFSM.OnEnteringActivePath(object parentTrigger)
        {
            // Try to evaluate the trigger against own transitions.
            // Save state before to detect explicit transition.
            var historyState = _current;

            // Run own transitions only (not child propagation yet).
            bool explicitTransition = TryEvaluateOwnTransitions(parentTrigger);

            if (!explicitTransition)
            {
                // History restoration — fire EnterState with prev=default(TState)
                FireEnter(_current, default, parentTrigger);

                // Recurse into active child for history restoration
                if (_children != null && _children.TryGetValue(_current, out var child))
                    child.OnEnteringActivePath(parentTrigger);
            }
            // If explicit transition: ExecuteTransitionCore already handled
            // FireEnter and child.OnEnteringActivePath for the new state.
        }

        /// <summary>
        /// Evaluates trigger against this FSM's own transitions only.
        /// Does NOT propagate to children. Returns true if a transition occurred.
        /// </summary>
        private bool TryEvaluateOwnTransitions(object trigger)
        {
            if (_disposed || trigger == null || _deactivateCount > 0) return false;

            foreach (var t in _transitions)
            {
                if (!t.Match(_current, trigger)) continue;
                if (!t.IsForce && IsGuardBlocking(_current, trigger)) break;
                if (t.IsForce) ResetGuardsForState(_current);

                // No filter pipeline for child-triggered transitions (keep simple)
                var prev = _current;
                ExecuteTransitionCore(prev, t.To, trigger);
                return true;
            }
            return false;
        }

        // ── Deactivate override: propagate to all children ─────────────────────────

        // The Deactivate() method lives in FSM.Guards.cs. We patch it here by
        // overriding the child propagation. Since we can't override a non-virtual method,
        // we use a hook: _onDeactivateChildren is called from the Deactivate() wrapper below.
        // HOWEVER — both Deactivate() and IFSM.IncrementDeactivate live in the same partial
        // class. We patch by adding child propagation directly into the Deactivate() method
        // via the helpers below (called from FSM.Guards.cs's Deactivate).

        internal void IncrementChildrenDeactivate()
        {
            if (_children == null) return;
            foreach (var c in _children.Values) c.IncrementDeactivate();
        }

        internal void DecrementChildrenDeactivate()
        {
            if (_children == null) return;
            foreach (var c in _children.Values) c.DecrementDeactivate();
        }

        // ── Dispose: recurse into children ────────────────────────────────────────

        private void DisposeChildren()
        {
            if (_children == null) return;
            foreach (var c in _children.Values)
                c.Dispose();
            _children.Clear();
        }
    }
}
