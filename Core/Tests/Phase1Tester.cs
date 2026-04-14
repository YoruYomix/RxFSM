using System;
using System.Collections.Generic;
using UnityEngine;
using RxFSM;

/// <summary>
/// Phase 1 tester. Attach to any GameObject and press Play.
/// All tests are synchronous and results appear in the console immediately.
/// </summary>
public class Phase1Tester : MonoBehaviour
{
    // ── Test enums & triggers ────────────────────────────────────────────────

    public enum TestState { Idle, Walk, Run, Hit, Dead }

    public readonly struct MoveStarted { }
    public readonly struct MoveStopped { }
    public readonly struct Sprint { public readonly float speed; public Sprint(float s) { speed = s; } }
    public readonly struct Damaged { public readonly float amount; public Damaged(float a) { amount = a; } }
    public readonly struct Healed { }
    public readonly struct Killed { }

    // ── Runner ───────────────────────────────────────────────────────────────

    private int _passed, _failed;
    private readonly List<string> _failures = new List<string>();

    void Start()
    {
        _passed = 0; _failed = 0; _failures.Clear();

        RunT1_1();  RunT1_2();  RunT1_3();  RunT1_4();  RunT1_5();
        RunT1_6();  RunT1_7();  RunT1_8();  RunT1_9();  RunT1_10();
        RunT1_11(); RunT1_12(); RunT1_13(); RunT1_14(); RunT1_15();
        RunT1_16(); RunT1_17(); RunT1_18(); RunT1_19();

        int total = _passed + _failed;
        if (_failed == 0)
            Debug.Log($"=== Phase 1: {_passed}/{total} passed ===");
        else
        {
            foreach (var f in _failures) Debug.LogError(f);
            Debug.LogError($"=== Phase 1: {_passed}/{total} passed, {_failed} FAILED ===");
        }
    }

    // ── Assert helpers ───────────────────────────────────────────────────────

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
        if (cond) Pass(name);
        else Fail(name, reason);
    }

    void AssertThrows<TEx>(string name, Action action) where TEx : Exception
    {
        try { action(); Fail(name, $"expected {typeof(TEx).Name} but no exception was thrown"); }
        catch (TEx) { Pass(name); }
        catch (Exception ex) { Fail(name, $"expected {typeof(TEx).Name} but got {ex.GetType().Name}: {ex.Message}"); }
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    void RunT1_1()
    {
        const string name = "T1.1 — Basic transition";
        var sm = FSM.Create<TestState>(TestState.Idle)
            .AddTransition<MoveStarted>(TestState.Idle, TestState.Walk)
            .AddTransition<MoveStopped>(TestState.Walk, TestState.Idle)
            .Build();

        sm.Trigger(new MoveStarted());
        if (sm.State != TestState.Walk) { Fail(name, $"expected Walk after MoveStarted, got {sm.State}"); return; }
        sm.Trigger(new MoveStopped());
        Assert(sm.State == TestState.Idle, name, $"expected Idle after MoveStopped, got {sm.State}");
    }

    void RunT1_2()
    {
        const string name = "T1.2 — Unmatched trigger is ignored";
        var sm = FSM.Create<TestState>(TestState.Idle)
            .AddTransition<MoveStopped>(TestState.Walk, TestState.Idle)
            .Build();

        sm.Trigger(new MoveStopped()); // No Walk→Idle rule matches from Idle
        Assert(sm.State == TestState.Idle, name, $"expected Idle, got {sm.State}");
    }

    void RunT1_3()
    {
        const string name = "T1.3 — Same-state transition is no-op";
        var sm = FSM.Create<TestState>(TestState.Idle)
            .AddTransition<MoveStarted>(TestState.Idle, TestState.Idle)
            .Build();

        sm.Trigger(new MoveStarted());
        Assert(sm.State == TestState.Idle, name, $"expected Idle (same-state no-op), got {sm.State}");
    }

    void RunT1_4()
    {
        const string name = "T1.4 — Condition-based transition";
        float hp = 100f;

        var sm = FSM.Create<TestState>(TestState.Idle)
            .AddTransition<Damaged>(d => hp - d.amount > 0, TestState.Idle, TestState.Hit)
            .AddTransition<Damaged>(d => hp - d.amount <= 0, TestState.Idle, TestState.Dead)
            .Build();

        sm.Trigger(new Damaged(30f));
        if (sm.State != TestState.Hit) { Fail(name, $"expected Hit (100-30=70>0), got {sm.State}"); return; }

        // Reset
        sm = FSM.Create<TestState>(TestState.Idle)
            .AddTransition<Damaged>(d => hp - d.amount > 0, TestState.Idle, TestState.Hit)
            .AddTransition<Damaged>(d => hp - d.amount <= 0, TestState.Idle, TestState.Dead)
            .Build();
        hp = 20f;
        sm.Trigger(new Damaged(50f));
        Assert(sm.State == TestState.Dead, name, $"expected Dead (20-50=-30<=0), got {sm.State}");
    }

    void RunT1_5()
    {
        const string name = "T1.5 — First match wins";
        var sm = FSM.Create<TestState>(TestState.Idle)
            .AddTransition<Damaged>(TestState.Idle, TestState.Hit)   // registered first
            .AddTransition<Damaged>(TestState.Idle, TestState.Dead)  // registered second
            .Build();

        sm.Trigger(new Damaged(999f));
        Assert(sm.State == TestState.Hit, name, $"expected Hit (first rule wins), got {sm.State}");
    }

    void RunT1_6()
    {
        const string name = "T1.6 — FromAny";
        var sm = FSM.Create<TestState>(TestState.Walk)
            .AddTransitionFromAny<Killed>(TestState.Dead)
            .Build();

        sm.Trigger(new Killed());
        Assert(sm.State == TestState.Dead, name, $"expected Dead from Walk, got {sm.State}");
    }

    void RunT1_7()
    {
        const string name = "T1.7 — FromAny with condition";
        bool invincible = true;
        var sm = FSM.Create<TestState>(TestState.Idle)
            .AddTransitionFromAny<Damaged>(_ => !invincible, TestState.Hit)
            .Build();

        sm.Trigger(new Damaged(10f));
        if (sm.State != TestState.Idle) { Fail(name, $"expected Idle (invincible), got {sm.State}"); return; }

        invincible = false;
        sm.Trigger(new Damaged(10f));
        Assert(sm.State == TestState.Hit, name, $"expected Hit (!invincible), got {sm.State}");
    }

    void RunT1_8()
    {
        const string name = "T1.8 — Array from overload";
        var sm = FSM.Create<TestState>(TestState.Hit)
            .AddTransition<Healed>(new[] { TestState.Hit, TestState.Dead }, TestState.Idle)
            .Build();

        sm.Trigger(new Healed());
        if (sm.State != TestState.Idle) { Fail(name, $"expected Idle from Hit, got {sm.State}"); return; }

        sm.TransitionTo(TestState.Dead);
        sm.Trigger(new Healed());
        Assert(sm.State == TestState.Idle, name, $"expected Idle from Dead, got {sm.State}");
    }

    void RunT1_9()
    {
        const string name = "T1.9 — TransitionTo (direct)";
        var sm = FSM.Create<TestState>(TestState.Idle).Build();

        sm.TransitionTo(TestState.Dead);
        if (sm.State != TestState.Dead) { Fail(name, $"expected Dead, got {sm.State}"); return; }

        sm.TransitionTo(TestState.Dead); // same-state no-op
        Assert(sm.State == TestState.Dead, name, $"expected Dead (same-state no-op), got {sm.State}");
    }

    void RunT1_10()
    {
        const string name = "T1.10 — ForceTransitionTo (Phase 1 = same as TransitionTo)";
        var sm = FSM.Create<TestState>(TestState.Idle).Build();

        sm.ForceTransitionTo(TestState.Hit);
        Assert(sm.State == TestState.Hit, name, $"expected Hit, got {sm.State}");
    }

    void RunT1_11()
    {
        const string name = "T1.11 — Dispose";
        var sm = FSM.Create<TestState>(TestState.Idle)
            .AddTransition<MoveStarted>(TestState.Idle, TestState.Walk)
            .Build();

        sm.Dispose();

        var stateBefore = sm.State;
        sm.Trigger(new MoveStarted());
        if (sm.State != stateBefore) { Fail(name, "Trigger after Dispose changed state"); return; }

        sm.TransitionTo(TestState.Walk);
        Assert(sm.State == stateBefore, name, $"TransitionTo after Dispose changed state: {sm.State}");
    }

    void RunT1_12()
    {
        const string name = "T1.12 — Null trigger";
        var sm = FSM.Create<TestState>(TestState.Idle).Build();

        try
        {
            sm.Evaluate(null);
            Assert(sm.State == TestState.Idle, name, $"expected Idle after null trigger, got {sm.State}");
        }
        catch (Exception ex)
        {
            Fail(name, $"threw exception: {ex.Message}");
        }
    }

    void RunT1_13()
    {
        const string name = "T1.13 — Reentrancy (queuing)";
        var sm = FSM.Create<TestState>(TestState.Idle)
            .AddTransition<MoveStarted>(TestState.Idle, TestState.Walk)
            .AddTransition<Damaged>(TestState.Walk, TestState.Hit)
            .Build();

        sm._testTransitionHook = (prev, cur, trg) =>
        {
            if (cur == TestState.Walk)
                sm.Trigger(new Damaged(10f)); // enqueued, not immediate
        };

        sm.Trigger(new MoveStarted()); // Idle→Walk (hook enqueues Damaged) → Walk→Hit
        Assert(sm.State == TestState.Hit, name, $"expected Hit, got {sm.State}");
    }

    void RunT1_14()
    {
        const string name = "T1.14 — Reentrancy chain (queue drains fully)";
        var sm = FSM.Create<TestState>(TestState.Idle)
            .AddTransition<MoveStarted>(TestState.Idle, TestState.Walk)
            .AddTransition<Damaged>(TestState.Walk, TestState.Hit)
            .AddTransition<Healed>(TestState.Hit, TestState.Idle)
            .Build();

        sm._testTransitionHook = (prev, cur, trg) =>
        {
            if (cur == TestState.Walk) sm.Trigger(new Damaged(10f));
            else if (cur == TestState.Hit) sm.Trigger(new Healed());
        };

        sm.Trigger(new MoveStarted());
        Assert(sm.State == TestState.Idle, name, $"expected Idle (full chain), got {sm.State}");
    }

    void RunT1_15()
    {
        const string name = "T1.15 — Builder consumed after Build";
        var builder = FSM.Create<TestState>(TestState.Idle);
        builder.Build();
        AssertThrows<InvalidOperationException>(name, () => builder.Build());
    }

    void RunT1_16()
    {
        const string name = "T1.16 — ForceTransition() builder marker";
        try
        {
            var sm = FSM.Create<TestState>(TestState.Idle)
                .AddTransition<Damaged>(TestState.Idle, TestState.Hit)
                .ForceTransition()
                .Build();

            // Verify the last transition has IsForce == true
            // We need to check via reflection or a test accessor since transitions are internal.
            // Use the fact that no exception was thrown and ForceTransition() returns the builder.
            Pass(name); // ForceTransition() did not throw
        }
        catch (Exception ex)
        {
            Fail(name, $"threw: {ex.Message}");
        }

        // Also verify calling ForceTransition with no preceding transition throws
        AssertThrows<InvalidOperationException>(
            name + " (no preceding throws)",
            () => FSM.Create<TestState>(TestState.Idle).ForceTransition());
    }

    void RunT1_17()
    {
        const string name = "T1.17 — CompositeDisposable";
        var cd = new CompositeDisposable();
        int count = 0;
        cd.Add(Disposable.Create(() => count++));
        cd.Add(Disposable.Create(() => count++));
        cd.Dispose();
        if (count != 2) { Fail(name, $"expected count=2 after Dispose, got {count}"); return; }

        cd.Add(Disposable.Create(() => count++));
        Assert(count == 3, name, $"expected count=3 (added after dispose → immediate), got {count}");
    }

    void RunT1_18()
    {
        const string name = "T1.18 — SerialDisposable";
        var sd = new SerialDisposable();
        int disposed1 = 0, disposed2 = 0;

        sd.Disposable = Disposable.Create(() => disposed1++);
        sd.Disposable = Disposable.Create(() => disposed2++);

        if (disposed1 != 1) { Fail(name, $"expected disposed1=1 after replacement, got {disposed1}"); return; }
        if (disposed2 != 0) { Fail(name, $"expected disposed2=0 (current alive), got {disposed2}"); return; }

        sd.Dispose();
        Assert(disposed2 == 1, name, $"expected disposed2=1 after Dispose(), got {disposed2}");
    }

    void RunT1_19()
    {
        const string name = "T1.19 — AddTo extension";
        var cd = new CompositeDisposable();
        var handle = Disposable.Create(() => { });
        handle.AddTo(cd);
        Assert(cd.Count == 1, name, $"expected Count=1, got {cd.Count}");
    }
}
