using System;
using System.Collections.Generic;
using Sundoll.Core;

namespace Sundoll.Application
{
    [Serializable]
    public sealed class M3ClipboardCell
    {
        public int offsetX;
        public int offsetY;
        public string layerId;
        public string contentId;

        public M3ClipboardCell Clone()
        {
            return new M3ClipboardCell
            {
                offsetX = offsetX,
                offsetY = offsetY,
                layerId = layerId,
                contentId = contentId
            };
        }
    }

    /// <summary>
    /// A transient selection payload. It intentionally has no persistence path:
    /// a workspace can restore its view state without restoring stale clipboard
    /// content from a previous editing session.
    /// </summary>
    [Serializable]
    public sealed class M3MapClipboard
    {
        public int width;
        public int height;
        public List<M3ClipboardCell> cells = new List<M3ClipboardCell>();

        public bool IsEmpty => cells == null || cells.Count == 0;

        public M3MapClipboard Clone()
        {
            var clone = new M3MapClipboard
            {
                width = width,
                height = height
            };
            if (cells != null)
            {
                foreach (var cell in cells)
                {
                    if (cell != null)
                    {
                        clone.cells.Add(cell.Clone());
                    }
                }
            }

            return clone;
        }

        public M3MapClipboard RotateClockwise()
        {
            var rotated = new M3MapClipboard
            {
                width = height,
                height = width
            };
            if (cells == null)
            {
                return rotated;
            }

            foreach (var cell in cells)
            {
                if (cell == null)
                {
                    continue;
                }

                rotated.cells.Add(new M3ClipboardCell
                {
                    offsetX = height - 1 - cell.offsetY,
                    offsetY = cell.offsetX,
                    layerId = cell.layerId,
                    contentId = cell.contentId
                });
            }

            return rotated;
        }
    }
}
