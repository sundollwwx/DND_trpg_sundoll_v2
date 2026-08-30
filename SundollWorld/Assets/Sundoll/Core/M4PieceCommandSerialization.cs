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
    public sealed class M4UpdatePieceDefinitionCommandPayload
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

    /// <summary>
    /// One destination in an atomic multi-piece move. Keeping this payload as
    /// data (rather than a screen-space drag gesture) makes it safe to replay
    /// from the Journal on another machine.
    /// </summary>
    [Serializable]
    public sealed class M4PieceMoveMutation
    {
        public string instanceId;
        public int x;
        public int y;

        public M4PieceMoveMutation()
        {
        }

        public M4PieceMoveMutation(string instanceId, int x, int y)
        {
            this.instanceId = instanceId;
            this.x = x;
            this.y = y;
        }

        public M4PieceMoveMutation DeepClone()
        {
            return new M4PieceMoveMutation(instanceId, x, y);
        }
    }

    [Serializable]
    public sealed class M4MovePiecesCommandPayload
    {
        public List<M4PieceMoveMutation> mutations = new List<M4PieceMoveMutation>();
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

    [Serializable]
    public sealed class M4PiecePresentationMutation
    {
        public string instanceId;
        public int rotation;
        public bool flipped;
        public bool visible;

        public M4PiecePresentationMutation()
        {
        }

        public M4PiecePresentationMutation(string instanceId, int rotation, bool flipped, bool visible)
        {
            this.instanceId = instanceId;
            this.rotation = rotation;
            this.flipped = flipped;
            this.visible = visible;
        }

        public M4PiecePresentationMutation DeepClone()
        {
            return new M4PiecePresentationMutation(instanceId, rotation, flipped, visible);
        }
    }

    [Serializable]
    public sealed class M4SetPiecePresentationsCommandPayload
    {
        public List<M4PiecePresentationMutation> mutations = new List<M4PiecePresentationMutation>();
    }

    [Serializable]
    public sealed class M4SetPieceStackOrderCommandPayload
    {
        public string instanceId;
        public int stackOrder;
    }

    [Serializable]
    public sealed class M4DeletePiecesCommandPayload
    {
        public List<string> instanceIds = new List<string>();
    }
}
