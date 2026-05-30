using UnityEngine;

public static class FaceCoordinateTransform
{
    public static Vector2 TransformPoint(
        Vector2 point,
        bool mirrorX,
        bool invertY,
        bool swapXY,
        bool rotate90Clockwise,
        bool rotate90CounterClockwise)
    {
        point.x = Mathf.Clamp01(point.x);
        point.y = Mathf.Clamp01(point.y);

        if (mirrorX)
            point.x = 1f - point.x;

        if (invertY)
            point.y = 1f - point.y;

        if (swapXY)
            point = new Vector2(point.y, point.x);

        if (rotate90Clockwise)
            point = new Vector2(point.y, 1f - point.x);

        if (rotate90CounterClockwise)
            point = new Vector2(1f - point.y, point.x);

        return new Vector2(Mathf.Clamp01(point.x), Mathf.Clamp01(point.y));
    }

    public static Rect TransformRect(
        Rect rect,
        bool mirrorX,
        bool invertY,
        bool swapXY,
        bool rotate90Clockwise,
        bool rotate90CounterClockwise)
    {
        Vector2 a = TransformPoint(new Vector2(rect.xMin, rect.yMin), mirrorX, invertY, swapXY, rotate90Clockwise, rotate90CounterClockwise);
        Vector2 b = TransformPoint(new Vector2(rect.xMin, rect.yMax), mirrorX, invertY, swapXY, rotate90Clockwise, rotate90CounterClockwise);
        Vector2 c = TransformPoint(new Vector2(rect.xMax, rect.yMin), mirrorX, invertY, swapXY, rotate90Clockwise, rotate90CounterClockwise);
        Vector2 d = TransformPoint(new Vector2(rect.xMax, rect.yMax), mirrorX, invertY, swapXY, rotate90Clockwise, rotate90CounterClockwise);

        float minX = Mathf.Min(a.x, b.x, c.x, d.x);
        float minY = Mathf.Min(a.y, b.y, c.y, d.y);
        float maxX = Mathf.Max(a.x, b.x, c.x, d.x);
        float maxY = Mathf.Max(a.y, b.y, c.y, d.y);
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }
}
