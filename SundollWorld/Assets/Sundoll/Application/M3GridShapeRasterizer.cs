using System;
using System.Collections.Generic;

namespace Sundoll.Application
{
    public static class M3GridShapeRasterizer
    {
        public static List<M3GridPoint> RasterizeRectangle(int startX, int startY, int endX, int endY, bool filled)
        {
            var minX = Math.Min(startX, endX);
            var maxX = Math.Max(startX, endX);
            var minY = Math.Min(startY, endY);
            var maxY = Math.Max(startY, endY);
            var points = new List<M3GridPoint>();

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    if (filled || x == minX || x == maxX || y == minY || y == maxY)
                    {
                        points.Add(new M3GridPoint(x, y));
                    }
                }
            }

            return points;
        }

        public static List<M3GridPoint> FloodFill(
            int width,
            int height,
            int startX,
            int startY,
            Func<int, int, string> contentAt)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Map dimensions must be positive.");
            }

            if (contentAt == null)
            {
                throw new ArgumentNullException(nameof(contentAt));
            }

            if (startX < 0 || startX >= width || startY < 0 || startY >= height)
            {
                return new List<M3GridPoint>();
            }

            var targetContentId = contentAt(startX, startY);
            var visited = new bool[width * height];
            var pending = new Queue<M3GridPoint>();
            var points = new List<M3GridPoint>();
            pending.Enqueue(new M3GridPoint(startX, startY));
            visited[startY * width + startX] = true;

            while (pending.Count > 0)
            {
                var point = pending.Dequeue();
                if (!string.Equals(contentAt(point.x, point.y), targetContentId, StringComparison.Ordinal))
                {
                    continue;
                }

                points.Add(point);
                EnqueueIfMatching(point.x - 1, point.y, width, height, targetContentId, contentAt, visited, pending);
                EnqueueIfMatching(point.x + 1, point.y, width, height, targetContentId, contentAt, visited, pending);
                EnqueueIfMatching(point.x, point.y - 1, width, height, targetContentId, contentAt, visited, pending);
                EnqueueIfMatching(point.x, point.y + 1, width, height, targetContentId, contentAt, visited, pending);
            }

            return points;
        }

        private static void EnqueueIfMatching(
            int x,
            int y,
            int width,
            int height,
            string targetContentId,
            Func<int, int, string> contentAt,
            bool[] visited,
            Queue<M3GridPoint> pending)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                return;
            }

            var index = y * width + x;
            if (visited[index] || !string.Equals(contentAt(x, y), targetContentId, StringComparison.Ordinal))
            {
                return;
            }

            visited[index] = true;
            pending.Enqueue(new M3GridPoint(x, y));
        }
    }
}
