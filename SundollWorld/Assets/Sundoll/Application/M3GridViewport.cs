using System;
using Sundoll.Core;

namespace Sundoll.Application
{
    public struct M3GridBounds
    {
        private readonly bool hasValue;
        private readonly int minX;
        private readonly int minY;
        private readonly int maxX;
        private readonly int maxY;

        public M3GridBounds(int minX, int minY, int maxX, int maxY)
        {
            if (minX > maxX || minY > maxY)
            {
                throw new ArgumentException("Grid bounds must have a non-empty range.");
            }

            hasValue = true;
            this.minX = minX;
            this.minY = minY;
            this.maxX = maxX;
            this.maxY = maxY;
        }

        public static M3GridBounds Empty => default(M3GridBounds);
        public bool IsEmpty => !hasValue;
        public int MinX => minX;
        public int MinY => minY;
        public int MaxX => maxX;
        public int MaxY => maxY;
        public int Width => IsEmpty ? 0 : maxX - minX + 1;
        public int Height => IsEmpty ? 0 : maxY - minY + 1;
        public int CellCount => Width * Height;

        public M3GridBounds Include(int x, int y)
        {
            if (IsEmpty)
            {
                return new M3GridBounds(x, y, x, y);
            }

            return new M3GridBounds(
                Math.Min(minX, x),
                Math.Min(minY, y),
                Math.Max(maxX, x),
                Math.Max(maxY, y));
        }

        public M3GridBounds Include(M3GridBounds other)
        {
            if (other.IsEmpty)
            {
                return this;
            }

            if (IsEmpty)
            {
                return other;
            }

            return new M3GridBounds(
                Math.Min(minX, other.minX),
                Math.Min(minY, other.minY),
                Math.Max(maxX, other.maxX),
                Math.Max(maxY, other.maxY));
        }

        public M3GridBounds ClampToMap(int mapWidth, int mapHeight)
        {
            if (IsEmpty || mapWidth <= 0 || mapHeight <= 0)
            {
                return Empty;
            }

            var clampedMinX = Math.Max(0, minX);
            var clampedMinY = Math.Max(0, minY);
            var clampedMaxX = Math.Min(mapWidth - 1, maxX);
            var clampedMaxY = Math.Min(mapHeight - 1, maxY);
            return clampedMinX > clampedMaxX || clampedMinY > clampedMaxY
                ? Empty
                : new M3GridBounds(clampedMinX, clampedMinY, clampedMaxX, clampedMaxY);
        }

        public bool Contains(int x, int y)
        {
            return !IsEmpty && x >= minX && x <= maxX && y >= minY && y <= maxY;
        }

        public override string ToString()
        {
            return IsEmpty ? "空" : $"({minX},{minY})–({maxX},{maxY})";
        }
    }

    public sealed class M3DirtyRegion
    {
        private M3GridBounds bounds = M3GridBounds.Empty;

        public bool IsEmpty => bounds.IsEmpty;
        public M3GridBounds Bounds => bounds;

        public void Include(int x, int y)
        {
            bounds = bounds.Include(x, y);
        }

        public void Include(M3GridBounds region)
        {
            bounds = bounds.Include(region);
        }

        public void Clear()
        {
            bounds = M3GridBounds.Empty;
        }
    }

    public static class M3GridViewport
    {
        public static M3GridBounds CalculateVisibleBounds(
            int mapWidth,
            int mapHeight,
            float viewportWidth,
            float viewportHeight,
            float panX,
            float panY,
            float cellPixels)
        {
            if (mapWidth <= 0 || mapHeight <= 0 || viewportWidth <= 0f ||
                viewportHeight <= 0f || cellPixels <= 0f)
            {
                return M3GridBounds.Empty;
            }

            var minColumn = (int)Math.Floor((0f - panX) / cellPixels);
            var maxColumn = (int)Math.Ceiling((viewportWidth - panX) / cellPixels) - 1;
            var minRowFromTop = (int)Math.Floor((0f - panY) / cellPixels);
            var maxRowFromTop = (int)Math.Ceiling((viewportHeight - panY) / cellPixels) - 1;

            var bounds = new M3GridBounds(
                minColumn,
                mapHeight - 1 - maxRowFromTop,
                maxColumn,
                mapHeight - 1 - minRowFromTop);
            return bounds.ClampToMap(mapWidth, mapHeight);
        }

        public static M3GridBounds FullMapBounds(int mapWidth, int mapHeight)
        {
            return mapWidth <= 0 || mapHeight <= 0
                ? M3GridBounds.Empty
                : new M3GridBounds(0, 0, mapWidth - 1, mapHeight - 1);
        }
    }
}
