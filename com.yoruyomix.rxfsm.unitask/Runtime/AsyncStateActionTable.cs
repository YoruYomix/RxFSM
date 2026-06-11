using System;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace RxFSM
{
    [AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public sealed class StateCallbackAttribute : Attribute
    {
        public TransitionOperation Operation { get; }

        public StateCallbackAttribute(TransitionOperation operation)
        {
            Operation = operation;
        }
    }

    public abstract class AsyncStateActionTableBase<TState> : StateActionTableBase<TState>
        where TState : Enum
    {
        public virtual UniTask EnterStateAsync(TState prev, object trigger, CancellationToken ct)
            => UniTask.CompletedTask;

        protected override IDisposable Register(FSM<TState> fsm, TState state)
        {
            var cd = new FSMCompositeDisposable();
            base.Register(fsm, state).AddTo(cd);

            var asyncMethod = GetOverriddenMethod(GetType(), nameof(EnterStateAsync),
                typeof(TState), typeof(object), typeof(CancellationToken));

            if (asyncMethod != null)
            {
                var operation = GetStateCallbackOperation(asyncMethod);
                fsm.EnterStateAsync(
                    state,
                    (prev, trigger, ct) => EnterStateAsync(prev, trigger, ct).AsTask(),
                    operation).AddTo(cd);
            }

            return cd;
        }

        protected static TransitionOperation GetStateCallbackOperation(MethodInfo method)
        {
            var attr = method.GetCustomAttribute<StateCallbackAttribute>(true);
            return attr != null ? attr.Operation : TransitionOperation.Switch;
        }
    }

    public abstract class AsyncStateActionTableBase<TState, TTrigger> : StateActionTableBase<TState, TTrigger>
        where TState : Enum
        where TTrigger : struct
    {
        public virtual UniTask EnterStateAsync(TState prev, TTrigger trigger, CancellationToken ct)
            => UniTask.CompletedTask;

        protected override IDisposable Register(FSM<TState> fsm, TState state)
        {
            var cd = new FSMCompositeDisposable();
            base.Register(fsm, state).AddTo(cd);

            var asyncMethod = GetOverriddenMethod(GetType(), nameof(EnterStateAsync),
                typeof(TState), typeof(TTrigger), typeof(CancellationToken));

            if (asyncMethod != null)
            {
                var operation = GetStateCallbackOperation(asyncMethod);
                fsm.EnterStateAsync<TTrigger>(
                    state,
                    (prev, trigger, ct) => EnterStateAsync(prev, trigger, ct).AsTask(),
                    operation).AddTo(cd);
            }

            return cd;
        }

        protected static TransitionOperation GetStateCallbackOperation(MethodInfo method)
        {
            var attr = method.GetCustomAttribute<StateCallbackAttribute>(true);
            return attr != null ? attr.Operation : TransitionOperation.Switch;
        }
    }
}
