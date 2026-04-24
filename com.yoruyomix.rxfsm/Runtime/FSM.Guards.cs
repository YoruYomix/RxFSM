using System;
using System.Collections.Generic;

namespace RxFSM
{
    public sealed partial class FSM<TState>
    {
        // ── ThrottleState ────────────────────────────────────────────────────────

        private class ThrottleStateInfo
        {
            public float     Duration;
            public bool      IsFrameBased;
            public float     Elapsed;
            public bool      Active;
        }

        private Dictionary<TState, ThrottleStateInfo> _throttleInfos;

        // ── HoldState ────────────────────────────────────────────────────────────

        private class HoldStateInfo
        {
            public Func<bool> WaitUntil;
            public bool       Holding;   // true = pending trigger is waiting
        }

        private Dictionary<TState, HoldStateInfo> _holdInfos;

        // ── Shared guard pending (last-wins per state, Decision #3/#9) ──────────

        private Dictionary<TState, object> _guardPending;

        // ── TransitionOperation.Throttle count (set by FSM.Async.cs) ─────────────────

        internal Dictionary<TState, int> _asyncThrottleCount;

        // ── AutoTransition timer handles (for Deactivate cancellation) ──────────

        private List<FSMSerialDisposable> _autoTransitionTimers;

        // ── Guard API (called from ProcessEvaluate in RxFSM.cs) ─────────────────

        /// <summary>Returns true if any guard blocks the transition out of fromState.
        /// Stores the trigger as pending (overwrite semantics), UNLESS Drop is active
        /// (Drop discards incoming triggers rather than queuing them).</summary>
        internal bool IsGuardBlocking(TState fromState, object trigger)
        {
            // Drop has highest priority: if active, block and discard (no pending storage).
            if (_activeDropCount != null &&
                _activeDropCount.TryGetValue(fromState, out var dropCnt) && dropCnt > 0)
                return true;

            bool blocked = false;

            if (_throttleInfos != null && _throttleInfos.TryGetValue(fromState, out var ti) && ti.Active)
                blocked = true;

            if (_holdInfos != null && _holdInfos.TryGetValue(fromState, out var hi))
            {
                if (!hi.WaitUntil())
                {
                    hi.Holding = true;
                    blocked = true;
                }
            }

            if (_asyncThrottleCount != null &&
                _asyncThrottleCount.TryGetValue(fromState, out var cnt) && cnt > 0)
                blocked = true;

            if (blocked)
            {
                _guardPending ??= new Dictionary<TState, object>();
                _guardPending[fromState] = trigger;
            }

            return blocked;
        }

        /// <summary>Returns true if any guard is still active for the given state.</summary>
        private bool AnyGuardActive(TState state)
        {
            if (_activeDropCount != null &&
                _activeDropCount.TryGetValue(state, out var dropCnt) && dropCnt > 0)
                return true;
            if (_throttleInfos != null && _throttleInfos.TryGetValue(state, out var ti) && ti.Active)
                return true;
            if (_holdInfos != null && _holdInfos.TryGetValue(state, out var hi) && hi.Holding)
                return true;
            if (_asyncThrottleCount != null &&
                _asyncThrottleCount.TryGetValue(state, out var cnt) && cnt > 0)
                return true;
            return false;
        }

        /// <summary>Called by each guard when it releases. Evaluates pending trigger
        /// once ALL guards for the state have released.</summary>
        internal void TryReleasePending(TState fromState)
        {
            if (AnyGuardActive(fromState)) return;
            if (_guardPending == null ||
                !_guardPending.TryGetValue(fromState, out var trigger) || trigger == null)
                return;
            _guardPending.Remove(fromState);
            Evaluate(trigger);
        }

        /// <summary>Called by ForceTransitionTo to bypass/cancel all guards for a state.</summary>
        internal void ResetGuardsForState(TState state)
        {
            if (_throttleInfos != null && _throttleInfos.TryGetValue(state, out var ti))
            { ti.Active = false; ti.Elapsed = 0f; }

            if (_holdInfos != null && _holdInfos.TryGetValue(state, out var hi))
                hi.Holding = false;

            if (_asyncThrottleCount != null)
                _asyncThrottleCount[state] = 0;

            if (_activeDropCount != null)
                _activeDropCount[state] = 0;

            CancelAsyncCtsForState(state);

            _guardPending?.Remove(state);
        }

        // ── Configure ThrottleState ─────────────────────────────────────────────

        internal void ConfigureThrottle(TState state, float duration, bool isFrameBased)
        {
            _throttleInfos ??= new Dictionary<TState, ThrottleStateInfo>();
            var info = new ThrottleStateInfo { Duration = duration, IsFrameBased = isFrameBased };
            _throttleInfos[state] = info;

            // Activate on entry
            EnterState(state, (prev, trg) =>
            {
                info.Active  = true;
                info.Elapsed = 0f;
            }).AddTo(_loopDisposables);

            // Deactivate on exit (transition happened, reset for next entry)
            ExitState(state, (next, trg) =>
            {
                info.Active  = false;
                info.Elapsed = 0f;
            }).AddTo(_loopDisposables);

            // Per-frame timer
            FSMLoop.Register(FSMLoop.STAGE_TIMERS, (dt) =>
            {
                if (!info.Active || _disposed) return;
                info.Elapsed += info.IsFrameBased ? 1f : dt;
                if (info.Elapsed >= info.Duration)
                {
                    info.Active = false;
                    if (_deactivateCount == 0)
                        TryReleasePending(state);
                }
            }).AddTo(_loopDisposables);
        }

        // ── Configure HoldState ─────────────────────────────────────────────────

        internal void ConfigureHold(TState state, Func<bool> waitUntil)
        {
            _holdInfos ??= new Dictionary<TState, HoldStateInfo>();
            var info = new HoldStateInfo { WaitUntil = waitUntil };
            _holdInfos[state] = info;

            // Deactivate on exit
            ExitState(state, (next, trg) =>
            {
                info.Holding = false;
            }).AddTo(_loopDisposables);

            // Per-frame condition check (Stage 0, before triggers)
            FSMLoop.Register(FSMLoop.STAGE_TIMERS, (dt) =>
            {
                if (!info.Holding || _disposed || _deactivateCount > 0) return;
                if (!waitUntil()) return;
                info.Holding = false;
                TryReleasePending(state);
            }).AddTo(_loopDisposables);
        }

        // ── Configure AutoTransition (time-based) ───────────────────────────────

        internal void ConfigureAutoTransitionTime(TState from, TState to, float time)
        {
            _autoTransitionTimers ??= new List<FSMSerialDisposable>();
            var timerHandle = new FSMSerialDisposable();
            _autoTransitionTimers.Add(timerHandle);
            timerHandle.AddTo(_loopDisposables);

            EnterState(from, (prev, trg) =>
            {
                float elapsed = 0f;
                timerHandle.Disposable = FSMLoop.Register(FSMLoop.STAGE_TIMERS, (dt) =>
                {
                    if (_deactivateCount > 0 || _disposed) return;
                    elapsed += dt;
                    if (elapsed >= time)
                    {
                        timerHandle.Disposable = Disposable.Empty;
                        TransitionTo(to);
                    }
                });
            }).AddTo(_loopDisposables);

            ExitState(from, (next, trg) =>
            {
                timerHandle.Disposable = Disposable.Empty;
            }).AddTo(_loopDisposables);
        }

        // ── Configure AutoTransition (callback-based) ───────────────────────────

        internal void ConfigureAutoTransitionCallback(TState from, TState to, Action<Action> onComplete)
        {
            int generation = 0;

            EnterState(from, (prev, trg) =>
            {
                int gen = ++generation;
                onComplete(() =>
                {
                    if (gen == generation && State.Equals(from))
                        TransitionTo(to);
                });
            }).AddTo(_loopDisposables);

            ExitState(from, (next, trg) =>
            {
                generation++;   // invalidate any previously issued callbacks
            }).AddTo(_loopDisposables);
        }

        // ── Deactivate ──────────────────────────────────────────────────────────

        public IDisposable Deactivate()
        {
            if (_disposed) return Disposable.Empty;
            if (_deactivateCount == 0)
                OnFirstDeactivate();
            _deactivateCount++;
            IncrementChildrenDeactivate(); // nested FSM propagation
            return Disposable.Create(() =>
            {
                if (_deactivateCount > 0) _deactivateCount--;
                DecrementChildrenDeactivate();
            });
        }

        private void OnFirstDeactivate()
        {
            // Cancel all async CancellationTokens
            CancelAllAsyncCts();

            // Reset ThrottleState timers
            if (_throttleInfos != null)
                foreach (var info in _throttleInfos.Values)
                { info.Active = false; info.Elapsed = 0f; }

            // Reset HoldState
            if (_holdInfos != null)
                foreach (var info in _holdInfos.Values)
                    info.Holding = false;

            // Reset async throttle counts
            if (_asyncThrottleCount != null)
            {
                var keys = new List<TState>(_asyncThrottleCount.Keys);
                foreach (var k in keys)
                    _asyncThrottleCount[k] = 0;
            }

            // Reset Drop counts
            if (_activeDropCount != null)
            {
                var keys = new List<TState>(_activeDropCount.Keys);
                foreach (var k in keys)
                    _activeDropCount[k] = 0;
            }

            // Cancel AutoTransition timers
            if (_autoTransitionTimers != null)
                foreach (var t in _autoTransitionTimers)
                    t.Disposable = Disposable.Empty;

            // Discard all pending guard triggers
            _guardPending?.Clear();
        }

        // ── Connect ─────────────────────────────────────────────────────────────

        public IDisposable Connect<TTrigger>(
            Action<Action<TTrigger>> subscribe,
            Action<Action<TTrigger>> unsubscribe)
            where TTrigger : struct
        {
            Action<TTrigger> handler = t => Evaluate(t);
            subscribe(handler);
            return Disposable.Create(() => unsubscribe(handler));
        }

        // ── Cleanup ─────────────────────────────────────────────────────────────

        private void DisposeGuards()
        {
            _throttleInfos?.Clear();
            _holdInfos?.Clear();
            _guardPending?.Clear();
            _asyncThrottleCount?.Clear();
            _activeDropCount?.Clear();
        }
    }
}
