using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RxFSM
{
    public class FSMUniTaskExtensionTests : MonoBehaviour
    {
        public enum S { Idle, Walk, Run, Hit }
        public readonly struct MoveStarted { }
        readonly struct Sprint      { }
        readonly struct Damaged     { public readonly float amount; public Damaged(float a) { amount = a; } }

        int _pass, _fail;
        void Assert(bool c, string label)
        {
            if (c) { Debug.Log($"[PASS] {label}"); _pass++; }
            else   { Debug.LogError($"[FAIL] {label}"); _fail++; }
        }

        void Start() => StartCoroutine(RunAll());

        IEnumerator RunAll()
        {
            yield return T6_1_ToUniTaskAwaitsState().ToCoroutine();
            yield return T6_2_ToUniTaskCancellation().ToCoroutine();
            yield return T6_3_ToUniTaskPredicateOverload().ToCoroutine();
            yield return T6_4_ToUniTaskPredicateChecksTriggerValue().ToCoroutine();
            yield return T6_5_ToUniTaskAlreadyInTargetState().ToCoroutine();
            yield return T6_6_ToUniTaskDisposedFsmWhileWaiting().ToCoroutine();
            yield return T6_7_ToUniTaskMultipleConcurrentAwaits().ToCoroutine();
            yield return T6_8_ActionTableExitStateAsyncAttribute().ToCoroutine();
            PrintFinal();
        }

        void PrintFinal()
        {
            int total = _pass + _fail;
            if (_fail == 0) Debug.Log($"=== FSMUniTaskExtensionTests: {_pass}/{total} passed ===");
            else            Debug.LogError($"=== FSMUniTaskExtensionTests: {_pass}/{total} passed, {_fail} FAILED ===");
        }

        // ── T6.1 — ToUniTask awaits state ────────────────────────────────────────

        async UniTask T6_1_ToUniTaskAwaitsState()
        {
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .Build();

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            TriggerAfterDelay(sm, 100, cts.Token).Forget();

            await sm.ToUniTask(S.Walk, cts.Token);
            Assert(sm.State == S.Walk, "T6.1 — ToUniTask resolves when target state entered");
            sm.Dispose();
        }

        // ── T6.2 — ToUniTask cancellation ────────────────────────────────────────

        async UniTask T6_2_ToUniTaskCancellation()
        {
            var sm  = FSM.Create<S>(S.Idle).Build();
            var cts = new CancellationTokenSource();

            var task = sm.ToUniTask(S.Walk, cts.Token);
            cts.Cancel();

            bool wasCancelled = false;
            try   { await task; }
            catch (OperationCanceledException) { wasCancelled = true; }

            Assert(wasCancelled, "T6.2 — ToUniTask throws OperationCanceledException on cancel");
            sm.Dispose();
        }

        // ── T6.3 — ToUniTask predicate overload resolves on matching state ───────

        async UniTask T6_3_ToUniTaskPredicateOverload()
        {
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .AddTransition<MoveStarted>(S.Walk, S.Run)
                .Build();

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            TriggerTwice(sm, 50, cts.Token).Forget();

            await sm.ToUniTask(t => t.Current.Equals(S.Run), cts.Token);
            Assert(sm.State == S.Run, "T6.3 — predicate overload resolves when state == Run");
            sm.Dispose();
        }

        // ── T6.4 — predicate receives trigger value ───────────────────────────────

        async UniTask T6_4_ToUniTaskPredicateChecksTriggerValue()
        {
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<Damaged>(S.Idle, S.Hit)
                .AddTransition<Damaged>(S.Idle, S.Hit) // same — different amounts trigger each time
                .Build();

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Only resolve when Damaged.amount > 50
            TriggerDamagedSequence(sm, cts.Token).Forget();

            await sm.ToUniTask(
                t => t.Current.Equals(S.Hit) && t.Trigger is Damaged d && d.amount > 50f,
                cts.Token);

            Assert(sm.State == S.Hit, "T6.4 — predicate with trigger value check works");
            sm.Dispose();
        }

        // ── T6.5 — FSM already in target state when ToUniTask is called ──────────

        async UniTask T6_5_ToUniTaskAlreadyInTargetState()
        {
            var sm = FSM.Create<S>(S.Walk).Build(); // starts at Walk

            bool resolved = false;
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            // If FSM is already in Walk, ToUniTask must wait for the NEXT Walk entry
            // (i.e., it registers an EnterState callback, not a current-state check)
            var awaiter = sm.ToUniTask(S.Walk, cts.Token)
                .ContinueWith(() => resolved = true);

            await UniTask.Delay(100, cancellationToken: cts.Token);
            Assert(!resolved, "T6.5a — ToUniTask does not resolve immediately if already in state");

            sm.ForceTransitionTo(S.Idle);
            sm.TransitionTo(S.Walk); // actual entry event
            await UniTask.Delay(50, cancellationToken: cts.Token);
            Assert(resolved, "T6.5b — ToUniTask resolves on next Walk entry");
            sm.Dispose();
        }

        // ── T6.6 — FSM disposed while ToUniTask is waiting ───────────────────────

        async UniTask T6_6_ToUniTaskDisposedFsmWhileWaiting()
        {
            var sm  = FSM.Create<S>(S.Idle).Build();
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var task = sm.ToUniTask(S.Walk, cts.Token);
            sm.Dispose(); // dispose before state is entered

            // Either cancels or completes — must not hang or throw unexpected
            bool finished = false;
            DisposeAndAwait(task, () => finished = true, cts.Token).Forget();
            await UniTask.Delay(200, cancellationToken: cts.Token);
            Assert(finished, "T6.6 — ToUniTask does not hang after FSM disposed");
            cts.Cancel();
        }

        // ── T6.7 — Multiple concurrent ToUniTask awaits on same FSM ─────────────

        async UniTask T6_7_ToUniTaskMultipleConcurrentAwaits()
        {
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .AddTransition<Sprint>     (S.Walk, S.Run)
                .Build();

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            bool walkResolved = false;
            bool runResolved  = false;

            var t1 = sm.ToUniTask(S.Walk, cts.Token).ContinueWith(() => walkResolved = true);
            var t2 = sm.ToUniTask(S.Run,  cts.Token).ContinueWith(() => runResolved  = true);

            TriggerSequential(sm, cts.Token).Forget();

            await UniTask.WhenAll(t1, t2);

            Assert(walkResolved, "T6.7a — first concurrent ToUniTask (Walk) resolved");
            Assert(runResolved,  "T6.7b — second concurrent ToUniTask (Run) resolved");
            sm.Dispose();
        }

        // ── T6.8 — [ExitStateAsync] action table source generator ───────────────

        async UniTask T6_8_ActionTableExitStateAsyncAttribute()
        {
            var table = new UniTaskExitAsyncActionTable();
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .Build();

            sm.AddActionTable(table);
            sm.Trigger(new MoveStarted());
            Assert(sm.State == S.Idle, "T6.8a — [ExitStateAsync] blocks transition until awaited");

            await UniTask.Delay(100);

            Assert(sm.State == S.Walk, "T6.8b — [ExitStateAsync] completes transition");
            Assert(table.CallCount == 1 &&
                   table.GotNext == S.Walk &&
                   table.GotTrigger is MoveStarted,
                "T6.8c — source generator wires [ExitStateAsync] with next and trigger");
            sm.Dispose();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        async UniTask TriggerAfterDelay(FSM<S> sm, int ms, CancellationToken ct)
        {
            await UniTask.Delay(ms, cancellationToken: ct);
            sm.Trigger(new MoveStarted());
        }

        async UniTask TriggerTwice(FSM<S> sm, int ms, CancellationToken ct)
        {
            await UniTask.Delay(ms, cancellationToken: ct); sm.Trigger(new MoveStarted());
            await UniTask.Delay(ms, cancellationToken: ct); sm.Trigger(new MoveStarted());
        }

        async UniTask TriggerDamagedSequence(FSM<S> sm, CancellationToken ct)
        {
            await UniTask.Delay(50, cancellationToken: ct);
            sm.Trigger(new Damaged(10f));  // small — predicate rejects
            sm.ForceTransitionTo(S.Idle);
            await UniTask.Delay(50, cancellationToken: ct);
            sm.Trigger(new Damaged(100f)); // large — predicate accepts
        }

        async UniTask TriggerSequential(FSM<S> sm, CancellationToken ct)
        {
            await UniTask.Delay(50, cancellationToken: ct);
            sm.Trigger(new MoveStarted()); // → Walk
            await UniTask.Delay(50, cancellationToken: ct);
            sm.Trigger(new Sprint());       // → Run
        }

        async UniTask DisposeAndAwait(UniTask task, Action onDone, CancellationToken ct)
        {
            try   { await task; }
            catch { /* cancelled or other — either is acceptable */ }
            onDone();
        }
    }

    [ActionTable(FSMUniTaskExtensionTests.S.Idle)]
    partial class UniTaskExitAsyncActionTable
    {
        public int CallCount;
        public FSMUniTaskExtensionTests.S GotNext;
        public object GotTrigger;

        [ExitStateAsync]
        async UniTask OnExit(FSMUniTaskExtensionTests.S next, FSMUniTaskExtensionTests.MoveStarted trigger, CancellationToken ct)
        {
            CallCount++;
            GotNext = next;
            GotTrigger = trigger;
            await UniTask.Delay(50, cancellationToken: ct);
        }
    }
}
