using System;
using System.Collections;
using UnityEngine;

namespace RxFSM
{
    public class Phase5Tester : MonoBehaviour
    {
        // ── Enums / Triggers ─────────────────────────────────────────────────────
        enum TestState  { Idle, Walk, Run, Hit }
        readonly struct MoveStarted { }
        readonly struct MoveStopped { }
        readonly struct Damaged     { }

        // ── Counters ─────────────────────────────────────────────────────────────
        int _pass, _fail;
        void Assert(bool c, string label)
        {
            if (c) { Debug.Log($"[PASS] {label}"); _pass++; }
            else   { Debug.LogError($"[FAIL] {label}"); _fail++; }
        }

        // ── Entry ─────────────────────────────────────────────────────────────────
        void Start()
        {
            T5_1_ObserverCanTrigger();
            T5_2_ObservableCanEnterState();
            StartCoroutine(RunAsync());
        }

        IEnumerator RunAsync()
        {
            yield return StartCoroutine(T5_3_Phase1234Regression());
            PrintFinal();
        }

        void PrintFinal()
        {
            int total = _pass + _fail;
            if (_fail == 0) Debug.Log($"=== Phase 5: {_pass}/{total} passed ===");
            else            Debug.LogError($"=== Phase 5: {_pass}/{total} passed, {_fail} FAILED ===");
        }

        // ─────────────────────────────────────────────────────────────────────────

        void T5_1_ObserverCanTrigger()
        {
            var sm = FSM.Create<TestState>(TestState.Idle)
                .AddTransition<MoveStarted>(TestState.Idle, TestState.Walk)
                .Build();

            // Assign to restricted interface — only Trigger/TransitionTo visible
            IFSMObserver<TestState> observer = sm;
            observer.Trigger(new MoveStarted());
            Assert(observer.State == TestState.Walk,
                   "T5.1 — IFSMObserver.Trigger drives state");

            // Verify interface type constraints compile: observer.EnterState is NOT available
            // (would be CS1061 if attempted — validated by absence in this file)
            sm.Dispose();
        }

        void T5_2_ObservableCanEnterState()
        {
            var sm = FSM.Create<TestState>(TestState.Idle)
                .AddTransition<MoveStarted>(TestState.Idle, TestState.Walk)
                .Build();

            bool entered = false;
            // Assign to restricted interface — only observation visible
            IFSMObservable<TestState> observable = sm;
            observable.EnterState((cur, prev) => entered = true);

            // Trigger via full reference (not via observable — Trigger not visible there)
            sm.Trigger(new MoveStarted());
            Assert(entered,
                   "T5.2a — IFSMObservable.EnterState callback fires");
            Assert(observable.State == TestState.Walk,
                   "T5.2b — IFSMObservable.State reflects current state");
            sm.Dispose();
        }

        IEnumerator T5_3_Phase1234Regression()
        {
            // ── Phase 1 basic ────────────────────────────────────────────────────
            {
                var sm = FSM.Create<TestState>(TestState.Idle)
                    .AddTransition<MoveStarted>(TestState.Idle, TestState.Walk)
                    .AddTransition<MoveStopped>(TestState.Walk, TestState.Idle)
                    .Build();
                sm.Trigger(new MoveStarted()); Assert(sm.State == TestState.Walk, "T5.3/T1.1a");
                sm.Trigger(new MoveStopped()); Assert(sm.State == TestState.Idle, "T5.3/T1.1b");
                sm.Dispose();
            }

            // ── Phase 2 TickState ────────────────────────────────────────────────
            {
                int ticks = 0;
                var sm = FSM.Create<TestState>(TestState.Idle).Build();
                sm.TickState(TestState.Walk, (prev, trg) => ticks++);
                sm.TransitionTo(TestState.Walk);
                yield return null; yield return null;
                Assert(ticks > 0, "T5.3/T2.11 — TickState fires");
                sm.Dispose();
            }

            // ── Phase 3 ThrottleState ────────────────────────────────────────────
            {
                var sm = FSM.Create<TestState>(TestState.Idle)
                    .AddTransition<MoveStarted>(TestState.Idle, TestState.Walk)
                    .AddTransition<MoveStopped>(TestState.Walk, TestState.Idle)
                    .ThrottleState(TestState.Walk, 0.3f)
                    .Build();
                sm.Trigger(new MoveStarted());
                sm.Trigger(new MoveStopped());
                Assert(sm.State == TestState.Walk, "T5.3/T3.1a — throttle blocks");
                yield return new WaitForSeconds(0.5f);
                Assert(sm.State == TestState.Idle, "T5.3/T3.1b — released after timer");
                sm.Dispose();
            }

            // ── Phase 4 nested ───────────────────────────────────────────────────
            {
                var groundSm = FSM.Create<TestState>(TestState.Idle)
                    .AddTransition<MoveStarted>(TestState.Idle, TestState.Walk)
                    .Build();
                var charFsm = FSM.Create<TestState>(TestState.Idle)
                    .AddTransition<MoveStarted>(TestState.Idle, TestState.Walk)
                    .Register(TestState.Walk, groundSm)
                    .Build();

                // Trigger propagation: charFsm → Walk, then MoveStarted again propagates to groundSm
                charFsm.Trigger(new MoveStarted()); // Idle→Walk (charFsm)
                charFsm.Trigger(new MoveStarted()); // propagates to groundSm → Walk
                Assert(groundSm.State == TestState.Walk, "T5.3/T4.1 — nested trigger propagation");
                charFsm.Dispose();
            }

            // ── Phase 5 IFSM<TState> full interface ──────────────────────────────
            {
                var sm = FSM.Create<TestState>(TestState.Idle)
                    .AddTransition<MoveStarted>(TestState.Idle, TestState.Walk)
                    .Build();

                IFSM<TestState> ifsm = sm;   // implicit upcast
                bool gotEnter = false;
                ifsm.EnterState((cur, prev) => gotEnter = true);
                ifsm.Trigger(new MoveStarted());
                Assert(gotEnter, "T5.3/T5 — IFSM<TState> exposes both observer and observable");
                ifsm.Dispose();
            }
        }
    }
}
