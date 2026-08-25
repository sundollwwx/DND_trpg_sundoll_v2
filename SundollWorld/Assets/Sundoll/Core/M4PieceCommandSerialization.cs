using System;
using System.Collections.Generic;

namespace Sundoll.Core
{
    [Serializable]
    public sealed class M4RegisterPieceAssetCommandPayload
    {
        public string assetId;
        public string sha256;
        public string extension;
        public string mimeType;
        public long byteLength;
        public string relativePath;
        public string thumbnailSha256;
        public string thumbnailRelativePath;
    }

    [Serializable]
    public sealed class M4CreatePieceDefinitionCommandPayload
    {
        public string definitionId;
        public string displayName;
        public string category;
        public List<string> tags = new List<string>();
        public string assetId;
        public int footprintWidth = 1;
        public int footprintHeight = 1;
    }

    [Serializable]
    public sealed class M4CreatePieceInstanceCommandPayload
    {
        public string definitionId;
        public string instanceId;
    }

    [Serializable]
    public sealed class M4PlacePieceCommandPayload
    {
        public string instanceId;
        public int x;
        public int y;
    }

    [Serializable]
    public sealed class M4MovePieceCommandPayload
    {
        public string instanceId;
        public int x;
        public int y;
    }

    [Serializable]
    public sealed class M4MovePieceToContainerCommandPayload
    {
        public string instanceId;
        public string containerPieceId;
    }

    [Serializable]
    public sealed class M4AttachPieceCommandPayload
    {
        public string instanceId;
        public string targetPieceId;
        public string attachmentSlot;
    }

    [Serializable]
    public sealed class M4DetachPieceCommandPayload
    {
        public string instanceId;
    }

    [Serializable]
    public sealed class M4SetPiecePresentationCommandPayload
    {
        public string instanceId;
        public int rotation;
        public bool flipped;
        public bool visible;
    }
}
