using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RxFSM
{
    public class FSMUniTaskExtensionTests : MonoBehaviour
    {
        enum TestState { Idle, Walk, Run, Hit }
        readonly struct MoveStarted { }

        int _pass, _fail;
        void Assert(bool c, string label)
        {
            if (c) { Debug.Log($"[PASS] {label}"); _pass++; }
            else   { Debug.LogError($"[FAIL] {label}"); _fail++; }
        }

        void Start() => StartCoroutine(RunAll());

        IEnumerator RunAll()
        {
            yield return T_ToUniTaskAwaitsState().ToCoroutine();
            yield return T_ToUniTaskCancellation().ToCoroutine();
            yield return T_ToUniTaskPredicateOverload().ToCoroutine();
            PrintFinal();
        }

        void PrintFinal()
        {
            int total = _pass + _fail;
            if (_fail == 0) Debug.Log($"=== FSMUniTaskExtensionTests: {_pass}/{total} passed ===");
            else            Debug.LogError($"=== FSMUniTaskExtensionTests: {_pass}/{total} passed, {_fail} FAILED ===");
        }

        // ── ToUniTask awaits state ────────────────────────────────────────────────

        async UniTask T_ToUniTaskAwaitsState()
        {
            var sm = FSM.Create<TestState>(TestState.Idle)
                .AddTransition<MoveStarted>(TestState.Idle, TestState.Walk)
                .Build();

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            TriggerAfterDelay(sm, cts.Token).Forget();

            await sm.ToUniTask(TestState.Walk, cts.Token);
            Assert(sm.State == TestState.Walk, "ToUniTask resolves when target state entered");
            sm.Dispose();
        }

        async UniTask TriggerAfterDelay(FSM<TestState> sm, CancellationToken ct)
        {
            await UniTask.Delay(100, cancellationToken: ct);
            sm.Trigger(new MoveStarted());
        }

        // ── ToUniTask cancellation ────────────────────────────────────────────────

        async UniTask T_ToUniTaskCancellation()
        {
            var sm = FSM.Create<TestState>(TestState.Idle).Build();
            var cts = new CancellationTokenSource();

            var task = sm.ToUniTask(TestState.Walk, cts.Token);
            cts.Cancel();

            bool wasCancelled = false;
            try   { await task; }
            catch (OperationCanceledException) { wasCancelled = true; }

            Assert(wasCancelled, "ToUniTask throws OperationCanceledException on cancel");
            sm.Dispose();
        }

        // ── ToUniTask predicate overload ──────────────────────────────────────────

        async UniTask T_ToUniTaskPredicateOverload()
        {
            var sm = FSM.Create<TestState>(TestState.Idle)
                .AddTransition<MoveStarted>(TestState.Idle, TestState.Walk)
                .AddTransition<MoveStarted>(TestState.Walk, TestState.Run)
                .Build();

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            TriggerTwice(sm, cts.Token).Forget();

            await sm.ToUniTask(t => t.Current.Equals(TestState.Run), cts.Token);
            Assert(sm.State == TestState.Run, "ToUniTask predicate overload resolves on Run");
            sm.Dispose();
        }

        async UniTask TriggerTwice(FSM<TestState> sm, CancellationToken ct)
        {
            await UniTask.Delay(50, cancellationToken: ct); sm.Trigger(new MoveStarted());
            await UniTask.Delay(50, cancellationToken: ct); sm.Trigger(new MoveStarted());
        }
    }
}