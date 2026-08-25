using System;
using System.Collections.Generic;

namespace Sundoll.Core
{
    public enum M3MapObjectAction
    {
        Add = 0,
        Open = 1,
        Close = 2,
        Toggle = 3,
        RotateClockwise = 4
    }

    [Serializable]
    public sealed class M3MapObjectCommandPayload
    {
        public string objectId;
        public int kind;
        public int x;
        public int y;
        public int rotation;
        public int action;
    }

    /// <summary>
    /// Stable-ID map object command. Object edits use the existing command bus;
    /// the bus keeps a deep before/after delta for this non-cell domain branch.
    /// </summary>
    public sealed class M3MapObjectCommand : M1Command
    {
        private readonly string objectId;
        private readonly M3MapObjectKind kind;
        private readonly int x;
        private readonly int y;
        private readonly int rotation;
        private readonly M3MapObjectAction action;

        public M3MapObjectCommand(
            string commandId,
            int baseRevision,
            string objectId,
            M3MapObjectKind kind,
            int x,
            int y,
            int rotation,
            M3MapObjectAction action)
            : base(commandId, baseRevision)
        {
            if (string.IsNullOrWhiteSpace(objectId))
            {
                throw new ArgumentException("Map object ID is required.", nameof(objectId));
            }

            this.objectId = objectId;
            this.kind = kind;
            this.x = x;
            this.y = y;
            this.rotation = M3MapObject.NormalizeRotation(rotation);
            this.action = action;
        }

        public override string Description
        {
            get
            {
                switch (action)
                {
                    case M3MapObjectAction.Add:
                        return "添加" + (kind == M3MapObjectKind.Door ? "门" : "箱子") + " " + objectId;
                    case M3MapObjectAction.Open:
                        return "打开对象 " + objectId;
                    case M3MapObjectAction.Close:
                        return "关闭对象 " + objectId;
                    case M3MapObjectAction.Toggle:
                        return "切换对象 " + objectId;
                    default:
                        return "旋转对象 " + objectId;
                }
            }
        }

        public override string CommandType => "M3.MapObject";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M3MapObjectCommandPayload
        {
            objectId = objectId,
            kind = (int)kind,
            x = x,
            y = y,
            rotation = rotation,
            action = (int)action
        };

        public override void Apply(M1WorldState state)
        {
            if (state == null || state.map == null)
            {
                throw new InvalidOperationException("Map does not exist.");
            }

            if (state.map.objects == null)
            {
                state.map.objects = new List<M3MapObject>();
            }

            if (action == M3MapObjectAction.Add)
            {
                if (x < 0 || x >= state.map.width || y < 0 || y >= state.map.height)
                {
                    throw new InvalidOperationException("Map object is outside the map.");
                }

                if (Find(state.map.objects, objectId) != null)
                {
                    throw new InvalidOperationException("Map object ID already exists: " + objectId);
                }

                state.map.objects.Add(new M3MapObject
                {
                    id = objectId,
                    kind = kind,
                    x = x,
                    y = y,
                    rotation = rotation,
                    state = M3MapObjectOpenState.Closed
                });
                return;
            }

            var mapObject = Find(state.map.objects, objectId);
            if (mapObject == null)
            {
                throw new InvalidOperationException("Map object was not found: " + objectId);
            }

            switch (action)
            {
                case M3MapObjectAction.Open:
                    mapObject.state = M3MapObjectOpenState.Open;
                    break;
                case M3MapObjectAction.Close:
                    mapObject.state = M3MapObjectOpenState.Closed;
                    break;
                case M3MapObjectAction.Toggle:
                    mapObject.state = mapObject.state == M3MapObjectOpenState.Open
                        ? M3MapObjectOpenState.Closed
                        : M3MapObjectOpenState.Open;
                    break;
                case M3MapObjectAction.RotateClockwise:
                    mapObject.rotation = M3MapObject.NormalizeRotation(mapObject.rotation + 90);
                    break;
                default:
                    throw new InvalidOperationException("Unknown map object action.");
            }
        }

        private static M3MapObject Find(List<M3MapObject> objects, string id)
        {
            foreach (var mapObject in objects)
            {
                if (mapObject != null && string.Equals(mapObject.id, id, StringComparison.Ordinal))
                {
                    return mapObject;
                }
            }

            return null;
        }
    }
}
