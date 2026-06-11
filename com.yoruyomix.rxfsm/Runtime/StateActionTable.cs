using System;
using System.ComponentModel;
using System.Reflection;

namespace RxFSM
{
    public interface IStateActionTable<TState>
        where TState : Enum
    {
        [EditorBrowsable(EditorBrowsableState.Never)]
        FSM<TState> StateMachine { set { } }

        void EnterState(TState prev, object trigger) { }
        void ExitState(TState next, object trigger) { }
        void TickState(TState prev, object trigger) { }

        [EditorBrowsable(EditorBrowsableState.Never)]
        IDisposable Register(FSM<TState> fsm, TState state);
    }

    public abstract class StateActionTable<TState> : IStateActionTable<TState>
        where TState : Enum
    {
        protected FSM<TState> FSM { get; private set; }

        FSM<TState> IStateActionTable<TState>.StateMachine
        {
            set => FSM = value;
        }

        IDisposable IStateActionTable<TState>.Register(FSM<TState> fsm, TState state)
            => StateActionTableRegistration.Register((IStateActionTable<TState>)this, fsm, state);
    }

    public interface IStateActionTable<TState, TTrigger> : IStateActionTable<TState>
        where TState : Enum
        where TTrigger : struct
    {
        void EnterState(TState prev, TTrigger trigger) { }
        void ExitState(TState next, TTrigger trigger) { }
        void TickState(TState prev, TTrigger trigger) { }
    }

    public abstract class StateActionTable<TState, TTrigger> :
        StateActionTable<TState>,
        IStateActionTable<TState, TTrigger>
        where TState : Enum
        where TTrigger : struct
    {
        IDisposable IStateActionTable<TState>.Register(FSM<TState> fsm, TState state)
            => StateActionTableRegistration.Register((IStateActionTable<TState, TTrigger>)this, fsm, state);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class StateActionTableRegistration
    {
        public static IDisposable Register<TState>(
            IStateActionTable<TState> actionTable,
            FSM<TState> fsm,
            TState state)
            where TState : Enum
        {
            if (actionTable == null)
                throw new ArgumentNullException(nameof(actionTable));

            var cd = new FSMCompositeDisposable();
            actionTable.StateMachine = fsm;

            var actionType = actionTable.GetType();
            var interfaceType = typeof(IStateActionTable<TState>);

            if (HasImplementation(actionType, interfaceType, nameof(IStateActionTable<TState>.EnterState),
                    typeof(TState), typeof(object)))
                fsm.EnterState(state, actionTable.EnterState).AddTo(cd);

            if (HasImplementation(actionType, interfaceType, nameof(IStateActionTable<TState>.ExitState),
                    typeof(TState), typeof(object)))
                fsm.ExitState(state, actionTable.ExitState).AddTo(cd);

            if (HasImplementation(actionType, interfaceType, nameof(IStateActionTable<TState>.TickState),
                    typeof(TState), typeof(object)))
                fsm.TickState(state, actionTable.TickState).AddTo(cd);

            return cd;
        }

        public static IDisposable Register<TState, TTrigger>(
            IStateActionTable<TState, TTrigger> actionTable,
            FSM<TState> fsm,
            TState state)
            where TState : Enum
            where TTrigger : struct
        {
            if (actionTable == null)
                throw new ArgumentNullException(nameof(actionTable));

            var cd = new FSMCompositeDisposable();
            Register((IStateActionTable<TState>)actionTable, fsm, state).AddTo(cd);

            var actionType = actionTable.GetType();
            var interfaceType = typeof(IStateActionTable<TState, TTrigger>);

            if (HasImplementation(actionType, interfaceType, nameof(IStateActionTable<TState, TTrigger>.EnterState),
                    typeof(TState), typeof(TTrigger)))
                fsm.EnterState<TTrigger>(state, (Action<TState, TTrigger>)actionTable.EnterState).AddTo(cd);

            if (HasImplementation(actionType, interfaceType, nameof(IStateActionTable<TState, TTrigger>.ExitState),
                    typeof(TState), typeof(TTrigger)))
                fsm.ExitState<TTrigger>(state, actionTable.ExitState).AddTo(cd);

            if (HasImplementation(actionType, interfaceType, nameof(IStateActionTable<TState, TTrigger>.TickState),
                    typeof(TState), typeof(TTrigger)))
                fsm.TickState(state, (prev, trigger) =>
                {
                    if (trigger is TTrigger typed)
                        actionTable.TickState(prev, typed);
                }).AddTo(cd);

            return cd;
        }

        public static bool HasImplementation(Type actionType, Type interfaceType, string name, params Type[] parameterTypes)
        {
            var method = interfaceType.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                parameterTypes,
                null);

            if (method == null)
                return false;

            var map = actionType.GetInterfaceMap(interfaceType);

            for (var i = 0; i < map.InterfaceMethods.Length; i++)
            {
                if (map.InterfaceMethods[i] != method)
                    continue;

                var target = map.TargetMethods[i];
                return target.DeclaringType != null && !target.DeclaringType.IsInterface;
            }

            return false;
        }
    }
}
