using System;
using System.Collections.Generic;

namespace Sundoll.Core
{
    [Serializable]
    public sealed class M1CreateProjectCommandPayload
    {
        public string projectId;
        public string projectName;
        public string mapId;
    }

    [Serializable]
    public sealed class M1PaintCellCommandPayload
    {
        public int x;
        public int y;
        public string contentId;
    }

    [Serializable]
    public sealed class M1PublishMapContentCommandPayload
    {
        public string contentVersionId;
    }

    [Serializable]
    public sealed class M1CreateScenarioCommandPayload
    {
        public string scenarioId;
        public string boardId;
    }

    [Serializable]
    public sealed class M1CreatePieceCommandPayload
    {
        public string definitionId;
        public string instanceId;
        public string displayName;
        public string visualKey;
    }

    [Serializable]
    public sealed class M1PlacePieceCommandPayload
    {
        public int x;
        public int y;
    }

    [Serializable]
    public sealed class M1MovePieceCommandPayload
    {
        public int x;
        public int y;
    }

    [Serializable]
    public sealed class M3PaintCellsCommandPayload
    {
        public List<M3CellMutation> mutations = new List<M3CellMutation>();
    }

    [Serializable]
    public sealed class M1CommandEnvelope
    {
        public int formatVersion = 1;
        public string commandType;
        public int payloadVersion;
        public string commandId;
        public int baseRevision;
        public string payloadJson;
    }

    [Serializable]
    public sealed class AcceptedOperationBatch
    {
        public int formatVersion = 1;
        public string actorId = "local";
        public int revisionBefore;
        public int revisionAfter;
        public M1CommandEnvelope commandEnvelope;
        public WorldChangeSet changeSet;
    }
}
