using System;
using System.Threading;
using System.Threading.Tasks;

namespace RxFSM
{
    public sealed partial class FSM<TState>
    {
        internal CancellationTokenSource _interruptCts;

        public void Interrupt(IInterrupt interrupt)
        {
            if (_disposed) return;
            _interruptCts?.Cancel();
            _interruptCts = new CancellationTokenSource();
            _ = RunInterruptAsync(interrupt, _interruptCts.Token);
        }

        private async Task RunInterruptAsync(IInterrupt interrupt, CancellationToken ct)
        {
            try   { await interrupt.InvokeAsync(_current, ct); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { OnError?.Invoke(ex, null, CallbackType.EnterStateAsync); }
        }

        internal void CancelInterrupt()
        {
            _interruptCts?.Cancel();
            _interruptCts = null;
        }
    }
}
