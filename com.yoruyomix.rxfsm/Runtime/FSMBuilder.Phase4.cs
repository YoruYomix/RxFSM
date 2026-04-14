using System;
using System.Collections.Generic;

namespace RxFSM
{
    public sealed partial class FSMBuilder<TState> where TState : Enum
    {
        // ── Nested FSM children ──────────────────────────────────────────────────

        private List<(TState parentState, IFSM child)> _childFSMs;

        public FSMBuilder<TState> Register(TState parentState, IFSM child)
        {
            (_childFSMs ??= new List<(TState, IFSM)>()).Add((parentState, child));
            return this;
        }

        // ── Filters ──────────────────────────────────────────────────────────────

        private List<ITransitionFilter> _globalFilters;

        public FSMBuilder<TState> UseGlobalFilter(ITransitionFilter filter)
        {
            (_globalFilters ??= new List<ITransitionFilter>()).Add(filter);
            return this;
        }

        /// <summary>Applies filter to the LAST added transition.</summary>
        public FSMBuilder<TState> UseFilter(ITransitionFilter filter)
        {
            if (_transitions.Count == 0)
                throw new InvalidOperationException("UseFilter() called with no preceding transition.");
            var t = _transitions[_transitions.Count - 1];
            (t.LocalFilters ??= new List<ITransitionFilter>()).Add(filter);
            return this;
        }

        // ── Phase 4 config applied in ApplyPhase3Config (extended here) ─────────

        // Called by the existing ApplyPhase3Config in FSMBuilder.Phase3.cs.
        // We extend via partial by hooking into the existing method via a new helper.
        // Since Build() already calls ApplyPhase3Config, we add Phase4 logic there.

        private void ApplyPhase4Config(FSM<TState> sm)
        {
            if (_childFSMs != null)
                foreach (var (state, child) in _childFSMs)
                    sm.RegisterChild(state, child);

            if (_globalFilters != null)
                sm.SetGlobalFilters(_globalFilters);
        }
    }
}
