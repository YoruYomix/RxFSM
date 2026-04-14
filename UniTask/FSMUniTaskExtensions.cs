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
            if (ct.IsCancellationRequested)
                return UniTask.FromCanceled(ct);

            var tcs = new UniTaskCompletionSource();
            IDisposable enterHandle = null;
            CancellationTokenRegistration ctReg = default;

            enterHandle = sm.EnterState(targetState, (prev, trg) =>
            {
                ctReg.Dispose();
                enterHandle?.Dispose();
                tcs.TrySetResult();
            });

            if (ct.CanBeCanceled)
                ctReg = ct.Register(() =>
                {
                    enterHandle?.Dispose();
                    tcs.TrySetCanceled(ct);
                });

            return tcs.Task;
        }

        // ── Await any state matching a predicate ────────────────────────────────

        public static UniTask ToUniTask<TState>(
            this IFSM<TState> sm,
            Func<(TState Current, object Trigger), bool> predicate,
            CancellationToken ct = default)
            where TState : Enum
        {
            if (ct.IsCancellationRequested)
                return UniTask.FromCanceled(ct);

            var tcs = new UniTaskCompletionSource();
            IDisposable enterHandle = null;
            CancellationTokenRegistration ctReg = default;

            enterHandle = sm.EnterState((cur, prev, trg) =>
            {
                if (!predicate((cur, trg))) return;
                ctReg.Dispose();
                enterHandle?.Dispose();
                tcs.TrySetResult();
            });

            if (ct.CanBeCanceled)
                ctReg = ct.Register(() =>
                {
                    enterHandle?.Dispose();
                    tcs.TrySetCanceled(ct);
                });

            return tcs.Task;
        }
    }
}
