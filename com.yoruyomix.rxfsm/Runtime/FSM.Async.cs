using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RxFSM
{
    public sealed partial class FSM<TState>
    {
        // ── AsyncSub ─────────────────────────────────────────────────────────────

        private class AsyncSub
        {
            public CancellationTokenSource Cts;
            public bool                    IsActive;   // Throttle/Drop: true while task running
            public AsyncOperation          Policy;
            public bool                    HasTargetState;
            public TState                  TargetState; // meaningful only if HasTargetState
        }

        private List<AsyncSub>         _asyncSubs;
        private Dictionary<TState, int> _activeDropCount;  // Drop-only counter (separate from _asyncThrottleCount)

        // ── Unfiltered — (cur, prev, ct) ─────────────────────────────────────────

        public IDisposable EnterStateAsync(
            Func<TState, TState, CancellationToken, Task> callback,
            AsyncOperation policy)
        {
            var sub = new AsyncSub { Policy = policy, HasTargetState = false };
            (_asyncSubs ??= new List<AsyncSub>()).Add(sub);

            var enterHandle = EnterState((cur, prev) =>
            {
                var enteredState = cur;
                var capturedCur  = cur;
                var capturedPrev = prev;
                HandleAsyncEntry(sub, enteredState,
                    ct => callback(capturedCur, capturedPrev, ct));
            });

            // Switch: cancel when any state exits
            IDisposable exitHandle = policy == AsyncOperation.Switch
                ? ExitState((cur, next) => sub.Cts?.Cancel())
                : Disposable.Empty;

            return Disposable.Create(() =>
            {
                _asyncSubs?.Remove(sub);
                CancelSubAndRelease(sub);
                enterHandle.Dispose();
                exitHandle.Dispose();
            });
        }

        // ── Unfiltered — (cur, prev, trg, ct) ───────────────────────────────────

        public IDisposable EnterStateAsync(
            Func<TState, TState, object, CancellationToken, Task> callback,
            AsyncOperation policy)
        {
            var sub = new AsyncSub { Policy = policy, HasTargetState = false };
            (_asyncSubs ??= new List<AsyncSub>()).Add(sub);

            var enterHandle = EnterState((Action<TState, TState, object>)((cur, prev, trg) =>
            {
                var capturedCur  = cur;
                var capturedPrev = prev;
                var capturedTrg  = trg;
                HandleAsyncEntry(sub, capturedCur,
                    ct => callback(capturedCur, capturedPrev, capturedTrg, ct));
            }));

            IDisposable exitHandle = policy == AsyncOperation.Switch
                ? ExitState((cur, next) => sub.Cts?.Cancel())
                : Disposable.Empty;

            return Disposable.Create(() =>
            {
                _asyncSubs?.Remove(sub);
                CancelSubAndRelease(sub);
                enterHandle.Dispose();
                exitHandle.Dispose();
            });
        }

        // ── State-filtered — (prev, ct) ──────────────────────────────────────────

        public IDisposable EnterStateAsync(
            TState targetState,
            Func<TState, CancellationToken, Task> callback,
            AsyncOperation policy)
        {
            var sub = new AsyncSub { Policy = policy, HasTargetState = true, TargetState = targetState };
            (_asyncSubs ??= new List<AsyncSub>()).Add(sub);

            var enterHandle = EnterState(targetState, (prev, trg) =>
            {
                var capturedPrev = prev;
                HandleAsyncEntry(sub, targetState,
                    ct => callback(capturedPrev, ct));
            });

            IDisposable exitHandle = policy == AsyncOperation.Switch
                ? ExitState(targetState, (next, trg) => sub.Cts?.Cancel())
                : Disposable.Empty;

            return Disposable.Create(() =>
            {
                _asyncSubs?.Remove(sub);
                CancelSubAndRelease(sub);
                enterHandle.Dispose();
                exitHandle.Dispose();
            });
        }

        // ── State+Trigger filtered — (prev, trg, ct) ────────────────────────────

        public IDisposable EnterStateAsync<TTrigger>(
            TState targetState,
            Func<TState, TTrigger, CancellationToken, Task> callback,
            AsyncOperation policy)
            where TTrigger : struct
        {
            var sub = new AsyncSub { Policy = policy, HasTargetState = true, TargetState = targetState };
            (_asyncSubs ??= new List<AsyncSub>()).Add(sub);

            var enterHandle = EnterState<TTrigger>(targetState, (TState prev, TTrigger trg) =>
            {
                var capturedPrev = prev;
                var capturedTrg  = trg;
                HandleAsyncEntry(sub, targetState,
                    ct => callback(capturedPrev, capturedTrg, ct));
            });

            IDisposable exitHandle = policy == AsyncOperation.Switch
                ? ExitState(targetState, (next, trg) => sub.Cts?.Cancel())
                : Disposable.Empty;

            return Disposable.Create(() =>
            {
                _asyncSubs?.Remove(sub);
                CancelSubAndRelease(sub);
                enterHandle.Dispose();
                exitHandle.Dispose();
            });
        }

        // ── HandleAsyncEntry ─────────────────────────────────────────────────────

        private void HandleAsyncEntry(
            AsyncSub                    sub,
            TState                      enteredState,
            Func<CancellationToken, Task> invoke)
        {
            switch (sub.Policy)
            {
                case AsyncOperation.Switch:
                    sub.Cts?.Cancel();
                    sub.Cts = new CancellationTokenSource();
                    _ = RunFireAndForget(invoke(sub.Cts.Token));
                    break;

                case AsyncOperation.Throttle:
                    if (sub.IsActive) return;  // already throttling, guard already stored pending
                    sub.Cts      = new CancellationTokenSource();
                    sub.IsActive = true;
                    IncrementAsyncThrottle(enteredState);
                    _ = RunThrottleAsync(invoke, sub.Cts.Token, sub, enteredState);
                    break;

                case AsyncOperation.Parallel:
                    var ctsPar = new CancellationTokenSource();
                    sub.Cts = ctsPar;
                    _ = RunFireAndForget(invoke(ctsPar.Token));
                    break;

                case AsyncOperation.Drop:
                    if (sub.IsActive) return;  // drop while task is running
                    sub.Cts      = new CancellationTokenSource();
                    sub.IsActive = true;
                    IncrementDropCount(enteredState);
                    _ = RunDropAsync(invoke, sub.Cts.Token, sub, enteredState);
                    break;
            }
        }

        // ── Async runners ────────────────────────────────────────────────────────

        private async Task RunThrottleAsync(
            Func<CancellationToken, Task> invoke,
            CancellationToken             ct,
            AsyncSub                      sub,
            TState                        enteredState)
        {
            try
            {
                await invoke(ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                OnError?.Invoke(ex, null, CallbackType.EnterStateAsync);
            }
            finally
            {
                sub.IsActive = false;
                DecrementAsyncThrottle(enteredState);
                if (!_disposed)
                    TryReleasePending(enteredState);
            }
        }

        private async Task RunDropAsync(
            Func<CancellationToken, Task> invoke,
            CancellationToken             ct,
            AsyncSub                      sub,
            TState                        enteredState)
        {
            try
            {
                await invoke(ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                OnError?.Invoke(ex, null, CallbackType.EnterStateAsync);
            }
            finally
            {
                sub.IsActive = false;
                DecrementDropCount(enteredState);
                // Drop does NOT call TryReleasePending — blocked transitions are discarded
            }
        }

        private async Task RunFireAndForget(Task task)
        {
            try   { await task; }
            catch (OperationCanceledException) { }
            catch (Exception ex) { OnError?.Invoke(ex, null, CallbackType.EnterStateAsync); }
        }

        // ── Throttle count helpers ────────────────────────────────────────────────

        private void IncrementAsyncThrottle(TState state)
        {
            _asyncThrottleCount ??= new Dictionary<TState, int>();
            _asyncThrottleCount.TryGetValue(state, out var c);
            _asyncThrottleCount[state] = c + 1;
        }

        private void DecrementAsyncThrottle(TState state)
        {
            if (_asyncThrottleCount == null ||
                !_asyncThrottleCount.TryGetValue(state, out var c)) return;
            if (c > 0) _asyncThrottleCount[state] = c - 1;
        }

        private void IncrementDropCount(TState state)
        {
            _activeDropCount ??= new Dictionary<TState, int>();
            _activeDropCount.TryGetValue(state, out var c);
            _activeDropCount[state] = c + 1;
        }

        private void DecrementDropCount(TState state)
        {
            if (_activeDropCount == null ||
                !_activeDropCount.TryGetValue(state, out var c)) return;
            if (c > 0) _activeDropCount[state] = c - 1;
        }

        // ── Cancel helpers (called by Guards / Dispose) ───────────────────────────

        internal void CancelAllAsyncCts()
        {
            if (_asyncSubs == null) return;
            foreach (var sub in _asyncSubs)
                sub.Cts?.Cancel();
        }

        internal void CancelAsyncCtsForState(TState state)
        {
            if (_asyncSubs == null) return;
            foreach (var sub in _asyncSubs)
            {
                if (!sub.HasTargetState || sub.TargetState.Equals(state))
                    sub.Cts?.Cancel();
            }
        }

        private void CancelSubAndRelease(AsyncSub sub)
        {
            // Always cancel any running task, regardless of policy.
            // Switch/Parallel never set IsActive, but their CTS must still be cancelled on dispose.
            sub.Cts?.Cancel();
            if (sub.IsActive)
            {
                sub.IsActive = false;
                if (sub.HasTargetState)
                {
                    if (sub.Policy == AsyncOperation.Drop)
                        DecrementDropCount(sub.TargetState);
                    else
                        DecrementAsyncThrottle(sub.TargetState);
                }
            }
        }

        // ── Cleanup ──────────────────────────────────────────────────────────────

        private void DisposeAsync()
        {
            if (_asyncSubs == null) return;
            foreach (var sub in _asyncSubs)
                sub.Cts?.Cancel();
            _asyncSubs.Clear();
            _activeDropCount?.Clear();
        }
    }
}
