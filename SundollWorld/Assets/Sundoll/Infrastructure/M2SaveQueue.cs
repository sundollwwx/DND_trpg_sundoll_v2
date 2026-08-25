using System;
using System.Threading;
using System.Threading.Tasks;
using Sundoll.Core;

namespace Sundoll.Infrastructure
{
    public enum M2SaveStatus
    {
        Unsaved = 0,
        Saving = 1,
        Safe = 2,
        Failed = 3
    }

    public sealed class M2SaveOperation
    {
        private readonly object syncRoot = new object();
        private readonly TaskCompletionSource<M2SaveResult> completion = new TaskCompletionSource<M2SaveResult>();
        private M2SaveStatus status = M2SaveStatus.Saving;
        private M2SaveResult result;
        private Exception error;

        internal M2SaveOperation(string reason, long journalOperationSequence, int capturedPendingTransactions)
        {
            Reason = reason;
            JournalOperationSequence = journalOperationSequence;
            CapturedPendingTransactions = capturedPendingTransactions;
        }

        public string Reason { get; }
        public long JournalOperationSequence { get; }
        public int CapturedPendingTransactions { get; }

        public M2SaveStatus Status
        {
            get
            {
                lock (syncRoot)
                {
                    return status;
                }
            }
        }

        public bool IsCompleted => Status == M2SaveStatus.Safe || Status == M2SaveStatus.Failed;

        public M2SaveResult Result
        {
            get
            {
                lock (syncRoot)
                {
                    return result;
                }
            }
        }

        public Exception Error
        {
            get
            {
                lock (syncRoot)
                {
                    return error;
                }
            }
        }

        public M2SaveResult Wait()
        {
            return completion.Task.GetAwaiter().GetResult();
        }

        internal void MarkSafe(M2SaveResult saveResult)
        {
            lock (syncRoot)
            {
                result = saveResult;
                status = M2SaveStatus.Safe;
            }

            completion.TrySetResult(saveResult);
        }

        internal void MarkFailed(Exception exception)
        {
            lock (syncRoot)
            {
                error = exception;
                status = M2SaveStatus.Failed;
            }

            completion.TrySetException(exception);
        }
    }

    public sealed class M2SaveQueue : IDisposable
    {
        private readonly object syncRoot = new object();
        private readonly M2ProjectStore projectStore;
        private Task tail = Task.CompletedTask;
        private bool disposed;

        public M2SaveQueue(M2ProjectStore projectStore)
        {
            this.projectStore = projectStore ?? throw new ArgumentNullException(nameof(projectStore));
        }

        public M2SaveOperation Enqueue(
            M1WorldState state,
            string journalStreamId,
            long journalOperationSequence,
            long? expectedGeneration,
            string reason,
            int capturedPendingTransactions = 0)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("Save reason is required.", nameof(reason));
            }

            lock (syncRoot)
            {
                ThrowIfDisposed();

                // DeepClone is the main-thread capture boundary. The worker only sees
                // this immutable-for-the-duration snapshot and never the live domain state.
                var snapshot = state.DeepClone();
                var operation = new M2SaveOperation(reason, journalOperationSequence, capturedPendingTransactions);
                var previous = tail;
                tail = previous.ContinueWith(
                    _ => Execute(operation, snapshot, journalStreamId, journalOperationSequence, expectedGeneration),
                    CancellationToken.None,
                    TaskContinuationOptions.DenyChildAttach,
                    TaskScheduler.Default);
                return operation;
            }
        }

        public void WaitForIdle()
        {
            Task pending;
            lock (syncRoot)
            {
                pending = tail;
            }

            pending.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Task pending;
            lock (syncRoot)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                pending = tail;
            }

            pending.GetAwaiter().GetResult();
        }

        private void Execute(
            M2SaveOperation operation,
            M1WorldState snapshot,
            string journalStreamId,
            long journalOperationSequence,
            long? expectedGeneration)
        {
            try
            {
                var result = projectStore.Save(
                    snapshot,
                    journalStreamId,
                    journalOperationSequence,
                    expectedGeneration);
                operation.MarkSafe(result);
            }
            catch (Exception exception)
            {
                operation.MarkFailed(exception);
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(M2SaveQueue));
            }
        }
    }
}
