using System;

namespace RxFSM
{
    public sealed partial class FSM<TState>
    {
        public IDisposable AddActionTable(TState state, StateActionTable<TState> actionTable)
        {
            if (actionTable == null)
                throw new ArgumentNullException(nameof(actionTable));

            return ((IStateActionTable<TState>)actionTable).Register(this, state);
        }
    }
}
