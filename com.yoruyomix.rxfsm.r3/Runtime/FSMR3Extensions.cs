using R3;

namespace RxFSM
{
    public static class FSMR3Extensions
    {
        public static System.IDisposable Connect<TTrigger, TState>(
            this Observable<TTrigger> source,
            IFSM<TState> sm)
            where TTrigger : struct
            where TState : System.Enum
        {
            return source.Subscribe(value => sm.Trigger(value));
        }
    }
}