using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RxFSM;

/// <summary>
/// Tests for FSM.Start(state, fireEnter).
/// Attach to any GameObject and press Play.
/// Sync tests run immediately; tick tests use coroutines.
/// </summary>
public class StartTester : MonoBehaviour
{
    public enum S { Idle, Walk, Run, Dead }

    public readonly struct Go   { }
    public readonly struct Stop { }
    public readonly struct Die  { }

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

    void Start()
    {
        _passed = 0; _failed = 0; _failures.Clear();

        RunTS_1(); RunTS_2(); RunTS_3(); RunTS_4();
        RunTS_5(); RunTS_6(); RunTS_7(); RunTS_8();

        StartCoroutine(RunAsyncTests());
    }

    IEnumerator RunAsyncTests()
    {
        yield return StartCoroutine(RunTS_9());
        yield return StartCoroutine(RunTS_10());
        yield return StartCoroutine(RunTS_11());

        PrintFinal();
    }

    void PrintFinal()
    {
        int total = _passed + _failed;
        if (_failed == 0)
            Debug.Log($"=== Start: {_passed}/{total} passed ===");
        else
        {
            foreach (var f in _failures) Debug.LogError(f);
            Debug.LogError($"=== Start: {_passed}/{total} passed, {_failed} FAILED ===");
        }
    }

    FSM<S> BuildFSM()
        => FSM.Create<S>(S.Idle)
            .AddTransition<Go>(S.Idle, S.Walk)
            .AddTransition<Stop>(S.Walk, S.Idle)
            .AddTransitionFromAny<Die>(S.Dead)
            .Build();

    // ── Sync tests ───────────────────────────────────────────────────────────

    void RunTS_1()
    {
        const string name = "T-S.1 — Start(state, true) sets State";
        var sm = BuildFSM();
        sm.Start(S.Walk, true);
        Assert(sm.State == S.Walk, name, $"State={sm.State}");
        sm.Dispose();
    }

    void RunTS_2()
    {
        const string name = "T-S.2 — Start(state, true) fires EnterState callback";
        var sm = BuildFSM();
        int calls = 0;
        sm.EnterState(S.Walk, (prev, trg) => calls++);
        sm.Start(S.Walk, true);
        Assert(calls == 1, name, $"calls={calls}");
        sm.Dispose();
    }

    void RunTS_3()
    {
        const string name = "T-S.3 — Start(state, true) Enter receives prev==state, trg==null";
        var sm = BuildFSM();
        S      capturedPrev = S.Dead;      // sentinel
        object capturedTrg  = new object(); // sentinel
        sm.EnterState(S.Walk, (prev, trg) => { capturedPrev = prev; capturedTrg = trg; });
        sm.Start(S.Walk, true);
        if (capturedPrev != S.Walk) { Fail(name, $"prev={capturedPrev}"); return; }
        Assert(capturedTrg == null, name, $"trg={capturedTrg}");
        sm.Dispose();
    }

    void RunTS_4()
    {
        const string name = "T-S.4 — Start(state, false) sets State, no Enter fired";
        var sm = BuildFSM();
        int calls = 0;
        sm.EnterState(S.Walk, (prev, trg) => calls++);
        sm.Start(S.Walk, false);
        if (sm.State != S.Walk) { Fail(name, $"State={sm.State}"); return; }
        Assert(calls == 0, name, $"calls={calls}");
        sm.Dispose();
    }

    void RunTS_5()
    {
        const string name = "T-S.5 — Start(Walk, false) then Trigger fires Exit for Walk";
        var sm = BuildFSM();
        int exitCalls = 0;
        sm.ExitState(S.Walk, (next, trg) => exitCalls++);
        sm.Start(S.Walk, false);
        sm.Trigger(new Stop()); // Walk→Idle
        Assert(exitCalls == 1, name, $"exitCalls={exitCalls}");
        sm.Dispose();
    }

    void RunTS_6()
    {
        const string name = "T-S.6 — Start(Walk, false) then Trigger fires Enter for new state";
        var sm = BuildFSM();
        int enterCalls = 0;
        sm.EnterState(S.Idle, (prev, trg) => enterCalls++);
        sm.Start(S.Walk, false);
        sm.Trigger(new Stop()); // Walk→Idle
        Assert(enterCalls == 1, name, $"enterCalls={enterCalls}");
        sm.Dispose();
    }

    void RunTS_7()
    {
        const string name = "T-S.7 — Start(state, true) then Trigger: Enter fires twice total";
        var sm = BuildFSM();
        int totalEnter = 0;
        sm.EnterState((cur, prev) => totalEnter++);
        sm.Start(S.Walk, true);  // Enter #1
        sm.Trigger(new Stop());  // Walk→Idle: Enter #2
        Assert(totalEnter == 2, name, $"totalEnter={totalEnter}");
        sm.Dispose();
    }

    void RunTS_8()
    {
        const string name = "T-S.8 — Start on disposed FSM does not throw";
        var sm = BuildFSM();
        sm.Dispose();
        try { sm.Start(S.Walk, true); Pass(name); }
        catch (System.Exception ex) { Fail(name, ex.Message); }
    }

    // ── Coroutine tests ──────────────────────────────────────────────────────

    IEnumerator RunTS_9()
    {
        const string name = "T-S.9 — Start(state, false) tick fires without Enter";
        var sm = BuildFSM();
        int tickCount = 0;
        sm.TickState(S.Walk, (prev, trg) => tickCount++);
        sm.Start(S.Walk, false); // no Enter → no skipNext → tick starts immediately next frame

        yield return null; // frame 1: tick fires (no skip-first-tick)
        yield return null; // frame 2: tick fires again

        Assert(tickCount >= 2, name, $"tickCount={tickCount} after 2 frames");
        sm.Dispose();
    }

    IEnumerator RunTS_10()
    {
        const string name = "T-S.10 — Start(state, true) tick has one-frame skip then fires";
        var sm = BuildFSM();
        int tickCount = 0;
        sm.TickState(S.Walk, (prev, trg) => tickCount++);
        sm.Start(S.Walk, true); // Enter fires → skipNext=true

        yield return null; // frame 1: skip frame
        if (tickCount != 0) { Fail(name, $"tick fired on skip frame: count={tickCount}"); yield break; }

        yield return null; // frame 2: tick fires
        Assert(tickCount > 0, name, $"tickCount={tickCount} after skip+tick frames");
        sm.Dispose();
    }

    IEnumerator RunTS_11()
    {
        const string name = "T-S.11 — Start(Walk, false) tick stops after transitioning away";
        var sm = BuildFSM();
        int tickCount = 0;
        sm.TickState(S.Walk, (prev, trg) => tickCount++);
        sm.Start(S.Walk, false);

        yield return null;
        yield return null; // ticking

        sm.Trigger(new Stop()); // Walk→Idle
        int frozen = tickCount;

        yield return null;
        yield return null;

        Assert(tickCount == frozen, name, $"tick continued after exit: {tickCount} vs {frozen}");
        sm.Dispose();
    }
}
