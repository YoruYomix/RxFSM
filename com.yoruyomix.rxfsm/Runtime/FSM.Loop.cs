using System;
using UnityEngine;

namespace RxFSM
{
    public sealed partial class FSM<TState>
    {
        // ── TickState ────────────────────────────────────────────────────────────

        /// <summary>
        /// Executes callback every frame while in targetState.
        /// prev/trg are captured at entry time (Decision #8).
        /// First tick fires on the NEXT frame after entry.
        /// </summary>
        public IDisposable TickState(TState targetState, Action<TState, object> callback)
        {
            TState capturedPrev = default;
            object capturedTrg  = null;
            bool   skipNext     = false;
            bool   disposed     = false;

            // Captures entry context and sets skip-first-tick when Enter fires normally.
            // When Start(state, false) is used, Enter never fires so tick begins immediately
            // the next frame — _current.Equals(targetState) drives activation instead.
            var enterHandle = EnterState(targetState, (prev, trg) =>
            {
                capturedPrev = prev;
                capturedTrg  = trg;
                skipNext     = true;
            });

            var tickHandle = FSMLoop.Register(FSMLoop.STAGE_TICKS, () =>
            {
                if (disposed || !_current.Equals(targetState) || _deactivateCount > 0) return;
                if (skipNext) { skipNext = false; return; }
                try { callback(capturedPrev, capturedTrg); }
                catch (Exception ex) { OnError?.Invoke(ex, capturedTrg, CallbackType.TickState); }
            });

            var outerHandle = Disposable.Create(() =>
            {
                if (disposed) return;
                disposed = true;
                enterHandle.Dispose();
                tickHandle.Dispose();
            });

            outerHandle.AddTo(_loopDisposables);
            return outerHandle;
        }

        // ── TriggerEveryUpdate ───────────────────────────────────────────────────

        /// <summary>
        /// Fires trigger every frame via FSMLoop Stage 2.
        /// The trigger struct is boxed once and reused (no per-frame allocation).
        /// </summary>
        public IDisposable TriggerEveryUpdate<TTrigger>(TTrigger trigger)
            where TTrigger : struct
        {
            object boxed = trigger;   // box once
            var handle = new TriggerEveryUpdateDisposable();
            var loopReg = FSMLoop.Register(FSMLoop.STAGE_TRIGGERS, (dt) =>
            {
                if (handle.ShouldFire(dt))
                    Evaluate(boxed);
            });
            handle.Init(loopReg);
            handle.AddTo(_loopDisposables);
            return handle;
        }

        // ── AddTo ────────────────────────────────────────────────────────────────

        public void AddTo(GameObject gameObject)
        {
            if (_disposed) return;
            gameObject.AddComponent<FSMDisposer>().Set(this);
        }
    }

    // ── TriggerEveryUpdateDisposable ─────────────────────────────────────────────

    internal sealed class TriggerEveryUpdateDisposable : IDisposable
    {
        private IDisposable _loopHandle;
        private bool  _hasThrottle;
        private float _throttleInterval;
        private float _elapsed;
        private bool  _firstFire;
        private bool  _disposed;

        internal void Init(IDisposable loopHandle)
        {
            _loopHandle = loopHandle;
            _firstFire  = true;
        }

        internal void SetThrottle(float intervalSeconds)
        {
            _hasThrottle      = true;
            _throttleInterval = intervalSeconds;
            _elapsed          = 0f;
            _firstFire        = true;
        }

        internal bool ShouldFire(float dt)
        {
            if (_disposed) return false;
            if (!_hasThrottle) return true;
            if (_firstFire) { _firstFire = false; _elapsed = 0f; return true; }
            _elapsed += dt;
            if (_elapsed >= _throttleInterval) { _elapsed = 0f; return true; }
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _loopHandle?.Dispose();
        }
    }

    // ── Throttle extension ───────────────────────────────────────────────────────

    public static class TriggerEveryUpdateExtensions
    {
        public static IDisposable Throttle(this IDisposable handle, float intervalSeconds)
        {
            if (handle is TriggerEveryUpdateDisposable teu)
                teu.SetThrottle(intervalSeconds);
            return handle;
        }
    }

    // ── FSMDisposer ──────────────────────────────────────────────────────────────

    internal sealed class FSMDisposer : UnityEngine.MonoBehaviour
    {
        private IDisposable _target;
        internal void Set(IDisposable target) => _target = target;
        void OnDestroy() => _target?.Dispose();
    }
}
