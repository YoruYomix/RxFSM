using System;
using System.Reflection;

namespace RxFSM
{
    public abstract class StateActionTableBase<TState>
        where TState : Enum
    {
        public virtual void EnterState(TState prev, object trigger) { }
        public virtual void ExitState(TState next, object trigger) { }
        public virtual void TickState(TState prev, object trigger) { }

        protected internal virtual IDisposable Register(FSM<TState> fsm, TState state)
        {
            var cd = new FSMCompositeDisposable();
            var actionType = GetType();

            if (IsOverridden(actionType, nameof(EnterState), typeof(TState), typeof(object)))
                fsm.EnterState(state, EnterState).AddTo(cd);

            if (IsOverridden(actionType, nameof(ExitState), typeof(TState), typeof(object)))
                fsm.ExitState(state, ExitState).AddTo(cd);

            if (IsOverridden(actionType, nameof(TickState), typeof(TState), typeof(object)))
                fsm.TickState(state, TickState).AddTo(cd);

            return cd;
        }

        protected static bool IsOverridden(Type actionType, string name, params Type[] parameterTypes)
            => GetOverriddenMethod(actionType, name, parameterTypes) != null;

        protected static MethodInfo GetOverriddenMethod(Type actionType, string name, params Type[] parameterTypes)
        {
            var method = actionType.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                parameterTypes,
                null);

            if (method == null || !method.IsVirtual)
                return null;

            var baseDefinition = method.GetBaseDefinition();
            return baseDefinition.DeclaringType != method.DeclaringType ? method : null;
        }

    }

    public abstract class StateActionTableBase<TState, TTrigger> : StateActionTableBase<TState>
        where TState : Enum
        where TTrigger : struct
    {
        public sealed override void EnterState(TState prev, object trigger)
        {
            if (trigger is TTrigger typed)
                EnterState(prev, typed);
        }

        public virtual void EnterState(TState prev, TTrigger trigger) { }

        public sealed override void ExitState(TState next, object trigger)
        {
            if (trigger is TTrigger typed)
                ExitState(next, typed);
        }

        public virtual void ExitState(TState next, TTrigger trigger) { }

        public sealed override void TickState(TState prev, object trigger)
        {
            if (trigger is TTrigger typed)
                TickState(prev, typed);
        }

        public virtual void TickState(TState prev, TTrigger trigger) { }

        protected internal override IDisposable Register(FSM<TState> fsm, TState state)
        {
            var cd = new FSMCompositeDisposable();
            var actionType = GetType();

            if (IsOverridden(actionType, nameof(EnterState), typeof(TState), typeof(TTrigger)))
                fsm.EnterState<TTrigger>(state, (Action<TState, TTrigger>)EnterState).AddTo(cd);

            if (IsOverridden(actionType, nameof(ExitState), typeof(TState), typeof(TTrigger)))
                fsm.ExitState<TTrigger>(state, ExitState).AddTo(cd);

            if (IsOverridden(actionType, nameof(TickState), typeof(TState), typeof(TTrigger)))
                fsm.TickState(state, TickState).AddTo(cd);

            return cd;
        }
    }
}
