using System;
using System.Collections.Generic;

namespace Sundoll.Core
{
    [Serializable]
    public sealed class M5CreateMapSlotCommandPayload
    {
        public string mapId;
        public string displayName;
        public int width;
        public int height;
    }

    [Serializable]
    public sealed class M5SwitchMapCommandPayload
    {
        public string mapId;
    }

    [Serializable]
    public sealed class M5RenameMapCommandPayload
    {
        public string mapId;
        public string displayName;
    }

    [Serializable]
    public sealed class M5SetFogCommandPayload
    {
        public string mapId;
        public int x;
        public int y;
        public bool revealed;
    }

    [Serializable]
    public sealed class M5SetFogBatchCommandPayload
    {
        public string mapId;
        public List<M5FogCellMutation> mutations = new List<M5FogCellMutation>();
    }

    [Serializable]
    public sealed class M5UpsertAnnotationCommandPayload
    {
        public string annotationId;
        public string mapId;
        public int x;
        public int y;
        public string text;
        public string colorHex;
        public bool visible;
    }

    [Serializable]
    public sealed class M5RemoveAnnotationCommandPayload
    {
        public string annotationId;
    }

    [Serializable]
    public sealed class M5SetInteractionStateCommandPayload
    {
        public string objectId;
        public bool open;
    }
}
