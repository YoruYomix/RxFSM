namespace RxFSM
{
    /// <summary>
    /// Non-generic static entry point.
    /// Usage: FSM.Create&lt;MyState&gt;(MyState.Idle).AddTransition(...).Build()
    /// </summary>
    public static class FSM
    {
        public static FSMBuilder<TState> Create<TState>(TState initialState) where TState : System.Enum
            => FSMBuilder<TState>.Create(initialState);
    }
}
