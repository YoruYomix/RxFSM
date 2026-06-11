using System;

namespace RxFSM
{
    public sealed partial class FSM<TState>
    {
        public IDisposable AddActionTable(TState state, StateActionTableBase<TState> actionTable)
        {
            if (actionTable == null)
                throw new ArgumentNullException(nameof(actionTable));

            return actionTable.Register(this, state);
        }
    }
}
