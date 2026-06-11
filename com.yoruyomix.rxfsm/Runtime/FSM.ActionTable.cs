using System;

namespace RxFSM
{
    public sealed partial class FSM<TState>
    {
        public IDisposable AddActionTable(IActionTable<TState> actionTable)
        {
            if (actionTable == null)
                throw new ArgumentNullException(nameof(actionTable));

            return actionTable.Register(this);
        }
    }
}
