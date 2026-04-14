using System;
using System.Collections.Generic;

namespace RxFSM
{
    public sealed partial class FSMBuilder<TState> where TState : Enum
    {
        private List<(TState state, float duration, bool isFrameBased)> _throttleConfigs;
        private List<(TState state, Func<bool> waitUntil)>              _holdConfigs;
        private List<(TState from, TState to, float time)>              _autoTransTimeConfigs;
        private List<(TState from, TState to, Action<Action> onComplete)> _autoTransCallbackConfigs;

        public FSMBuilder<TState> ThrottleState(TState state, float durationSeconds)
        {
            (_throttleConfigs ??= new List<(TState, float, bool)>()).Add((state, durationSeconds, false));
            return this;
        }

        public FSMBuilder<TState> ThrottleFrameState(TState state, int frameCount)
        {
            (_throttleConfigs ??= new List<(TState, float, bool)>()).Add((state, (float)frameCount, true));
            return this;
        }

        public FSMBuilder<TState> HoldState(TState state, Func<bool> waitUntil)
        {
            (_holdConfigs ??= new List<(TState, Func<bool>)>()).Add((state, waitUntil));
            return this;
        }

        public FSMBuilder<TState> AutoTransition(TState from, TState to, float time)
        {
            (_autoTransTimeConfigs ??= new List<(TState, TState, float)>()).Add((from, to, time));
            return this;
        }

        public FSMBuilder<TState> AutoTransition(TState from, TState to, Action<Action> onComplete)
        {
            (_autoTransCallbackConfigs ??= new List<(TState, TState, Action<Action>)>()).Add((from, to, onComplete));
            return this;
        }

        private void ApplyPhase3Config(FSM<TState> sm)
        {
            if (_throttleConfigs != null)
                foreach (var (state, duration, isFrameBased) in _throttleConfigs)
                    sm.ConfigureThrottle(state, duration, isFrameBased);

            if (_holdConfigs != null)
                foreach (var (state, waitUntil) in _holdConfigs)
                    sm.ConfigureHold(state, waitUntil);

            if (_autoTransTimeConfigs != null)
                foreach (var (from, to, time) in _autoTransTimeConfigs)
                    sm.ConfigureAutoTransitionTime(from, to, time);

            if (_autoTransCallbackConfigs != null)
                foreach (var (from, to, onComplete) in _autoTransCallbackConfigs)
                    sm.ConfigureAutoTransitionCallback(from, to, onComplete);

            ApplyPhase4Config(sm);
        }
    }
}
