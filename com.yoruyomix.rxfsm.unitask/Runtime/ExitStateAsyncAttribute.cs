using System;

namespace RxFSM
{
    /// <summary>
    /// Marks a method as an async exit-state callback for attribute-driven action tables.
    /// Signature: <c>async UniTask M(TState next, TTrigger trigger, CancellationToken ct)</c>
    /// (TTrigger : struct). The RxFSM source generator wires it through
    /// <see cref="FSM{TState}.ExitStateAsync{TTrigger}"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class ExitStateAsyncAttribute : Attribute
    {
    }
}
