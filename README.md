# RxFSM: A library that lets you code like you're writing a game design document.

[한국어](README.ko.md) | [日本語](README.ja.md) | [MIT License](LICENSE)

**[API Reference & Detailed Guide](API_REFERENCE.md)** — Covers exact behavior, execution order, and usage patterns in depth. Handy for developers, and paste it into Claude, Gemini, or ChatGPT for accurate AI-assisted coding.

---
**You can code just like writing a game design document.**

```csharp
public readonly struct Damaged {  // The event "Damaged"
    public readonly float amount;   // must have an amount,
    public readonly Element element;  // an element type,
    public readonly Vector3 direction;  // and a direction.
// Looks just like a design spec, doesn't it?

      public Damaged(float amount, Element element, Vector3 direction) {
        this.amount = amount;
        this.element = element;
        this.direction = direction; }
}

// Trigger a new "Damaged" event: 50 damage, Fire element, from the hit direction.
sm.Trigger(new Damaged(50f, Element.Fire, hitDir));

sm.AddTransitionFromAny<Damaged> // When the "Damaged" event is triggered
(
      _ => !invincible, // if not invincible,
      to: CharState.Hit  // transition to the Hit state.
)

// Upon receiving damage // and entering the Hit state:
sm.EnterState<Damaged>(State.Hit, (prev, trg) =>
{
    switch (trg.element)  // Depending on the damage element,
    {   // play Fire, Ice, or Dark effects in that direction,
        case Element.Fire: SpawnFireEffect(trg.direction); break;
        case Element.Ice:  SpawnIceEffect(trg.direction);  break;
        case Element.Dark: SpawnDarkEffect(trg.direction); break;
    }
    hp -= trg.amount;  // and reduce HP by the damage amount.
});

```
**Can’t you just visualize the in-game scene just by looking at the code?**

```csharp
// Even more concise with C# 10+!
public readonly record struct Damaged(float amount, Element element, Vector3 direction);
```

---

**You can see at a glance why your character moves.**

```csharp
var sm = RxFSM.Create<CharState>(CharState.Idle)
                          
    .AddTransition<MoveStarted>  // When movement starts
    ( 
        from: CharState.Idle,    // from idle
        to: CharState.Walk       // to walk
    )
    .AddTransition<MoveStopped>  // When movement stops
    ( 
        from: CharState.Walk,    // from walk
        to: CharState.Idle       // to idle
    )
    .AddTransition<Sprint>       // When sprinting
    (
        trg => speed > 5f,       // and speed exceeds 5
        from: CharState.Walk,    // from walk
        to: CharState.Run        // to run
    )
    .AddTransition<MoveStopped>( from: CharState.Run,    to: CharState.Walk)
    .AddTransitionFromAny<Damaged>(                      to: CharState.Hit)
    .AddTransition<Recovered>(   from: CharState.Hit,    to: CharState.Idle)
    .Build();
```

Reads like a character spec sheet, doesn't it?

---

**Wizard interrupted mid-cast** — spell cancelled, half mana refunded.

```csharp
        // The CastSpell event fires    // and we've entered Casting state
sm.EnterStateAsync<CastSpell>(State.Casting, async (prev, trg, ct) =>
{
    if (trg.Spell == Spell.MeteorSwarm) // if the spell is Meteor Swarm
    {
        try
        {
            mana -= trg.ManaCost;  // spend the mana
            await PrepareMeteorSwarmAsync(trg.Target, trg.SkillLevel, ct); // begin the incantation
            CastMeteorSwarm(trg.Target, trg.Power, trg.SkillLevel); // and let it fly
        }
        catch(OperationCanceledException)  // if the cast is cancelled
        {
            mana += trg.ManaCost * 0.5f;  // refund half the mana
        } // reads like a scene from the game, doesn't it?
    }
}, TransitionOperation.Switch);
```

`TransitionOperation.Switch` cancels whatever is in progress when transitioning to a different state — by firing the **cancellation token**.

---

**Attack cooldown** — no complex timer logic, just one line

```csharp
var sm = RxFSM.Create<CharState>(CharState.Idle)
    .AddTransition<AttackInput>(from: CharState.Idle,   to: CharState.Attack)
    .AddTransition<AttackEnd>(  from: CharState.Attack, to: CharState.Idle)
    .ThrottleState(CharState.Attack, 0.5f)  // blocks the next transition for 0.5s after entering Attack
    .Build();
```

`ThrottleState` handles attack delay, hitstun, and cooldowns all at once.

---

**Install the core package** via Package Manager → **+** → **Add package from git URL**:

```
https://github.com/YoruYomix/RxFSM.git?path=com.yoruyomix.rxfsm
```

Or directly in `Packages/manifest.json`:

```json
"com.yoruyomix.rxfsm": "https://github.com/YoruYomix/RxFSM.git?path=com.yoruyomix.rxfsm"
```

---

**More complex attack cooldown** — Complex Conditions at a Glance

```csharp
sm.EnterStateAsync<AttackInput>(State.Attack, async (prev, trg, ct) =>
{
    await UniTask.WaitUntil(() => isGrounded, cancellationToken: ct);
    await UniTask.WaitWhile(() => isAttacking, cancellationToken: ct);
    
    if(trg.Power > 30 && prev == State.Idle)
        await UniTask.WaitUntil(() => atkCooldown <= 0, cancellationToken: ct);
	    
    await animationCompletionSource.Task;
    
}, TransitionOperation.Throttle); 
```

`TransitionOperation.Throttle` Prevents transition from State.Attack until all conditions above are met.


---

**Dialogue Box** — Complete text, whether cancelled or finished.

```csharp
dialogueSM.EnterStateAsync<PlayDialogue>(State.Play, async (prev, trg, ct) =>
{
    try
    {
        await TypeText(trg.Dialogue, trg.speed ,ct);
    }
    finally
    {
        text.text = trg.Dialogue;
        PlayCursorBlink();
    }
}, TransitionOperation.Switch);
```

---

**Skill effects that change depending on the terrain** — replacing the callback itself without complex branching.

```csharp
var handle1 = sm.EnterState<SkillCast>(State.SkillCast, (prev, trg) =>
    {
		UseSkillOnGroundTile(trg.Target, trg.SkillId);
    });

handle1.Dispose(); // No longer receive callbacks

handle1 = sm.EnterState<SkillCast>(State.SkillCast, (prev, trg) => 
    {
		UseSkillOnWaterTile(trg.Target, trg.SkillId);
    });
    
var handle2 = sm.EnterState<SkillCast>(State.SkillCast, (prev, trg) =>  
    {
		WaterTileSkillBonus(trg.Target, trg.SkillId);
    }); // Executes independently of handle1
```

`EnterState`  Dispose the handle returned upon callback reception, then receive a new callback

---

**Volatile design changes**

```csharp
// Designer: "Can we try the burst version?"
var handle = sm.EnterState<SkillCast>(State.Casting, (prev, trg) =>
    ApplyBurstEffect(trg.SkillId, trg.Target));

// Designer: "Actually, the DoT version feels better."
handle.Dispose();
handle = sm.EnterState<SkillCast>(State.Casting, (prev, trg) =>
    ApplyDotEffect(trg.SkillId, trg.Target));

// Designer: "Hmm. What if it's an AoE?"
handle.Dispose();
handle = sm.EnterState<SkillCast>(State.Casting, (prev, trg) =>
    ApplyAoeEffect(trg.SkillId, trg.Target));

// Designer: "...Let's go back to burst."
handle.Dispose();
handle = sm.EnterState<SkillCast>(State.Casting, (prev, trg) =>
    ApplyBurstEffect(trg.SkillId, trg.Target));
```

Just swap the handle. No if-chains, no version flags. One Dispose and the old logic is gone.

---

**Monster AI with Day/Night Behavior** — Swapping Ticks Without Complex Branching

```csharp
var handle = sm.TickState(Monster.Patrol, (prev, trg) =>
    {
        var cmd = (PatrolCommand)trg;
        DayPatrolLogic(cmd.LastPlayerPosition);
    });

handle.Dispose(); // Logic execution rights revoked immediately

handle = sm.TickState(Monster.Patrol, (prev, trg) =>
    {
        var cmd = (PatrolCommand)trg;
        NightPatrolLogic(cmd.LastPlayerPosition);
    });
```

`TickState`  Executes every frame while the state is active.

---

**Same trigger, different outcomes** — branching on HP.

```csharp
.AddTransition<Damaged>(trg => hp - trg.amount > 0,  from: CharState.Idle, to: CharState.Hit)
.AddTransition<Damaged>(trg => hp - trg.amount <= 0, from: CharState.Idle, to: CharState.Dead)
```

Conditions are evaluated directly from trigger data — no external variable lookups needed.

---

**Reflect Damage** — One Trigger Leads to Another

```csharp
sm.EnterState<Damaged>(State.Hit, (prev, trg) =>
{
    if (hasReflectBuff)
        trg.attacker.sm.Trigger(new Hit(
            damage: trg.damage * 0.5f,
            attacker: this,
            element: trg.element,
            direction: -trg.direction
        ));
});
```

Delivering a trigger containing who attacked, how, and with how much damage

---

**ChainLightning** — Bouncing Trigger

```csharp
public readonly record struct ChainLightning(float damage, int remainingChains);

sm.EnterState<ChainLightning>(State.Hit, (prev, trg) =>
{
    hp -= trg.damage;
    SpawnLightningEffect();

    if (trg.remainingChains > 0)
    {
        var nextTarget = FindNearestEnemy(except: this);
        nextTarget?.sm.Trigger(new ChainLightning(
            damage:          trg.damage * 0.7f,
            remainingChains: trg.remainingChains - 1
        ));
    }
});
```

---

**Boss Defeated** — Seamless Cleanup

```csharp
systemSm.ExitState<BossDefeated>(GameState.BossFight, (next, trg) =>
{
    HideBossHealthBar();

    StopBossBGM();

    bossManager.Cleanup();

    assetManager.Unload(trg.bossID);
    assetManager.Unload(trg.bossRoomID);
});
```

**`ExitState`** is executed before transitioning to the next state. Use it for UI and resource cleanup.

---

**Character Icons** — Thumbnail Swaps, NEW Badges, & Awakening Effects

```csharp
iconSm.EnterState(CharacterState.Undiscovered, (prev, trg) =>
{
    thumbnail.ShowSilhouette(); 
});

iconSm.EnterState(CharacterState.New, (prev, trg) =>
{
    thumbnail.ShowCharacter();
    thumbnail.ShowNewBadge(); 
    thumbnail.PlayGetAnimation();
});

iconSm.EnterState(CharacterState.Owned, (prev, trg) =>
{
    thumbnail.HideNewBadge();
});

iconSm.EnterState(CharacterState.Awakened, (prev, trg) =>
{
    thumbnail.ShowAwakeningEffect(); 
    thumbnail.ShowAwakenedCharacter();
});
```

---

**Dash Interrupt** — Per-state reactions, runtime-tunable, without touching the FSM.

```csharp
public readonly struct DashInterrupt : IInterrupt
{
    public async ValueTask InvokeAsync(TState currentState, CancellationToken ct)
    {
        if (currentState is CharState.Channeling)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(_delay), cancellationToken: ct);
        }

        if (currentState is CharState.Stunned) return;

        _sm.Trigger(new DashStart(_speed));
    }
}
```

```csharp
_sm.Interrupt(new DashInterrupt(sm: _sm, delay: 0.5f, speed: 20));
```

`Interrupt` reads the current state and branches accordingly — without touching the FSM definition. If another interrupt fires mid-await, `ct` cancels automatically.

---

**Invincibility frames** — define once, reuse across any number of characters.

```csharp
public class InvincibleFilter : ITransitionFilter
{
    public async ValueTask Invoke(
        object trigger, TransitionContext ctx,
        Func<ValueTask> next, CancellationToken ct)
    {
        if (trigger is Damaged && ctx.From == CharState.Hit)
            return; // invincible window — block the transition

        await next(); // everything else passes through
    }
}
```

```csharp
// share the same filter instance across characters
var invFilter = new InvincibleFilter();

playerSm .UseGlobalFilter(invFilter);
enemySm  .UseGlobalFilter(invFilter);
bossSm   .UseGlobalFilter(invFilter);
```

`UseGlobalFilter` applies middleware to every transition on that FSM. The filter decides whether to call `next()` (allow) or return without calling it (block). Stateless filters can be shared across as many FSMs as needed.

---

**Mana check** — per-transition filter, so only skill transitions pay the cost.

```csharp
public class ManaFilter : ITransitionFilter
{
    private readonly ManaPool _mana;
    public ManaFilter(ManaPool mana) { _mana = mana; }

    public async ValueTask Invoke(
        object trigger, TransitionContext ctx,
        Func<ValueTask> next, CancellationToken ct)
    {
        if (trigger is SkillCast skill && !_mana.IsSufficient(skill.ManaCost))
            return; // not enough mana — block silently

        await next();
    }
}
```

```csharp
var sm = FSM.Create<CharState>(CharState.Idle)
    .UseGlobalFilter(new LogFilter())             // runs on every transition — logging, analytics, etc.
    .AddTransition<SkillCast>(CharState.Idle,   CharState.Casting)
        .UseFilter(new ManaFilter(_mana))         // runs only on this transition
        .UseFilter(new CooldownFilter(_cooldown)) // multiple per-transition filters chain in order
    .AddTransition<AttackInput>(CharState.Idle, CharState.Attack)
        // no mana filter here — attack costs nothing
    .Build();
```

`UseGlobalFilter` runs on every transition — ideal for cross-cutting concerns like logging or telemetry. `UseFilter` runs only on the transition it follows, keeping expensive or context-specific checks out of the global path.

---

**Connect to any external system, in one line.**

```csharp
// Input System
sm.Connect(input.AttackPressedEvent);

// Behavior Tree
sm.Connect(bt.EnemyDetectedEvent);

// Apply to all characters in an AoE simultaneously
foreach (var ch in aoeTargets)
    ch.sm.Connect(skill.HitEvent).AddTo(composite);
```

Any system plugs in via `.Connect()`. The FSM doesn't need to reference them directly.

---

**Damage over time only while a skill is active** — cleans up automatically.

```csharp
using (sm.TriggerEveryUpdate(new DotDamage(3)))
{
    await skill.Execute();
}
// DoT stops automatically when the using block exits
```

---

**Fully pause the FSM** during cutscenes or boss phase transitions.

```csharp
using (sm.Deactivate())
{
    await PlayCutscene();
}
// Resumes automatically when the using block exits
```

---

**Await a specific state** in async flows.

```csharp
async UniTask Tutorial(CancellationToken ct)
{
    ShowGuide("Try attacking!");
    await playerFSM.ToUniTask(CharState.Attack, ct);

    ShowGuide("Now try dodging!");
    await playerFSM.ToUniTask(CharState.Dodge, ct);
}
```

---

**Hierarchical FSM** — remembers ground state across jumps and restores it on landing.

```csharp
var groundSm = RxFSM.Create<GroundState>(GroundState.Idle)
    .AddTransition<MoveStarted>(from: GroundState.Idle, to: GroundState.Run)
    .Build();

var characterFsm = RxFSM.Create<AliveState>(AliveState.Ground)
    .Register(AliveState.Ground, groundSm)
    .Register(AliveState.Aerial, aerialSm)
    .AddTransition<Jump>(   from: AliveState.Ground, to: AliveState.Aerial)
    .AddTransition<Landed>( from: AliveState.Aerial, to: AliveState.Ground)
    .Build();

// Jumping from Run → landing returns to Run automatically. No extra declarations needed.
```

---

**Efficient Parent-Child State Management**

```csharp
groundSm.EnterStateAsync(GroundState.Idle, async (prev, ct) => {

    await IdleDelay(ct);
}, TransitionOperation.Throttle);


groundSm.EnterStateAsync(async (cur, prev, ct) => {

    await IdleDelay(ct);
}, TransitionOperation.Throttle);
```

`TransitionOperation.Throttle` blocks the next transition out of that state until the async task completes.

---

**Error Handling and Debugging** — Error, its cause, and current state, all delivered at once.

```csharp
public enum CallbackType { EnterState, ExitState, TickState, EnterStateAsync }

sm.OnError = (ex, trg, type) =>
{
	if (ex is OperationCanceledException) return;
	
    if (ex is ArgumentException || ex is NullReferenceException) 
    { 
	    Debug.LogWarning($"[FSM] {type} | State: {string.Join(" → ", sm.GetActiveStateHierarchy())}, Trigger: {trg}, Exception: {ex}");  
	    sm.ForceTransitionTo(State.Idle);
	    return; 
    }
    
    Debug.LogError($"[FSM] {type} | State: {string.Join(" → ", sm.GetActiveStateHierarchy())}, Trigger: {trg}, Exception: {ex}");
    
    sm.Dispose();
    Destroy(gameObject);
};
```

`OnError` receives every exception from any callback — along with the trigger that caused it and which `CallbackType` it came from.

---

## Quick Start

### Define triggers

```csharp
public readonly record struct Damaged(float amount, Element element, Vector3 direction);
public readonly record struct Healed(float amount);
public readonly record struct AttackInput;
public readonly record struct AttackEnd;
```

### Build the FSM

```csharp
var sm = RxFSM.Create<CharState>(CharState.Idle)
    .AddTransition<AttackInput>(from: CharState.Idle,   to: CharState.Attack)
    .AddTransition<AttackEnd>(  from: CharState.Attack, to: CharState.Idle)
    .AddTransitionFromAny<Damaged>(_ => !invincible,    to: CharState.Hit)
    .AddTransition<Healed>(     from: CharState.Hit,    to: CharState.Idle)
    .AddTransitionFromAny<Killed>(                      to: CharState.Dead)
    .ThrottleState(CharState.Attack, 0.5f)
    .AutoTransition(from: CharState.Hit, to: CharState.Idle, time: hitClip.length)
    .Build();
```

### Fire triggers

```csharp
sm.Trigger(new Damaged(50f, Element.Fire, hitDir));
sm.Trigger(new AttackInput());
```

### React to states

```csharp
sm.EnterState<Damaged>(State.Hit, (prev, trg) =>
{
    hp -= trg.amount;
    SpawnEffect(trg.element, trg.direction);
});

sm.EnterState((prev, cur, trg) =>
{
    animator.Play(cur.ToString());
});
```

> **Note:** Callbacks only fire on transitions. The initial state set in `Create<>()` does not fire Enter — the first callback fires when the first `Trigger` or `TransitionTo` occurs.

### Lifecycle integration

```csharp
void Start()
{
    sm.AddTo(gameObject); // auto-disposes when the GameObject is destroyed
}
```

---
## Examples

### UI Interaction

```csharp
public readonly record struct PointerEnter;
public readonly record struct PointerExit;
public readonly record struct PointerDown;
public readonly record struct PointerUp(bool isPointerInside);

var sm = RxFSM.Create<UIState>(UIState.Normal)
    .AddTransition<PointerEnter>(from: UIState.Normal,  to: UIState.Hover  )
    .AddTransition<PointerExit>( from: UIState.Hover,   to: UIState.Normal )
    .AddTransition<PointerExit>( from: UIState.Pressed, to: UIState.Normal )
    .AddTransition<PointerDown>( from: UIState.Hover,   to: UIState.Pressed)
    .AddTransition<PointerUp>(trg => trg.isPointerInside,
                              from: UIState.Pressed,    to: UIState.Hover  )
    .AddTransition<PointerUp>(trg => !trg.isPointerInside,
                              from: UIState.Pressed,    to: UIState.Normal )
    .Build();

sm.EnterStateAsync(async (cur, prev, trg, ct) =>
    {
        await ScaleAnimation(cur, trg, ct);
    }, TransitionOperation.Switch)
    .AddTo(gameObject);
```

### Game Flow

```csharp
public readonly record GameStart(string id);
public readonly record Pause;
public readonly record Resume;
public readonly record PlayerDied;
public readonly record Retry;

var sm = RxFSM.Create<GameState>(GameState.Title)
    .AddTransition<GameStart>( from: GameState.Title,    to: GameState.Playing)
    .AddTransition<Pause>(     from: GameState.Playing,  to: GameState.Paused)
    .AddTransition<Resume>(    from: GameState.Paused,   to: GameState.Playing)
    .AddTransition<PlayerDied>(from: GameState.Playing,  to: GameState.GameOver)
    .AddTransition<Retry>(     from: GameState.GameOver, to: GameState.Title)
    .Build();

sm.EnterState(GameState.Playing, (prev, trg) =>
{
	if (trg is GameStart gameStart)
		sceneManager.Load(gameStart.id);
	if (trg is Resume)
		Time.timeScale = 1f
});
sm.EnterState(GameState.Paused,   (prev, trg) => Time.timeScale = 0f);
sm.EnterState(GameState.GameOver, (prev, trg) => ShowGameOverUI());
```

### Turn-Based Battle

```csharp
public record BattleReady;
public record CountdownEnd;
public record UltimateActivated(Character caster, UltimateData data);
public record UltimateFinished;
public record AllEnemyDead(Character finisher, float gameTime);
public record AllAllyDead(Character lastSurvivor, float gameTime);
public record TimeUp(float gameTime);
public record ResultConfirmed;

var battleFsm = RxFSM.Create<BattleState>(BattleState.Preparing)
    .AddTransition<BattleReady>(   from: BattleState.Preparing, to: BattleState.Countdown)
    .AddTransition<CountdownEnd>(  from: BattleState.Countdown, to: BattleState.Fighting)
    .AddTransition<UltimateActivated>(   from: BattleState.Fighting,  to: BattleState.UltimateCutscene)
    .AddTransition<UltimateFinished>(    from: BattleState.UltimateCutscene, to: BattleState.Fighting)
    .AddTransition<AllEnemyDead>(  from: BattleState.Fighting,  to: BattleState.Victory)
    .AddTransition<AllAllyDead>(   from: BattleState.Fighting,  to: BattleState.Defeat)
    .AddTransition<TimeUp>(        from: BattleState.Fighting,  to: BattleState.TimeOut)
    .AddTransition<ResultConfirmed>(from: BattleState.Victory,  to: BattleState.Result)
    .AddTransition<ResultConfirmed>(from: BattleState.Defeat,   to: BattleState.Result)
    .AddTransition<ResultConfirmed>(from: BattleState.TimeOut,  to: BattleState.Result)
    .Build();

battleFsm.EnterStateAsync<UltimateActivated>(BattleState.UltimateCutscene, async (prev, trg, ct) =>
{
	using(var handle = new FSMCompositeDisposable())
	{
	    foreach (var ch in allCharacters)
	        if (ch != trg.caster) ch.sm.Deactivate().AddTo(handle);
	
	    await PlayUltimateCutIn(trg.caster, trg.data, ct);
	    await PlayUltimateAnimation(trg.caster, trg.data, ct);
	}
	
    battleFsm.Trigger(new UltimateFinished());
}, TransitionOperation.Throttle);

battleFsm.EnterStateAsync<AllEnemyDead>(BattleState.Victory, async (prev, trg, ct) =>
{
    StopAllCharacterAI();
    ShowVictoryUI(trg.finisher, trg.gameTime);
    await PlayVictoryAnimation(trg.finisher, ct);
}, TransitionOperation.Switch);
```

---

**UniTask extension** — await state transitions in async flows.

Requires [UniTask](https://github.com/Cysharp/UniTask). Add via Package Manager → **+** → **Add package from git URL**:

```
https://github.com/YoruYomix/RxFSM.git?path=com.yoruyomix.rxfsm.unitask
```

```csharp
async UniTask Tutorial(CancellationToken ct)
{
    ShowGuide("Try attacking!");
    await playerFSM.ToUniTask(CharState.Attack, ct);

    ShowGuide("Now try dodging!");
    await playerFSM.ToUniTask(CharState.Dodge, ct);
}
```

`ToUniTask(state, ct)` completes the moment the FSM enters the target state. Pair it with `TransitionOperation.Switch` handlers to build step-by-step tutorial or cutscene flows without polling.

---

**R3 extension** — bridge R3 / UniRx streams directly into the FSM.

Requires [R3](https://github.com/Cysharp/R3). Add via Package Manager → **+** → **Add package from git URL**:

```
https://github.com/YoruYomix/RxFSM.git?path=com.yoruyomix.rxfsm.r3
```

```csharp
// Connect any IObservable<T> — no wrapper needed
sm.Connect(input.attackStream);      // IObservable<AttackInput>
sm.Connect(damageSystem.hitStream);  // IObservable<Damaged>
sm.Connect(network.commandStream);   // IObservable<NetworkCommand>
```

`Connect(IObservable<T>)` subscribes the stream and calls `Trigger` on each emission. Returns `IDisposable` — dispose to unsubscribe.

---

## API Overview

| Method                               | Description                                                                                                                                              |
| ------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Trigger(struct)`                    | Fire a trigger. Processed synchronously and immediately.                                                                                                 |
| `TriggerEveryUpdate(struct)`         | Fire a trigger every frame. Managed as `IDisposable`.                                                                                                    |
| `Connect(Action<T> event)`           | Subscribe to an external event source.                                                                                                                   |
| `EnterState / ExitState`             | Callbacks on state entry and exit.                                                                                                                       |
| `TickState(state, callback)`         | Execute a callback every frame while in the specified state. Managed as `IDisposable`.                                                                   |
| `EnterStateAsync`                    | Async callback on state entry. Supports Throttle, Switch, Parallel, and Drop policies. Can be filtered by state and/or trigger type.                     |
| `ThrottleState(state, time)`         | Block further transitions for a duration after entering a state. Intermediate transitions are discarded; only the final one executes.                    |
| `ThrottleFrameState(state, frames)`  | Frame-based version of ThrottleState.                                                                                                                    |
| `HoldState(state, waitUntil)`        | Defer transition out of a state until a condition is met. Pending transitions execute once the condition is satisfied.                                   |
| `AutoTransition(from, to, time)`     | Automatically transition after a set duration or clip completion.                                                                                        |
| `ForceTransitionTo(state)`           | Bypass all guards: TransitionOperation.Throttle, TransitionFilter, ThrottleState, ThrottleFrameState, and HoldState.                                             |
| `TransitionTo(state)`                | Directly transition to a specified state.                                                                                                                |
| `Deactivate()`                       | Pause the FSM. Resume by disposing the returned handle.                                                                                                  |
| `Interrupt(IInterrupt)`              | Inject branching async logic based on current state, with cancellation support.                                                                          |
| `UseGlobalFilter(ITransitionFilter)` | Apply middleware to all transitions.                                                                                                                     |
| `UseFilter(ITransitionFilter)`       | Apply middleware to a specific transition.                                                                                                               |
| `AddTransitionFromAny<T>`            | Register a transition from any state for the given trigger.                                                                                              |
| `Register(state, childFsm)`          | Attach a child FSM for hierarchical state machines.                                                                                                      |
| `GetActiveStateHierarchy()`          | Query the currently active state hierarchy (parent → child order).                                                                                       |
| `AddTo(gameObject)`                  | Automatically dispose the FSM when the GameObject is destroyed.                                                                                          |
| `ToUniTask(state, ct)`               | Await entry into a specific state. (UniTask extension)                                                                                                   |
| `ToUniTask().WithTrigger()`          | Await a state transition matching a specific trigger condition. (UniTask extension)                                                                      |
| `Connect(IObservable<T>)`            | Subscribe to a UniRx / R3 stream. (extension)                                                                                                            |
| `TriggerEveryUpdate().Throttle(t)`   | Set the interval between repeated trigger firings (e.g., once every `t` seconds).                                                                        |
| `IFSMObserver`                       | Restricted interface: trigger-only access (`Trigger`, `Interrupt`, `TransitionTo`, etc.). Prevents subscribers from driving state. (Advanced)            |
| `IFSMObservable`                     | Restricted interface: observe-only access (`EnterState`, `ExitState`, `TickState`, `EnterStateAsync`). Prevents observers from driving state. (Advanced) |

---

## License

MIT License
