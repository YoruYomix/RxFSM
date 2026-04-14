# RxFSM: 트리거가 데이터를 직접 운반하고, 모든 것이 그에 반응하는 Unity 상태 머신.

[English](README.md) | [日本語](README.ja.md) | [MIT License](LICENSE)

**[API 레퍼런스 & 상세 가이드](API_REFERENCE.md)** — 정확한 동작 순서, 사용 패턴, 예문을 담은 상세 매뉴얼입니다. 직접 참고하거나, Claude·Gemini·ChatGPT에 붙여넣으면 AI가 정확한 코드를 생성할 수 있습니다.

---

## 이런 것들이 가능해집니다

**공격 속성에 따라 효과 분기** — 트리거 자체가 데이터를 운반하기 때문입니다.

```csharp
// C# < 10
public readonly struct Damaged {
    public readonly float amount;
    public readonly Element element;
    public readonly Vector3 direction;

    public Damaged(float amount, Element element, Vector3 direction) {
        this.amount = amount;
        this.element = element;
        this.direction = direction; }
}

// C# 10+
public readonly record struct Damaged(float amount, Element element, Vector3 direction);
```

```csharp
sm.EnterState<Damaged>((cur, prev, trg) =>
{
    switch (trg.element)
    {
        case Element.Fire: SpawnFireEffect(trg.direction); break;
        case Element.Ice:  SpawnIceEffect(trg.direction);  break;
        case Element.Dark: SpawnDarkEffect(trg.direction); break;
    }
    hp -= trg.amount;
});
```

기존 방식은 트리거와 데이터를 분리해서 관리해야 했습니다. `lastDamageType` 같은 전역 변수에 저장해두고 타이밍이 맞기를 바라는 방식이었죠.

---

**캐릭터가 왜 움직이는지 한눈에 파악됩니다.**

```csharp
var sm = RxFSM.Create<CharState>(CharState.Idle)
    .AddTransition<MoveStarted>( from: CharState.Idle,   to: CharState.Walk)
    .AddTransition<MoveStopped>( from: CharState.Walk,   to: CharState.Idle)
    .AddTransition<Sprint>(trg => speed > 5f,
                                 from: CharState.Walk,   to: CharState.Run)
    .AddTransition<MoveStopped>( from: CharState.Run,    to: CharState.Walk)
    .AddTransitionFromAny<Damaged>(                      to: CharState.Hit)
    .AddTransition<Recovered>(   from: CharState.Hit,    to: CharState.Idle)
    .Build();
```

`RxFSM.Create`는 **전이 테이블**입니다. 모든 전이를 한 곳에, 한눈에 읽을 수 있습니다.

---

**코어 패키지 설치** — Package Manager → **+** → **Add package from git URL**:

```
https://github.com/YoruYomix/RxFSM.git?path=com.yoruyomix.rxfsm
```

또는 `Packages/manifest.json`에 직접 추가:

```json
"com.yoruyomix.rxfsm": "https://github.com/YoruYomix/RxFSM.git?path=com.yoruyomix.rxfsm"
```

---

**공격 쿨다운** — 한 줄로.

```csharp
var sm = RxFSM.Create<CharState>(CharState.Idle)
    .AddTransition<AttackInput>(from: CharState.Idle,   to: CharState.Attack)
    .AddTransition<AttackEnd>(  from: CharState.Attack, to: CharState.Idle)
    .ThrottleState(CharState.Attack, 0.5f)  // Attack 진입 후 0.5초간 다음 전이 차단
    .Build();
```

`ThrottleState`가 공격 딜레이, 히트스턴, 쿨다운을 한 번에 처리합니다.

---

**복잡한 공격 쿨다운** — 복잡한 조건도 한눈에

```csharp
sm.EnterStateAsync<AttackInput>(State.Attack, async (prev, trg, ct) =>
{
    await UniTask.WaitUntil(() => isGrounded, cancellationToken: ct);
    await UniTask.WaitWhile(() => isAttacking, cancellationToken: ct);

    if(trg.Power > 30 && prev == State.Idle)
        await UniTask.WaitUntil(() => atkCooldown <= 0, cancellationToken: ct);

    await animationCompletionSource.Task;

}, AsyncOperation.Throttle);
```

`AsyncOperation.Throttle`은 위 조건이 모두 충족될 때까지 State.Attack에서의 전이를 막습니다.

---

**영창 도중 방해받는 마법사** — 취소 시 마나 절반 환불.

```csharp
sm.EnterStateAsync<CastSpell>(State.Casting, async (prev, trg, ct) =>
{
    if (trg.Spell == Spell.MeteorSwarm)
    {
        try
        {
            mana -= trg.ManaCost;
            await PrepareMeteorSwarmAsync(trg.Target, trg.SkillLevel, ct);
            CastMeteorSwarm(trg.Target, trg.Power, trg.SkillLevel);
        }
        catch(OperationCanceledException)
        {
            mana += trg.ManaCost * 0.5f;
        }
    }
}, AsyncOperation.Switch);
```

`AsyncOperation.Switch`는 다른 상태로 전이할 때 **캔슬레이션 토큰**을 자동으로 발동합니다.

---

**다이얼로그 박스** — 취소되든 완료되든 텍스트를 완성합니다.

```csharp
dialogueSM.EnterStateAsync<PlayDialogue>(State.Play, async (prev, trg, ct) =>
{
    try
    {
        await TypeText(trg.Dialogue, trg.speed, ct);
    }
    finally
    {
        text.text = trg.Dialogue;
        PlayCursorBlink();
    }
}, AsyncOperation.Switch);
```

---

**지형에 따라 달라지는 스킬 효과** — 복잡한 분기 없이 콜백 자체를 교체.

```csharp
var handle1 = sm.EnterState<SkillCast>(State.SkillCast, (prev, trg) =>
    {
        UseSkillOnGroundTile(trg.Target, trg.SkillId);
    });

handle1.Dispose(); // 더 이상 콜백을 받지 않음

handle1 = sm.EnterState<SkillCast>(State.SkillCast, (prev, trg) =>
    {
        UseSkillOnWaterTile(trg.Target, trg.SkillId);
    });

var handle2 = sm.EnterState<SkillCast>(State.SkillCast, (prev, trg) =>
    {
        WaterTileSkillBonus(trg.Target, trg.SkillId);
    }); // handle1과 독립적으로 실행
```

`EnterState` 콜백 수신 시 반환된 핸들을 Dispose하고, 새 콜백을 등록하세요.

---

**낮/밤 행동 패턴을 가진 몬스터 AI** — 복잡한 분기 없이 틱 교체

```csharp
var handle = sm.TickState(Monster.Patrol, (prev, trg) =>
    {
        var cmd = (PatrolCommand)trg;
        DayPatrolLogic(cmd.LastPlayerPosition);
    });

handle.Dispose(); // 즉시 로직 실행 권한 회수

handle = sm.TickState(Monster.Patrol, (prev, trg) =>
    {
        var cmd = (PatrolCommand)trg;
        NightPatrolLogic(cmd.LastPlayerPosition);
    });
```

`TickState`는 해당 상태가 활성화된 동안 매 프레임 실행됩니다.

---

**같은 트리거, 다른 결과** — HP에 따른 분기.

```csharp
.AddTransition<Damaged>(trg => hp - trg.amount > 0,  from: CharState.Idle, to: CharState.Hit)
.AddTransition<Damaged>(trg => hp - trg.amount <= 0, from: CharState.Idle, to: CharState.Dead)
```

조건은 트리거 데이터에서 직접 평가됩니다. 외부 변수 조회가 필요 없습니다.

---

**피해 반사** — 트리거 하나가 또 다른 트리거를 유발

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

누가, 어떻게, 얼마나 공격했는지 데이터를 담아 트리거로 전달합니다.

---

**연쇄 번개** — 튕기는 트리거

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

**보스 처치** — 깔끔한 정리

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

**`ExitState`**는 다음 상태로 전이하기 전에 실행됩니다. UI와 리소스 정리에 사용하세요.

---

**캐릭터 아이콘** — 썸네일 교체, NEW 뱃지, 각성 효과

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

**대쉬 인터럽트** — FSM 정의를 건드리지 않고, 상태별 반응을 런타임에 조정.

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

`Interrupt`는 현재 상태를 읽고 그에 맞게 분기합니다. FSM 정의를 수정할 필요가 없습니다. await 도중 다른 인터럽트가 발동되면 `ct`가 자동으로 취소됩니다.

---

**무적 프레임** — 한 번 정의해서 여러 캐릭터에 재사용.

```csharp
public class InvincibleFilter : ITransitionFilter
{
    public async ValueTask Invoke(
        object trigger, TransitionContext ctx,
        Func<ValueTask> next, CancellationToken ct)
    {
        if (trigger is Damaged && ctx.From == CharState.Hit)
            return; // 무적 구간 — 전이 차단

        await next(); // 그 외는 통과
    }
}
```

```csharp
// 동일한 필터 인스턴스를 여러 캐릭터가 공유
var invFilter = new InvincibleFilter();

playerSm .UseGlobalFilter(invFilter);
enemySm  .UseGlobalFilter(invFilter);
bossSm   .UseGlobalFilter(invFilter);
```

`UseGlobalFilter`는 해당 FSM의 모든 전이에 미들웨어를 적용합니다. 필터가 `next()`를 호출하면 허용, 호출하지 않으면 차단입니다. 상태가 없는 필터는 FSM 수만큼 공유할 수 있습니다.

---

**마나 체크** — 전이별 필터로, 스킬 전이만 비용을 부담.

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
            return; // 마나 부족 — 조용히 차단

        await next();
    }
}
```

```csharp
var sm = FSM.Create<CharState>(CharState.Idle)
    .UseGlobalFilter(new LogFilter())             // 모든 전이에 실행 — 로그, 분석 등
    .AddTransition<SkillCast>(CharState.Idle,   CharState.Casting)
        .UseFilter(new ManaFilter(_mana))         // 이 전이에만 실행
        .UseFilter(new CooldownFilter(_cooldown)) // 여러 필터가 순서대로 체인
    .AddTransition<AttackInput>(CharState.Idle, CharState.Attack)
        // 공격에는 마나 필터 없음
    .Build();
```

`UseGlobalFilter`는 모든 전이에 실행됩니다. 로그나 텔레메트리 같은 횡단 관심사에 적합합니다. `UseFilter`는 바로 앞 전이에만 실행되어 비용이 큰 검사를 전역 경로에서 제외합니다.

---

**외부 시스템 연결 — 한 줄로.**

```csharp
// Input System
sm.Connect(input.AttackPressedEvent);

// Behavior Tree
sm.Connect(bt.EnemyDetectedEvent);

// AoE 범위 내 모든 캐릭터에 동시 적용
foreach (var ch in aoeTargets)
    ch.sm.Connect(skill.HitEvent).AddTo(composite);
```

어떤 시스템이든 `.Connect()`로 연결됩니다. FSM이 외부 시스템을 직접 참조할 필요가 없습니다.

---

**스킬 활성화 중에만 지속 피해** — 자동으로 정리됩니다.

```csharp
using (sm.TriggerEveryUpdate(new DotDamage(3)))
{
    await skill.Execute();
}
// using 블록이 끝나면 DoT가 자동으로 멈춥니다
```

---

**FSM 완전 일시 정지** — 컷씬이나 보스 페이즈 전환 중.

```csharp
using (sm.Deactivate())
{
    await PlayCutscene();
}
// using 블록이 끝나면 자동으로 재개됩니다
```

---

**비동기 흐름에서 특정 상태 대기.**

```csharp
async UniTask Tutorial(CancellationToken ct)
{
    ShowGuide("공격을 해보세요!");
    await playerFSM.ToUniTask(CharState.Attack, ct);

    ShowGuide("이번엔 회피해보세요!");
    await playerFSM.ToUniTask(CharState.Dodge, ct);
}
```

---

**계층 FSM** — 점프 전 지상 상태를 기억하고, 착지 시 복원합니다.

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

// Run 중 점프 → 착지 시 Run으로 자동 복원. 추가 선언 불필요.
```

---

**효율적인 부모-자식 상태 관리**

```csharp
groundSm.EnterStateAsync(GroundState.Idle, async (prev, ct) => {

    await IdleDelay(ct);
}, AsyncOperation.Throttle);


groundSm.EnterStateAsync(async (cur, prev, ct) => {

    await IdleDelay(ct);
}, AsyncOperation.Throttle);
```

`AsyncOperation.Throttle`은 비동기 작업이 완료될 때까지 해당 상태에서의 다음 전이를 차단합니다.

---

**에러 처리와 디버깅** — 에러, 원인, 현재 상태를 한꺼번에 전달.

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

`OnError`는 모든 콜백에서 발생하는 예외를 수신합니다. 예외를 유발한 트리거와 `CallbackType`도 함께 전달됩니다.

---

## 빠른 시작

### 트리거 정의

```csharp
public readonly record struct Damaged(float amount, Element element, Vector3 direction);
public readonly record struct Healed(float amount);
public readonly record struct AttackInput;
public readonly record struct AttackEnd;
```

### FSM 빌드

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

### 트리거 발동

```csharp
sm.Trigger(new Damaged(50f, Element.Fire, hitDir));
sm.Trigger(new AttackInput());
```

### 상태 반응

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

### 라이프사이클 연동

```csharp
void Start()
{
    sm.AddTo(gameObject); // GameObject가 파괴될 때 자동으로 Dispose
}
```

---

## 예제

### UI 인터랙션

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
    }, AsyncOperation.Switch)
    .AddTo(gameObject);
```

### 게임 플로우

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
        Time.timeScale = 1f;
});
sm.EnterState(GameState.Paused,  (prev, trg) => Time.timeScale = 0f);
sm.EnterState(GameState.GameOver, (prev, trg) => ShowGameOverUI());
```

### 턴제 배틀

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
    .AddTransition<BattleReady>(      from: BattleState.Preparing,        to: BattleState.Countdown)
    .AddTransition<CountdownEnd>(     from: BattleState.Countdown,        to: BattleState.Fighting)
    .AddTransition<UltimateActivated>(from: BattleState.Fighting,         to: BattleState.UltimateCutscene)
    .AddTransition<UltimateFinished>( from: BattleState.UltimateCutscene, to: BattleState.Fighting)
    .AddTransition<AllEnemyDead>(     from: BattleState.Fighting,         to: BattleState.Victory)
    .AddTransition<AllAllyDead>(      from: BattleState.Fighting,         to: BattleState.Defeat)
    .AddTransition<TimeUp>(           from: BattleState.Fighting,         to: BattleState.TimeOut)
    .AddTransition<ResultConfirmed>(  from: BattleState.Victory,          to: BattleState.Result)
    .AddTransition<ResultConfirmed>(  from: BattleState.Defeat,           to: BattleState.Result)
    .AddTransition<ResultConfirmed>(  from: BattleState.TimeOut,          to: BattleState.Result)
    .Build();

battleFsm.EnterStateAsync<UltimateActivated>(BattleState.UltimateCutscene, async (prev, trg, ct) =>
{
    using(var handle = new CompositeDisposable())
    {
        foreach (var ch in allCharacters)
            if (ch != trg.caster) ch.sm.Deactivate().AddTo(handle);

        await PlayUltimateCutIn(trg.caster, trg.data, ct);
        await PlayUltimateAnimation(trg.caster, trg.data, ct);
    }

    battleFsm.Trigger(new UltimateFinished());
}, AsyncOperation.Throttle);

battleFsm.EnterStateAsync<AllEnemyDead>(BattleState.Victory, async (prev, trg, ct) =>
{
    StopAllCharacterAI();
    ShowVictoryUI(trg.finisher, trg.gameTime);
    await PlayVictoryAnimation(trg.finisher, ct);
}, AsyncOperation.Switch);
```

---

**UniTask 확장** — 비동기 흐름에서 상태 전이 대기.

[UniTask](https://github.com/Cysharp/UniTask) 설치 후 Package Manager → **+** → **Add package from git URL**:

```
https://github.com/YoruYomix/RxFSM.git?path=com.yoruyomix.rxfsm.unitask
```

```csharp
async UniTask Tutorial(CancellationToken ct)
{
    ShowGuide("공격을 해보세요!");
    await playerFSM.ToUniTask(CharState.Attack, ct);

    ShowGuide("이번엔 회피해보세요!");
    await playerFSM.ToUniTask(CharState.Dodge, ct);
}
```

`ToUniTask(state, ct)`는 FSM이 목표 상태에 진입하는 순간 완료됩니다. `AsyncOperation.Switch` 핸들러와 함께 사용하면 폴링 없이 단계별 튜토리얼이나 컷씬 흐름을 구성할 수 있습니다.

---

**R3 확장** — R3 / UniRx 스트림을 FSM에 직접 연결.

[R3](https://github.com/Cysharp/R3) 설치 후 Package Manager → **+** → **Add package from git URL**:

```
https://github.com/YoruYomix/RxFSM.git?path=com.yoruyomix.rxfsm.r3
```

```csharp
// IObservable<T>를 직접 연결 — 래퍼 불필요
sm.Connect(input.attackStream);      // IObservable<AttackInput>
sm.Connect(damageSystem.hitStream);  // IObservable<Damaged>
sm.Connect(network.commandStream);   // IObservable<NetworkCommand>
```

`Connect(IObservable<T>)`는 스트림을 구독하고 각 발행 시마다 `Trigger`를 호출합니다. `IDisposable`을 반환하며, Dispose하면 구독이 해제됩니다.

---

## API 개요

| 메서드 | 설명 |
| --- | --- |
| `Trigger(struct)` | 트리거 발동. 동기적으로 즉시 처리됩니다. |
| `TriggerEveryUpdate(struct)` | 매 프레임 트리거 발동. `IDisposable`로 관리됩니다. |
| `Connect(Action<T> event)` | 외부 이벤트 소스 구독. |
| `EnterState / ExitState` | 상태 진입/이탈 콜백. |
| `TickState(state, callback)` | 지정 상태 활성화 중 매 프레임 콜백 실행. `IDisposable`로 관리됩니다. |
| `EnterStateAsync` | 상태 진입 시 비동기 콜백. Throttle, Switch, Parallel, Drop 정책 지원. |
| `ThrottleState(state, time)` | 상태 진입 후 지정 시간 동안 다음 전이 차단. 중간 전이는 버려지고 마지막 것만 실행됩니다. |
| `ThrottleFrameState(state, frames)` | ThrottleState의 프레임 기반 버전. |
| `HoldState(state, waitUntil)` | 조건이 충족될 때까지 상태 이탈 지연. |
| `AutoTransition(from, to, time)` | 지정 시간 후 또는 콜백 완료 시 자동 전이. |
| `ForceTransitionTo(state)` | 모든 가드 우회. AsyncOperation.Throttle, TransitionFilter, ThrottleState 등 무시. |
| `TransitionTo(state)` | 지정 상태로 직접 전이. |
| `Deactivate()` | FSM 일시 정지. 반환된 핸들을 Dispose하면 재개됩니다. |
| `Interrupt(IInterrupt)` | 현재 상태 기반 비동기 분기 로직 삽입. 취소 지원. |
| `UseGlobalFilter(ITransitionFilter)` | 모든 전이에 미들웨어 적용. |
| `UseFilter(ITransitionFilter)` | 특정 전이에 미들웨어 적용. |
| `AddTransitionFromAny<T>` | 어떤 상태에서든 해당 트리거에 대한 전이 등록. |
| `Register(state, childFsm)` | 계층 FSM용 자식 FSM 연결. |
| `GetActiveStateHierarchy()` | 현재 활성 상태 계층 조회 (부모→자식 순서). |
| `AddTo(gameObject)` | GameObject 파괴 시 FSM 자동 Dispose. |
| `ToUniTask(state, ct)` | 특정 상태 진입 대기. (UniTask 확장) |
| `ToUniTask().WithTrigger()` | 특정 트리거 조건에 맞는 상태 전이 대기. (UniTask 확장) |
| `Connect(IObservable<T>)` | UniRx / R3 스트림 구독. (확장) |
| `IFSMObserver` | 제한 인터페이스: 트리거 전용 접근. (고급) |
| `IFSMObservable` | 제한 인터페이스: 관찰 전용 접근. (고급) |

---

## 라이센스

MIT License
