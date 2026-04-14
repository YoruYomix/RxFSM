using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RxFSM;

/// <summary>
/// Phase 2 tester. Attach to any GameObject and press Play.
/// Sync tests run in Start; frame-dependent tests use coroutines.
/// </summary>
public class Phase2Tester : MonoBehaviour
{
    // ── Shared enums & triggers (reused from Phase 1) ────────────────────────

    public enum TestState { Idle, Walk, Run, Hit, Dead }

    public readonly struct MoveStarted { }
    public readonly struct MoveStopped { }
    public readonly struct Sprint      { public readonly float speed; public Sprint(float s) { speed = s; } }
    public readonly struct Damaged     { public readonly float amount; public Damaged(float a) { amount = a; } }
    public readonly struct Healed      { }
    public readonly struct Killed      { }

    public readonly struct AttackInput { }
    public readonly struct AttackEnd   { }
    public readonly struct Recovered   { }

    // ── Result tracking ──────────────────────────────────────────────────────

    private int _passed, _failed;
    private readonly List<string> _failures = new List<string>();

    void Pass(string name) { _passed++; Debug.Log($"[PASS] {name}"); }

    void Fail(string name, string reason)
    {
        _failed++;
        var msg = $"[FAIL] {name}: {reason}";
        _failures.Add(msg);
        Debug.LogError(msg);
    }

    void Assert(bool cond, string name, string reason = "assertion failed")
    {
        if (cond) Pass(name); else Fail(name, reason);
    }

    // ── Entry point ──────────────────────────────────────────────────────────

    void Start()
    {
        _passed = 0; _failed = 0; _failures.Clear();

        // ── Phase 1 regression (T2.18) ───────────────────────────────────────
        RunPhase1Regression();

        // ── Sync Phase 2 tests ────────────────────────────────────────────────
        RunT2_1(); RunT2_2(); RunT2_3(); RunT2_4();
        RunT2_5(); RunT2_6(); RunT2_7(); RunT2_8();
        RunT2_9(); RunT2_10();

        // ── Async/coroutine Phase 2 tests ─────────────────────────────────────
        StartCoroutine(RunAsyncTests());
    }

    IEnumerator RunAsyncTests()
    {
        yield return StartCoroutine(RunT2_11());
        yield return StartCoroutine(RunT2_12());
        yield return StartCoroutine(RunT2_13());
        yield return StartCoroutine(RunT2_14());
        yield return StartCoroutine(RunT2_15());
        yield return StartCoroutine(RunT2_16());
        yield return StartCoroutine(RunT2_17());

        PrintFinal();
    }

    void PrintFinal()
    {
        int total = _passed + _failed;
        if (_failed == 0)
            Debug.Log($"=== Phase 2: {_passed}/{total} passed ===");
        else
        {
            foreach (var f in _failures) Debug.LogError(f);
            Debug.LogError($"=== Phase 2: {_passed}/{total} passed, {_failed} FAILED ===");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    FSM<TestState> BuildBasic()
        => FSM.Create<TestState>(TestState.Idle)
            .AddTransition<MoveStarted>(TestState.Idle, TestState.Walk)
            .AddTransition<MoveStopped>(TestState.Walk, TestState.Idle)
            .AddTransition<Damaged>(TestState.Idle, TestState.Hit)
            .AddTransition<Damaged>(TestState.Walk, TestState.Hit)
            .AddTransition<Healed>(TestState.Hit, TestState.Idle)
            .AddTransitionFromAny<Killed>(TestState.Dead)
            .Build();

    // ── Phase 2 sync tests ───────────────────────────────────────────────────

    void RunT2_1()
    {
        const string name = "T2.1 — EnterState (cur, prev)";
        var sm = BuildBasic();
        TestState logCur = default, logPrev = default;
        bool called = false;

        sm.EnterState((cur, prev) => { logCur = cur; logPrev = prev; called = true; });
        sm.Trigger(new MoveStarted());

        if (!called)        { Fail(name, "callback not called"); return; }
        if (logCur  != TestState.Walk) { Fail(name, $"cur={logCur}"); return; }
        Assert(logPrev == TestState.Idle, name, $"prev={logPrev}");
    }

    void RunT2_2()
    {
        const string name = "T2.2 — EnterState (cur, prev, trg)";
        var sm = BuildBasic();
        TestState logCur = default, logPrev = default;
        object logTrg = null;

        sm.EnterState((cur, prev, trg) => { logCur = cur; logPrev = prev; logTrg = trg; });
        sm.Trigger(new Damaged(50f));

        if (logCur != TestState.Hit) { Fail(name, $"cur={logCur}"); return; }
        if (logPrev != TestState.Idle) { Fail(name, $"prev={logPrev}"); return; }
        Assert(logTrg is Damaged d && d.amount == 50f, name, $"trg={logTrg}");
    }

    void RunT2_3()
    {
        const string name = "T2.3 — EnterState state-filtered";
        var sm = BuildBasic();
        int callCount = 0;
        TestState logPrev = default;

        sm.EnterState(TestState.Hit, (prev, trg) => { callCount++; logPrev = prev; });

        sm.Trigger(new MoveStarted()); // Idle→Walk — should NOT fire
        if (callCount != 0) { Fail(name, "fired on Walk entry"); return; }

        sm.TransitionTo(TestState.Idle);
        sm.Trigger(new Damaged(10f)); // Idle→Hit — should fire
        if (callCount != 1) { Fail(name, $"call count={callCount}"); return; }
        Assert(logPrev == TestState.Idle, name, $"prev={logPrev}");
    }

    void RunT2_4()
    {
        const string name = "T2.4 — EnterState trigger-type-filtered";
        var sm = BuildBasic();
        int callCount = 0;
        float capturedAmount = -1f;

        sm.EnterState<Damaged>((cur, prev, trg) =>
        {
            callCount++;
            capturedAmount = ((Damaged)trg).amount;
        });

        sm.Trigger(new MoveStarted()); // should NOT fire
        if (callCount != 0) { Fail(name, "fired on MoveStarted"); return; }

        sm.Trigger(new Damaged(10f)); // Walk→Hit via Damaged
        if (callCount != 1) { Fail(name, $"count={callCount}"); return; }
        Assert(capturedAmount == 10f, name, $"amount={capturedAmount}");
    }

    void RunT2_5()
    {
        const string name = "T2.5 — ExitState fires before state change";
        var sm = BuildBasic();
        TestState stateInsideCallback = (TestState)(-1);

        sm.ExitState((cur, next, trg) => { stateInsideCallback = sm.State; });
        sm.Trigger(new MoveStarted()); // Idle→Walk

        Assert(stateInsideCallback == TestState.Idle, name,
            $"sm.State inside ExitState was {stateInsideCallback}, expected Idle");
    }

    void RunT2_6()
    {
        const string name = "T2.6 — Execution order: Exit → state change → Enter";
        var sm = BuildBasic();
        var seq = new List<string>();

        sm.ExitState((cur, next, trg) => seq.Add("exit"));
        sm.EnterState((cur, prev, trg) => seq.Add("enter"));
        sm.Trigger(new MoveStarted());

        bool ok = seq.Count == 2 && seq[0] == "exit" && seq[1] == "enter";
        Assert(ok, name, $"sequence=[{string.Join(",", seq)}]");
    }

    void RunT2_7()
    {
        const string name = "T2.7 — Callback dispose";
        var sm = BuildBasic();
        int count = 0;

        var handle = sm.EnterState((cur, prev) => count++);
        sm.Trigger(new MoveStarted());
        if (count != 1) { Fail(name, $"expected 1 before dispose, got {count}"); return; }

        handle.Dispose();
        sm.TransitionTo(TestState.Idle);
        sm.Trigger(new MoveStarted());
        Assert(count == 1, name, $"expected 1 after dispose, got {count}");
    }

    void RunT2_8()
    {
        const string name = "T2.8 — Multiple callbacks on same event";
        var sm = BuildBasic();
        int count = 0;

        sm.EnterState((cur, prev) => count++);
        sm.EnterState((cur, prev) => count++);
        sm.Trigger(new MoveStarted());
        Assert(count == 2, name, $"expected 2, got {count}");
    }

    void RunT2_9()
    {
        const string name = "T2.9 — TransitionTo triggers callbacks with null trg";
        var sm = BuildBasic();
        bool trgWasNull = false;
        bool called = false;

        sm.EnterState((cur, prev, trg) => { trgWasNull = trg == null; called = true; });
        sm.TransitionTo(TestState.Walk);

        if (!called)    { Fail(name, "callback not called"); return; }
        Assert(trgWasNull, name, "trg was not null");
    }

    void RunT2_10()
    {
        const string name = "T2.10 — EnterState + reentrancy";
        var sm = FSM.Create<TestState>(TestState.Idle)
            .AddTransition<MoveStarted>(TestState.Idle, TestState.Walk)
            .AddTransition<Damaged>(TestState.Walk, TestState.Hit)
            .Build();

        sm.EnterState(TestState.Walk, (prev, trg) =>
        {
            sm.Trigger(new Damaged(10f)); // reentrancy → queued
        });

        sm.Trigger(new MoveStarted()); // Idle→Walk, then queued Damaged → Walk→Hit
        Assert(sm.State == TestState.Hit, name, $"expected Hit, got {sm.State}");
    }

    // ── Coroutine tests ──────────────────────────────────────────────────────

    IEnumerator RunT2_11()
    {
        const string name = "T2.11 — TickState basic";
        var sm = BuildBasic();
        int tickCount = 0;

        var handle = sm.TickState(TestState.Walk, (prev, trg) => tickCount++);
        sm.TransitionTo(TestState.Walk);

        yield return null; // wait 1 frame (skip-first-tick)
        yield return null; // wait another frame → tick should fire

        if (tickCount == 0) { Fail(name, "tickCount == 0 after 2 frames"); yield break; }

        int before = tickCount;
        yield return null;
        if (tickCount <= before) { Fail(name, "tickCount not increasing"); yield break; }

        sm.TransitionTo(TestState.Idle);
        int frozen = tickCount;
        yield return null;
        yield return null;
        Assert(tickCount == frozen, name, $"tick continued after exit: {tickCount} vs {frozen}");

        handle.Dispose();
        sm.Dispose();
    }

    IEnumerator RunT2_12()
    {
        const string name = "T2.12 — TickState captures entry context";
        var sm = FSM.Create<TestState>(TestState.Idle)
            .AddTransition<MoveStarted>(TestState.Idle, TestState.Walk)
            .Build();

        TestState capturedPrev = TestState.Dead; // sentinel

        sm.TickState(TestState.Walk, (prev, trg) => capturedPrev = prev);
        sm.Trigger(new MoveStarted()); // Idle→Walk, captures prev=Idle

        yield return null; // skip-first-tick frame
        yield return null; // tick fires

        Assert(capturedPrev == TestState.Idle, name, $"capturedPrev={capturedPrev}");
        sm.Dispose();
    }

    IEnumerator RunT2_13()
    {
        const string name = "T2.13 — TickState dispose stops permanently";
        var sm = BuildBasic();
        int count = 0;

        var handle = sm.TickState(TestState.Walk, (prev, trg) => count++);
        sm.TransitionTo(TestState.Walk);

        yield return null;
        yield return null; // let it tick at least once
        if (count == 0) { Fail(name, "tick never fired before dispose"); yield break; }

        handle.Dispose();
        int frozen = count;
        yield return null;
        yield return null;
        if (count != frozen) { Fail(name, $"tick continued after dispose: {count}"); yield break; }

        // Re-enter Walk — still stopped
        sm.TransitionTo(TestState.Idle);
        sm.TransitionTo(TestState.Walk);
        yield return null;
        yield return null;
        Assert(count == frozen, name, $"tick restarted after re-entry: {count}");
        sm.Dispose();
    }

    IEnumerator RunT2_14()
    {
        const string name = "T2.14 — TriggerEveryUpdate basic";
        var sm = FSM.Create<TestState>(TestState.Idle)
            .AddTransition<Damaged>(TestState.Idle, TestState.Hit)
            .Build();

        var handle = sm.TriggerEveryUpdate(new Damaged(5f));
        yield return null; // TriggerEveryUpdate fires in Stage 2 → Idle→Hit

        Assert(sm.State == TestState.Hit, name, $"expected Hit, got {sm.State}");
        handle.Dispose();
        sm.Dispose();
    }

    IEnumerator RunT2_15()
    {
        const string name = "T2.15 — TriggerEveryUpdate additive";
        var sm = FSM.Create<TestState>(TestState.Idle)
            .AddTransition<Damaged>(TestState.Idle, TestState.Hit)
            .AddTransition<Healed>(TestState.Hit, TestState.Idle)
            .Build();

        int enterCount = 0;
        sm.EnterState(TestState.Hit, (prev, trg) => enterCount++);

        var h1 = sm.TriggerEveryUpdate(new Damaged(1f));
        var h2 = sm.TriggerEveryUpdate(new Damaged(2f));

        yield return null; // both fire independently

        bool ok = sm.State == TestState.Hit || enterCount > 0;
        Assert(ok, name, $"neither Damaged fired (state={sm.State}, enterCount={enterCount})");

        h1.Dispose(); h2.Dispose();
        sm.Dispose();
    }

    IEnumerator RunT2_16()
    {
        const string name = "T2.16 — TriggerEveryUpdate dispose stops firing";
        var sm = FSM.Create<TestState>(TestState.Idle)
            .AddTransition<Damaged>(TestState.Idle, TestState.Hit)
            .Build();

        var handle = sm.TriggerEveryUpdate(new Damaged(5f));
        handle.Dispose(); // dispose immediately

        yield return null;
        yield return null;
        Assert(sm.State == TestState.Idle, name, $"expected Idle, got {sm.State}");
        sm.Dispose();
    }

    IEnumerator RunT2_17()
    {
        const string name = "T2.17 — AddTo disposes on destroy";
        var sm = FSM.Create<TestState>(TestState.Idle)
            .AddTransition<MoveStarted>(TestState.Idle, TestState.Walk)
            .Build();

        var go = new GameObject("fsm_disposer_test");
        sm.AddTo(go);
        Destroy(go);

        yield return null; // Destroy is deferred; OnDestroy fires this frame's end

        sm.Trigger(new MoveStarted());
        Assert(sm.State == TestState.Idle, name,
            $"expected Idle (disposed), got {sm.State}");
    }

    // ── Phase 1 regression ───────────────────────────────────────────────────

    void RunPhase1Regression()
    {
        float hp = 100f;
        bool invincible = false;

        // T1.1
        {
            const string n = "T1.1(reg) — Basic transition";
            var sm = FSM.Create<TestState>(TestState.Idle)
                .AddTransition<MoveStarted>(TestState.Idle, TestState.Walk)
                .AddTransition<MoveStopped>(TestState.Walk, TestState.Idle).Build();
            sm.Trigger(new MoveStarted());
            bool ok = sm.State == TestState.Walk;
            sm.Trigger(new MoveStopped());
            Assert(ok && sm.State == TestState.Idle, n);
        }
        // T1.2
        {
            const string n = "T1.2(reg) — Unmatched trigger ignored";
            var sm = FSM.Create<TestState>(TestState.Idle)
                .AddTransition<MoveStopped>(TestState.Walk, TestState.Idle).Build();
            sm.Trigger(new MoveStopped());
            Assert(sm.State == TestState.Idle, n);
        }
        // T1.3
        {
            const string n = "T1.3(reg) — Same-state no-op";
            var sm = FSM.Create<TestState>(TestState.Idle)
                .AddTransition<MoveStarted>(TestState.Idle, TestState.Idle).Build();
            sm.Trigger(new MoveStarted());
            Assert(sm.State == TestState.Idle, n);
        }
        // T1.4
        {
            const string n = "T1.4(reg) — Condition transition";
            hp = 100f;
            var sm = FSM.Create<TestState>(TestState.Idle)
                .AddTransition<Damaged>(d => hp - d.amount > 0, TestState.Idle, TestState.Hit)
                .AddTransition<Damaged>(d => hp - d.amount <= 0, TestState.Idle, TestState.Dead).Build();
            sm.Trigger(new Damaged(30f));
            bool a = sm.State == TestState.Hit;
            sm = FSM.Create<TestState>(TestState.Idle)
                .AddTransition<Damaged>(d => hp - d.amount > 0, TestState.Idle, TestState.Hit)
                .AddTransition<Damaged>(d => hp - d.amount <= 0, TestState.Idle, TestState.Dead).Build();
            hp = 20f;
            sm.Trigger(new Damaged(50f));
            Assert(a && sm.State == TestState.Dead, n);
        }
        // T1.5
        {
            const string n = "T1.5(reg) — First match wins";
            var sm = FSM.Create<TestState>(TestState.Idle)
                .AddTransition<Damaged>(TestState.Idle, TestState.Hit)
                .AddTransition<Damaged>(TestState.Idle, TestState.Dead).Build();
            sm.Trigger(new Damaged(999f));
            Assert(sm.State == TestState.Hit, n);
        }
        // T1.6
        {
            const string n = "T1.6(reg) — FromAny";
            var sm = FSM.Create<TestState>(TestState.Walk)
                .AddTransitionFromAny<Killed>(TestState.Dead).Build();
            sm.Trigger(new Killed());
            Assert(sm.State == TestState.Dead, n);
        }
        // T1.7
        {
            const string n = "T1.7(reg) — FromAny condition";
            invincible = true;
            var sm = FSM.Create<TestState>(TestState.Idle)
                .AddTransitionFromAny<Damaged>(_ => !invincible, TestState.Hit).Build();
            sm.Trigger(new Damaged(10f));
            bool a = sm.State == TestState.Idle;
            invincible = false;
            sm.Trigger(new Damaged(10f));
            Assert(a && sm.State == TestState.Hit, n);
        }
        // T1.8
        {
            const string n = "T1.8(reg) — Array from";
            var sm = FSM.Create<TestState>(TestState.Hit)
                .AddTransition<Healed>(new[] { TestState.Hit, TestState.Dead }, TestState.Idle).Build();
            sm.Trigger(new Healed());
            bool a = sm.State == TestState.Idle;
            sm.TransitionTo(TestState.Dead);
            sm.Trigger(new Healed());
            Assert(a && sm.State == TestState.Idle, n);
        }
        // T1.9
        {
            const string n = "T1.9(reg) — TransitionTo";
            var sm = FSM.Create<TestState>(TestState.Idle).Build();
            sm.TransitionTo(TestState.Dead);
            bool a = sm.State == TestState.Dead;
            sm.TransitionTo(TestState.Dead);
            Assert(a && sm.State == TestState.Dead, n);
        }
        // T1.10
        {
            const string n = "T1.10(reg) — ForceTransitionTo";
            var sm = FSM.Create<TestState>(TestState.Idle).Build();
            sm.ForceTransitionTo(TestState.Hit);
            Assert(sm.State == TestState.Hit, n);
        }
        // T1.11
        {
            const string n = "T1.11(reg) — Dispose";
            var sm = FSM.Create<TestState>(TestState.Idle)
                .AddTransition<MoveStarted>(TestState.Idle, TestState.Walk).Build();
            sm.Dispose();
            var before = sm.State;
            sm.Trigger(new MoveStarted());
            Assert(sm.State == before, n);
        }
        // T1.12
        {
            const string n = "T1.12(reg) — Null trigger";
            var sm = FSM.Create<TestState>(TestState.Idle).Build();
            try { sm.Evaluate(null); Assert(sm.State == TestState.Idle, n); }
            catch (Exception ex) { Fail(n, ex.Message); }
        }
        // T1.13
        {
            const string n = "T1.13(reg) — Reentrancy";
            var sm = FSM.Create<TestState>(TestState.Idle)
                .AddTransition<MoveStarted>(TestState.Idle, TestState.Walk)
                .AddTransition<Damaged>(TestState.Walk, TestState.Hit).Build();
            sm._testTransitionHook = (prev, cur, trg) => {
                if (cur == TestState.Walk) sm.Trigger(new Damaged(10f));
            };
            sm.Trigger(new MoveStarted());
            Assert(sm.State == TestState.Hit, n, $"got {sm.State}");
        }
        // T1.14
        {
            const string n = "T1.14(reg) — Reentrancy chain";
            var sm = FSM.Create<TestState>(TestState.Idle)
                .AddTransition<MoveStarted>(TestState.Idle, TestState.Walk)
                .AddTransition<Damaged>(TestState.Walk, TestState.Hit)
                .AddTransition<Healed>(TestState.Hit, TestState.Idle).Build();
            sm._testTransitionHook = (prev, cur, trg) => {
                if (cur == TestState.Walk) sm.Trigger(new Damaged(10f));
                else if (cur == TestState.Hit) sm.Trigger(new Healed());
            };
            sm.Trigger(new MoveStarted());
            Assert(sm.State == TestState.Idle, n, $"got {sm.State}");
        }
        // T1.17
        {
            const string n = "T1.17(reg) — CompositeDisposable";
            var cd = new CompositeDisposable();
            int c = 0;
            cd.Add(Disposable.Create(() => c++));
            cd.Add(Disposable.Create(() => c++));
            cd.Dispose();
            bool a = c == 2;
            cd.Add(Disposable.Create(() => c++));
            Assert(a && c == 3, n);
        }
        // T1.18
        {
            const string n = "T1.18(reg) — SerialDisposable";
            var sd = new SerialDisposable();
            int d1 = 0, d2 = 0;
            sd.Disposable = Disposable.Create(() => d1++);
            sd.Disposable = Disposable.Create(() => d2++);
            bool a = d1 == 1 && d2 == 0;
            sd.Dispose();
            Assert(a && d2 == 1, n);
        }
        // T1.19
        {
            const string n = "T1.19(reg) — AddTo extension";
            var cd = new CompositeDisposable();
            Disposable.Create(() => { }).AddTo(cd);
            Assert(cd.Count == 1, n);
        }
    }
}
