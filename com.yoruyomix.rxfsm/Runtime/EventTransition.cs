using System;
using System.Collections.Generic;

namespace RxFSM
{
    public sealed class EventTransition<TState> where TState : Enum
    {
        public Type EventType { get; }
        public TState From { get; }
        public TState To { get; }
        public bool FromAny { get; }
        public Func<object, bool> Condition { get; }
        public bool IsForce { get; internal set; }

        // Phase 4: per-transition filters (set via UseFilter() on builder)
        internal List<ITransitionFilter> LocalFilters;

        internal EventTransition(
            Type eventType,
            TState from,
            TState to,
            bool fromAny,
            Func<object, bool> condition)
        {
            EventType = eventType;
            From = from;
            To = to;
            FromAny = fromAny;
            Condition = condition;
        }

        /// <summary>
        /// Returns true if this transition matches the current state and trigger.
        /// Evaluation order (short-circuits on first false):
        /// 1. trigger type must match EventType
        /// 2. if not FromAny, current state must match From
        /// 3. To must differ from currentState (same-state transitions are no-ops)
        /// 4. Condition (if any) must return true
        /// </summary>
        public bool Match(TState currentState, object trigger)
        {
            if (trigger.GetType() != EventType) return false;
            if (!FromAny && !From.Equals(currentState)) return false;
            if (To.Equals(currentState)) return false;
            if (Condition != null && !Condition(trigger)) return false;
            return true;
        }
    }
}
