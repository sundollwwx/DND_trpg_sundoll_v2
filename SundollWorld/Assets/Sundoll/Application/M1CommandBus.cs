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
                message = message
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
            if (receipts.TryGetValue(command.CommandId, out var existing))
            {
                return existing.CloneForDuplicate();
            }

            var receipt = new M1CommandReceipt
            {
                commandId = command.CommandId,
                revisionBefore = state.revision,
                revisionAfter = state.revision,
                message = command.Description
            };

            if (command.BaseRevision != state.revision)
            {
                receipt.conflict = true;
                receipt.message = $"Revision 冲突：命令基于 {command.BaseRevision}，当前为 {state.revision}";
                receipts[command.CommandId] = receipt;
                return receipt;
            }

            if (!rulePolicy.Allow(state, command, out var reason))
            {
                receipt.message = reason;
                receipts[command.CommandId] = receipt;
                return receipt;
            }

            command.Apply(state);
            state.revision++;
            receipt.accepted = true;
            receipt.revisionAfter = state.revision;
            receipts[command.CommandId] = receipt;
            return receipt;
        }
    }

    public sealed class M1CommandBus
    {
        private sealed class HistoryEntry
        {
            public M1WorldState before;
            public M1WorldState after;
            public string description;
        }

        private readonly M1WorldState state;
        private readonly M1LocalAuthority authority;
        private readonly List<HistoryEntry> undoHistory = new List<HistoryEntry>();
        private readonly List<HistoryEntry> redoHistory = new List<HistoryEntry>();

        public M1CommandBus(M1WorldState state, M1LocalAuthority authority)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
        }

        public M1WorldState State => state;
        public string LastAction { get; private set; } = "尚未执行操作";

        public M1CommandReceipt Execute(M1Command command)
        {
            var before = state.DeepClone();
            var receipt = authority.Execute(state, command);
            if (receipt.accepted && !receipt.duplicate)
            {
                undoHistory.Add(new HistoryEntry
                {
                    before = before,
                    after = state.DeepClone(),
                    description = command.Description
                });
                redoHistory.Clear();
                LastAction = command.Description;
            }
            else if (!receipt.accepted)
            {
                LastAction = receipt.message;
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
            state.CopyFrom(entry.before);
            state.revision = nextRevision;
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
            state.CopyFrom(entry.after);
            state.revision = nextRevision;
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
