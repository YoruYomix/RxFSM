using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RxFSM
{
    public sealed partial class FSM<TState>
    {
        // ── Storage ──────────────────────────────────────────────────────────────

        private List<ITransitionFilter> _globalFilters;

        // Active in-flight async filter pipelines: cancelled on any state change
        private List<CancellationTokenSource> _activeFilterCts;

        // ── Configuration (called by builder) ────────────────────────────────────

        internal void SetGlobalFilters(List<ITransitionFilter> filters)
            => _globalFilters = filters;

        // ── Pipeline execution ────────────────────────────────────────────────────

        /// <summary>
        /// Runs the filter pipeline for the given transition.
        /// Returns true if the transition was executed synchronously.
        /// Returns false if the transition was blocked (filter did not call next).
        /// Returns null if the pipeline is async (running in background).
        /// </summary>
        internal bool? RunFilterPipeline(
            EventTransition<TState> transition,
            TState                  prev,
            object                  trigger)
        {
            bool hasGlobal = _globalFilters != null && _globalFilters.Count > 0;
            bool hasLocal  = transition.LocalFilters != null && transition.LocalFilters.Count > 0;

            if (!hasGlobal && !hasLocal)
                return true; // no filters — proceed

            var filterCts = new CancellationTokenSource();
            (_activeFilterCts ??= new List<CancellationTokenSource>()).Add(filterCts);

            bool transitioned = false;

            // The terminal "next" executes the actual transition
            Func<ValueTask> executeTransition = () =>
            {
                if (!filterCts.IsCancellationRequested)
                {
                    transitioned = true;
                    ExecuteTransitionCore(prev, transition.To, trigger);
                }
                return default;
            };

            // Build pipeline: global → local → terminal
            Func<ValueTask> pipeline = executeTransition;

            // Chain local filters in reverse
            if (transition.LocalFilters != null)
                for (int i = transition.LocalFilters.Count - 1; i >= 0; i--)
                {
                    var f    = transition.LocalFilters[i];
                    var next = pipeline;
                    var ctx  = new TransitionContext(prev, transition.To);
                    pipeline = () => f.Invoke(trigger, ctx, next, filterCts.Token);
                }

            // Chain global filters in reverse
            if (_globalFilters != null)
                for (int i = _globalFilters.Count - 1; i >= 0; i--)
                {
                    var f    = _globalFilters[i];
                    var next = pipeline;
                    var ctx  = new TransitionContext(prev, transition.To);
                    pipeline = () => f.Invoke(trigger, ctx, next, filterCts.Token);
                }

            var task = pipeline();

            if (task.IsCompleted)
            {
                _activeFilterCts?.Remove(filterCts);
                filterCts.Dispose();
                // If transitioned=true, ExecuteTransitionCore already ran.
                // If transitioned=false, the filter blocked → try next rule.
                return transitioned ? (bool?)true : false;
            }

            // Async pipeline — fire and forget, transition will happen when it resolves
            _ = WatchFilterPipelineAsync(task.AsTask(), filterCts);
            return null; // async, caller should break
        }

        private async Task WatchFilterPipelineAsync(Task pipeline, CancellationTokenSource cts)
        {
            try   { await pipeline; }
            catch (OperationCanceledException) { }
            catch (Exception ex) { OnError?.Invoke(ex, null, CallbackType.EnterStateAsync); }
            finally
            {
                _activeFilterCts?.Remove(cts);
                cts.Dispose();
            }
        }

        // ── Cancel in-flight filter pipelines on state change ─────────────────────

        internal void CancelAllFilterPipelines()
        {
            if (_activeFilterCts == null || _activeFilterCts.Count == 0) return;
            var copy = new List<CancellationTokenSource>(_activeFilterCts);
            _activeFilterCts.Clear();
            foreach (var cts in copy)
                cts.Cancel();
        }

        // ── ExecuteTransitionCore ─────────────────────────────────────────────────

        /// <summary>
        /// Performs the actual Exit → StateChange → Enter sequence.
        /// Called from ProcessEvaluate (direct path) and from filter pipelines.
        /// </summary>
        internal void ExecuteTransitionCore(TState prev, TState next, object trigger)
        {
            // Cancel in-flight filter pipelines and interrupt
            CancelAllFilterPipelines();
            CancelInterrupt();

            // Phase 3: exit callbacks on this layer first (top-down order)
            FireExit(prev, next, trigger);

            // Then notify leaving child subtree
            IFSM leavingChild = null;
            _children?.TryGetValue(prev, out leavingChild);
            leavingChild?.OnLeavingActivePath(trigger);

            // Phase 4: state enum change
            _current = next;
            _testTransitionHook?.Invoke(prev, next, trigger);

            // Phase 5: enter callbacks on this layer
            FireEnter(next, prev, trigger);

            // Phase 5/6: notify entering child
            IFSM enteringChild = null;
            _children?.TryGetValue(next, out enteringChild);
            enteringChild?.OnEnteringActivePath(trigger);
        }

        // ── Cleanup ──────────────────────────────────────────────────────────────

        private void DisposeFilters()
        {
            CancelAllFilterPipelines();
            _globalFilters?.Clear();
        }
    }
}
