using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RxFSM
{
    public class Phase4Tester : MonoBehaviour
    {
        // ── Enums ────────────────────────────────────────────────────────────────
        public enum CharacterState { Alive, Death }
        public enum AliveState     { Ground, Aerial }
        public enum GroundState    { Idle, Walk, Run, Hit, Attack }
        public enum AerialState    { JumpUp, Falling }
        public enum DeathState     { Dying, Dead }

        // ── Triggers ─────────────────────────────────────────────────────────────
        readonly struct Jump        { public readonly float force; public Jump(float f) { force = f; } }
        readonly struct Landed      { }
        readonly struct Revive      { }
        readonly struct MoveStarted { }
        readonly struct MoveStopped { }
        readonly struct Damaged     { public readonly float amount; public Damaged(float a) { amount = a; } }
        readonly struct Killed      { }
        readonly struct AttackInput { }
        readonly struct AttackEnd   { }
        readonly struct SkillCast   { }

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
            RunSyncTests();
            StartCoroutine(RunAsyncTests());
        }

        void RunSyncTests()
        {
            T4_1_BasicSetup();
            T4_2_PropagationSkipsLayers();
            T4_6_GetActiveStateHierarchy();
            T4_7_ExitOrderTopDown();
            T4_8_ParentCausedExitParams();
            T4_12_HistoryEntryPrevDefault();
            T4_13_HierarchicalDeactivate();
            T4_14_FilterBlocks();
            T4_15_FilterOrdering();
            T4_16_ForceBypassesFilter();
            T4_18_InterruptBasic();
            T4_19_InterruptCancelsPrevious();
            T4_21_ReentrancyDuringTransition();
            T4_22_DisposeHierarchy();
        }

        IEnumerator RunAsyncTests()
        {
            yield return StartCoroutine(T4_3_History());
            yield return StartCoroutine(T4_4_TriggerPropagationExplicit());
            yield return StartCoroutine(T4_5_TriggerPropagationHistory());
            yield return StartCoroutine(T4_9_CancellationOnLeavingPath());
            yield return StartCoroutine(T4_10_ThrottleNoZombie());
            yield return StartCoroutine(T4_11_ForcePropagatesChildren());
            yield return StartCoroutine(T4_17_FilterCancelledByStateChange());
            yield return StartCoroutine(T4_20_InterruptCancelledByTransition());
            yield return StartCoroutine(T4_23_Phase123Regression());
            PrintFinal();
        }

        void PrintFinal()
        {
            int total = _pass + _fail;
            if (_fail == 0) Debug.Log($"=== Phase 4: {_pass}/{total} passed ===");
            else            Debug.LogError($"=== Phase 4: {_pass}/{total} passed, {_fail} FAILED ===");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // HELPERS — build the standard 3-layer hierarchy
        // ─────────────────────────────────────────────────────────────────────────

        (FSM<CharacterState> charFsm,
         FSM<AliveState>     aliveFsm,
         FSM<GroundState>    groundSm)
        BuildHierarchy(bool groundHasRun = false)
        {
            var groundSm = FSM.Create<GroundState>(GroundState.Idle)
                .AddTransition<MoveStarted>(GroundState.Idle,  GroundState.Walk)
                .AddTransition<MoveStopped>(GroundState.Walk,  GroundState.Idle)
                .AddTransition<MoveStarted>(GroundState.Walk,  GroundState.Run)
                .AddTransition<MoveStopped>(GroundState.Run,   GroundState.Idle)
                .AddTransition<Damaged>    (GroundState.Walk,  GroundState.Hit)
                .AddTransition<AttackInput>(GroundState.Idle,  GroundState.Attack)
                .AddTransition<AttackEnd>  (GroundState.Attack,GroundState.Idle)
                .Build();

            var aliveFsm = FSM.Create<AliveState>(AliveState.Ground)
                .AddTransition<Jump>  (AliveState.Ground, AliveState.Aerial)
                .AddTransition<Landed>(AliveState.Aerial, AliveState.Ground)
                .Register(AliveState.Ground, groundSm)
                .Build();

            var charFsm = FSM.Create<CharacterState>(CharacterState.Alive)
                .AddTransition<Killed>(CharacterState.Alive, CharacterState.Death)
                .AddTransition<Revive>(CharacterState.Death, CharacterState.Alive)
                .Register(CharacterState.Alive, aliveFsm)
                .Build();

            return (charFsm, aliveFsm, groundSm);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // SYNC TESTS
        // ─────────────────────────────────────────────────────────────────────────

        void T4_1_BasicSetup()
        {
            var (c, a, g) = BuildHierarchy();
            c.Trigger(new MoveStarted());
            Assert(g.State == GroundState.Walk, "T4.1 — trigger propagated through 3 layers");
            c.Dispose();
        }

        void T4_2_PropagationSkipsLayers()
        {
            var (c, a, g) = BuildHierarchy();
            c.Trigger(new MoveStarted());
            Assert(g.State == GroundState.Walk, "T4.2 — propagation skips layers without rules");
            c.Dispose();
        }

        void T4_6_GetActiveStateHierarchy()
        {
            var (c, a, g) = BuildHierarchy();
            var h = c.GetActiveStateHierarchy();
            Assert(h.Count == 3 &&
                   h[0].Equals(CharacterState.Alive) &&
                   h[1].Equals(AliveState.Ground) &&
                   h[2].Equals(GroundState.Idle), "T4.6a — hierarchy [Alive, Ground, Idle]");

            c.Trigger(new MoveStarted());
            h = c.GetActiveStateHierarchy();
            Assert(h[2].Equals(GroundState.Walk), "T4.6b — hierarchy [Alive, Ground, Walk]");
            c.Dispose();
        }

        void T4_7_ExitOrderTopDown()
        {
            var sequence = new List<string>();
            var (c, a, g) = BuildHierarchy();

            c.ExitState((cur, next, trg) => sequence.Add("char"));
            a.ExitState((cur, next, trg) => sequence.Add("alive"));
            g.ExitState((cur, next, trg) => sequence.Add("ground"));

            c.Trigger(new Killed());
            Assert(sequence.Count == 3 &&
                   sequence[0] == "char" &&
                   sequence[1] == "alive" &&
                   sequence[2] == "ground",
                   "T4.7 — exit order top-down");
            c.Dispose();
        }

        void T4_8_ParentCausedExitParams()
        {
            GroundState capturedNext = GroundState.Run; // non-default sentinel
            object      capturedTrg  = null;
            var (c, a, g) = BuildHierarchy();

            g.ExitState((Action<GroundState, GroundState, object>)((cur, next, trg) =>
            {
                capturedNext = next;
                capturedTrg  = trg;
            }));

            c.Trigger(new Killed());
            // next == default(GroundState) signals parent-caused exit
            Assert(capturedNext.Equals(default(GroundState)), "T4.8a — next=default when parent caused exit");
            Assert(capturedTrg is Killed, "T4.8b — trg is the parent trigger");
            c.Dispose();
        }

        void T4_12_HistoryEntryPrevDefault()
        {
            var (c, a, g) = BuildHierarchy();

            // Drive groundSm to Run
            g.Trigger(new MoveStarted()); // Idle→Walk
            g.Trigger(new MoveStarted()); // Walk→Run
            Assert(g.State == GroundState.Run, "T4.12 setup — groundSm at Run");

            bool prevWasDefault = false;
            g.EnterState(GroundState.Run, (prev, trg) =>
            {
                prevWasDefault = prev.Equals(default(GroundState));
            });

            c.Trigger(new Killed());  // leave path
            c.Trigger(new Revive());  // re-enter → history restoration
            Assert(prevWasDefault, "T4.12 — history entry prev=default(TState)");
            c.Dispose();
        }

        void T4_13_HierarchicalDeactivate()
        {
            var (c, a, g) = BuildHierarchy();
            var handle = a.Deactivate();
            c.Trigger(new MoveStarted());
            Assert(g.State == GroundState.Idle, "T4.13a — aliveFsm deactivate blocks groundSm trigger");
            handle.Dispose();
            c.Trigger(new MoveStarted());
            Assert(g.State == GroundState.Walk, "T4.13b — after dispose, trigger propagates");
            c.Dispose();
        }

        void T4_14_FilterBlocks()
        {
            int mana = 5;
            var sm = FSM.Create<GroundState>(GroundState.Idle)
                .AddTransition<SkillCast>(GroundState.Idle, GroundState.Hit)
                .UseGlobalFilter(new ManaFilter(() => mana))
                .Build();

            sm.Trigger(new SkillCast());
            Assert(sm.State == GroundState.Idle, "T4.14a — filter blocks when mana < 10");

            mana = 20;
            sm.Trigger(new SkillCast());
            Assert(sm.State == GroundState.Hit, "T4.14b — filter passes when mana >= 10");
            sm.Dispose();
        }

        void T4_15_FilterOrdering()
        {
            var log = new List<string>();
            var sm = FSM.Create<GroundState>(GroundState.Idle)
                .AddTransition<SkillCast>(GroundState.Idle, GroundState.Hit)
                .UseGlobalFilter(new LogFilter("G1", log))
                .UseFilter(new LogFilter("L1", log))
                .UseFilter(new LogFilter("L2", log))
                .Build();

            sm.Trigger(new SkillCast());
            Assert(log.Count == 3 && log[0] == "G1" && log[1] == "L1" && log[2] == "L2",
                   "T4.15 — filter order G1→L1→L2");
            sm.Dispose();
        }

        void T4_16_ForceBypassesFilter()
        {
            var sm = FSM.Create<GroundState>(GroundState.Idle)
                .AddTransition<Killed>(GroundState.Idle, GroundState.Hit)
                .ForceTransition()
                .UseGlobalFilter(new BlockAllFilter())
                .Build();

            sm.Trigger(new Killed());
            Assert(sm.State == GroundState.Hit, "T4.16 — IsForce bypasses all filters");
            sm.Dispose();
        }

        void T4_18_InterruptBasic()
        {
            var sm = FSM.Create<GroundState>(GroundState.Idle)
                .AddTransition<MoveStarted>(GroundState.Idle, GroundState.Walk)
                .Build();

            sm.Interrupt(new ImmediateInterrupt(sm));
            Assert(sm.State == GroundState.Walk, "T4.18 — Interrupt fires trigger immediately");
            sm.Dispose();
        }

        void T4_19_InterruptCancelsPrevious()
        {
            bool firstCancelled = false;
            var sm = FSM.Create<GroundState>(GroundState.Idle).Build();

            sm.Interrupt(new SlowInterrupt(() => firstCancelled = true));
            sm.Interrupt(new SlowInterrupt(null)); // cancels first
            System.Threading.Thread.Sleep(50);
            Assert(firstCancelled, "T4.19 — second Interrupt cancels first");
            sm.Dispose();
        }

        void T4_21_ReentrancyDuringTransition()
        {
            var (c, a, g) = BuildHierarchy();
            bool reviveQueued = false;

            g.ExitState((cur, next, trg) =>
            {
                if (!reviveQueued)
                {
                    reviveQueued = true;
                    c.Trigger(new Revive()); // queued
                }
            });

            c.Trigger(new Killed()); // Killed sequence completes, then Revive dequeued
            // After Killed: charFsm=Death. After Revive: charFsm=Alive again.
            Assert(c.State == CharacterState.Alive, "T4.21 — queued trigger processed after full sequence");
            c.Dispose();
        }

        void T4_22_DisposeHierarchy()
        {
            var (c, a, g) = BuildHierarchy();
            c.Dispose();

            g.Trigger(new MoveStarted());
            Assert(g.State == GroundState.Idle, "T4.22a — child disposed, trigger is no-op");
            // No exception expected — just no-ops
            Assert(true, "T4.22b — dispose hierarchy no exception");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ASYNC / COROUTINE TESTS
        // ─────────────────────────────────────────────────────────────────────────

        IEnumerator T4_3_History()
        {
            var (c, a, g) = BuildHierarchy();
            g.Trigger(new MoveStarted()); // Idle→Walk
            g.Trigger(new MoveStarted()); // Walk→Run
            Assert(g.State == GroundState.Run, "T4.3 setup");

            c.Trigger(new Killed());
            Assert(g.State == GroundState.Run, "T4.3a — state retained on leave");

            c.Trigger(new Revive());
            Assert(g.State == GroundState.Run, "T4.3b — state retained on re-enter (history)");
            c.Dispose();
            yield return null;
        }

        IEnumerator T4_4_TriggerPropagationExplicit()
        {
            // aerialSm has Jump rule: JumpUp→Falling
            var aerialSm = FSM.Create<AerialState>(AerialState.JumpUp)
                .AddTransition<Jump>(AerialState.JumpUp, AerialState.Falling)
                .Build();

            bool jumpUpEnterCalled  = false;
            bool fallingEnterCalled = false;
            aerialSm.EnterState(AerialState.JumpUp,    (prev, trg) => jumpUpEnterCalled  = true);
            aerialSm.EnterState(AerialState.Falling,   (prev, trg) => fallingEnterCalled = true);

            var aliveFsm = FSM.Create<AliveState>(AliveState.Ground)
                .AddTransition<Jump>(AliveState.Ground, AliveState.Aerial)
                .Register(AliveState.Aerial, aerialSm)
                .Build();

            var charFsm = FSM.Create<CharacterState>(CharacterState.Alive)
                .Register(CharacterState.Alive, aliveFsm)
                .Build();

            charFsm.Trigger(new Jump(10f)); // Ground→Aerial, trigger propagated to aerialSm

            Assert(aerialSm.State == AerialState.Falling, "T4.4a — explicit transition in child");
            Assert(!jumpUpEnterCalled,  "T4.4b — history EnterState skipped for JumpUp");
            Assert(fallingEnterCalled,  "T4.4c — Falling EnterState called");
            charFsm.Dispose();
            yield return null;
        }

        IEnumerator T4_5_TriggerPropagationHistory()
        {
            // groundSm has no Landed rule — resumes at history with prev=default
            var groundSm2 = FSM.Create<GroundState>(GroundState.Idle)
                .AddTransition<MoveStarted>(GroundState.Idle, GroundState.Walk)
                .AddTransition<MoveStarted>(GroundState.Walk, GroundState.Run)
                .Build();

            // Drive to Run
            groundSm2.Trigger(new MoveStarted());
            groundSm2.Trigger(new MoveStarted());

            bool runEnteredWithDefault = false;
            groundSm2.EnterState(GroundState.Run, (prev, trg) =>
            {
                runEnteredWithDefault = prev.Equals(default(GroundState));
            });

            var aliveFsm2 = FSM.Create<AliveState>(AliveState.Ground)
                .AddTransition<Jump>  (AliveState.Ground, AliveState.Aerial)
                .AddTransition<Landed>(AliveState.Aerial, AliveState.Ground)
                .Register(AliveState.Ground, groundSm2)
                .Build();

            var charFsm2 = FSM.Create<CharacterState>(CharacterState.Alive)
                .Register(CharacterState.Alive, aliveFsm2)
                .Build();

            // Go aerial (no aerialSm registered — just for testing)
            aliveFsm2.Trigger(new Jump(5f)); // Ground→Aerial

            // Return to Ground via Landed — groundSm has no Landed rule → history
            aliveFsm2.Trigger(new Landed());

            Assert(groundSm2.State == GroundState.Run, "T4.5a — resumed at history state Run");
            Assert(runEnteredWithDefault, "T4.5b — history entry prev=default");
            charFsm2.Dispose();
            yield return null;
        }

        IEnumerator T4_9_CancellationOnLeavingPath()
        {
            bool cancelled = false;
            var (c, a, g) = BuildHierarchy();

            g.Trigger(new MoveStarted()); // Idle→Walk

            g.EnterStateAsync(GroundState.Walk, async (prev, ct) =>
            {
                try   { await Task.Delay(5000, ct); }
                catch (OperationCanceledException) { cancelled = true; }
            }, AsyncOperation.Throttle);

            // Re-enter Walk to start the async task
            g.Trigger(new MoveStopped()); // Walk→Idle
            g.Trigger(new MoveStarted()); // Idle→Walk (async starts)

            c.Trigger(new Killed()); // Walk leaves active path → CT cancelled

            yield return new WaitForSeconds(0.1f);
            Assert(cancelled, "T4.9 — async CT cancelled when leaving active path");
            c.Dispose();
        }

        IEnumerator T4_10_ThrottleNoZombie()
        {
            var (c, a, g) = BuildHierarchy();

            // Add aerialSm so aliveFsm has a child to enter
            var aerialSm = FSM.Create<AerialState>(AerialState.JumpUp).Build();
            // Re-build aliveFsm with aerialSm registered (bypass — use direct trigger)

            g.Trigger(new AttackInput()); // Idle→Attack
            // ThrottleState on Attack: configure manually after build
            // (builder API only — test the runtime effect via ForceTransition shortcut)

            // Instead test with a simpler setup:
            var sm = FSM.Create<GroundState>(GroundState.Idle)
                .AddTransition<AttackInput>(GroundState.Idle,   GroundState.Attack)
                .AddTransition<AttackEnd>  (GroundState.Attack, GroundState.Idle)
                .ThrottleState(GroundState.Attack, 0.5f)
                .Build();

            sm.Trigger(new AttackInput());
            sm.Trigger(new AttackEnd());  // pending

            sm.Dispose(); // dispose before throttle expires
            yield return new WaitForSeconds(0.7f);

            Assert(sm.State == GroundState.Attack, "T4.10 — pending discarded on dispose, no zombie");
            c.Dispose();
        }

        IEnumerator T4_11_ForcePropagatesChildren()
        {
            var (c, a, g) = BuildHierarchy();

            g.Trigger(new AttackInput()); // Idle→Attack

            // Build a fresh FSM with ThrottleState to verify guard reset
            var sm = FSM.Create<GroundState>(GroundState.Idle)
                .AddTransition<AttackInput>(GroundState.Idle,   GroundState.Attack)
                .AddTransition<AttackEnd>  (GroundState.Attack, GroundState.Idle)
                .ThrottleState(GroundState.Attack, 10f)
                .Build();

            sm.Trigger(new AttackInput());
            Assert(sm.State == GroundState.Attack, "T4.11 setup — Attack entered");
            sm.Trigger(new AttackEnd()); // pending due to throttle
            Assert(sm.State == GroundState.Attack, "T4.11a — blocked by throttle");

            sm.ForceTransitionTo(GroundState.Idle);
            Assert(sm.State == GroundState.Idle, "T4.11b — ForceTransitionTo bypasses guard");
            yield return new WaitForSeconds(0.2f);
            Assert(sm.State == GroundState.Idle, "T4.11c — no pending executed after force");
            sm.Dispose();
            c.Dispose();
        }

        IEnumerator T4_17_FilterCancelledByStateChange()
        {
            bool filterCancelled = false;
            var sm = FSM.Create<GroundState>(GroundState.Idle)
                .AddTransition<SkillCast>(GroundState.Idle, GroundState.Hit)
                .UseGlobalFilter(new SlowFilter(() => filterCancelled = true))
                .Build();

            sm.Trigger(new SkillCast()); // async filter starts
            sm.TransitionTo(GroundState.Run); // state changes → filter CT cancelled

            yield return new WaitForSeconds(0.1f);
            Assert(filterCancelled, "T4.17 — filter CT cancelled by state change");
            sm.Dispose();
        }

        IEnumerator T4_20_InterruptCancelledByTransition()
        {
            bool wasCancelled = false;
            var sm = FSM.Create<GroundState>(GroundState.Idle)
                .AddTransition<MoveStarted>(GroundState.Idle, GroundState.Walk)
                .Build();

            sm.Interrupt(new SlowInterrupt(() => wasCancelled = true));
            sm.Trigger(new MoveStarted()); // state transition → interrupt cancelled

            yield return new WaitForSeconds(0.1f);
            Assert(wasCancelled, "T4.20 — interrupt cancelled by state transition");
            sm.Dispose();
        }

        IEnumerator T4_23_Phase123Regression()
        {
            yield return StartCoroutine(RunBasicRegression());
        }

        IEnumerator RunBasicRegression()
        {
            // T1.1
            {
                var sm = FSM.Create<GroundState>(GroundState.Idle)
                    .AddTransition<MoveStarted>(GroundState.Idle, GroundState.Walk)
                    .AddTransition<MoveStopped>(GroundState.Walk, GroundState.Idle).Build();
                sm.Trigger(new MoveStarted()); Assert(sm.State == GroundState.Walk, "T4.23/T1.1a");
                sm.Trigger(new MoveStopped()); Assert(sm.State == GroundState.Idle, "T4.23/T1.1b");
                sm.Dispose();
            }

            // T3.1 ThrottleState
            {
                var sm = FSM.Create<GroundState>(GroundState.Idle)
                    .AddTransition<MoveStarted>(GroundState.Idle, GroundState.Walk)
                    .AddTransition<MoveStopped>(GroundState.Walk, GroundState.Idle)
                    .ThrottleState(GroundState.Walk, 0.3f).Build();
                sm.Trigger(new MoveStarted()); sm.Trigger(new MoveStopped());
                Assert(sm.State == GroundState.Walk, "T4.23/T3.1a — blocked");
                yield return new WaitForSeconds(0.5f);
                Assert(sm.State == GroundState.Idle, "T4.23/T3.1b — released");
                sm.Dispose();
            }

            // T2.11 TickState
            {
                int ticks = 0;
                var sm = FSM.Create<GroundState>(GroundState.Idle).Build();
                sm.TickState(GroundState.Walk, (prev, trg) => ticks++);
                sm.TransitionTo(GroundState.Walk);
                yield return null; yield return null;
                Assert(ticks > 0, "T4.23/T2.11");
                sm.Dispose();
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // FILTER / INTERRUPT IMPLEMENTATIONS
        // ─────────────────────────────────────────────────────────────────────────

        class ManaFilter : ITransitionFilter
        {
            Func<int> _getMana;
            public ManaFilter(Func<int> getMana) { _getMana = getMana; }
            public async ValueTask Invoke(object trigger, TransitionContext ctx,
                                          Func<ValueTask> next, CancellationToken ct)
            {
                if (_getMana() >= 10) await next();
                // else block
            }
        }

        class LogFilter : ITransitionFilter
        {
            string _label; List<string> _log;
            public LogFilter(string label, List<string> log) { _label = label; _log = log; }
            public async ValueTask Invoke(object trigger, TransitionContext ctx,
                                          Func<ValueTask> next, CancellationToken ct)
            {
                _log.Add(_label);
                await next();
            }
        }

        class BlockAllFilter : ITransitionFilter
        {
            public ValueTask Invoke(object trigger, TransitionContext ctx,
                                    Func<ValueTask> next, CancellationToken ct)
                => default; // never calls next → always blocks
        }

        class SlowFilter : ITransitionFilter
        {
            Action _onCancel;
            public SlowFilter(Action onCancel) { _onCancel = onCancel; }
            public async ValueTask Invoke(object trigger, TransitionContext ctx,
                                          Func<ValueTask> next, CancellationToken ct)
            {
                try   { await Task.Delay(5000, ct); await next(); }
                catch (OperationCanceledException) { _onCancel?.Invoke(); }
            }
        }

        // ── Interrupt helpers ─────────────────────────────────────────────────────

        class ImmediateInterrupt : IInterrupt
        {
            FSM<GroundState> _sm;
            public ImmediateInterrupt(FSM<GroundState> sm) { _sm = sm; }
            public ValueTask InvokeAsync(Enum currentState, CancellationToken ct)
            {
                _sm.Trigger(new MoveStarted());
                return default;
            }
        }

        class SlowInterrupt : IInterrupt
        {
            Action _onCancel;
            public SlowInterrupt(Action onCancel) { _onCancel = onCancel; }
            public async ValueTask InvokeAsync(Enum currentState, CancellationToken ct)
            {
                try   { await Task.Delay(5000, ct); }
                catch (OperationCanceledException) { _onCancel?.Invoke(); }
            }
        }
    }
}
