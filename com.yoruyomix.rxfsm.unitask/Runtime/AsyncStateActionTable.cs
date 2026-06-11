using System;
using System.ComponentModel;
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

    public interface IAsyncStateActionTable<TState> : IStateActionTable<TState>
        where TState : Enum
    {
        UniTask EnterStateAsync(TState prev, object trigger, CancellationToken ct)
            => UniTask.CompletedTask;
    }

    public abstract class AsyncStateActionTable<TState> :
        StateActionTable<TState>,
        IAsyncStateActionTable<TState>
        where TState : Enum
    {
        IDisposable IStateActionTable<TState>.Register(FSM<TState> fsm, TState state)
            => AsyncStateActionTableRegistration.Register((IAsyncStateActionTable<TState>)this, fsm, state);
    }

    public interface IAsyncStateActionTable<TState, TTrigger> :
        IAsyncStateActionTable<TState>,
        IStateActionTable<TState, TTrigger>
        where TState : Enum
        where TTrigger : struct
    {
        UniTask EnterStateAsync(TState prev, TTrigger trigger, CancellationToken ct)
            => UniTask.CompletedTask;
    }

    public abstract class AsyncStateActionTable<TState, TTrigger> :
        StateActionTable<TState, TTrigger>,
        IAsyncStateActionTable<TState, TTrigger>
        where TState : Enum
        where TTrigger : struct
    {
        IDisposable IStateActionTable<TState>.Register(FSM<TState> fsm, TState state)
            => AsyncStateActionTableRegistration.Register((IAsyncStateActionTable<TState, TTrigger>)this, fsm, state);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class AsyncStateActionTableRegistration
    {
        public static IDisposable Register<TState>(
            IAsyncStateActionTable<TState> actionTable,
            FSM<TState> fsm,
            TState state)
            where TState : Enum
        {
            if (actionTable == null)
                throw new ArgumentNullException(nameof(actionTable));

            var cd = new FSMCompositeDisposable();
            StateActionTableRegistration.Register((IStateActionTable<TState>)actionTable, fsm, state).AddTo(cd);

            var actionType = actionTable.GetType();
            var interfaceType = typeof(IAsyncStateActionTable<TState>);

            if (StateActionTableRegistration.HasImplementation(actionType, interfaceType,
                    nameof(IAsyncStateActionTable<TState>.EnterStateAsync),
                    typeof(TState), typeof(object), typeof(CancellationToken)))
            {
                var method = GetImplementation(actionType, interfaceType,
                    nameof(IAsyncStateActionTable<TState>.EnterStateAsync),
                    typeof(TState), typeof(object), typeof(CancellationToken));
                var operation = GetStateCallbackOperation(method);

                fsm.EnterStateAsync(
                    state,
                    (prev, trigger, ct) => actionTable.EnterStateAsync(prev, trigger, ct).AsTask(),
                    operation).AddTo(cd);
            }

            return cd;
        }

        public static IDisposable Register<TState, TTrigger>(
            IAsyncStateActionTable<TState, TTrigger> actionTable,
            FSM<TState> fsm,
            TState state)
            where TState : Enum
            where TTrigger : struct
        {
            if (actionTable == null)
                throw new ArgumentNullException(nameof(actionTable));

            var cd = new FSMCompositeDisposable();
            StateActionTableRegistration.Register((IStateActionTable<TState, TTrigger>)actionTable, fsm, state).AddTo(cd);

            var actionType = actionTable.GetType();
            var interfaceType = typeof(IAsyncStateActionTable<TState, TTrigger>);

            if (StateActionTableRegistration.HasImplementation(actionType, interfaceType,
                    nameof(IAsyncStateActionTable<TState, TTrigger>.EnterStateAsync),
                    typeof(TState), typeof(TTrigger), typeof(CancellationToken)))
            {
                var method = GetImplementation(actionType, interfaceType,
                    nameof(IAsyncStateActionTable<TState, TTrigger>.EnterStateAsync),
                    typeof(TState), typeof(TTrigger), typeof(CancellationToken));
                var operation = GetStateCallbackOperation(method);

                fsm.EnterStateAsync<TTrigger>(
                    state,
                    (prev, trigger, ct) => actionTable.EnterStateAsync(prev, trigger, ct).AsTask(),
                    operation).AddTo(cd);
            }

            return cd;
        }

        private static MethodInfo GetImplementation(Type actionType, Type interfaceType, string name, params Type[] parameterTypes)
        {
            var method = interfaceType.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                parameterTypes,
                null);

            if (method == null)
                return null;

            var map = actionType.GetInterfaceMap(interfaceType);

            for (var i = 0; i < map.InterfaceMethods.Length; i++)
            {
                if (map.InterfaceMethods[i] == method)
                    return map.TargetMethods[i];
            }

            return null;
        }

        private static TransitionOperation GetStateCallbackOperation(MethodInfo method)
        {
            var attr = method?.GetCustomAttribute<StateCallbackAttribute>(true);
            return attr != null ? attr.Operation : TransitionOperation.Switch;
        }
    }
}
