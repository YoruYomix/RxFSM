using System;

namespace RxFSM
{
    /// <summary>
    /// Declares an attribute-driven action table for a single <paramref name="state"/>.
    /// Put on a <c>partial</c> class; the RxFSM source generator infers TState from the
    /// enum value, attaches <see cref="IActionTable{TState}"/>, and emits the Register
    /// body by scanning for [EnterState]/[ExitState]/[TickState]/[EnterStateAsync] methods.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class ActionTableAttribute : Attribute
    {
        public object State { get; }
        public ActionTableAttribute(object state) => State = state;
    }

    /// <summary>
    /// Generated contract for attribute-driven action tables. The source generator
    /// implements this on any <c>partial</c> class marked with [ActionTable]; the bound
    /// state is baked into the generated <see cref="Register"/>.
    /// </summary>
    public interface IActionTable<TState>
        where TState : Enum
    {
        /// <summary>The owning FSM, assigned during <see cref="Register"/>. Generator-populated.</summary>
        FSM<TState> FSM { get; }

        /// <summary>Wires all attributed callbacks to <paramref name="fsm"/> for the bound state.</summary>
        IDisposable Register(FSM<TState> fsm);
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
