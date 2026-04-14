# RxFSM — API Reference

정확한 API 시그니처, 동작 순서, 사용 패턴을 담은 상세 레퍼런스입니다.
구현 시 직접 참고하거나, AI 어시스턴트에게 이 문서를 붙여넣으면 정확한 코드를 생성할 수 있습니다.

---

## 핵심 개념

- **트리거는 `readonly struct`** — 발동 시점의 데이터를 직접 운반합니다. `Trigger<TTrigger>` 사용 시 제로 할당.
- `sm.Trigger(new T())`는 **동기적·즉시 실행** — 큐잉 없음, 프레임 지연 없음.
- 빌더(`FSM.Create`)는 **전이 테이블** — 모든 전이를 여기에 선언합니다.
- `EnterState` / `ExitState`는 **액션 테이블** — 전이 테이블과 분리해서 작성합니다.
- 같은 프레임에 발동된 트리거는 **호출 순서대로** 결정론적으로 처리됩니다.
- `TState`는 반드시 **enum**이어야 합니다.

---

## For AI Assistants

You are helping a Unity developer use **RxFSM**, a data-carrying trigger-based FSM library.
Follow the rules and patterns below precisely when generating code.

---

## Trigger Definition Rules

```csharp
// ALWAYS use readonly record struct (C# 10+)
public readonly record struct Damaged(float amount, Vector3 direction);
public readonly record struct Healed(float amount);
public readonly record struct MoveStarted;   // no data = empty struct is fine

// Legacy fallback (C# < 10)
public readonly struct Damaged
{
    public readonly float amount;
    public readonly Vector3 direction;
    public Damaged(float amount, Vector3 direction) { this.amount = amount; this.direction = direction; }
}
```

---

## Builder API

Entry point: `FSM.Create<TState>(initialState)` — returns `FSMBuilder<TState>`.

```csharp
var sm = FSM.Create<TState>(initialState)

    // Basic transition
    .AddTransition<TTrigger>(from: S.A, to: S.B)

    // Conditional transition — evaluated in registration order; first passing condition wins
    .AddTransition<TTrigger>(trg => condition, from: S.A, to: S.B)

    // Multiple from-states (sugar — expands to one rule per state)
    .AddTransition<TTrigger>(from: new[]{ S.A, S.B }, to: S.C)

    // Transition from any state
    .AddTransitionFromAny<TTrigger>(to: S.X)
    .AddTransitionFromAny<TTrigger>(_ => !invincible, to: S.X)

    // ForceTransition modifier — MUST chain immediately after AddTransition / AddTransitionFromAny
    // Bypasses: ThrottleState, HoldState, AsyncOperation.Throttle guard, TransitionFilter
    .AddTransition<TTrigger>(trg => trg.amount > hp, from: S.A, to: S.Dead)
        .ForceTransition()

    // Timing guards
    .ThrottleState(S.Attack, 0.5f)                           // block exit for N seconds after entry; only final pending trigger executes
    .ThrottleFrameState(S.Walk, 3)                           // same, frame-based
    .HoldState(S.Attack, waitUntil: () => condition)         // defer exit until condition returns true

    // Auto-transition
    .AutoTransition(from: S.Hit, to: S.Idle, time: 0.8f)    // after N seconds
    .AutoTransition(from: S.Hit, to: S.Idle, onComplete: cb) // Action<Action>: call the provided Action when done

    .Build();
```

---

## Firing Triggers

```csharp
sm.Trigger(new Damaged(50f, hitDir));   // immediate, synchronous — evaluated this frame
sm.TransitionTo(S.Idle);               // direct transition, no trigger, no condition checks
sm.ForceTransitionTo(S.Dead);          // bypasses all guards (ThrottleState, HoldState, AsyncOperation.Throttle, TransitionFilter)
```

---

## Per-Frame Execution Order

Each Unity frame, RxFSM runs three stages in order:

1. **STAGE_TRIGGERS** — `TriggerEveryUpdate` fires (calls `sm.Trigger` internally)
2. **STAGE_TIMERS** — ThrottleState timers, HoldState condition checks, AutoTransition timers
3. **STAGE_TICKS** — `TickState` callbacks execute

`sm.Trigger()` called from user code (outside FSMLoop) executes immediately, before any stage.

---

## Callbacks: EnterState / ExitState

**Execution order on transition:** `ExitState` callbacks → state enum changes → `EnterState` callbacks.

### Callback signatures and parameter names

```csharp
// ── EnterState ─────────────────────────────────────────────────────────────────

// Unfiltered — fires on every state entry
sm.EnterState((TState cur, TState prev) => { });
sm.EnterState((TState cur, TState prev, object trg) => { });

// State-filtered — fires only when entering targetState
sm.EnterState(S.Idle, (TState prev, object trg) => { });

// Trigger-type-filtered — fires on any state entered via TTrigger, regardless of which state
sm.EnterState<Damaged>((TState cur, TState prev, object trg) =>
{
    var d = (Damaged)trg;     // trg is always boxed object — cast inside
    SpawnHitEffect(d.direction);
});

// State + Trigger filtered — fires only when entering targetState via TTrigger
// trg is object — cast manually:
sm.EnterState<Damaged>(S.Hit, (TState prev, object trg) =>
{
    var d = (Damaged)trg;
    hp -= d.amount;
});

// State + Trigger filtered — typed trg (no cast needed):
sm.EnterState<Damaged>(S.Hit, (TState prev, Damaged trg) =>
{
    hp -= trg.amount;
    SpawnEffect(trg.element, trg.direction);
});

// ── ExitState ──────────────────────────────────────────────────────────────────

sm.ExitState((TState cur, TState next) => { });
sm.ExitState((TState cur, TState next, object trg) => { });
sm.ExitState(S.BossFight, (TState next, object trg) => { });         // state-filtered, trg is object
sm.ExitState<Damaged>((TState cur, TState next, object trg) => { }); // trigger-type-filtered
sm.ExitState<BossDefeated>(S.BossFight, (TState next, BossDefeated trg) => { }); // state+trigger filtered, typed trg
```

**All callbacks return `IDisposable`.** Dispose the handle to unregister.

---

## TickState

Runs every frame while in the specified state. Executes at STAGE_TICKS (after TriggerEveryUpdate and timers).

```csharp
IDisposable handle = sm.TickState(S.Patrol, (TState prev, object trg) =>
{
    // prev and trg are captured at state ENTRY time — they do not change each tick
    PatrolLogic();
});

handle.Dispose(); // unregisters immediately, even while in S.Patrol
```

**Important:** The **first tick fires on the frame AFTER entry** — not the same frame as `EnterState`.
`prev` and `trg` inside the TickState callback are frozen at entry time.

---

## Polling: TriggerEveryUpdate

```csharp
// Fire trigger every frame — trigger struct is boxed once, no per-frame allocation
IDisposable handle = sm.TriggerEveryUpdate(new PositionTick(pos));
handle.Dispose();

// Throttled — first fire is immediate, then once per interval
sm.TriggerEveryUpdate(new DotDamage(3)).Throttle(0.2f);

// Scoped — stops automatically when using block exits
using (sm.TriggerEveryUpdate(new DotDamage(2)))
{
    await skill.Execute();
}

// Multiple handles = multiple independent triggers per frame (additive)
IDisposable h1 = sm.TriggerEveryUpdate(new DotDamage(1));
IDisposable h2 = sm.TriggerEveryUpdate(new DotDamage(3));

// CompositeDisposable — batch release
var cd = new CompositeDisposable();
sm.TriggerEveryUpdate(new DotDamage(1)).AddTo(cd);
sm.TriggerEveryUpdate(new DotDamage(3)).AddTo(cd);
cd.Dispose();

// SerialDisposable — only one active at a time; assigning a new one disposes the previous
var sd = new SerialDisposable();
sd.Disposable = sm.TriggerEveryUpdate(new DotDamage(1));
sd.Disposable = sm.TriggerEveryUpdate(new DotDamage(3)); // previous auto-disposed
sd.Dispose();
```

---

## Async Callbacks: EnterStateAsync

Four policies:

| Policy | Behavior |
|---|---|
| `AsyncOperation.Throttle` | Blocks next transition out of the state until task completes. Only the most recent pending trigger is stored — intermediates are discarded. |
| `AsyncOperation.Switch` | Cancels the current task (via `CancellationToken`) when a new entry or exit occurs. |
| `AsyncOperation.Parallel` | No blocking, no cancellation. Runs independently. Only `ForceTransitionTo` / `Deactivate` / `Dispose` cancel it. |
| `AsyncOperation.Drop` | If a task is already running, new state entries are **silently discarded** — no queuing, no restart, no pending storage. |

**Policy priority when multiple subscriptions coexist on the same state: Drop → Throttle → Switch / Parallel.**

- While any **Drop** task is running: all incoming transitions out of that state are discarded (no pending stored).
- After all Drop tasks finish: **Throttle** resumes — transitions are now queued (last-wins), and pending fires when Throttle completes.
- Once Throttle releases: **Switch** and **Parallel** fire normally on the next transition.

**`ForceTransitionTo`, `Deactivate`, and `Dispose` cancel ALL active async tasks (including Drop) without exception.**

**Drop also blocks normal exit transitions** while its task is running (via the guard system). Use `ForceTransitionTo` to escape if needed.

```csharp
// Unfiltered — (cur, prev, ct) — fires on every state entry
sm.EnterStateAsync(
    async (TState cur, TState prev, CancellationToken ct) =>
    {
        await PlayAnimation(cur, ct);
    }, AsyncOperation.Throttle);

// Unfiltered with trigger — (cur, prev, trg, ct)
sm.EnterStateAsync(
    async (TState cur, TState prev, object trg, CancellationToken ct) =>
    {
        var d = (Damaged)trg;
        await PlayHitAsync(d.direction, ct);
    }, AsyncOperation.Switch);

// State-filtered — (prev, ct) — fires only when entering targetState
sm.EnterStateAsync(S.Attack,
    async (TState prev, CancellationToken ct) =>
    {
        await AttackAnimation(ct);
    }, AsyncOperation.Throttle);

// State + Trigger filtered — (prev, trg, ct) — fires only when entering targetState via TTrigger
sm.EnterStateAsync<CastSpell>(S.Casting,
    async (TState prev, object trg, CancellationToken ct) =>
    {
        var spell = (CastSpell)trg;
        mana -= spell.ManaCost;
        try   { await CastAsync(spell, ct); }
        catch (OperationCanceledException) { mana += spell.ManaCost * 0.5f; }
    }, AsyncOperation.Switch);
```

**All `EnterStateAsync` overloads return `IDisposable`.** Disposing cancels the running task immediately and unregisters the callback.

---

## Guard Interaction Rules

Guards block transitions out of a state. Multiple guards use **AND logic** — all must clear before the pending trigger fires.

| Guard | Pending behavior |
|---|---|
| `ThrottleState` | Stores last trigger while active; fires when timer expires |
| `HoldState` | Stores last trigger; fires when `waitUntil()` returns true |
| `AsyncOperation.Throttle` | Stores last trigger; fires when async task completes |
| `AsyncOperation.Drop` | **Discards** trigger (no pending stored); does NOT fire after task completes |

Only one pending trigger is stored per state (last-wins). Guards share this slot.

---

## Connect to External Events

```csharp
// Subscribe/unsubscribe via Action<Action<TTrigger>> delegates
// subscribe and unsubscribe are called ONCE at connection time
IDisposable conn = sm.Connect<Hit>(
    subscribe:   handler => boss.OnHit += handler,
    unsubscribe: handler => boss.OnHit -= handler);
conn.Dispose(); // calls unsubscribe

// Scoped
using (sm.Connect<Hit>(h => boss.OnHit += h, h => boss.OnHit -= h))
{
    await boss.Phase();
}
```

---

## Deactivate

Freezes the FSM. All async tasks are cancelled. All guards are reset. Triggers are ignored.

```csharp
IDisposable handle = sm.Deactivate();
handle.Dispose(); // resume

using (sm.Deactivate()) { await PlayCutscene(); }

// Reference-counted: FSM stays deactivated until every handle is disposed
IDisposable h1 = sm.Deactivate();
IDisposable h2 = sm.Deactivate(); // still deactivated
h1.Dispose();                     // still deactivated
h2.Dispose();                     // now active again
```

---

## TransitionFilter (Middleware)

`ITransitionFilter.Invoke` receives the trigger as a **boxed `object`** and the context as `TransitionContext` (fields `From` and `To` are `Enum` — cast as needed). Call `next()` to allow the transition; omit to block it.

```csharp
public class InvincibleFilter : ITransitionFilter
{
    public async ValueTask Invoke(
        object trigger, TransitionContext ctx,
        Func<ValueTask> next, CancellationToken ct)
    {
        if (trigger is Damaged && (CharState)ctx.From == CharState.Hit)
            return;         // block — do NOT call next()

        await next();       // allow the transition to proceed
    }
}

public class LogFilter : ITransitionFilter
{
    public async ValueTask Invoke(
        object trigger, TransitionContext ctx,
        Func<ValueTask> next, CancellationToken ct)
    {
        Debug.Log($"{ctx.From} → {ctx.To} via {trigger?.GetType().Name}");
        await next();
    }
}

FSM.Create<S>(S.Idle)
    .UseGlobalFilter(new LogFilter())          // applies to ALL transitions on this FSM
    .AddTransition<SkillCast>(S.Idle, S.Casting)
        .UseFilter(new ManaFilter())           // applies only to this transition; must chain directly after AddTransition
    .Build();
```

- If the filter is `async` (awaits something), the transition fires asynchronously — the call to `sm.Trigger()` returns immediately.
- If the filter is sync (no await), `await next()` runs synchronously and `sm.Trigger()` fully completes before returning.
- Stateless filters can be shared across multiple FSMs.
- `ct` is cancelled if another state change occurs before the filter completes.

---

## Interrupt

`IInterrupt.InvokeAsync` receives the current state as a **boxed `Enum`** — cast inside. Interrupts run on the active state and fire before any pending async tasks for that trigger.

```csharp
public readonly struct DashInterrupt : IInterrupt
{
    private readonly FSM<CharState> _sm;
    private readonly float _delay;

    public DashInterrupt(FSM<CharState> sm, float delay) { _sm = sm; _delay = delay; }

    public async ValueTask InvokeAsync(Enum currentState, CancellationToken ct)
    {
        if ((CharState)currentState == CharState.Channeling)
            await UniTask.Delay(TimeSpan.FromSeconds(_delay), cancellationToken: ct);

        if ((CharState)currentState == CharState.Stunned) return; // do nothing

        _sm.Trigger(new DashStart());
    }
}

sm.Interrupt(new DashInterrupt(sm, delay: 0.5f));
```

- If another Interrupt fires mid-await, the previous one's `ct` is cancelled.
- `currentState` reflects the leaf state of the active hierarchy (deepest active child).
- Interrupts are cancelled by any state transition or by `Deactivate` / `Dispose`.

---

## Nested FSM

```csharp
var groundSm = FSM.Create<GroundState>(GroundState.Idle)
    .AddTransition<MoveStarted>(from: GroundState.Idle, to: GroundState.Walk)
    .Build();

var aliveFsm = FSM.Create<AliveState>(AliveState.Ground)
    .Register(AliveState.Ground, groundSm)    // attach child — groundSm is owned by aliveFsm
    .Register(AliveState.Aerial, aerialSm)
    .AddTransition<Jump>(from: AliveState.Ground, to: AliveState.Aerial)
    .Build();

var characterFsm = FSM.Create<CharacterState>(CharacterState.Alive)
    .Register(CharacterState.Alive, aliveFsm)
    .AddTransition<Killed>(from: CharacterState.Alive, to: CharacterState.Death)
    .Build();
```

**Trigger propagation:** parent tries its own transitions first. If no match, the trigger is forwarded to the active child. Each layer is independent.

**History:** child FSMs always retain their internal state. Returning to a parent state resumes the child where it left off.

**Transition phases (per trigger) — exact order within each layer:**
1. **Tick cut** — active `TickState` loops stop (STAGE_TICKS unregistered), parent → child
2. **Cancellation** — guards reset, async CTS cancelled, active `Interrupt` cancelled, parent → child
3. **Exit** — `ExitState` callbacks fire, parent → child (deepest child exits last)
4. **State change** — `_current` enums updated, parent → child
5. **Enter** — `EnterState` / `EnterStateAsync` callbacks fire, parent → child
6. **Tick start** — new `TickState` loops register (first tick fires next frame), parent → child

Triggers queued during phases 1–6 (e.g. from an `ExitState` callback) are processed **after** the full 6-phase sequence completes.

**Hierarchical deactivation — propagates recursively to all descendants:**
```csharp
using (aliveFsm.Deactivate()) { await PlayCutscene(); }
// groundSm and aerialSm are also deactivated for the duration
```

**Query active hierarchy:**
```csharp
IReadOnlyList<Enum> states = characterFsm.GetActiveStateHierarchy();
// → [CharacterState.Alive, AliveState.Ground, GroundState.Run]
```

**Disposing a parent recursively disposes all registered children.**

---

## Access Control Interfaces

```csharp
IFSMObserver<TState>  observer   = sm; // drive-only:   Trigger, TriggerEveryUpdate, TransitionTo, ForceTransitionTo, Interrupt
IFSMObservable<TState> observable = sm; // observe-only: EnterState, ExitState, TickState, EnterStateAsync, State
IFSM<TState>           full       = sm; // both + Connect, Deactivate, AddTo, GetActiveStateHierarchy, OnError, OnDisposed
```

---

## UniTask Extensions (Optional)

```csharp
// Await entry into a specific state — resolves on next entry, cancels if FSM is disposed
await sm.ToUniTask(CharState.Attack, ct);

// Await any entry matching a predicate — (Current, Trigger)
await sm.ToUniTask(
    t => t.Current == CharState.Idle && t.Trigger is StunRelease,
    ct);
```

Both overloads cancel if:
- The provided `CancellationToken` is cancelled
- `sm.Dispose()` is called (resolves as cancelled)

---

## R3 Extension (Optional)

The extension is defined on `Observable<TTrigger>`, not on `sm`.

```csharp
// Bridge an R3 Observable stream into the FSM
someObservable
    .Select(v => new MoveTrigger(v))
    .Connect(sm)
    .AddTo(compositeDisposable);
```

---

## Error Handling

```csharp
sm.OnError = (Exception ex, object trigger, CallbackType type) =>
{
    if (ex is OperationCanceledException) return; // normal cancellation — ignore

    Debug.LogError($"[FSM] {type} | State: {string.Join(" → ", sm.GetActiveStateHierarchy())} | Trigger: {trigger} | {ex}");
    sm.ForceTransitionTo(S.Idle); // or sm.Dispose()
};
```

`CallbackType` values: `EnterState`, `ExitState`, `TickState`, `EnterStateAsync`.

---

## Lifetime Management

```csharp
sm.AddTo(gameObject);  // auto-dispose when the GameObject is destroyed

sm.Dispose();          // manual dispose
                       //   - all async tasks cancelled
                       //   - all callbacks unregistered
                       //   - further Trigger() calls are no-ops
                       //   - nested: recursively disposes all children

// OnDisposed — fires once when Dispose() is called
sm.OnDisposed += () => CleanupReferences();
```

---

## Common Patterns

**Conditional branch on same trigger — first passing condition wins:**
```csharp
.AddTransition<Damaged>(trg => hp - trg.amount > 0,  from: S.Idle, to: S.Hit)
.AddTransition<Damaged>(trg => hp - trg.amount <= 0, from: S.Idle, to: S.Dead)
```

**Reflect damage:**
```csharp
sm.EnterState<Damaged>((cur, prev, trg) =>
{
    var d = (Damaged)trg;
    if (hasReflectBuff)
        d.attacker.sm.Trigger(new Damaged(d.amount * 0.5f, -d.direction));
});
```

**Chain lightning:**
```csharp
public readonly record struct ChainLightning(float damage, int remainingChains);

sm.EnterState<ChainLightning>((cur, prev, trg) =>
{
    var c = (ChainLightning)trg;
    hp -= c.damage;
    if (c.remainingChains > 0)
        FindNearest()?.sm.Trigger(new ChainLightning(c.damage * 0.7f, c.remainingChains - 1));
});
```

**DoT scoped to skill duration:**
```csharp
using (sm.TriggerEveryUpdate(new DotDamage(3)))
{
    await skill.Execute();
}
```

**AoE ultimate cutscene:**
```csharp
battleFsm.EnterStateAsync<UltimateActivated>(BattleState.UltimateCutscene,
    async (TState prev, object trg, CancellationToken ct) =>
    {
        var u = (UltimateActivated)trg;
        using (var cd = new CompositeDisposable())
        {
            foreach (var ch in allCharacters)
                if (ch != u.caster) ch.sm.Deactivate().AddTo(cd);

            await PlayUltimateCutIn(u.caster, u.data, ct);
        }
        battleFsm.Trigger(new UltimateFinished());
    }, AsyncOperation.Throttle);
```

---

## API Quick Reference

| API | Notes |
|---|---|
| `FSM.Create<TState>(initial)` | Entry point. `TState` must be an `enum`. Returns `FSMBuilder<TState>`. |
| `Trigger(struct)` | Synchronous, immediate. |
| `TriggerEveryUpdate(struct)` | Per-frame at STAGE_TRIGGERS. Returns `IDisposable`. Multiple handles are additive. |
| `.Throttle(t)` | Extension on the handle returned by `TriggerEveryUpdate`. First fire is immediate. |
| `Connect<TTrigger>(subscribe, unsubscribe)` | Subscribe to external event. `Action<Action<TTrigger>>` delegates. |
| `EnterState(cur, prev)` | Unfiltered, every entry. |
| `EnterState(cur, prev, trg)` | Unfiltered with trigger (`object`). |
| `EnterState(targetState, (prev, trg) => {})` | State-filtered. |
| `EnterState<TTrigger>((cur, prev, trg) => {})` | Trigger-type-filtered (any state), `trg` is `object`. |
| `EnterState<TTrigger>(targetState, (prev, trg) => {})` | State + trigger filtered, `trg` is `object`. |
| `EnterState<TTrigger>(targetState, (prev, TTrigger trg) => {})` | State + trigger filtered, `trg` is typed `TTrigger`. |
| `ExitState(cur, next)` | Unfiltered exit. |
| `ExitState(targetState, (next, trg) => {})` | State-filtered exit — `trg` is `object`. |
| `ExitState<TTrigger>((cur, next, trg) => {})` | Trigger-type-filtered exit. |
| `ExitState<TTrigger>(targetState, (next, trg) => {})` | State + trigger filtered, `trg` is typed `TTrigger`. |
| `TickState(state, (prev, trg) => {})` | Per-frame at STAGE_TICKS. First tick is next frame after entry. `prev`/`trg` frozen at entry. |
| `EnterStateAsync(callback, policy)` | Async callback. Policies: `Throttle` / `Switch` / `Parallel` / `Drop`. |
| `ThrottleState(state, t)` | Block exit for `t` seconds. Last pending trigger fires when timer expires. |
| `ThrottleFrameState(state, n)` | Frame-based version of `ThrottleState`. |
| `HoldState(state, () => bool)` | Defer exit until condition is true. |
| `AutoTransition(from, to, time)` | Auto-transition after duration or on `Action<Action>` callback. |
| `TransitionTo(state)` | Direct transition, no trigger, no guards bypassed. |
| `ForceTransitionTo(state)` | Bypasses all guards. Cancels all async tasks, filters, interrupts. |
| `ForceTransition()` | Builder modifier. Must chain immediately after `AddTransition`. |
| `Deactivate()` | Pause. Reference-counted. Cancels all async tasks. Returns `IDisposable`. |
| `Interrupt(IInterrupt)` | Async branch on current state with auto CT. `InvokeAsync(Enum, ct)` — cast Enum. |
| `UseGlobalFilter(ITransitionFilter)` | Middleware on all transitions. `Invoke(object, ctx, next, ct)` — cast trigger. |
| `UseFilter(ITransitionFilter)` | Middleware on one transition. Must chain after `AddTransition`. |
| `AddTransitionFromAny<T>` | Transition from any state. |
| `Register(state, childFsm)` | Attach child FSM. Disposed with parent. |
| `GetActiveStateHierarchy()` | `IReadOnlyList<Enum>` — active states parent → child. |
| `AddTo(gameObject)` | Auto-dispose on `OnDestroy`. |
| `OnError` | `Action<Exception, object, CallbackType>`. Catches all callback exceptions. |
| `OnDisposed` | `Action`. Fires once on `Dispose()`. |
| `ToUniTask(state, ct)` | Await state entry. Cancels on FSM dispose. (UniTask ext) |
| `ToUniTask(pred, ct)` | Await entry matching `Func<(TState Current, object Trigger), bool>`. (UniTask ext) |
| `observable.Connect(sm)` | `Observable<TTrigger>` extension → bridge to FSM. (R3 ext) |
| `IFSMObserver<TState>` | Drive-only: `Trigger`, `TriggerEveryUpdate`, `TransitionTo`, `ForceTransitionTo`, `Interrupt`. |
| `IFSMObservable<TState>` | Observe-only: `EnterState`, `ExitState`, `TickState`, `EnterStateAsync`, `State`. |
