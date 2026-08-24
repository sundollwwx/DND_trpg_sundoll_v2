using System.Collections.Generic;

namespace Sundoll.Application
{
    public struct M3GridPoint
    {
        public int x;
        public int y;

        public M3GridPoint(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }

    public static class M3GridStrokeRasterizer
    {
        public static List<M3GridPoint> Rasterize(int startX, int startY, int endX, int endY)
        {
            var points = new List<M3GridPoint>();
            var x = startX;
            var y = startY;
            var deltaX = Abs(endX - startX);
            var stepX = startX < endX ? 1 : -1;
            var deltaY = -Abs(endY - startY);
            var stepY = startY < endY ? 1 : -1;
            var error = deltaX + deltaY;

            while (true)
            {
                points.Add(new M3GridPoint(x, y));
                if (x == endX && y == endY)
                {
                    break;
                }

                var doubledError = 2 * error;
                if (doubledError >= deltaY)
                {
                    error += deltaY;
                    x += stepX;
                }

                if (doubledError <= deltaX)
                {
                    error += deltaX;
                    y += stepY;
                }
            }

            return points;
        }

        private static int Abs(int value)
        {
            return value < 0 ? -value : value;
        }
    }
}
