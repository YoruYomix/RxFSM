# RxFSM: 企画書を書くようにコーディングできるライブラリ

[English](README.md) | [한국어](README.ko.md) | [MIT License](LICENSE)

**[APIリファレンス & 詳細ガイド](API_REFERENCE.md)** — 正確な動作順序・使用パターン・サンプルコードを網羅した詳細マニュアルです。自分で参照するのはもちろん、Claude・Gemini・ChatGPTに貼り付ければ、AIが正確なコードを生成できます.

---

```csharp
      // ダメージを受けて    // Hit状態になったなら
sm.EnterState<Damaged>(State.Hit, (prev, trg) =>
{
    switch (trg.element)  // ダメージの属性に応じて
    {   // 炎・氷・闇属性のエフェクトをこの方向に再生して
        case Element.Fire: SpawnFireEffect(trg.direction); break;
        case Element.Ice:  SpawnIceEffect(trg.direction);  break;
        case Element.Dark: SpawnDarkEffect(trg.direction); break;
    }
    hp -= trg.amount;  // ダメージ分だけ体力を減らす
});

```
**まるで企画書を書くようにコーディングできます。**

---

**キャラクターがなぜ動くのか、一目でわかります.**

```csharp
var sm = RxFSM.Create<CharState>(CharState.Idle)
                          
    .AddTransition<MoveStarted>  // 動き始めたら
    ( 
        from: CharState.Idle,    // 通常状態から
        to: CharState.Walk       // 歩きへ
    )
    .AddTransition<MoveStopped>  // 動きが止まったら
    ( 
        from: CharState.Walk,    // 歩きから
        to: CharState.Idle       // 通常状態へ
    )
    .AddTransition<Sprint>       // ダッシュしたとき
    (
        trg => speed > 5f,       // 速度が5を超えていたら
        from: CharState.Walk,    // 歩きから
        to: CharState.Run        // 走りへ
    )
    .AddTransition<MoveStopped>( from: CharState.Run,    to: CharState.Walk)
    .AddTransitionFromAny<Damaged>(                      to: CharState.Hit)
    .AddTransition<Recovered>(   from: CharState.Hit,    to: CharState.Idle)
    .Build();
```

キャラクターの動きが、まるで企画書を見ているように浮かびませんか？

---

**ダメージを受けるというゲーム内イベント**も、企画書を書くようにコーディングできます

```csharp
// C# < 10
public readonly struct Damaged {  // 「ダメージを受けた」というイベント
    public readonly float amount;     // ダメージ量
    public readonly Element element;  // 属性
    public readonly Vector3 direction;  // 方向

    public Damaged(float amount, Element element, Vector3 direction) {
        this.amount = amount;
        this.element = element;
        this.direction = direction; }
}

// C# 10+ ならさらに簡潔に書けます！
public readonly record struct Damaged(float amount, Element element, Vector3 direction);
```

---

**攻撃クールダウン** — 複雑なタイマー処理なしに一行で

```csharp
var sm = RxFSM.Create<CharState>(CharState.Idle)
    .AddTransition<AttackInput>(from: CharState.Idle,   to: CharState.Attack)
    .AddTransition<AttackEnd>(  from: CharState.Attack, to: CharState.Idle)
    .ThrottleState(CharState.Attack, 0.5f)  // Attack進入後0.5秒間、次の遷移をブロック
    .Build();
```

`ThrottleState` が攻撃ディレイ、ヒットスタン、クールダウンをまとめて処理します。

---

**詠唱中に妨害された魔法使い** — キャンセル時にマナ半分還元.

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

`AsyncOperation.Switch` は別の状態に遷移するとき、**キャンセルトークン**を自動的に発動します。

---

---

**コアパッケージのインストール** — Package Manager → **+** → **Add package from git URL**:

```
https://github.com/YoruYomix/RxFSM.git?path=com.yoruyomix.rxfsm
```

または `Packages/manifest.json` に直接追加:

```json
"com.yoruyomix.rxfsm": "https://github.com/YoruYomix/RxFSM.git?path=com.yoruyomix.rxfsm"
```

---

**複雑な攻撃クールダウン** — 複雑な条件も一目瞭然

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

`AsyncOperation.Throttle` は、上記の条件がすべて満たされるまで State.Attack からの遷移を防ぎます。

---

**ダイアログボックス** — キャンセルされても完了しても、テキストを完成させます.

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

**地形によって変わるスキル演出** — 複雑な分岐なしにコールバック自体を差し替え.

```csharp
var handle1 = sm.EnterState<SkillCast>(State.SkillCast, (prev, trg) =>
    {
        UseSkillOnGroundTile(trg.Target, trg.SkillId);
    });

handle1.Dispose(); // コールバックの受信を停止

handle1 = sm.EnterState<SkillCast>(State.SkillCast, (prev, trg) =>
    {
        UseSkillOnWaterTile(trg.Target, trg.SkillId);
    });

var handle2 = sm.EnterState<SkillCast>(State.SkillCast, (prev, trg) =>
    {
        WaterTileSkillBonus(trg.Target, trg.SkillId);
    }); // handle1とは独立して実行
```

`EnterState` コールバック受信時に返されたハンドルをDisposeし、新しいコールバックを登録します。

---

**気まぐれな仕様変更**

```csharp
// 企画者: 「バースト版でお願いします。」
var handle = sm.EnterState<SkillCast>(State.Casting, (prev, trg) =>
    ApplyBurstEffect(trg.SkillId, trg.Target));

// 企画者: 「やっぱりDoT版の方がいいかも。」
handle.Dispose();
handle = sm.EnterState<SkillCast>(State.Casting, (prev, trg) =>
    ApplyDotEffect(trg.SkillId, trg.Target));

// 企画者: 「AoEはどうでしょう？」
handle.Dispose();
handle = sm.EnterState<SkillCast>(State.Casting, (prev, trg) =>
    ApplyAoeEffect(trg.SkillId, trg.Target));

// 企画者: 「……やっぱりバーストに戻しましょう。」
handle.Dispose();
handle = sm.EnterState<SkillCast>(State.Casting, (prev, trg) =>
    ApplyBurstEffect(trg.SkillId, trg.Target));
```

ハンドルを差し替えるだけです。ifチェーンも、バージョンフラグも不要。Disposeの一行で古いロジックは消えます。

---

**昼夜行動パターンを持つモンスターAI** — 複雑な分岐なしにティックを交換

```csharp
var handle = sm.TickState(Monster.Patrol, (prev, trg) =>
    {
        var cmd = (PatrolCommand)trg;
        DayPatrolLogic(cmd.LastPlayerPosition);
    });

handle.Dispose(); // ロジック実行権限を即座に回収

handle = sm.TickState(Monster.Patrol, (prev, trg) =>
    {
        var cmd = (PatrolCommand)trg;
        NightPatrolLogic(cmd.LastPlayerPosition);
    });
```

`TickState` は、その状態がアクティブな間、毎フレーム実行されます。

---

**同じトリガー、異なる結果** — HPによる分岐.

```csharp
.AddTransition<Damaged>(trg => hp - trg.amount > 0,  from: CharState.Idle, to: CharState.Hit)
.AddTransition<Damaged>(trg => hp - trg.amount <= 0, from: CharState.Idle, to: CharState.Dead)
```

条件はトリガーデータから直接評価されます。外部変数の参照は不要です。

---

**ダメージ反射** — 1つのトリガーが別のトリガーを誘発

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

誰が、どのように、どれだけダメージを与えたかをデータとして含めたトリガーを送信します。

---

**チェインライトニング** — 跳ねるトリガー

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

**ボス撃破** — シームレスなクリーンアップ

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

**`ExitState`** は次の状態に遷移する前に実行されます。UIとリソースのクリーンアップに使用してください。

---

**キャラクターアイコン** — サムネイル切り替え、NEWバッジ、覚醒エフェクト

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

**ダッシュ割り込み** — FSM定義を変えずに、状態ごとの反応を実行時に調整.

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

`Interrupt` は現在の状態を読み取り、それに応じて分岐します。FSMの定義を修正する必要はありません。await中に別の割り込みが発動すると、`ct` が自動的にキャンセルされます。

---

**無敵フレーム** — 一度定義すれば、何体のキャラクターにでも再利用可能.

```csharp
public class InvincibleFilter : ITransitionFilter
{
    public async ValueTask Invoke(
        object trigger, TransitionContext ctx,
        Func<ValueTask> next, CancellationToken ct)
    {
        if (trigger is Damaged && ctx.From == CharState.Hit)
            return; // 無敵ウィンドウ — 遷移をブロック

        await next(); // それ以外は通過
    }
}
```

```csharp
// 同じフィルターインスタンスをキャラクター間で共有
var invFilter = new InvincibleFilter();

playerSm .UseGlobalFilter(invFilter);
enemySm  .UseGlobalFilter(invFilter);
bossSm   .UseGlobalFilter(invFilter);
```

`UseGlobalFilter` は、そのFSMのすべての遷移にミドルウェアを適用します。フィルターが `next()` を呼べば許可、呼ばなければブロックです。ステートレスなフィルターは必要な数だけFSM間で共有できます。

---

**マナチェック** — 遷移ごとのフィルターで、スキル遷移だけがコストを負担.

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
            return; // マナ不足 — 静かにブロック

        await next();
    }
}
```

```csharp
var sm = FSM.Create<CharState>(CharState.Idle)
    .UseGlobalFilter(new LogFilter())             // すべての遷移で実行 — ログ、分析など
    .AddTransition<SkillCast>(CharState.Idle,   CharState.Casting)
        .UseFilter(new ManaFilter(_mana))         // この遷移にのみ実行
        .UseFilter(new CooldownFilter(_cooldown)) // 複数フィルターが順番にチェーン
    .AddTransition<AttackInput>(CharState.Idle, CharState.Attack)
        // 攻撃にはマナフィルターなし
    .Build();
```

`UseGlobalFilter` はすべての遷移で実行されます。ログやテレメトリなど横断的関心事に最適です。`UseFilter` は直前の遷移にのみ実行され、コストの高いチェックをグローバルパスから除外します。

---

**外部システムへの接続 — 1行で.**

```csharp
// Input System
sm.Connect(input.AttackPressedEvent);

// Behavior Tree
sm.Connect(bt.EnemyDetectedEvent);

// AoE範囲内の全キャラクターに同時適用
foreach (var ch in aoeTargets)
    ch.sm.Connect(skill.HitEvent).AddTo(composite);
```

どんなシステムも `.Connect()` で接続できます。FSMが外部システムを直接参照する必要はありません。

---

**スキルがアクティブな間だけ継続ダメージ** — 自動でクリーンアップ.

```csharp
using (sm.TriggerEveryUpdate(new DotDamage(3)))
{
    await skill.Execute();
}
// usingブロックを抜けるとDoTが自動的に停止
```

---

**カットシーンやボスフェーズ切り替え中にFSMを完全停止.**

```csharp
using (sm.Deactivate())
{
    await PlayCutscene();
}
// usingブロックを抜けると自動的に再開
```

---

**非同期フローで特定の状態を待機.**

```csharp
async UniTask Tutorial(CancellationToken ct)
{
    ShowGuide("攻撃してみよう！");
    await playerFSM.ToUniTask(CharState.Attack, ct);

    ShowGuide("今度は回避してみよう！");
    await playerFSM.ToUniTask(CharState.Dodge, ct);
}
```

---

**階層FSM** — ジャンプ前の地上状態を記憶し、着地時に復元します.

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

// Run中にジャンプ → 着地するとRunに自動復元。追加の宣言は不要。
```

---

**効率的な親子状態管理**

```csharp
groundSm.EnterStateAsync(GroundState.Idle, async (prev, ct) => {

    await IdleDelay(ct);
}, AsyncOperation.Throttle);


groundSm.EnterStateAsync(async (cur, prev, ct) => {

    await IdleDelay(ct);
}, AsyncOperation.Throttle);
```

`AsyncOperation.Throttle` は非同期タスクが完了するまで、その状態からの次の遷移をブロックします。

---

**エラーハンドリングとデバッグ** — エラー、その原因、現在の状態を一度に受け取ります.

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

`OnError` はすべてのコールバックからの例外を受け取ります。例外を引き起こしたトリガーと `CallbackType` も一緒に渡されます。

---

## クイックスタート

### トリガーの定義

```csharp
public readonly record struct Damaged(float amount, Element element, Vector3 direction);
public readonly record struct Healed(float amount);
public readonly record struct AttackInput;
public readonly record struct AttackEnd;
```

### FSMのビルド

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

### トリガーの発動

```csharp
sm.Trigger(new Damaged(50f, Element.Fire, hitDir));
sm.Trigger(new AttackInput());
```

### 状態への反応

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

### ライフサイクル連携

```csharp
void Start()
{
    sm.AddTo(gameObject); // GameObjectが破棄されたとき自動でDispose
}
```

---

## サンプル

### UIインタラクション

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

### ゲームフロー

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
sm.EnterState(GameState.Paused,   (prev, trg) => Time.timeScale = 0f);
sm.EnterState(GameState.GameOver, (prev, trg) => ShowGameOverUI());
```

### ターン制バトル

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

**UniTask拡張** — 非同期フローで状態遷移を待機.

[UniTask](https://github.com/Cysharp/UniTask) をインストール後、Package Manager → **+** → **Add package from git URL**:

```
https://github.com/YoruYomix/RxFSM.git?path=com.yoruyomix.rxfsm.unitask
```

```csharp
async UniTask Tutorial(CancellationToken ct)
{
    ShowGuide("攻撃してみよう！");
    await playerFSM.ToUniTask(CharState.Attack, ct);

    ShowGuide("今度は回避してみよう！");
    await playerFSM.ToUniTask(CharState.Dodge, ct);
}
```

`ToUniTask(state, ct)` はFSMが目標状態に入った瞬間に完了します。`AsyncOperation.Switch` ハンドラと組み合わせることで、ポーリングなしにステップバイステップのチュートリアルやカットシーンフローを構築できます。

---

**R3拡張** — R3 / UniRxストリームをFSMに直接接続.

[R3](https://github.com/Cysharp/R3) をインストール後、Package Manager → **+** → **Add package from git URL**:

```
https://github.com/YoruYomix/RxFSM.git?path=com.yoruyomix.rxfsm.r3
```

```csharp
// IObservable<T>を直接接続 — ラッパー不要
sm.Connect(input.attackStream);      // IObservable<AttackInput>
sm.Connect(damageSystem.hitStream);  // IObservable<Damaged>
sm.Connect(network.commandStream);   // IObservable<NetworkCommand>
```

`Connect(IObservable<T>)` はストリームを購読し、各発行ごとに `Trigger` を呼び出します。`IDisposable` を返し、Disposeすると購読が解除されます。

---

## APIの概要

| メソッド | 説明 |
| --- | --- |
| `Trigger(struct)` | トリガーの発動。同期的に即座に処理されます。 |
| `TriggerEveryUpdate(struct)` | 毎フレームトリガーを発動。`IDisposable` で管理されます。 |
| `Connect(Action<T> event)` | 外部イベントソースを購読。 |
| `EnterState / ExitState` | 状態の進入/離脱コールバック。 |
| `TickState(state, callback)` | 指定状態がアクティブな間、毎フレームコールバックを実行。`IDisposable` で管理されます。 |
| `EnterStateAsync` | 状態進入時の非同期コールバック。Throttle、Switch、Parallel、Dropポリシーをサポート。 |
| `ThrottleState(state, time)` | 状態進入後、指定時間の間、次の遷移をブロック。 |
| `ThrottleFrameState(state, frames)` | ThrottleStateのフレームベース版。 |
| `HoldState(state, waitUntil)` | 条件が満たされるまで状態の離脱を遅延。 |
| `AutoTransition(from, to, time)` | 指定時間後またはコールバック完了時に自動遷移。 |
| `ForceTransitionTo(state)` | すべてのガードを無視して遷移。AsyncOperation.Throttle、TransitionFilterなどをバイパス。 |
| `TransitionTo(state)` | 指定状態へ直接遷移。 |
| `Deactivate()` | FSMを一時停止。返されたハンドルをDisposeすると再開します。 |
| `Interrupt(IInterrupt)` | 現在の状態に基づく非同期分岐ロジックを注入。キャンセルサポートあり。 |
| `UseGlobalFilter(ITransitionFilter)` | すべての遷移にミドルウェアを適用。 |
| `UseFilter(ITransitionFilter)` | 特定の遷移にミドルウェアを適用。 |
| `AddTransitionFromAny<T>` | どの状態からでも指定トリガーに対する遷移を登録。 |
| `Register(state, childFsm)` | 階層FSM用の子FSMを接続。 |
| `GetActiveStateHierarchy()` | 現在アクティブな状態階層を照会（親→子の順）。 |
| `AddTo(gameObject)` | GameObjectが破棄されたときFSMを自動でDispose。 |
| `ToUniTask(state, ct)` | 特定状態への進入を待機。（UniTask拡張） |
| `ToUniTask().WithTrigger()` | 特定のトリガー条件に合致する状態遷移を待機。（UniTask拡張） |
| `Connect(IObservable<T>)` | UniRx / R3ストリームを購読。（拡張） |
| `IFSMObserver` | 制限インターフェース: トリガー専用アクセス。（上級） |
| `IFSMObservable` | 制限インターフェース: 観察専用アクセス。（上級） |

---

## ライセンス

MIT License
