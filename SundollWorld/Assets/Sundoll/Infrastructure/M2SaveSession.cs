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
            pendingTransactions = 0;
            elapsedSeconds = 0f;
        }
    }

    public sealed class M2SaveSession
    {
        private readonly M2ProjectStore projectStore;
        private readonly M2AutosavePolicy autosavePolicy;
        private M2JournalStore journalStore;
        private M1WorldState currentState;
        private string journalStreamId;

        private M2SaveSession(M2ProjectStore projectStore, M2AutosavePolicy autosavePolicy)
        {
            this.projectStore = projectStore;
            this.autosavePolicy = autosavePolicy;
        }

        public string ProjectRoot => projectStore.RootPath;
        public string ActiveRevisionId { get; private set; }
        public string LastAction { get; private set; } = "M2 尚未保存";
        public int PendingTransactions => autosavePolicy.PendingTransactions;
        public M2SaveResult LastSave { get; private set; }
        public M1WorldState State => currentState == null ? null : currentState.DeepClone();

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
            session.journalStreamId = loaded.head.activeJournalStreamId;
            session.journalStore = new M2JournalStore(projectRoot, session.journalStreamId);
            if (session.journalStore.TryLoadLatest(out var recovery) && recovery.batch.worldRevision > session.currentState.revision)
            {
                session.currentState = recovery.state;
                session.LastAction = "已从 Journal 恢复 Revision " + recovery.batch.worldRevision;
            }
            else
            {
                session.LastAction = "已从 " + loaded.source + " 加载 Revision";
            }

            session.ActiveRevisionId = loaded.manifest.saveRevisionId;
            return session;
        }

        public void RecordAccepted(M1CommandReceipt receipt, M1WorldState state)
        {
            if (receipt == null)
            {
                throw new ArgumentNullException(nameof(receipt));
            }

            RecordMutation(receipt.commandId, receipt.message, state, "DomainCommand", true);
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

            currentState = state.DeepClone();
            return SaveCurrent("手动保存 Snapshot");
        }

        public M2LoadResult Reload()
        {
            var loaded = projectStore.LoadBestAvailable();
            if (loaded == null)
            {
                throw new InvalidOperationException("M2 project has no valid HEAD.");
            }

            currentState = loaded.state;
            ActiveRevisionId = loaded.manifest.saveRevisionId;
            if (journalStore.TryLoadLatest(out var recovery) && recovery.batch.worldRevision > currentState.revision)
            {
                currentState = recovery.state;
                LastAction = "已从 Journal 恢复未写入 Snapshot 的完整 Batch";
            }
            else
            {
                LastAction = "已重新加载 " + loaded.source;
            }

            autosavePolicy.MarkSaved();
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
            if (autosavePolicy.Tick(deltaSeconds))
            {
                SaveCurrent("自动保存");
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

        private void RecordMutation(string commandId, string description, M1WorldState state, string operationType, bool allowAutosave)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            currentState = state.DeepClone();
            journalStore.Append(commandId, description, currentState, operationType);
            LastAction = description;
            if (allowAutosave && autosavePolicy.NotifyAccepted())
            {
                SaveCurrent("达到事务数量阈值，自动保存");
            }
        }

        private M2SaveResult SaveCurrent(string reason)
        {
            LastSave = projectStore.Save(currentState, journalStreamId, journalStore.LastSequence);
            ActiveRevisionId = LastSave.saveRevisionId;
            autosavePolicy.MarkSaved();
            LastAction = reason + "：" + ActiveRevisionId;
            return LastSave;
        }
    }
}
