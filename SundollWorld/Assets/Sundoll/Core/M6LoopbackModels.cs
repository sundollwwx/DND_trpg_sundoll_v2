using System;
using System.Collections.Generic;

namespace Sundoll.Core
{
    [Serializable]
    public sealed class M6AudiencePolicy
    {
        public bool revealAllFog = true;
        public bool includeHiddenPieces = true;
        public bool includePrivatePieceState;
        public List<string> allowedPieceInstanceIds = new List<string>();

        public M6AudiencePolicy DeepClone()
        {
            return new M6AudiencePolicy
            {
                revealAllFog = revealAllFog,
                includeHiddenPieces = includeHiddenPieces,
                includePrivatePieceState = includePrivatePieceState,
                allowedPieceInstanceIds = allowedPieceInstanceIds == null
                    ? new List<string>()
                    : new List<string>(allowedPieceInstanceIds)
            };
        }
    }

    [Serializable]
    public sealed class M6ProjectionSnapshot
    {
        public int protocolVersion = 1;
        public string audienceId;
        public int worldRevision;
        public string canonicalStateHash;
        public string stateJson;
    }

    [Serializable]
    public sealed class M6ProjectionDelta
    {
        public int protocolVersion = 1;
        public long sequence;
        public string audienceId;
        public int revisionBefore;
        public int revisionAfter;
        public string commandId;
        public M1CommandEnvelope commandEnvelope;
        public string canonicalStateHash;
    }
}
