using System;
using System.Collections.Generic;
using Sundoll.Core;

namespace Sundoll.Application
{
    public interface IM1RulePolicy
    {
        bool Allow(M1WorldState state, M1Command command, out string reason);
    }

    public sealed class AllowAllRulePolicy : IM1RulePolicy
    {
        public bool Allow(M1WorldState state, M1Command command, out string reason)
        {
            reason = string.Empty;
            return true;
        }
    }

    public sealed class M1CommandReceipt
    {
        public string commandId;
        public bool accepted;
        public bool duplicate;
        public bool conflict;
        public int revisionBefore;
        public int revisionAfter;
        public string message;
        public M1Command command;
        public WorldChangeSet changeSet;

        public M1CommandReceipt CloneForDuplicate()
        {
            return new M1CommandReceipt
            {
                commandId = commandId,
                accepted = accepted,
                duplicate = true,
                conflict = conflict,
                revisionBefore = revisionBefore,
                revisionAfter = revisionAfter,
                message = message,
                command = command,
                changeSet = changeSet
            };
        }
    }

    public sealed class M1LocalAuthority
    {
        private readonly IM1RulePolicy rulePolicy;
        private readonly Dictionary<string, M1CommandReceipt> receipts = new Dictionary<string, M1CommandReceipt>();

        public M1LocalAuthority(IM1RulePolicy rulePolicy)
        {
            this.rulePolicy = rulePolicy ?? throw new ArgumentNullException(nameof(rulePolicy));
        }

        public M1CommandReceipt Execute(M1WorldState state, M1Command command)
        {
            return Execute(state, command, () => command.Apply(state));
        }

        internal M1CommandReceipt Execute(M1WorldState state, M1Command command, Action apply)
        {
            if (apply == null)
            {
                throw new ArgumentNullException(nameof(apply));
            }

            if (receipts.TryGetValue(command.CommandId, out var existing))
            {
                return existing.CloneForDuplicate();
            }

            var receipt = new M1CommandReceipt
            {
                commandId = command.CommandId,
                revisionBefore = state.revision,
                revisionAfter = state.revision,
                message = command.Description,
                command = command
            };

            if (command.BaseRevision != state.revision)
            {
                receipt.conflict = true;
                receipt.message = $"Revision 冲突：命令基于 {command.BaseRevision}，当前为 {state.revision}";
                receipts[command.CommandId] = ToIdempotencyRecord(receipt);
                return receipt;
            }

            if (!rulePolicy.Allow(state, command, out var reason))
            {
                receipt.message = reason;
                receipts[command.CommandId] = ToIdempotencyRecord(receipt);
                return receipt;
            }

            apply();
            state.revision++;
            receipt.accepted = true;
            receipt.revisionAfter = state.revision;
            receipts[command.CommandId] = ToIdempotencyRecord(receipt);
            return receipt;
        }

        private static M1CommandReceipt ToIdempotencyRecord(M1CommandReceipt receipt)
        {
            return new M1CommandReceipt
            {
                commandId = receipt.commandId,
                accepted = receipt.accepted,
                duplicate = false,
                conflict = receipt.conflict,
                revisionBefore = receipt.revisionBefore,
                revisionAfter = receipt.revisionAfter,
                message = receipt.message
            };
        }
    }

    public sealed class M1CommandBus
    {
        public const int DefaultMaxHistoryEntries = 128;

        private sealed class HistoryEntry
        {
            public M1WorldState before;
            public M1WorldState after;
            public WorldChangeSet changeSet;
            public string description;
        }

        private readonly M1WorldState state;
        private readonly M1LocalAuthority authority;
        private readonly int maxHistoryEntries;
        private readonly List<HistoryEntry> undoHistory = new List<HistoryEntry>();
        private readonly List<HistoryEntry> redoHistory = new List<HistoryEntry>();

        public M1CommandBus(
            M1WorldState state,
            M1LocalAuthority authority,
            int maxHistoryEntries = DefaultMaxHistoryEntries)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
            if (maxHistoryEntries < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxHistoryEntries));
            }

            this.maxHistoryEntries = maxHistoryEntries;
        }

        public M1WorldState State => state;
        public int MaxHistoryEntries => maxHistoryEntries;
        public string LastAction { get; private set; } = "尚未执行操作";
        public WorldChangeSet LastChangeSet { get; private set; }

        public M1CommandReceipt Execute(M1Command command)
        {
            var changeSetCommand = command as IWorldChangeSetCommand;
            var before = changeSetCommand == null ? state.DeepClone() : null;
            WorldChangeSet changeSet = null;
            var receipt = changeSetCommand == null
                ? authority.Execute(state, command)
                : authority.Execute(state, command, () =>
                {
                    changeSet = changeSetCommand.CreateChangeSet(state);
                    changeSet.ApplyForward(state);
                });
            if (receipt.accepted && !receipt.duplicate)
            {
                receipt.changeSet = changeSet;
                undoHistory.Add(new HistoryEntry
                {
                    before = before,
                    after = changeSet == null ? state.DeepClone() : null,
                    changeSet = changeSet,
                    description = command.Description
                });
                while (undoHistory.Count > maxHistoryEntries)
                {
                    // History is an interaction aid, not a second persistence
                    // store. Drop the oldest snapshot to keep long-running
                    // editor sessions bounded in memory.
                    undoHistory.RemoveAt(0);
                }
                redoHistory.Clear();
                LastChangeSet = changeSet;
                LastAction = command.Description;
            }
            else if (!receipt.accepted)
            {
                LastChangeSet = null;
                LastAction = receipt.message;
            }
            else
            {
                LastChangeSet = null;
            }

            return receipt;
        }

        public bool Undo()
        {
            if (undoHistory.Count == 0)
            {
                LastAction = "没有可撤销操作";
                return false;
            }

            var entry = undoHistory[undoHistory.Count - 1];
            undoHistory.RemoveAt(undoHistory.Count - 1);
            redoHistory.Add(entry);
            var nextRevision = state.revision + 1;
            if (entry.changeSet == null)
            {
                state.CopyFrom(entry.before);
            }
            else
            {
                entry.changeSet.ApplyInverse(state);
            }

            state.revision = nextRevision;
            LastChangeSet = entry.changeSet;
            LastAction = "撤销：" + entry.description;
            return true;
        }

        public bool Redo()
        {
            if (redoHistory.Count == 0)
            {
                LastAction = "没有可重做操作";
                return false;
            }

            var entry = redoHistory[redoHistory.Count - 1];
            redoHistory.RemoveAt(redoHistory.Count - 1);
            undoHistory.Add(entry);
            var nextRevision = state.revision + 1;
            if (entry.changeSet == null)
            {
                state.CopyFrom(entry.after);
            }
            else
            {
                entry.changeSet.ApplyForward(state);
            }

            state.revision = nextRevision;
            LastChangeSet = entry.changeSet;
            LastAction = "重做：" + entry.description;
            return true;
        }
    }

    public interface IM1SnapshotStore
    {
        void Save(M1WorldState state);
        M1WorldState Load();
    }

    public sealed class M1MemorySnapshotStore : IM1SnapshotStore
    {
        private M1WorldState snapshot;

        public void Save(M1WorldState state)
        {
            snapshot = state.DeepClone();
        }

        public M1WorldState Load()
        {
            if (snapshot == null)
            {
                throw new InvalidOperationException("No snapshot has been saved.");
            }

            return snapshot.DeepClone();
        }
    }

    public static class M1VerticalSlice
    {
        public static M1CommandBus CreateDemoBus()
        {
            var bus = new M1CommandBus(
                M1WorldState.CreateEmpty(),
                new M1LocalAuthority(new AllowAllRulePolicy()));

            Execute(bus, new M1CreateProjectCommand("m1-create-project", 0, "project-m1", "Sundoll M1", "map-m1"));
            Execute(bus, new M1PaintCellCommand("m1-paint-cell", bus.State.revision, 2, 3, "placeholder-ground"));
            Execute(bus, new M1PublishMapContentCommand("m1-publish-map", bus.State.revision, "map-content-m1-v1"));
            Execute(bus, new M1CreateScenarioCommand("m1-create-scenario", bus.State.revision, "scenario-m1", "board-m1"));
            Execute(bus, new M1CreatePieceCommand(
                "m1-create-piece",
                bus.State.revision,
                "piece-definition-m1",
                "piece-instance-m1",
                "青色几何棋子",
                "placeholder-cyan-square"));
            Execute(bus, new M1PlacePieceCommand("m1-place-piece", bus.State.revision, 0, 0));
            Execute(bus, new M1MovePieceCommand("m1-move-piece", bus.State.revision, 1, 0));

            if (!bus.State.HasCompleteVerticalSlice())
            {
                throw new InvalidOperationException("M1 vertical slice did not reach a complete state.");
            }

            return bus;
        }

        private static void Execute(M1CommandBus bus, M1Command command)
        {
            var receipt = bus.Execute(command);
            if (!receipt.accepted)
            {
                throw new InvalidOperationException(receipt.message);
            }
        }
    }
}
