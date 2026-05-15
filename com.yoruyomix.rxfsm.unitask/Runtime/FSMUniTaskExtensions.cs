using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace RxFSM
{
    public static class FSMUniTaskExtensions
    {
        // ── Await a specific state ───────────────────────────────────────────────

        public static UniTask ToUniTask<TState>(
            this IFSM<TState> sm,
            TState targetState,
            CancellationToken ct = default)
            where TState : Enum
        {
            return ToUniTaskCore(sm, sm, targetState, ct);
        }

        public static UniTask ToUniTask<TState>(
            this IFSMObservable<TState> sm,
            TState targetState,
            CancellationToken ct = default)
            where TState : Enum
        {
            return ToUniTaskCore(sm, sm as IFSM<TState>, targetState, ct);
        }

        public static UniTask ToUniTask<TState>(
            this IFSMObserver<TState> sm,
            TState targetState,
            CancellationToken ct = default)
            where TState : Enum
        {
            if (sm is IFSMObservable<TState> observable)
                return ToUniTaskCore(observable, sm as IFSM<TState>, targetState, ct);

            return UniTask.FromException(new InvalidOperationException(
                "ToUniTask requires an FSM instance that also implements IFSMObservable<TState>."));
        }

        // ── Await any state matching a predicate ────────────────────────────────

        public static UniTask ToUniTask<TState>(
            this IFSM<TState> sm,
            Func<(TState Current, object Trigger), bool> predicate,
            CancellationToken ct = default)
            where TState : Enum
        {
            return ToUniTaskCore(sm, sm, predicate, ct);
        }

        public static UniTask ToUniTask<TState>(
            this IFSMObservable<TState> sm,
            Func<(TState Current, object Trigger), bool> predicate,
            CancellationToken ct = default)
            where TState : Enum
        {
            return ToUniTaskCore(sm, sm as IFSM<TState>, predicate, ct);
        }

        public static UniTask ToUniTask<TState>(
            this IFSMObserver<TState> sm,
            Func<(TState Current, object Trigger), bool> predicate,
            CancellationToken ct = default)
            where TState : Enum
        {
            if (sm is IFSMObservable<TState> observable)
                return ToUniTaskCore(observable, sm as IFSM<TState>, predicate, ct);

            return UniTask.FromException(new InvalidOperationException(
                "ToUniTask requires an FSM instance that also implements IFSMObservable<TState>."));
        }

        private static UniTask ToUniTaskCore<TState>(
            IFSMObservable<TState> sm,
            IFSM<TState> lifecycle,
            TState targetState,
            CancellationToken ct)
            where TState : Enum
        {
            if (ct.IsCancellationRequested)
                return UniTask.FromCanceled(ct);

            var tcs = new UniTaskCompletionSource();
            IDisposable enterHandle = null;
            CancellationTokenRegistration ctReg = default;

            void Cleanup()
            {
                ctReg.Dispose();
                enterHandle?.Dispose();
                if (lifecycle != null)
                    lifecycle.OnDisposed -= OnFSMDisposed;
            }

            void OnFSMDisposed()
            {
                Cleanup();
                tcs.TrySetCanceled();
            }

            enterHandle = sm.EnterState(targetState, (prev, trg) =>
            {
                Cleanup();
                tcs.TrySetResult();
            });

            if (lifecycle != null)
                lifecycle.OnDisposed += OnFSMDisposed;

            if (ct.CanBeCanceled)
                ctReg = ct.Register(() =>
                {
                    Cleanup();
                    tcs.TrySetCanceled(ct);
                });

            return tcs.Task;
        }

        private static UniTask ToUniTaskCore<TState>(
            IFSMObservable<TState> sm,
            IFSM<TState> lifecycle,
            Func<(TState Current, object Trigger), bool> predicate,
            CancellationToken ct)
            where TState : Enum
        {
            if (ct.IsCancellationRequested)
                return UniTask.FromCanceled(ct);

            var tcs = new UniTaskCompletionSource();
            IDisposable enterHandle = null;
            CancellationTokenRegistration ctReg = default;

            void Cleanup()
            {
                ctReg.Dispose();
                enterHandle?.Dispose();
                if (lifecycle != null)
                    lifecycle.OnDisposed -= OnFSMDisposed;
            }

            void OnFSMDisposed()
            {
                Cleanup();
                tcs.TrySetCanceled();
            }

            enterHandle = sm.EnterState((cur, prev, trg) =>
            {
                if (!predicate((cur, trg))) return;
                Cleanup();
                tcs.TrySetResult();
            });

            if (lifecycle != null)
                lifecycle.OnDisposed += OnFSMDisposed;

            if (ct.CanBeCanceled)
                ctReg = ct.Register(() =>
                {
                    Cleanup();
                    tcs.TrySetCanceled(ct);
                });

            return tcs.Task;
        }
    }
}
