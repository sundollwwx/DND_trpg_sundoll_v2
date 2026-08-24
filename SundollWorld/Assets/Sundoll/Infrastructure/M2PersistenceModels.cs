using System;
using System.Collections.Generic;
using Sundoll.Core;

namespace Sundoll.Infrastructure
{
    [Serializable]
    public sealed class M2Head
    {
        public int formatVersion = 1;
        public int worldSchemaVersion = 1;
        public string activeSaveRevisionId;
        public string activeJournalStreamId;
        public long generation;
        public string lastKnownGoodRevisionId;
    }

    [Serializable]
    public sealed class M2FileRecord
    {
        public string relativePath;
        public long byteLength;
        public string sha256;
    }

    [Serializable]
    public sealed class M2RevisionManifest
    {
        public int formatVersion = 1;
        public int worldSchemaVersion = 1;
        public string saveRevisionId;
        public string parentRevisionId;
        public string journalStreamId;
        public long journalOperationSequence;
        public int snapshotWorldRevision;
        public string canonicalStateHash;
        public string savedUtc;
        public List<M2FileRecord> files = new List<M2FileRecord>();
    }

    [Serializable]
    public sealed class M2PackageManifest
    {
        public int formatVersion = 1;
        public string saveRevisionId;
        public string canonicalStateHash;
        public List<string> entries = new List<string>();
    }

    [Serializable]
    public sealed class M2ContentRef
    {
        public string sha256;
        public string extension;
        public string mimeType;
        public long byteLength;
        public string relativePath;
        public string kind;
    }

    [Serializable]
    public sealed class M2AcceptedOperationBatch
    {
        public int formatVersion = 1;
        public string commandId;
        public string operationType;
        public string description;
        public string actorId = "local";
        public int worldRevision;
        public long operationSequence;
        public string stateJson;
        public string canonicalStateHash;
    }

    [Serializable]
    internal sealed class M2JournalRecord
    {
        public int formatVersion = 1;
        public long operationSequence;
        public string payloadJson;
        public string payloadSha256;
    }

    public enum M2SaveFaultPoint
    {
        BeforeRevisionCommit = 0,
        BeforeHeadCommit = 1,
        AfterHeadCommit = 2
    }

    public sealed class M2SaveResult
    {
        public string saveRevisionId;
        public string parentRevisionId;
        public string canonicalStateHash;
        public string headPath;
        public string revisionPath;
        public long generation;
    }

    public sealed class M2LoadResult
    {
        public M1WorldState state;
        public M2Head head;
        public M2RevisionManifest manifest;
        public string source;
        public string diagnostic;
    }

    public sealed class M2ValidationResult
    {
        public bool valid;
        public string source;
        public string saveRevisionId;
        public string canonicalStateHash;
        public string diagnostic;
    }
}
