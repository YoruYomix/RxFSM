using System;
using System.Collections.Generic;

namespace RxFSM
{
    public sealed partial class FSM<TState>
    {
        // ── Storage ─────────────────────────────────────────────────────────────

        // Unfiltered
        private List<Action<TState, TState>>         _onEnterList;
        private List<Action<TState, TState, object>> _onEnterWithTrgList;
        private List<Action<TState, TState>>         _onExitList;
        private List<Action<TState, TState, object>> _onExitWithTrgList;

        // State-filtered: key = cur (enter) or cur (exit)
        private Dictionary<TState, List<Action<TState, object>>> _onEnterState;
        private Dictionary<TState, List<Action<TState, object>>> _onExitState;

        // Trigger-type-filtered
        private Dictionary<Type, List<Action<TState, TState, object>>> _onEnterByTrigger;
        private Dictionary<Type, List<Action<TState, TState, object>>> _onExitByTrigger;

        // State+Trigger filtered
        private Dictionary<(TState, Type), List<Action<TState, object>>> _onEnterStateByTrigger;
        private Dictionary<(TState, Type), List<Action<TState, object>>> _onExitStateByTrigger;

        // ── EnterState overloads ─────────────────────────────────────────────────

        public IDisposable EnterState(Action<TState, TState> callback)
        {
            (_onEnterList ??= new List<Action<TState, TState>>()).Add(callback);
            return Disposable.Create(() => _onEnterList?.Remove(callback));
        }

        public IDisposable EnterState(Action<TState, TState, object> callback)
        {
            (_onEnterWithTrgList ??= new List<Action<TState, TState, object>>()).Add(callback);
            return Disposable.Create(() => _onEnterWithTrgList?.Remove(callback));
        }

        public IDisposable EnterState(TState targetState, Action<TState, object> callback)
        {
            _onEnterState ??= new Dictionary<TState, List<Action<TState, object>>>();
            if (!_onEnterState.TryGetValue(targetState, out var list))
                _onEnterState[targetState] = list = new List<Action<TState, object>>();
            list.Add(callback);
            return Disposable.Create(() => list.Remove(callback));
        }

        public IDisposable EnterState<TTrigger>(Action<TState, TState, object> callback)
            where TTrigger : struct
        {
            _onEnterByTrigger ??= new Dictionary<Type, List<Action<TState, TState, object>>>();
            var key = typeof(TTrigger);
            if (!_onEnterByTrigger.TryGetValue(key, out var list))
                _onEnterByTrigger[key] = list = new List<Action<TState, TState, object>>();
            list.Add(callback);
            return Disposable.Create(() => list.Remove(callback));
        }

        internal IDisposable EnterState<TTrigger>(TState targetState, Action<TState, object> callback)
            where TTrigger : struct
        {
            _onEnterStateByTrigger ??= new Dictionary<(TState, Type), List<Action<TState, object>>>();
            var key = (targetState, typeof(TTrigger));
            if (!_onEnterStateByTrigger.TryGetValue(key, out var list))
                _onEnterStateByTrigger[key] = list = new List<Action<TState, object>>();
            list.Add(callback);
            return Disposable.Create(() => list.Remove(callback));
        }

        public IDisposable EnterState<TTrigger>(TState targetState, Action<TState, TTrigger> callback)
            where TTrigger : struct
        {
            _onEnterStateByTrigger ??= new Dictionary<(TState, Type), List<Action<TState, object>>>();
            var key = (targetState, typeof(TTrigger));
            if (!_onEnterStateByTrigger.TryGetValue(key, out var list))
                _onEnterStateByTrigger[key] = list = new List<Action<TState, object>>();
            Action<TState, object> wrapper = (prev, trg) => callback(prev, (TTrigger)trg);
            list.Add(wrapper);
            return Disposable.Create(() => list.Remove(wrapper));
        }

        // ── ExitState overloads ──────────────────────────────────────────────────

        public IDisposable ExitState(Action<TState, TState> callback)
        {
            (_onExitList ??= new List<Action<TState, TState>>()).Add(callback);
            return Disposable.Create(() => _onExitList?.Remove(callback));
        }

        public IDisposable ExitState(Action<TState, TState, object> callback)
        {
            (_onExitWithTrgList ??= new List<Action<TState, TState, object>>()).Add(callback);
            return Disposable.Create(() => _onExitWithTrgList?.Remove(callback));
        }

        public IDisposable ExitState(TState targetState, Action<TState, object> callback)
        {
            _onExitState ??= new Dictionary<TState, List<Action<TState, object>>>();
            if (!_onExitState.TryGetValue(targetState, out var list))
                _onExitState[targetState] = list = new List<Action<TState, object>>();
            list.Add(callback);
            return Disposable.Create(() => list.Remove(callback));
        }

        public IDisposable ExitState<TTrigger>(Action<TState, TState, object> callback)
            where TTrigger : struct
        {
            _onExitByTrigger ??= new Dictionary<Type, List<Action<TState, TState, object>>>();
            var key = typeof(TTrigger);
            if (!_onExitByTrigger.TryGetValue(key, out var list))
                _onExitByTrigger[key] = list = new List<Action<TState, TState, object>>();
            list.Add(callback);
            return Disposable.Create(() => list.Remove(callback));
        }

        public IDisposable ExitState<TTrigger>(TState targetState, Action<TState, TTrigger> callback)
            where TTrigger : struct
        {
            _onExitStateByTrigger ??= new Dictionary<(TState, Type), List<Action<TState, object>>>();
            var key = (targetState, typeof(TTrigger));
            if (!_onExitStateByTrigger.TryGetValue(key, out var list))
                _onExitStateByTrigger[key] = list = new List<Action<TState, object>>();
            Action<TState, object> wrapper = (next, trg) => callback(next, (TTrigger)trg);
            list.Add(wrapper);
            return Disposable.Create(() => list.Remove(wrapper));
        }

        // ── FireEnter ────────────────────────────────────────────────────────────

        private void FireEnter(TState cur, TState prev, object trg)
        {
            if (_onEnterList != null)
                foreach (var cb in _onEnterList.ToArray())
                {
                    var c = cb;
                    SafeInvoke(() => c(cur, prev), trg, CallbackType.EnterState);
                }

            if (_onEnterWithTrgList != null)
                foreach (var cb in _onEnterWithTrgList.ToArray())
                {
                    var c = cb;
                    SafeInvoke(() => c(cur, prev, trg), trg, CallbackType.EnterState);
                }

            if (_onEnterState != null && _onEnterState.TryGetValue(cur, out var stateList))
                foreach (var cb in stateList.ToArray())
                {
                    var c = cb;
                    SafeInvoke(() => c(prev, trg), trg, CallbackType.EnterState);
                }

            if (trg != null)
            {
                var trigType = trg.GetType();

                if (_onEnterByTrigger != null && _onEnterByTrigger.TryGetValue(trigType, out var trigList))
                    foreach (var cb in trigList.ToArray())
                    {
                        var c = cb;
                        SafeInvoke(() => c(cur, prev, trg), trg, CallbackType.EnterState);
                    }

                if (_onEnterStateByTrigger != null &&
                    _onEnterStateByTrigger.TryGetValue((cur, trigType), out var stTrigList))
                    foreach (var cb in stTrigList.ToArray())
                    {
                        var c = cb;
                        SafeInvoke(() => c(prev, trg), trg, CallbackType.EnterState);
                    }
            }
        }

        // ── FireExit ─────────────────────────────────────────────────────────────

        private void FireExit(TState cur, TState next, object trg)
        {
            if (_onExitList != null)
                foreach (var cb in _onExitList.ToArray())
                {
                    var c = cb;
                    SafeInvoke(() => c(cur, next), trg, CallbackType.ExitState);
                }

            if (_onExitWithTrgList != null)
                foreach (var cb in _onExitWithTrgList.ToArray())
                {
                    var c = cb;
                    SafeInvoke(() => c(cur, next, trg), trg, CallbackType.ExitState);
                }

            if (_onExitState != null && _onExitState.TryGetValue(cur, out var stateList))
                foreach (var cb in stateList.ToArray())
                {
                    var c = cb;
                    SafeInvoke(() => c(next, trg), trg, CallbackType.ExitState);
                }

            if (trg != null)
            {
                var trigType = trg.GetType();
                if (_onExitByTrigger != null && _onExitByTrigger.TryGetValue(trigType, out var trigList))
                    foreach (var cb in trigList.ToArray())
                    {
                        var c = cb;
                        SafeInvoke(() => c(cur, next, trg), trg, CallbackType.ExitState);
                    }

                if (_onExitStateByTrigger != null &&
                    _onExitStateByTrigger.TryGetValue((cur, trigType), out var stTrigList))
                    foreach (var cb in stTrigList.ToArray())
                    {
                        var c = cb;
                        SafeInvoke(() => c(next, trg), trg, CallbackType.ExitState);
                    }
            }
        }

        // ── Cleanup ──────────────────────────────────────────────────────────────

        private void DisposeCallbacks()
        {
            _onEnterList?.Clear();
            _onEnterWithTrgList?.Clear();
            _onExitList?.Clear();
            _onExitWithTrgList?.Clear();
            _onEnterState?.Clear();
            _onExitState?.Clear();
            _onEnterByTrigger?.Clear();
            _onExitByTrigger?.Clear();
            _onEnterStateByTrigger?.Clear();
            _onExitStateByTrigger?.Clear();
        }
    }
}
