namespace RxFSM
{
    /// <summary>
    /// Declares that FSM&lt;TState&gt; implements IFSM&lt;TState&gt;.
    /// All method bodies live in the other partial-class files; this file
    /// is purely the interface inheritance declaration.
    /// </summary>
    public sealed partial class FSM<TState> : IFSM<TState>
    {
        // No new members — all interface methods are already implemented
        // in RxFSM.cs, FSM.Callbacks.cs, FSM.Async.cs, FSM.Loop.cs,
        // FSM.Guards.cs, FSM.Nested.cs, FSM.Interrupt.cs, FSM.Filter.cs.
    }
}
