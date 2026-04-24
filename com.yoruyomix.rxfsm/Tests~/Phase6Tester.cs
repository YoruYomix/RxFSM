using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RxFSM
{
    /// <summary>
    /// Phase 6 — High-stress tester.
    /// Focuses on: new trigger overloads, TransitionOperation.Drop,
    /// handle-dispose edge cases, all Appendix B scenarios, interrupt edge
    /// cases, and complex mixed compositions. Regressions for Phases 3-5
    /// are included at the end.
    /// </summary>
    public class Phase6Tester : MonoBehaviour
    {
        // ── Enums ────────────────────────────────────────────────────────────────
        enum S { Idle, Walk, Run, Hit, Dead, Attack, Cast }

        // ── Triggers ─────────────────────────────────────────────────────────────
        readonly struct MoveStarted { }
        readonly struct MoveStopped { }
        readonly struct Damaged     { public readonly float amount; public Damaged(float a) { amount = a; } }
        readonly struct Healed      { public readonly float hp;     public Healed(float h)  { hp = h;     } }
        readonly struct Killed      { }
        readonly struct AttackInput { public readonly float power;  public AttackInput(float p) { power = p; } }
        readonly struct AttackEnd   { }
        readonly struct CastSpell   { public readonly int   level;  public CastSpell(int l)  { level = l;  } }
        readonly struct Interrupt1  { }
        readonly struct Sprint      { }

        // ── F4 nested-FSM types ───────────────────────────────────────────────────
        enum F4ParentS { A, B }
        enum F4ChildS  { X, Y }
        readonly struct F4GoB { }
        readonly struct F4GoY { }

        // ── Counter + Assert ─────────────────────────────────────────────────────
        int _pass, _fail;
        void Assert(bool c, string label)
        {
            if (c) { Debug.Log($"[PASS] {label}"); _pass++; }
            else   { Debug.LogError($"[FAIL] {label}"); _fail++; }
        }

        // ── IInterrupt helper ────────────────────────────────────────────────────
        class LambdaInterrupt : IInterrupt
        {
            readonly Func<S, CancellationToken, Task> _fn;
            public LambdaInterrupt(Func<S, CancellationToken, Task> fn) { _fn = fn; }
            public ValueTask InvokeAsync(Enum currentState, CancellationToken ct)
                => new ValueTask(_fn((S)currentState, ct));
        }

        // ── ITransitionFilter helper ──────────────────────────────────────────────
        class PredicateFilter : ITransitionFilter
        {
            readonly Func<object, CancellationToken, Task<bool>> _pred;
            public PredicateFilter(Func<object, CancellationToken, Task<bool>> pred) { _pred = pred; }
            public async ValueTask Invoke(object trigger, TransitionContext ctx,
                                          Func<ValueTask> next, CancellationToken ct)
            {
                if (await _pred(trigger, ct)) await next();
            }
        }

        // ── Task → Coroutine bridge ──────────────────────────────────────────────
        IEnumerator Wait(Task t, float timeout = 3f)
        {
            float elapsed = 0f;
            while (!t.IsCompleted)
            {
                if ((elapsed += Time.deltaTime) > timeout) { Debug.LogWarning("Wait() timed out"); yield break; }
                yield return null;
            }
        }

        // ── Entry ─────────────────────────────────────────────────────────────────
        void Start()
        {
            RunSync();
            StartCoroutine(RunAsync());
        }

        void RunSync()
        {
            // Group A — new trigger overloads (sync parts)
            A5_EnterState_TriggerStateFiltered_OnlyFires();
            A6_EnterState_TriggerStateFiltered_ExcludesOtherTriggers();
            A7_EnterState_TriggerStateFiltered_ExcludesOtherStates();

            // Group D — Appendix B safety
            D1_TriggerUnregistered_NoException();
            D2_TriggerAfterDispose_NoException();
            D3_TransitionToSameState_NoCallbacks();
            D4_CallbackThrows_SequenceContinues();
            D5_MultipleCallbacks_OneThrows_OthersFire();

            // Group G — sync regression
            G1_AllFourEnterStateOverloads();
        }

        IEnumerator RunAsync()
        {
            // Group A — new async trigger overloads
            yield return StartCoroutine(A1_UnfilteredAsyncWithTrg_Fires());
            yield return StartCoroutine(A2_UnfilteredAsyncWithTrg_ValuePassed());
            yield return StartCoroutine(A3_StateTriggerFilteredAsync_FiltersByTriggerType());
            yield return StartCoroutine(A4_StateTriggerFilteredAsync_ValuePassed());

            // Group B — TransitionOperation.Drop
            yield return StartCoroutine(B1_Drop_DropsWhileActive());
            yield return StartCoroutine(B2_Drop_FiresAfterPreviousComplete());

            // Group C — handle dispose edge cases
            yield return StartCoroutine(C1_ThrottleAsyncHandleDispose_ReleasesBlock());
            yield return StartCoroutine(C2_AsyncHandleDispose_CTCancelledImmediately());
            yield return StartCoroutine(C3_SwitchHandleDispose_NoMoreCancellation());

            // Group D — async edge cases
            yield return StartCoroutine(D6_TickState_WhileDeactivated_Suppressed());

            // Group E — interrupt edge cases
            yield return StartCoroutine(E1_Interrupt_DuringInterrupt_PreviousCancelled());
            yield return StartCoroutine(E2_Interrupt_DuringTransition_Cancelled());

            // Group F — complex mixed
            yield return StartCoroutine(F1_AllFourAsync_OnSameState());
            yield return StartCoroutine(F2_EnterStateTrigger_And_AsyncTrigger_SameState());
            yield return StartCoroutine(F3_ThrottleState_AsyncThrottle_Switch_AllOnSameState());
            yield return StartCoroutine(F4_NestedFSM_ParentDispose_ChildrenDisposed());
            yield return StartCoroutine(F5_FullStack_FilterInterruptAsyncNested());

            // Group G — async regression
            yield return StartCoroutine(G2_Phase345KeyBehaviors());

            PrintFinal();
        }

        void PrintFinal()
        {
            int total = _pass + _fail;
            if (_fail == 0) Debug.Log($"=== Phase 6: {_pass}/{total} passed ===");
            else            Debug.LogError($"=== Phase 6: {_pass}/{total} passed, {_fail} FAILED ===");
        }

        // ═════════════════════════════════════════════════════════════════════════
        // GROUP A — New Trigger Overloads
        // ═════════════════════════════════════════════════════════════════════════

        /// T6.A1 — EnterStateAsync(Func<cur,prev,trg,ct>) fires on every entry
        IEnumerator A1_UnfilteredAsyncWithTrg_Fires()
        {
            int fireCount = 0;
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .AddTransition<MoveStopped>(S.Walk, S.Idle)
                .Build();

            sm.EnterStateAsync(async (cur, prev, trg, ct) =>
            {
                fireCount++;
                await Task.CompletedTask;
            }, TransitionOperation.Parallel);

            sm.Trigger(new MoveStarted()); // Idle→Walk
            sm.Trigger(new MoveStopped()); // Walk→Idle

            yield return null;
            Assert(fireCount == 2, "T6.A1 — unfiltered (cur,prev,trg,ct) fires on every entry");
            sm.Dispose();
        }

        /// T6.A2 — trigger value correctly passed through (cur, prev, trg, ct)
        IEnumerator A2_UnfilteredAsyncWithTrg_ValuePassed()
        {
            S    gotCur  = default;
            S    gotPrev = default;
            float gotAmt  = -1f;

            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<Damaged>(S.Idle, S.Hit)
                .Build();

            sm.EnterStateAsync(async (cur, prev, trg, ct) =>
            {
                gotCur  = cur;
                gotPrev = prev;
                gotAmt  = trg is Damaged d ? d.amount : -1f;
                await Task.CompletedTask;
            }, TransitionOperation.Parallel);

            sm.Trigger(new Damaged(42f));
            yield return null;

            Assert(gotCur  == S.Hit,  "T6.A2a — cur correct in (cur,prev,trg,ct)");
            Assert(gotPrev == S.Idle, "T6.A2b — prev correct in (cur,prev,trg,ct)");
            Assert(Math.Abs(gotAmt - 42f) < 0.001f, "T6.A2c — trigger value 42 correctly passed");
            sm.Dispose();
        }

        /// T6.A3 — EnterStateAsync<TTrigger>(state, …) fires only for matching trigger type
        IEnumerator A3_StateTriggerFilteredAsync_FiltersByTriggerType()
        {
            int damagedCount = 0;
            int healedCount  = 0;

            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<Damaged>(S.Idle, S.Hit)
                .AddTransition<Healed> (S.Hit,  S.Idle)
                .Build();

            // Only fires when entering Hit via Damaged
            sm.EnterStateAsync<Damaged>(S.Hit, async (prev, trg, ct) =>
            {
                damagedCount++;
                await Task.CompletedTask;
            }, TransitionOperation.Parallel);

            // Only fires when entering Idle via Healed
            sm.EnterStateAsync<Healed>(S.Idle, async (prev, trg, ct) =>
            {
                healedCount++;
                await Task.CompletedTask;
            }, TransitionOperation.Parallel);

            sm.Trigger(new Damaged(10f)); // → Hit (Damaged fires, Healed does not)
            sm.Trigger(new Healed(50f));  // → Idle (Healed fires, Damaged does not)
            sm.Trigger(new Damaged(10f)); // → Hit again

            yield return null;
            Assert(damagedCount == 2, "T6.A3a — <Damaged>(Hit) fires only for Damaged trigger");
            Assert(healedCount  == 1, "T6.A3b — <Healed>(Idle) fires only for Healed trigger");
            sm.Dispose();
        }

        /// T6.A4 — trigger struct fields accessible in state+trigger filtered async
        IEnumerator A4_StateTriggerFilteredAsync_ValuePassed()
        {
            int   gotLevel = -1;
            S     gotPrev  = default;

            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<CastSpell>(S.Idle, S.Cast)
                .Build();

            sm.EnterStateAsync<CastSpell>(S.Cast, async (prev, trg, ct) =>
            {
                gotPrev  = (S)prev;
                gotLevel = trg is CastSpell cs ? cs.level : -1;
                await Task.CompletedTask;
            }, TransitionOperation.Parallel);

            sm.Trigger(new CastSpell(3));
            yield return null;

            Assert(gotPrev  == S.Idle, "T6.A4a — prev correct in state+trigger async");
            Assert(gotLevel == 3,      "T6.A4b — CastSpell.level=3 correctly passed");
            sm.Dispose();
        }

        /// T6.A5 — EnterState<TTrigger>(state, callback) fires for matching state+trigger
        void A5_EnterState_TriggerStateFiltered_OnlyFires()
        {
            int fireCount = 0;
            float gotAmt = -1f;
            S gotPrev = default;

            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<Damaged>(S.Idle, S.Hit)
                .Build();

            sm.EnterState<Damaged>(S.Hit, (prev, trg) =>
            {
                fireCount++;
                gotPrev = (S)prev;
                gotAmt  = trg is Damaged d ? d.amount : -1f;
            });

            sm.Trigger(new Damaged(7f));
            Assert(fireCount == 1,          "T6.A5a — EnterState<Damaged>(Hit) fired");
            Assert(gotPrev  == S.Idle,      "T6.A5b — prev == Idle");
            Assert(Math.Abs(gotAmt - 7f) < 0.001f, "T6.A5c — Damaged.amount=7 passed");
            sm.Dispose();
        }

        /// T6.A6 — EnterState<TTrigger>(state) does NOT fire for other trigger types on same state
        void A6_EnterState_TriggerStateFiltered_ExcludesOtherTriggers()
        {
            int damagedFired = 0;
            int healedFired  = 0;

            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<Damaged>(S.Idle, S.Hit)
                .AddTransition<Healed> (S.Idle, S.Hit) // same destination, different trigger
                .Build();

            sm.EnterState<Damaged>(S.Hit, (prev, trg) => damagedFired++);
            sm.EnterState<Healed> (S.Hit, (prev, trg) => healedFired++);

            sm.Trigger(new Damaged(10f)); // → Hit via Damaged
            Assert(damagedFired == 1 && healedFired == 0,
                "T6.A6a — only Damaged callback fires when trigger is Damaged");

            sm.ForceTransitionTo(S.Idle);

            sm.Trigger(new Healed(10f));  // → Hit via Healed
            Assert(damagedFired == 1 && healedFired == 1,
                "T6.A6b — only Healed callback fires when trigger is Healed");
            sm.Dispose();
        }

        /// T6.A7 — EnterState<TTrigger>(state) does NOT fire when entering other states
        void A7_EnterState_TriggerStateFiltered_ExcludesOtherStates()
        {
            int hitDamagedCount  = 0;
            int walkDamagedCount = 0;

            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .AddTransition<Damaged>    (S.Idle, S.Hit)
                .AddTransition<Damaged>    (S.Walk, S.Hit)
                .Build();

            sm.EnterState<Damaged>(S.Hit,  (prev, trg) => hitDamagedCount++);
            sm.EnterState<Damaged>(S.Walk, (prev, trg) => walkDamagedCount++);

            sm.Trigger(new MoveStarted());  // → Walk (no Damaged callback)
            Assert(hitDamagedCount == 0 && walkDamagedCount == 0,
                "T6.A7a — Damaged state callbacks don't fire on MoveStarted→Walk");

            sm.ForceTransitionTo(S.Idle);
            sm.Trigger(new Damaged(5f));    // → Hit (only Hit callback)
            Assert(hitDamagedCount == 1 && walkDamagedCount == 0,
                "T6.A7b — only Hit callback fires when entering Hit via Damaged");
            sm.Dispose();
        }

        // ═════════════════════════════════════════════════════════════════════════
        // GROUP B — TransitionOperation.Drop
        // ═════════════════════════════════════════════════════════════════════════

        /// T6.B1 — Drop discards new entries while task is active
        IEnumerator B1_Drop_DropsWhileActive()
        {
            int startCount = 0;
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .AddTransition<MoveStopped>(S.Walk, S.Idle)
                .Build();

            sm.EnterStateAsync(S.Walk, async (prev, ct) =>
            {
                startCount++;
                await Task.Delay(400, ct);
            }, TransitionOperation.Drop);

            sm.TransitionTo(S.Walk);
            sm.TransitionTo(S.Idle);
            sm.TransitionTo(S.Walk); // should be dropped (first still running)
            sm.TransitionTo(S.Idle);
            sm.TransitionTo(S.Walk); // also dropped

            Assert(startCount == 1, "T6.B1a — Drop: only first call started");

            yield return new WaitForSeconds(0.6f);
            Assert(startCount == 1, "T6.B1b — Drop: still only one start after task completes");
            sm.Dispose();
        }

        /// T6.B2 — Drop fires again once the active task completes
        IEnumerator B2_Drop_FiresAfterPreviousComplete()
        {
            int startCount = 0;
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .Build();

            sm.EnterStateAsync(S.Walk, async (prev, ct) =>
            {
                startCount++;
                await Task.Delay(200, ct);
            }, TransitionOperation.Drop);

            sm.TransitionTo(S.Walk);
            Assert(startCount == 1, "T6.B2a — first entry starts");

            yield return new WaitForSeconds(0.4f); // first completes

            sm.ForceTransitionTo(S.Idle);
            sm.TransitionTo(S.Walk); // second entry — previous done, should fire
            yield return null;
            Assert(startCount == 2, "T6.B2b — Drop fires again after previous task completes");
            sm.Dispose();
        }

        // ═════════════════════════════════════════════════════════════════════════
        // GROUP C — Handle Dispose Edge Cases
        // ═════════════════════════════════════════════════════════════════════════

        /// T6.C1 — Disposing a Throttle handle while blocking releases the pending transition
        IEnumerator C1_ThrottleAsyncHandleDispose_ReleasesBlock()
        {
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .AddTransition<MoveStopped>(S.Walk, S.Idle)
                .Build();

            var handle = sm.EnterStateAsync(S.Walk, async (prev, ct) =>
            {
                await Task.Delay(10000, ct); // very long
            }, TransitionOperation.Throttle);

            sm.TransitionTo(S.Walk);
            sm.Trigger(new MoveStopped()); // queued — blocked by Throttle
            Assert(sm.State == S.Walk, "T6.C1a — Throttle blocking");

            handle.Dispose(); // cancel CT + remove from throttle count
            yield return null;
            Assert(sm.State == S.Idle, "T6.C1b — pending transition executes after handle dispose");
            sm.Dispose();
        }

        /// T6.C2 — Disposing async handle cancels the CT immediately
        IEnumerator C2_AsyncHandleDispose_CTCancelledImmediately()
        {
            bool wasCancelled = false;
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .Build();

            var handle = sm.EnterStateAsync(S.Walk, async (prev, ct) =>
            {
                try   { await Task.Delay(10000, ct); }
                catch (OperationCanceledException) { wasCancelled = true; }
            }, TransitionOperation.Switch);

            sm.TransitionTo(S.Walk);
            handle.Dispose();

            yield return new WaitForSeconds(0.1f);
            Assert(wasCancelled, "T6.C2 — handle dispose cancels CT immediately");
            sm.Dispose();
        }

        /// T6.C3 — Disposing a Switch handle: callback no longer registered
        IEnumerator C3_SwitchHandleDispose_NoMoreCancellation()
        {
            int fireCount = 0;
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .AddTransition<MoveStopped>(S.Walk, S.Idle)
                .Build();

            var handle = sm.EnterStateAsync(S.Walk, async (prev, ct) =>
            {
                fireCount++;
                await Task.CompletedTask;
            }, TransitionOperation.Switch);

            sm.TransitionTo(S.Walk); // fires once
            yield return null;
            Assert(fireCount == 1, "T6.C3a — Switch fired on first entry");

            handle.Dispose();
            sm.TransitionTo(S.Idle);
            sm.TransitionTo(S.Walk); // should NOT fire (handle disposed)
            yield return null;
            Assert(fireCount == 1, "T6.C3b — after handle dispose, no more fires");
            sm.Dispose();
        }

        // ═════════════════════════════════════════════════════════════════════════
        // GROUP D — Appendix B Safety / Edge Cases
        // ═════════════════════════════════════════════════════════════════════════

        /// Trigger(null) → no-op, no exception (Appendix B)
        void D1_TriggerUnregistered_NoException()
        {
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .Build();

            // Sprint has no registered transition from Idle — must be a silent no-op
            bool threw = false;
            try { sm.Trigger(new Sprint()); }
            catch { threw = true; }

            Assert(!threw,             "T6.D1a — Trigger with no matching rule does not throw");
            Assert(sm.State == S.Idle, "T6.D1b — state unchanged after unmatched trigger");
            sm.Dispose();
        }

        /// Trigger after Dispose → no-op, no exception (Appendix B)
        void D2_TriggerAfterDispose_NoException()
        {
            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .Build();
            sm.Dispose();

            bool threw = false;
            try { sm.Trigger(new MoveStarted()); }
            catch { threw = true; }

            Assert(!threw, "T6.D2 — Trigger after Dispose does not throw");
        }

        /// TransitionTo same state → no callbacks fire (Appendix B)
        void D3_TransitionToSameState_NoCallbacks()
        {
            int enterCount = 0;
            int exitCount  = 0;

            var sm = FSM.Create<S>(S.Idle).Build();
            sm.EnterState((cur, prev) => enterCount++);
            sm.ExitState ((cur, next) => exitCount++);

            sm.TransitionTo(S.Idle); // same state
            Assert(enterCount == 0, "T6.D3a — EnterState not called on same-state transition");
            Assert(exitCount  == 0, "T6.D3b — ExitState not called on same-state transition");
            sm.Dispose();
        }

        /// Callback throws → remaining callbacks still fire, sequence continues (Appendix B)
        void D4_CallbackThrows_SequenceContinues()
        {
            bool secondFired = false;
            bool exitFired   = false;
            S    stateAfter  = S.Dead;

            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .Build();

            sm.EnterState((cur, prev) => throw new InvalidOperationException("intentional"));
            sm.EnterState((cur, prev) => secondFired = true);
            sm.ExitState ((cur, next) => exitFired   = true);

            sm.Trigger(new MoveStarted());
            stateAfter = sm.State;

            Assert(secondFired, "T6.D4a — second EnterState fires even after first throws");
            Assert(exitFired,   "T6.D4b — ExitState fires even after EnterState throws");
            Assert(stateAfter == S.Walk, "T6.D4c — state transition completes despite throw");
            sm.Dispose();
        }

        /// Multiple EnterState callbacks, one throws → others still fire (Appendix B)
        void D5_MultipleCallbacks_OneThrows_OthersFire()
        {
            var log = new List<int>();

            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .Build();

            sm.EnterState((cur, prev) => log.Add(1));
            sm.EnterState((cur, prev) => { log.Add(2); throw new Exception("boom"); });
            sm.EnterState((cur, prev) => log.Add(3));
            sm.EnterState((cur, prev) => log.Add(4));

            sm.Trigger(new MoveStarted());

            Assert(log.Count == 4 && log[0] == 1 && log[1] == 2 && log[2] == 3 && log[3] == 4,
                "T6.D5 — all 4 EnterState callbacks invoked even when #2 throws");
            sm.Dispose();
        }

        /// TickState while deactivated → suppressed (Appendix B)
        IEnumerator D6_TickState_WhileDeactivated_Suppressed()
        {
            int ticks = 0;
            // Start in Walk so we can transition into Idle to activate TickState.
            // TickState.isActive is only true after an EnterState event fires.
            var sm = FSM.Create<S>(S.Walk)
                .AddTransition<MoveStopped>(S.Walk, S.Idle)
                .Build();
            sm.TickState(S.Idle, (prev, trg) => ticks++);

            sm.Trigger(new MoveStopped()); // enter Idle → isActive = true
            yield return null;             // skipNext frame consumed

            var handle = sm.Deactivate();
            int ticksBefore = ticks;
            yield return null; yield return null; yield return null;
            Assert(ticks == ticksBefore, "T6.D6a — TickState suppressed while deactivated");

            handle.Dispose();
            yield return null;
            Assert(ticks > ticksBefore, "T6.D6b — TickState resumes after deactivate released");
            sm.Dispose();
        }

        // ═════════════════════════════════════════════════════════════════════════
        // GROUP E — Interrupt Edge Cases
        // ═════════════════════════════════════════════════════════════════════════

        /// Interrupt during Interrupt → previous CT cancelled, new one starts (Appendix B)
        IEnumerator E1_Interrupt_DuringInterrupt_PreviousCancelled()
        {
            bool firstCancelled = false;
            bool secondRan      = false;

            var sm = FSM.Create<S>(S.Idle).Build();
            sm.TransitionTo(S.Walk);

            sm.Interrupt(new LambdaInterrupt(async (state, ct) =>
            {
                try   { await Task.Delay(5000, ct); }
                catch (OperationCanceledException) { firstCancelled = true; }
            }));

            // Second interrupt cancels the first
            sm.Interrupt(new LambdaInterrupt(async (state, ct) =>
            {
                await Task.Delay(50, ct);
                secondRan = true;
            }));

            yield return new WaitForSeconds(0.3f);
            Assert(firstCancelled, "T6.E1a — first Interrupt CT cancelled by second");
            Assert(secondRan,      "T6.E1b — second Interrupt runs to completion");
            sm.Dispose();
        }

        /// Interrupt cancelled when state transitions (Phase 4 regression + spec Appendix B)
        IEnumerator E2_Interrupt_DuringTransition_Cancelled()
        {
            bool interruptCancelled = false;

            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .Build();

            sm.Interrupt(new LambdaInterrupt(async (state, ct) =>
            {
                try   { await Task.Delay(5000, ct); }
                catch (OperationCanceledException) { interruptCancelled = true; }
            }));

            sm.Trigger(new MoveStarted()); // transition → cancels interrupt

            yield return new WaitForSeconds(0.1f);
            Assert(interruptCancelled, "T6.E2 — Interrupt CT cancelled by state transition");
            sm.Dispose();
        }

        // ═════════════════════════════════════════════════════════════════════════
        // GROUP F — Complex Mixed Scenarios
        // ═════════════════════════════════════════════════════════════════════════

        /// T6.F1 — All four EnterStateAsync overloads active on the same state simultaneously
        IEnumerator F1_AllFourAsync_OnSameState()
        {
            var fired = new bool[4];

            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<Damaged>(S.Idle, S.Hit)
                .Build();

            // Overload 1: (cur, prev, ct)
            sm.EnterStateAsync(async (cur, prev, ct) =>
            {
                fired[0] = true; await Task.CompletedTask;
            }, TransitionOperation.Parallel);

            // Overload 2: (cur, prev, trg, ct)  ← NEW
            sm.EnterStateAsync(async (cur, prev, trg, ct) =>
            {
                fired[1] = true; await Task.CompletedTask;
            }, TransitionOperation.Parallel);

            // Overload 3: state-filtered (prev, ct)
            sm.EnterStateAsync(S.Hit, async (prev, ct) =>
            {
                fired[2] = true; await Task.CompletedTask;
            }, TransitionOperation.Parallel);

            // Overload 4: state+trigger filtered (prev, trg, ct)  ← NEW
            sm.EnterStateAsync<Damaged>(S.Hit, async (prev, trg, ct) =>
            {
                fired[3] = true; await Task.CompletedTask;
            }, TransitionOperation.Parallel);

            sm.Trigger(new Damaged(10f));
            yield return null;

            Assert(fired[0], "T6.F1a — overload1 (cur,prev,ct) fired");
            Assert(fired[1], "T6.F1b — overload2 (cur,prev,trg,ct) fired");
            Assert(fired[2], "T6.F1c — overload3 state-filtered fired");
            Assert(fired[3], "T6.F1d — overload4 state+trigger-filtered fired");
            sm.Dispose();
        }

        /// T6.F2 — EnterState<TTrigger>(state) + EnterStateAsync<TTrigger>(state) on same state
        /// Only the matching trigger type should fire each
        IEnumerator F2_EnterStateTrigger_And_AsyncTrigger_SameState()
        {
            int syncDamaged  = 0;
            int asyncDamaged = 0;
            int syncHealed   = 0;
            int asyncHealed  = 0;

            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<Damaged>(S.Idle, S.Hit)
                .AddTransition<Healed> (S.Hit,  S.Idle)
                .Build();

            sm.EnterState<Damaged>(S.Hit,  (prev, trg) => syncDamaged++);
            sm.EnterState<Healed> (S.Idle, (prev, trg) => syncHealed++);

            sm.EnterStateAsync<Damaged>(S.Hit, async (prev, trg, ct) =>
            {
                asyncDamaged++; await Task.CompletedTask;
            }, TransitionOperation.Parallel);

            sm.EnterStateAsync<Healed>(S.Idle, async (prev, trg, ct) =>
            {
                asyncHealed++; await Task.CompletedTask;
            }, TransitionOperation.Parallel);

            sm.Trigger(new Damaged(10f)); // → Hit
            sm.Trigger(new Healed(50f));  // → Idle
            sm.Trigger(new Damaged(10f)); // → Hit again
            yield return null;

            Assert(syncDamaged  == 2, "T6.F2a — sync Damaged(Hit) fires twice");
            Assert(asyncDamaged == 2, "T6.F2b — async Damaged(Hit) fires twice");
            Assert(syncHealed   == 1, "T6.F2c — sync Healed(Idle) fires once");
            Assert(asyncHealed  == 1, "T6.F2d — async Healed(Idle) fires once");
            sm.Dispose();
        }

        /// T6.F3 — ThrottleState + TransitionOperation.Throttle + TransitionOperation.Switch all on Walk
        /// All three must release before pending transition executes;
        /// Switch is cancelled when transition finally occurs.
        IEnumerator F3_ThrottleState_AsyncThrottle_Switch_AllOnSameState()
        {
            bool switchCancelled = false;
            bool asyncThrottleDone = false;

            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<MoveStarted>(S.Idle, S.Walk)
                .AddTransition<MoveStopped>(S.Walk, S.Idle)
                .ThrottleState(S.Walk, 0.2f) // gate 1: time-based
                .Build();

            // gate 2: async throttle (longer than ThrottleState)
            sm.EnterStateAsync(S.Walk, async (prev, ct) =>
            {
                await Task.Delay(500, ct);
                asyncThrottleDone = true;
            }, TransitionOperation.Throttle);

            // gate 3: switch (should be cancelled when transition executes)
            sm.EnterStateAsync(S.Walk, async (prev, ct) =>
            {
                try   { await Task.Delay(10000, ct); }
                catch (OperationCanceledException) { switchCancelled = true; }
            }, TransitionOperation.Switch);

            sm.Trigger(new MoveStarted());
            sm.Trigger(new MoveStopped()); // blocked

            yield return new WaitForSeconds(0.35f);
            Assert(sm.State == S.Walk, "T6.F3a — ThrottleState done but AsyncThrottle still blocking");

            yield return new WaitForSeconds(0.3f);
            Assert(asyncThrottleDone, "T6.F3b — AsyncThrottle task completed");
            Assert(sm.State == S.Idle, "T6.F3c — pending transition executed after all gates released");

            yield return new WaitForSeconds(0.1f);
            Assert(switchCancelled, "T6.F3d — Switch cancelled when transition executed");
            sm.Dispose();
        }

        /// T6.F4 — Nested FSM: parent Dispose recursively disposes children (Appendix B)
        IEnumerator F4_NestedFSM_ParentDispose_ChildrenDisposed()
        {
            int childEnterCount = 0;

            var child = FSM.Create<F4ChildS>(F4ChildS.X)
                .AddTransition<F4GoY>(F4ChildS.X, F4ChildS.Y)
                .Build();
            child.EnterState((cur, prev) => childEnterCount++);

            var parent = FSM.Create<F4ParentS>(F4ParentS.A)
                .AddTransition<F4GoB>(F4ParentS.A, F4ParentS.B)
                .Register(F4ParentS.A, child)
                .Build();

            parent.Trigger(new F4GoY()); // propagates to child
            Assert(child.State == F4ChildS.Y, "T6.F4a — trigger propagated to child");
            Assert(childEnterCount == 1,      "T6.F4b — child EnterState fired");

            parent.Dispose(); // should recursively dispose child

            bool childThrew = false;
            try { child.Trigger(new F4GoY()); }
            catch { childThrew = true; }

            Assert(!childThrew,               "T6.F4c — child.Trigger after parent.Dispose: no exception");
            Assert(child.State == F4ChildS.Y, "T6.F4d — child state unchanged (no-op after dispose)");
            int countBefore = childEnterCount;
            Assert(childEnterCount == countBefore, "T6.F4e — child EnterState not fired after dispose");

            yield return null; // just to make this an IEnumerator for coroutine scheduling
        }

        /// T6.F5 — Full-stack: TransitionFilter (async) + Interrupt + EnterStateAsync<TTrigger>
        /// combined in one scenario
        IEnumerator F5_FullStack_FilterInterruptAsyncNested()
        {
            bool filterRan      = false;
            bool interruptRan   = false;
            bool asyncTrgFired  = false;
            float gotPower      = -1f;

            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<AttackInput>(S.Idle, S.Attack)
                .AddTransition<AttackEnd>  (S.Attack, S.Idle)
                .UseGlobalFilter(new PredicateFilter(async (trigger, ct) =>
                {
                    filterRan = true;
                    await Task.Delay(10, ct); // tiny async gap
                    return true;              // allow
                }))
                .Build();

            sm.EnterStateAsync<AttackInput>(S.Attack, async (prev, trg, ct) =>
            {
                gotPower   = trg is AttackInput a ? a.power : -1f;
                asyncTrgFired = true;
                await Task.Delay(50, ct);
            }, TransitionOperation.Switch);

            sm.Interrupt(new LambdaInterrupt(async (state, ct) =>
            {
                await Task.Delay(100, ct);
                interruptRan = true;
            }));

            sm.Trigger(new AttackInput(99f));

            yield return new WaitForSeconds(0.4f);

            Assert(filterRan,     "T6.F5a — TransitionFilter ran");
            Assert(asyncTrgFired, "T6.F5b — EnterStateAsync<AttackInput> fired");
            Assert(Math.Abs(gotPower - 99f) < 0.001f, "T6.F5c — AttackInput.power=99 received");
            // Interrupt cancelled by the transition to Attack
            Assert(!interruptRan, "T6.F5d — Interrupt cancelled by state transition before completion");
            sm.Dispose();
        }

        // ═════════════════════════════════════════════════════════════════════════
        // GROUP G — Regression
        // ═════════════════════════════════════════════════════════════════════════

        /// T6.G1 — All four EnterState overloads fire correctly (sync regression)
        void G1_AllFourEnterStateOverloads()
        {
            S   gotCur = default, gotPrev = default;
            float gotAmt = -1f;
            bool stateFiltered   = false;
            bool trigFiltered    = false;
            bool stateTrigFilter = false;

            var sm = FSM.Create<S>(S.Idle)
                .AddTransition<Damaged>(S.Idle, S.Hit)
                .Build();

            sm.EnterState((cur, prev) => { gotCur = cur; gotPrev = prev; });
            sm.EnterState((cur, prev, trg) => gotAmt = trg is Damaged d ? d.amount : -1f);
            sm.EnterState(S.Hit, (prev, trg) => stateFiltered = true);
            sm.EnterState<Damaged>(S.Hit, (prev, trg) => stateTrigFilter = true);
            sm.EnterState<Damaged>((cur, prev, trg) => trigFiltered = true);

            sm.Trigger(new Damaged(55f));

            Assert(gotCur == S.Hit && gotPrev == S.Idle, "T6.G1a — (cur,prev) correct");
            Assert(Math.Abs(gotAmt - 55f) < 0.001f,     "T6.G1b — (cur,prev,trg) value correct");
            Assert(stateFiltered,                        "T6.G1c — state-filtered EnterState fired");
            Assert(trigFiltered,                         "T6.G1d — trigger-filtered EnterState fired");
            Assert(stateTrigFilter,                      "T6.G1e — state+trigger filtered EnterState fired");
            sm.Dispose();
        }

        /// T6.G2 — Key Phase 3-5 behaviors still intact
        IEnumerator G2_Phase345KeyBehaviors()
        {
            // Phase 3: ThrottleState + ForceTransition bypass
            {
                var sm = FSM.Create<S>(S.Idle)
                    .AddTransition<MoveStarted>(S.Idle, S.Walk)
                    .ThrottleState(S.Walk, 10f)
                    .Build();
                sm.Trigger(new MoveStarted());
                sm.ForceTransitionTo(S.Dead);
                Assert(sm.State == S.Dead, "T6.G2/T3.4 — ForceTransition bypasses ThrottleState");
                sm.Dispose();
            }

            // Phase 3: HoldState
            {
                bool ready = false;
                var sm = FSM.Create<S>(S.Idle)
                    .AddTransition<MoveStarted>(S.Idle, S.Walk)
                    .AddTransition<MoveStopped>(S.Walk, S.Idle)
                    .HoldState(S.Walk, () => ready)
                    .Build();
                sm.Trigger(new MoveStarted());
                sm.Trigger(new MoveStopped());
                Assert(sm.State == S.Walk, "T6.G2/T3.6a — HoldState blocking");
                ready = true;
                yield return null;
                Assert(sm.State == S.Idle, "T6.G2/T3.6b — HoldState released");
                sm.Dispose();
            }

            // Phase 4: TransitionFilter blocks
            {
                bool allowed = false;
                var sm = FSM.Create<S>(S.Idle)
                    .AddTransition<MoveStarted>(S.Idle, S.Walk)
                    .UseGlobalFilter(new PredicateFilter(async (trg, ct) =>
                    {
                        await Task.CompletedTask;
                        return allowed;
                    }))
                    .Build();

                sm.Trigger(new MoveStarted());
                yield return null;
                Assert(sm.State == S.Idle, "T6.G2/T4.14a — filter blocks when allowed=false");

                allowed = true;
                sm.Trigger(new MoveStarted());
                yield return new WaitForSeconds(0.1f);
                Assert(sm.State == S.Walk, "T6.G2/T4.14b — filter allows when allowed=true");
                sm.Dispose();
            }

            // Phase 5: IFSM<TState> interface
            {
                var sm = FSM.Create<S>(S.Idle)
                    .AddTransition<MoveStarted>(S.Idle, S.Walk)
                    .Build();
                IFSM<S> ifsm = sm;
                bool entered = false;
                ifsm.EnterState((cur, prev) => entered = true);
                ifsm.Trigger(new MoveStarted());
                Assert(entered, "T6.G2/T5 — IFSM<TState> functional");
                ifsm.Dispose();
            }
        }
    }
}
