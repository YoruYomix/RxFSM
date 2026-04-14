using System;
using System.Collections.Generic;

namespace RxFSM
{
    public sealed partial class FSMBuilder<TState> where TState : Enum
    {
        private readonly TState _initialState;
        private readonly List<EventTransition<TState>> _transitions = new List<EventTransition<TState>>();
        private bool _built;

        private FSMBuilder(TState initialState)
        {
            _initialState = initialState;
        }

        public static FSMBuilder<TState> Create(TState initialState)
            => new FSMBuilder<TState>(initialState);

        // ── Single from ────────────────────────────────────────────────────────

        public FSMBuilder<TState> AddTransition<TTrigger>(
            TState from,
            TState to) where TTrigger : struct
        {
            _transitions.Add(new EventTransition<TState>(typeof(TTrigger), from, to, false, null));
            return this;
        }

        public FSMBuilder<TState> AddTransition<TTrigger>(
            Func<TTrigger, bool> condition,
            TState from,
            TState to) where TTrigger : struct
        {
            Func<object, bool> wrapped = obj => condition((TTrigger)obj);
            _transitions.Add(new EventTransition<TState>(typeof(TTrigger), from, to, false, wrapped));
            return this;
        }

        // ── Array from (syntactic sugar — expands to one entry per from state) ─

        public FSMBuilder<TState> AddTransition<TTrigger>(
            TState[] from,
            TState to) where TTrigger : struct
        {
            foreach (var f in from)
                _transitions.Add(new EventTransition<TState>(typeof(TTrigger), f, to, false, null));
            return this;
        }

        public FSMBuilder<TState> AddTransition<TTrigger>(
            Func<TTrigger, bool> condition,
            TState[] from,
            TState to) where TTrigger : struct
        {
            Func<object, bool> wrapped = obj => condition((TTrigger)obj);
            foreach (var f in from)
                _transitions.Add(new EventTransition<TState>(typeof(TTrigger), f, to, false, wrapped));
            return this;
        }

        // ── FromAny ────────────────────────────────────────────────────────────

        public FSMBuilder<TState> AddTransitionFromAny<TTrigger>(
            TState to) where TTrigger : struct
        {
            _transitions.Add(new EventTransition<TState>(typeof(TTrigger), default, to, true, null));
            return this;
        }

        public FSMBuilder<TState> AddTransitionFromAny<TTrigger>(
            Func<TTrigger, bool> condition,
            TState to) where TTrigger : struct
        {
            Func<object, bool> wrapped = obj => condition((TTrigger)obj);
            _transitions.Add(new EventTransition<TState>(typeof(TTrigger), default, to, true, wrapped));
            return this;
        }

        // ── ForceTransition marker ─────────────────────────────────────────────

        /// <summary>
        /// Marks the most recently added transition with IsForce = true.
        /// Must be called immediately after AddTransition / AddTransitionFromAny.
        /// </summary>
        public FSMBuilder<TState> ForceTransition()
        {
            if (_transitions.Count == 0)
                throw new InvalidOperationException(
                    "ForceTransition() called with no preceding transition.");
            _transitions[_transitions.Count - 1].IsForce = true;
            return this;
        }

        // ── Build ──────────────────────────────────────────────────────────────

        public FSM<TState> Build()
        {
            if (_built)
                throw new InvalidOperationException(
                    "Build() has already been called on this builder. Create a new builder instance.");
            _built = true;
            var sm = new FSM<TState>(_initialState, new List<EventTransition<TState>>(_transitions));
            ApplyPhase3Config(sm);
            return sm;
        }
    }
}
