using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RxFSM
{
    public class Phase3Tester : MonoBehaviour
    {
        // ── Shared enums & triggers ───────────────────────────────────────────────
        public enum S { Idle, Walk, Run, Hit, Dead }

        readonly struct MoveStarted { }
        readonly struct MoveStopped { }
        readonly struct Sprint      { }
        readonly struct Damaged     { public readonly float amount; public Damaged(float a) { amount = a; } }
        readonly struct Healed      { }
        readonly struct Killed      { }
        readonly struct AttackInput { }
        readonly struct AttackEnd   { }
        readonly struct Recovered   { }

        // ── Counters ──────────────────────────────────────────────────────────────
        int _pass, _fail;

        void Assert(bool cond, string label)
        {
            if (cond) { Debug.Log($"[PASS] {label}"); _pass++; }
            else      { Debug.LogError($"[FAIL] {label}"); _fail++; }
        }

        // ── Entry point ───────────────────────────────────────────────────────────
        void Start()
        {
            RunSyncTests();
            StartCoroutine(RunAsyncTests());
        }

        void RunSyncTests()
        {
            T3_4_ThrottleBypassedByForce();
            T3_9_AutoTransitionCallback();
            T3_18_Connect();
            T3_19_ConnectDispose();
            T3_20_DeactivateBlocksTrigger();
            T3_21_DeactivateBlocksTransitionTo();
            T3_22_DeactivateBlocksForce();
            T3_23_DeactivateCancelsAsync();
            T3_24_DeactivateRefCount();
        }

        IEnumerator RunAsyncTests()
        {
            yield return StartCoroutine(T3_1_ThrottleStateBlocks());
            yield return StartCoroutine(T3_2_ThrottleLastTriggerWins());
            yield return StartCoroutine(T3_5_ThrottleFrameState());
            yield return StartCoroutine(T3_6_HoldState());
            yield return StartCoroutine(T3_7_AutoTransitionTime());
            yield return StartCoroutine(T3_8_AutoTransitionCancelledByExit());
            yield return StartCoroutine(T3_10_AutoTransitionReenterResetsTimer());
            yield return StartCoroutine(T3_11_AsyncSwitch());
            yield return StartCoroutine(T3_12_AsyncThrottleBlocks());
            yield return StartCoroutine(T3_13_AsyncThrottlePlusThrottleState());
            yield return StartCoroutine(T3_14_AsyncParallelNoBlock());
            yield return StartCoroutine(T3_15_MultipleThrottlesAnd());
            yield return StartCoroutine(T3_16_MultipleSwitchesCancelled());
            yield return StartCoroutine(T3_17_ThrottlePlusSwitchMixed());
            yield return StartCoroutine(T3_25_DeactivateSuppressTEU());
            yield return StartCoroutine(T3_26_DeactivateCancelsThrottle());
            yield return StartCoroutine(T3_27_DeactivateCancelsAutoTransition());
            yield return StartCoroutine(T3_28_Phase12Regression());
            PrintFinal();
        }

        void PrintFinal()
        {
            int total = _pass + _fail;
            if (_fail == 0)
                Debug.Log($"=== Phase 3: {_pass}/{total} passed ===");
            else
                Debug.LogError($"=== Phase 3: {_pass}/{total} passed, {_fail} FAILED ===");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // SYNC TESTS
        // ─────────────────────────────────────────────────────────────────────────

        void T3_4_ThrottleBypassedByForce()
        {
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .ThrottleState(S.Walk, 10f)
                .Build();

            sm.Trigger(new MoveStarted());
            Assert(sm.State == S.Walk, "T3.4a — entered Walk");
            sm.ForceTransitionTo(S.Dead);
            Assert(sm.State == S.Dead, "T3.4 — ForceTransition bypasses ThrottleState");
            sm.Dispose();
        }

        void T3_9_AutoTransitionCallback()
        {
            Action onCompleteCallback = null;
            var sm = FSM.Create<S>(S.Idle)
                .AutoTransition(S.Hit, S.Idle, cb => onCompleteCallback = cb)
                .Build();

            sm.TransitionTo(S.Hit);
            Assert(onCompleteCallback != null, "T3.9a — callback registered on entry");
            onCompleteCallback?.Invoke();
            Assert(sm.State == S.Idle, "T3.9 — callback-based AutoTransition fires");
            sm.Dispose();
        }

        void T3_18_Connect()
        {
            Action<Damaged> externalEvent = null;
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<Damaged>(S.Idle, S.Hit)
                .Build();

            sm.Connect<Damaged>(h => externalEvent += h, h => externalEvent -= h);
            externalEvent?.Invoke(new Damaged(10));
            Assert(sm.State == S.Hit, "T3.18 — Connect routes external event to FSM");
            sm.Dispose();
        }

        void T3_19_ConnectDispose()
        {
            Action<Damaged> externalEvent = null;
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<Damaged>(S.Idle, S.Hit)
                .Build();

            var handle = sm.Connect<Damaged>(h => externalEvent += h, h => externalEvent -= h);
            handle.Dispose();
            externalEvent?.Invoke(new Damaged(10));
            Assert(sm.State == S.Idle, "T3.19 — Connect dispose unsubscribes");
            sm.Dispose();
        }

        void T3_20_DeactivateBlocksTrigger()
        {
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .Build();

            var handle = sm.Deactivate();
            sm.Trigger(new MoveStarted());
            Assert(sm.State == S.Idle, "T3.20a — Deactivate blocks Trigger");
            handle.Dispose();
            sm.Trigger(new MoveStarted());
            Assert(sm.State == S.Walk, "T3.20b — after Deactivate dispose, Trigger works");
            sm.Dispose();
        }

        void T3_21_DeactivateBlocksTransitionTo()
        {
            var sm = FSM.Create<S>(S.Idle).Build();

            var handle = sm.Deactivate();
            sm.TransitionTo(S.Walk);
            Assert(sm.State == S.Idle, "T3.21a — Deactivate blocks TransitionTo");
            handle.Dispose();
            sm.TransitionTo(S.Walk);
            Assert(sm.State == S.Walk, "T3.21b — after dispose, TransitionTo works");
            sm.Dispose();
        }

        void T3_22_DeactivateBlocksForce()
        {
            var sm = FSM.Create<S>(S.Idle).Build();

            var handle = sm.Deactivate();
            sm.ForceTransitionTo(S.Dead);
            Assert(sm.State == S.Idle, "T3.22a — Deactivate blocks ForceTransitionTo");
            handle.Dispose();
            sm.ForceTransitionTo(S.Dead);
            Assert(sm.State == S.Dead, "T3.22b — after dispose, ForceTransitionTo works");
            sm.Dispose();
        }

        void T3_23_DeactivateCancelsAsync()
        {
            bool wasCancelled = false;
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .Build();

            sm.EnterStateAsync(async (cur, prev, ct) =>
            {
                try   { await Task.Delay(5000, ct); }
                catch (OperationCanceledException) { wasCancelled = true; }
            }, AsyncOperation.Switch);

            sm.TransitionTo(S.Walk);
            var handle = sm.Deactivate();
            // CT cancel is synchronous; the catch runs on the task continuation thread,
            // so we give it a tiny moment via the Unity update loop — but since this
            // is sync test we just check the flag after a small actual sleep.
            System.Threading.Thread.Sleep(50);
            Assert(wasCancelled, "T3.23 — Deactivate cancels active async task");
            handle.Dispose();
            sm.Dispose();
        }

        void T3_24_DeactivateRefCount()
        {
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .Build();

            var h1 = sm.Deactivate();
            var h2 = sm.Deactivate();
            h1.Dispose();
            sm.Trigger(new MoveStarted());
            Assert(sm.State == S.Idle, "T3.24a — h2 still active, Trigger blocked");
            h2.Dispose();
            sm.Trigger(new MoveStarted());
            Assert(sm.State == S.Walk, "T3.24b — both disposed, Trigger works");
            sm.Dispose();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ASYNC / COROUTINE TESTS
        // ─────────────────────────────────────────────────────────────────────────

        IEnumerator T3_1_ThrottleStateBlocks()
        {
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .AddTransition<MoveStopped>(S.Walk, S.Idle)
                .ThrottleState(S.Walk, 0.3f)
                .Build();

            sm.Trigger(new MoveStarted());
            Assert(sm.State == S.Walk, "T3.1a — entered Walk");
            sm.Trigger(new MoveStopped());
            Assert(sm.State == S.Walk, "T3.1b — MoveStopped blocked by throttle");

            yield return new WaitForSeconds(0.5f);
            Assert(sm.State == S.Idle, "T3.1 — pending trigger executed after throttle");
            sm.Dispose();
        }

        IEnumerator T3_2_ThrottleLastTriggerWins()
        {
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .AddTransition<Damaged>(S.Walk, S.Hit)
                .AddTransition<Healed>(S.Walk, S.Idle)
                .ThrottleState(S.Walk, 0.3f)
                .Build();

            sm.Trigger(new MoveStarted());
            sm.Trigger(new Damaged(10));   // pending = Damaged
            sm.Trigger(new Healed());      // pending = Healed (overwrites)

            yield return new WaitForSeconds(0.5f);
            Assert(sm.State == S.Idle, "T3.2 — last trigger (Healed) wins, went to Idle");
            sm.Dispose();
        }

        IEnumerator T3_5_ThrottleFrameState()
        {
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .AddTransition<MoveStopped>(S.Walk, S.Idle)
                .ThrottleFrameState(S.Walk, 3)
                .Build();

            sm.Trigger(new MoveStarted());
            sm.Trigger(new MoveStopped());
            Assert(sm.State == S.Walk, "T3.5a — blocked immediately");

            yield return null; yield return null; // 2 frames
            Assert(sm.State == S.Walk, "T3.5b — still blocked after 2 frames");

            yield return null; yield return null; // 2 more frames (total 4 ≥ 3)
            Assert(sm.State == S.Idle, "T3.5 — unblocked after frame count reached");
            sm.Dispose();
        }

        IEnumerator T3_6_HoldState()
        {
            bool ready = false;
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .AddTransition<MoveStopped>(S.Walk, S.Idle)
                .HoldState(S.Walk, () => ready)
                .Build();

            sm.Trigger(new MoveStarted());
            sm.Trigger(new MoveStopped());
            Assert(sm.State == S.Walk, "T3.6a — HoldState blocks (ready=false)");

            ready = true;
            yield return null; // next frame — Stage 0 checks condition
            Assert(sm.State == S.Idle, "T3.6 — HoldState released when condition met");
            sm.Dispose();
        }

        IEnumerator T3_7_AutoTransitionTime()
        {
            var sm = FSM.Create<S>(S.Idle).AutoTransition(S.Hit, S.Idle, 0.3f).Build();

            sm.TransitionTo(S.Hit);
            yield return new WaitForSeconds(0.45f);
            Assert(sm.State == S.Idle, "T3.7 — time-based AutoTransition fired");
            sm.Dispose();
        }

        IEnumerator T3_8_AutoTransitionCancelledByExit()
        {
            var sm = FSM.Create<S>(S.Idle).AutoTransition(S.Hit, S.Idle, 1f).Build();

            sm.TransitionTo(S.Hit);
            yield return new WaitForSeconds(0.1f);
            sm.TransitionTo(S.Walk);
            yield return new WaitForSeconds(1.5f);
            Assert(sm.State == S.Walk, "T3.8 — AutoTransition cancelled by early exit");
            sm.Dispose();
        }

        IEnumerator T3_10_AutoTransitionReenterResetsTimer()
        {
            var sm = FSM.Create<S>(S.Idle).AutoTransition(S.Hit, S.Idle, 0.5f).Build();

            sm.TransitionTo(S.Hit);
            yield return new WaitForSeconds(0.3f);
            sm.TransitionTo(S.Walk);
            sm.TransitionTo(S.Hit); // re-enter → timer resets

            yield return new WaitForSeconds(0.3f);
            Assert(sm.State == S.Hit, "T3.10a — timer restarted, not yet fired");

            yield return new WaitForSeconds(0.3f);
            Assert(sm.State == S.Idle, "T3.10 — fired after full reset duration");
            sm.Dispose();
        }

        IEnumerator T3_11_AsyncSwitch()
        {
            bool wasCancelled = false;
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .Build();

            sm.EnterStateAsync(async (cur, prev, ct) =>
            {
                try   { await Task.Delay(5000, ct); }
                catch (OperationCanceledException) { wasCancelled = true; }
            }, AsyncOperation.Switch);

            sm.TransitionTo(S.Walk);
            sm.TransitionTo(S.Idle);

            yield return new WaitForSeconds(0.1f);
            Assert(wasCancelled, "T3.11 — Switch CT cancelled on transition");
            sm.Dispose();
        }

        IEnumerator T3_12_AsyncThrottleBlocks()
        {
            bool asyncDone = false;
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .AddTransition<MoveStopped>(S.Walk, S.Idle)
                .Build();

            sm.EnterStateAsync(async (cur, prev, ct) =>
            {
                await Task.Delay(300, ct);
                asyncDone = true;
            }, AsyncOperation.Throttle);

            sm.TransitionTo(S.Walk);
            sm.Trigger(new MoveStopped());
            Assert(sm.State == S.Walk, "T3.12a — transition blocked by Throttle");

            yield return new WaitForSeconds(0.5f);
            Assert(asyncDone,        "T3.12b — async task completed");
            Assert(sm.State == S.Idle, "T3.12 — pending trigger executed after Throttle");
            sm.Dispose();
        }

        IEnumerator T3_13_AsyncThrottlePlusThrottleState()
        {
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .AddTransition<MoveStopped>(S.Walk, S.Idle)
                .ThrottleState(S.Walk, 0.3f)
                .Build();

            sm.EnterStateAsync(S.Walk, async (prev, ct) =>
            {
                await Task.Delay(600, ct);
            }, AsyncOperation.Throttle);

            sm.Trigger(new MoveStarted());
            sm.Trigger(new MoveStopped());

            yield return new WaitForSeconds(0.45f);
            Assert(sm.State == S.Walk, "T3.13a — ThrottleState done, Async still running → blocked");

            yield return new WaitForSeconds(0.3f);
            Assert(sm.State == S.Idle, "T3.13 — both released → pending executed");
            sm.Dispose();
        }

        IEnumerator T3_14_AsyncParallelNoBlock()
        {
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .AddTransition<MoveStopped>(S.Walk, S.Idle)
                .Build();

            sm.EnterStateAsync(async (cur, prev, ct) =>
            {
                await Task.Delay(5000, ct);
            }, AsyncOperation.Parallel);

            sm.TransitionTo(S.Walk);
            sm.Trigger(new MoveStopped());
            yield return null;
            Assert(sm.State == S.Idle, "T3.14 — Parallel does not block transition");
            sm.Dispose();
        }

        IEnumerator T3_15_MultipleThrottlesAnd()
        {
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .AddTransition<MoveStopped>(S.Walk, S.Idle)
                .Build();

            sm.EnterStateAsync(S.Walk, async (prev, ct) =>
            {
                await Task.Delay(300, ct); // fast
            }, AsyncOperation.Throttle);

            sm.EnterStateAsync(S.Walk, async (prev, ct) =>
            {
                await Task.Delay(600, ct); // slow
            }, AsyncOperation.Throttle);

            sm.Trigger(new MoveStarted());
            sm.Trigger(new MoveStopped());

            yield return new WaitForSeconds(0.45f);
            Assert(sm.State == S.Walk, "T3.15a — fast done, slow still running → blocked");

            yield return new WaitForSeconds(0.3f);
            Assert(sm.State == S.Idle, "T3.15 — both done → pending executed");
            sm.Dispose();
        }

        IEnumerator T3_16_MultipleSwitchesCancelled()
        {
            bool cancel1 = false, cancel2 = false;
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .Build();

            sm.EnterStateAsync(S.Walk, async (prev, ct) =>
            {
                try   { await Task.Delay(5000, ct); }
                catch (OperationCanceledException) { cancel1 = true; }
            }, AsyncOperation.Switch);

            sm.EnterStateAsync(S.Walk, async (prev, ct) =>
            {
                try   { await Task.Delay(5000, ct); }
                catch (OperationCanceledException) { cancel2 = true; }
            }, AsyncOperation.Switch);

            sm.TransitionTo(S.Walk);
            sm.TransitionTo(S.Idle); // exit Walk → both cancelled

            yield return new WaitForSeconds(0.1f);
            Assert(cancel1 && cancel2, "T3.16 — multiple Switches all cancelled on transition");
            sm.Dispose();
        }

        IEnumerator T3_17_ThrottlePlusSwitchMixed()
        {
            bool switchCancelled = false;
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .AddTransition<MoveStopped>(S.Walk, S.Idle)
                .Build();

            sm.EnterStateAsync(S.Walk, async (prev, ct) =>
            {
                await Task.Delay(300, ct);
            }, AsyncOperation.Throttle);

            sm.EnterStateAsync(S.Walk, async (prev, ct) =>
            {
                try   { await Task.Delay(5000, ct); }
                catch (OperationCanceledException) { switchCancelled = true; }
            }, AsyncOperation.Switch);

            sm.TransitionTo(S.Walk);
            sm.Trigger(new MoveStopped());
            Assert(sm.State == S.Walk, "T3.17a — Throttle blocking");

            yield return new WaitForSeconds(0.5f);
            Assert(sm.State == S.Idle,    "T3.17b — Throttle done → transition executed");

            yield return new WaitForSeconds(0.1f);
            Assert(switchCancelled,        "T3.17 — Switch cancelled on transition");
            sm.Dispose();
        }

        IEnumerator T3_25_DeactivateSuppressTEU()
        {
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<Damaged>(S.Idle, S.Hit)
                .Build();

            sm.TriggerEveryUpdate(new Damaged(5));
            var handle = sm.Deactivate();

            yield return null; yield return null; yield return null; // 3 frames
            Assert(sm.State == S.Idle, "T3.25a — TriggerEveryUpdate suppressed by Deactivate");

            handle.Dispose();
            yield return null;
            Assert(sm.State == S.Hit, "T3.25 — TEU resumes after Deactivate released");
            sm.Dispose();
        }

        IEnumerator T3_26_DeactivateCancelsThrottle()
        {
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .AddTransition<Damaged>(S.Walk, S.Hit)
                .ThrottleState(S.Walk, 0.5f)
                .Build();

            sm.Trigger(new MoveStarted());
            sm.Trigger(new Damaged(10)); // pending during throttle
            var handle = sm.Deactivate();

            yield return new WaitForSeconds(0.7f);
            Assert(sm.State == S.Walk, "T3.26 — Deactivate cancels ThrottleState + pending discarded");
            handle.Dispose();
            sm.Dispose();
        }

        IEnumerator T3_27_DeactivateCancelsAutoTransition()
        {
            var sm = FSM.Create<S>(S.Idle).AutoTransition(S.Hit, S.Idle, 0.3f).Build();

            sm.TransitionTo(S.Hit);
            var handle = sm.Deactivate();

            yield return new WaitForSeconds(0.5f);
            Assert(sm.State == S.Hit, "T3.27 — Deactivate suppresses AutoTransition timer");
            handle.Dispose();
            sm.Dispose();
        }

        IEnumerator T3_28_Phase12Regression()
        {
            yield return StartCoroutine(RunPhase1And2Tests());
        }

        // ── Embedded Phase 1+2 regression ────────────────────────────────────────

        IEnumerator RunPhase1And2Tests()
        {
            // T1.1
            {
                var sm = FSM.Create<S>(S.Idle)
                    .AddTransition<MoveStarted>(S.Idle, S.Walk)
                    .AddTransition<MoveStopped>(S.Walk, S.Idle).Build();
                sm.Trigger(new MoveStarted()); Assert(sm.State == S.Walk, "T3.28/T1.1a");
                sm.Trigger(new MoveStopped()); Assert(sm.State == S.Idle, "T3.28/T1.1b");
                sm.Dispose();
            }

            // T1.2
            {
                var sm = FSM.Create<S>(S.Idle)
                    .AddTransition<MoveStarted>(S.Idle, S.Walk).Build();
                sm.Trigger(new MoveStopped());
                Assert(sm.State == S.Idle, "T3.28/T1.2");
                sm.Dispose();
            }

            // T1.9 TransitionTo
            {
                var sm = FSM.Create<S>(S.Idle).Build();
                sm.TransitionTo(S.Dead);
                Assert(sm.State == S.Dead, "T3.28/T1.9a");
                sm.TransitionTo(S.Dead);
                Assert(sm.State == S.Dead, "T3.28/T1.9b");
                sm.Dispose();
            }

            // T2.1 EnterState (cur, prev)
            {
                S gotCur = default, gotPrev = default;
                var sm = FSM.Create<S>(S.Idle)
                    .AddTransition<MoveStarted>(S.Idle, S.Walk).Build();
                sm.EnterState((cur, prev) => { gotCur = cur; gotPrev = prev; });
                sm.Trigger(new MoveStarted());
                Assert(gotCur == S.Walk && gotPrev == S.Idle, "T3.28/T2.1");
                sm.Dispose();
            }

            // T2.5 ExitState fires before state change
            {
                S stateAtExit = S.Dead;
                var sm = FSM.Create<S>(S.Idle)
                    .AddTransition<MoveStarted>(S.Idle, S.Walk).Build();
                sm.ExitState((cur, next, trg) => stateAtExit = sm.State);
                sm.Trigger(new MoveStarted());
                Assert(stateAtExit == S.Idle, "T3.28/T2.5");
                sm.Dispose();
            }

            // T2.11 TickState
            {
                int ticks = 0;
                var sm = FSM.Create<S>(S.Idle).Build();
                sm.TickState(S.Walk, (prev, trg) => ticks++);
                sm.TransitionTo(S.Walk);
                yield return null; yield return null;
                Assert(ticks > 0, "T3.28/T2.11a");
                sm.TransitionTo(S.Idle);
                int frozen = ticks;
                yield return null; yield return null;
                Assert(ticks == frozen, "T3.28/T2.11b");
                sm.Dispose();
            }

            // T2.14 TriggerEveryUpdate
            {
                var sm = FSM.Create<S>(S.Idle)
                    .AddTransition<Damaged>(S.Idle, S.Hit).Build();
                sm.TriggerEveryUpdate(new Damaged(5));
                yield return null;
                Assert(sm.State == S.Hit, "T3.28/T2.14");
                sm.Dispose();
            }
        }
    }
}
