using System;

namespace RxFSM
{
    /// <summary>
    /// Marker interface for attribute-driven action tables.
    /// Implement on a <c>partial</c> class; the RxFSM source generator emits the
    /// <see cref="Register"/> body by scanning for [EnterState]/[ExitState]/[TickState]
    /// (and [EnterStateAsync] when the UniTask package is present) methods.
    /// </summary>
    public interface IActionTable<TState>
        where TState : Enum
    {
        /// <summary>The owning FSM, assigned during <see cref="Register"/>. Generator-populated.</summary>
        FSM<TState> FSM { get; }

        /// <summary>Wires all attributed callbacks to <paramref name="fsm"/> for <paramref name="state"/>.</summary>
        IDisposable Register(FSM<TState> fsm, TState state);
    }

    /// <summary>
    /// Marks a method as an enter-state callback.
    /// Signature: <c>void M(TState prev, TTrigger trigger)</c> (TTrigger : struct) or
    /// <c>void M(TState prev, object trigger)</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class EnterStateAttribute : Attribute { }

    /// <summary>
    /// Marks a method as an exit-state callback.
    /// Signature: <c>void M(TState next, TTrigger trigger)</c> (TTrigger : struct) or
    /// <c>void M(TState next, object trigger)</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class ExitStateAttribute : Attribute { }

    /// <summary>
    /// Marks a method as a tick-state callback.
    /// Signature: <c>void M(TState prev, TTrigger trigger)</c> (TTrigger : struct) or
    /// <c>void M(TState prev, object trigger)</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class TickStateAttribute : Attribute { }
}
