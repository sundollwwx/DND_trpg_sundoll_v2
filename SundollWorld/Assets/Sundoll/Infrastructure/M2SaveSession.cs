using System;
using Sundoll.Application;
using Sundoll.Core;

namespace Sundoll.Infrastructure
{
    public sealed class M2AutosavePolicy
    {
        private readonly int transactionLimit;
        private readonly float secondsLimit;
        private int pendingTransactions;
        private float elapsedSeconds;

        public M2AutosavePolicy(int transactionLimit = 25, float secondsLimit = 180f)
        {
            if (transactionLimit < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(transactionLimit));
            }

            if (secondsLimit <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(secondsLimit));
            }

            this.transactionLimit = transactionLimit;
            this.secondsLimit = secondsLimit;
        }

        public int PendingTransactions => pendingTransactions;

        public bool NotifyAccepted()
        {
            pendingTransactions++;
            return pendingTransactions >= transactionLimit;
        }

        public bool Tick(float deltaSeconds)
        {
            if (deltaSeconds > 0f)
            {
                elapsedSeconds += deltaSeconds;
            }

            return pendingTransactions > 0 && elapsedSeconds >= secondsLimit;
        }

        public void MarkSaved()
        {
            MarkSaved(pendingTransactions);
        }

        public void MarkSaved(int savedTransactions)
        {
            if (savedTransactions < 0 || savedTransactions > pendingTransactions)
            {
                throw new ArgumentOutOfRangeException(nameof(savedTransactions));
            }

            pendingTransactions -= savedTransactions;
            elapsedSeconds = 0f;
        }
    }

    public sealed class M2SaveSession
    {
        private readonly M2ProjectStore projectStore;
        private readonly M2AutosavePolicy autosavePolicy;
        private readonly M2SaveQueue saveQueue;
        private M2JournalStore journalStore;
        private M1WorldState currentState;
        private string journalStreamId;
        private M2SaveOperation activeSaveOperation;

        private M2SaveSession(M2ProjectStore projectStore, M2AutosavePolicy autosavePolicy)
        {
            this.projectStore = projectStore;
            this.autosavePolicy = autosavePolicy;
            saveQueue = new M2SaveQueue(projectStore);
        }

        public string ProjectRoot => projectStore.RootPath;
        public string ActiveRevisionId { get; private set; }
        public long ActiveGeneration { get; private set; }
        public string LastAction { get; private set; } = "M2 尚未保存";
        public int PendingTransactions => autosavePolicy.PendingTransactions;
        public M2SaveResult LastSave { get; private set; }
        public M2SaveStatus SaveStatus { get; private set; } = M2SaveStatus.Unsaved;
        public string LastSaveError { get; private set; }
        public M1WorldState State => currentState == null ? null : currentState.DeepClone();

        public void WaitForSave()
        {
            WaitForActiveSave();
        }

        public static M2SaveSession Open(string projectRoot, M1WorldState initialState, M2AutosavePolicy autosavePolicy = null)
        {
            if (initialState == null)
            {
                throw new ArgumentNullException(nameof(initialState));
            }

            var session = new M2SaveSession(
                new M2ProjectStore(projectRoot),
                autosavePolicy ?? new M2AutosavePolicy());
            var loaded = session.projectStore.LoadBestAvailable();
            if (loaded == null)
            {
                session.currentState = initialState.DeepClone();
                session.journalStreamId = M2ProjectStore.NewStreamId();
                session.journalStore = new M2JournalStore(projectRoot, session.journalStreamId);
                session.SaveCurrent("创建 M2 首个完整 Revision");
                return session;
            }

            session.currentState = loaded.state;
            session.ActiveGeneration = loaded.head.generation;
            session.journalStreamId = loaded.head.activeJournalStreamId;
            session.journalStore = new M2JournalStore(projectRoot, session.journalStreamId);
            session.ActiveRevisionId = loaded.manifest.saveRevisionId;
            session.RestoreJournalAfterSnapshot(loaded, "已从 " + loaded.source + " 加载 Revision");
            session.SaveStatus = M2SaveStatus.Safe;
            return session;
        }

        public void RecordAccepted(M1CommandReceipt receipt, M1WorldState state)
        {
            if (receipt == null)
            {
                throw new ArgumentNullException(nameof(receipt));
            }

            if (!receipt.accepted)
            {
                throw new InvalidOperationException("Only an accepted command can be recorded in the Journal.");
            }

            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            RefreshSaveStatus();
            currentState = state.DeepClone();
            if (receipt.command == null)
            {
                // Keep the old state-based path for pre-envelope callers and test
                // fixtures. Real command receipts always use the v2 path below.
                journalStore.Append(receipt.commandId, receipt.message, currentState, "DomainCommand");
            }
            else
            {
                journalStore.Append(
                    M2CommandEnvelopeCodec.CreateAcceptedBatch(receipt),
                    receipt.message,
                    currentState,
                    "DomainCommand");
            }

            LastAction = receipt.message;
            SaveStatus = activeSaveOperation == null ? M2SaveStatus.Unsaved : M2SaveStatus.Saving;
            if (autosavePolicy.NotifyAccepted() && activeSaveOperation == null)
            {
                QueueSave("达到事务数量阈值，自动保存");
            }
        }

        public void RecordMutation(string commandId, string description, M1WorldState state)
        {
            RecordMutation(commandId, description, state, "StateMutation", true);
        }

        public M2SaveResult Save(M1WorldState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            WaitForActiveSave();
            currentState = state.DeepClone();
            return SaveCurrent("手动保存 Snapshot");
        }

        public M2SaveOperation QueueSave(string reason = "后台保存 Snapshot")
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("Save reason is required.", nameof(reason));
            }

            RefreshSaveStatus();
            if (activeSaveOperation != null)
            {
                return activeSaveOperation;
            }

            if (currentState == null)
            {
                throw new InvalidOperationException("M2 session has no current state.");
            }

            LastSaveError = null;
            SaveStatus = M2SaveStatus.Saving;
            activeSaveOperation = saveQueue.Enqueue(
                currentState,
                journalStreamId,
                journalStore.LastSequence,
                ActiveGeneration,
                reason,
                autosavePolicy.PendingTransactions);
            LastAction = reason + "（保存中）";
            return activeSaveOperation;
        }

        public void RefreshSaveStatus()
        {
            if (activeSaveOperation == null)
            {
                return;
            }

            if (!activeSaveOperation.IsCompleted)
            {
                SaveStatus = M2SaveStatus.Saving;
                return;
            }

            var completedOperation = activeSaveOperation;
            activeSaveOperation = null;
            if (completedOperation.Status == M2SaveStatus.Safe)
            {
                LastSave = completedOperation.Result;
                ActiveRevisionId = LastSave.saveRevisionId;
                ActiveGeneration = LastSave.generation;
                autosavePolicy.MarkSaved(completedOperation.CapturedPendingTransactions);
                LastAction = completedOperation.Reason + "：" + LastSave.saveRevisionId;
                SaveStatus = autosavePolicy.PendingTransactions > 0
                    ? M2SaveStatus.Unsaved
                    : M2SaveStatus.Safe;
                return;
            }

            LastSaveError = completedOperation.Error == null
                ? "后台保存失败"
                : completedOperation.Error.Message;
            LastAction = completedOperation.Reason + "失败：" + LastSaveError;
            SaveStatus = M2SaveStatus.Failed;
        }

        public M2LoadResult Reload()
        {
            WaitForActiveSave();
            var loaded = projectStore.LoadBestAvailable();
            if (loaded == null)
            {
                throw new InvalidOperationException("M2 project has no valid HEAD.");
            }

            currentState = loaded.state;
            ActiveRevisionId = loaded.manifest.saveRevisionId;
            ActiveGeneration = loaded.head.generation;
            journalStreamId = loaded.head.activeJournalStreamId;
            journalStore = new M2JournalStore(projectStore.RootPath, journalStreamId);
            RestoreJournalAfterSnapshot(loaded, "已重新加载 " + loaded.source);

            autosavePolicy.MarkSaved();
            SaveStatus = M2SaveStatus.Safe;
            return new M2LoadResult
            {
                state = currentState.DeepClone(),
                head = loaded.head,
                manifest = loaded.manifest,
                source = LastAction,
                diagnostic = loaded.diagnostic
            };
        }

        public bool TickAutosave(float deltaSeconds)
        {
            RefreshSaveStatus();
            if (activeSaveOperation == null && autosavePolicy.Tick(deltaSeconds))
            {
                QueueSave("自动保存");
                return true;
            }

            return false;
        }

        public M2ValidationResult Validate()
        {
            return projectStore.Validate();
        }

        public string ExportPackage(string packagePath)
        {
            var path = projectStore.ExportPackage(packagePath);
            LastAction = "已导出 .sundollpkg";
            return path;
        }

        public void Dispose()
        {
            WaitForActiveSave();
            saveQueue.Dispose();
        }

        private void RecordMutation(string commandId, string description, M1WorldState state, string operationType, bool allowAutosave)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            RefreshSaveStatus();
            currentState = state.DeepClone();
            journalStore.Append(commandId, description, currentState, operationType);
            LastAction = description;
            SaveStatus = activeSaveOperation == null ? M2SaveStatus.Unsaved : M2SaveStatus.Saving;
            if (allowAutosave && autosavePolicy.NotifyAccepted() && activeSaveOperation == null)
            {
                QueueSave("达到事务数量阈值，自动保存");
            }
        }

        private M2SaveResult SaveCurrent(string reason)
        {
            LastSave = projectStore.Save(currentState, journalStreamId, journalStore.LastSequence, ActiveGeneration);
            ActiveRevisionId = LastSave.saveRevisionId;
            ActiveGeneration = LastSave.generation;
            autosavePolicy.MarkSaved();
            LastSaveError = null;
            SaveStatus = M2SaveStatus.Safe;
            LastAction = reason + "：" + ActiveRevisionId;
            return LastSave;
        }

        private void WaitForActiveSave()
        {
            if (activeSaveOperation == null)
            {
                return;
            }

            try
            {
                activeSaveOperation.Wait();
            }
            catch (Exception)
            {
                // RefreshSaveStatus records the failure without hiding it from the
                // session. A subsequent manual save can retry with the latest state.
            }
            finally
            {
                RefreshSaveStatus();
            }
        }

        private void RestoreJournalAfterSnapshot(M2LoadResult loaded, string snapshotAction)
        {
            if (journalStore.TryReplay(currentState, loaded.manifest.journalOperationSequence, out var replay))
            {
                if (replay.complete)
                {
                    currentState = replay.state;
                    LastAction = "已从 Journal 重放 " + replay.appliedCount + " 个未写入 Snapshot 的操作";
                    return;
                }

                LastAction = snapshotAction + "；Journal 重放未完成，已保留 Snapshot（" + replay.diagnostic + "）";
                return;
            }

            LastAction = snapshotAction;
        }

    }
}
