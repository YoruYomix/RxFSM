using System;

namespace RxFSM
{
    /// <summary>
    /// Marks a method as an async enter-state callback for attribute-driven action tables.
    /// Signature: <c>async UniTask M(TState prev, TTrigger trigger, CancellationToken ct)</c>
    /// (TTrigger : struct). The RxFSM source generator wires it through
    /// <see cref="FSM{TState}.EnterStateAsync{TTrigger}"/> using <see cref="Operation"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class EnterStateAsyncAttribute : Attribute
    {
        public TransitionOperation Operation { get; }

        public EnterStateAsyncAttribute(TransitionOperation operation = TransitionOperation.Switch)
        {
            Operation = operation;
        }
    }
}
