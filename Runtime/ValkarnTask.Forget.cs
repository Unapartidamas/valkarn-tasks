// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Extension methods for fire-and-forget tasks.
    /// </summary>
    public static class ValkarnTaskForgetExtensions
    {
        /// <summary>Pooled state for pending discard callbacks. Avoids per-call allocation.</summary>
        sealed class ForgetState : IPoolNode<ForgetState>
        {
            static ValkarnPool<ForgetState> s_pool;
            static ValkarnPool<ForgetState> Pool => ValkarnPool<ForgetState>.GetOrCreate(ref s_pool);

            ForgetState nextNode;
            public ref ForgetState NextNode => ref nextNode;

            internal ValkarnTask.ISource Source;
            internal uint Token;

            internal static ForgetState Rent(ValkarnTask.ISource source, uint token)
            {
                var ds = Pool.TryRent() ?? Pool.TrackNew(new ForgetState());
                ds.Source = source;
                ds.Token = token;
                return ds;
            }

            internal void Return()
            {
                Source = null;
                Token = 0;
                Pool.TryReturn(this);
            }
        }

        static readonly Action<object> s_forgetContinuation = static state =>
        {
            var ds = (ForgetState)state;
            var source = ds.Source;
            var token = ds.Token;
            ds.Return(); // Return to pool immediately

            var status = source.UnsafeGetStatus();
            if (status == ValkarnTask.Status.Faulted)
            {
                try { source.GetResult(token); }
                catch (Exception ex) { ValkarnTask.PublishUnobservedException(ex); }
            }
            else
            {
                // Call GetResult to trigger pool return on the source
                try { source.GetResult(token); } catch { }
            }
        };

        /// <summary>
        /// Fire-and-forget: suppresses unawaited-task warnings.
        /// If the task faults, the exception goes to ValkarnTask.UnobservedException.
        /// Zero allocation on sync-completed tasks. Pooled allocation on pending tasks.
        /// </summary>
        public static void Forget(this ValkarnTask task)
        {
            if (task.source == null) return; // sync completed — no-op

            var status = task.source.UnsafeGetStatus();
            if (status.IsCompleted())
            {
                // Already done — check inline
                if (status == ValkarnTask.Status.Faulted)
                {
                    try { task.source.GetResult(task.token); }
                    catch (Exception ex) { ValkarnTask.PublishUnobservedException(ex); }
                }
                else
                {
                    try { task.source.GetResult(task.token); } catch { }
                }
                return;
            }

            // Pending — register for observation when complete (pooled state)
            task.source.OnCompleted(s_forgetContinuation,
                ForgetState.Rent(task.source, task.token), task.token);
        }

        /// <summary>
        /// Generic variant of Forget.
        /// </summary>
        public static void Forget<T>(this ValkarnTask<T> task)
        {
            if (task.source == null) return;

            var status = task.source.UnsafeGetStatus();
            if (status.IsCompleted())
            {
                if (status == ValkarnTask.Status.Faulted)
                {
                    try { ((ValkarnTask.ISource)task.source).GetResult(task.token); }
                    catch (Exception ex) { ValkarnTask.PublishUnobservedException(ex); }
                }
                else
                {
                    try { ((ValkarnTask.ISource)task.source).GetResult(task.token); } catch { }
                }
                return;
            }

            ((ValkarnTask.ISource)task.source).OnCompleted(s_forgetContinuation,
                ForgetState.Rent(task.source, task.token), task.token);
        }
    }
}
