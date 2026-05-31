using UnityEngine;

[System.Serializable]
public struct FaceCoordinateTransformSettings
{
    public bool mirrorX;
    public bool invertY;
    public bool swapXY;
    public bool rotate90Clockwise;
    public bool rotate90CounterClockwise;

    public FaceCoordinateTransformSettings(
        bool mirrorX,
        bool invertY,
        bool swapXY,
        bool rotate90Clockwise,
        bool rotate90CounterClockwise)
    {
        this.mirrorX = mirrorX;
        this.invertY = invertY;
        this.swapXY = swapXY;
        this.rotate90Clockwise = rotate90Clockwise;
        this.rotate90CounterClockwise = rotate90CounterClockwise;
    }

    public static FaceCoordinateTransformSettings CurrentMobileDefault =>
        new FaceCoordinateTransformSettings(
            mirrorX: true,
            invertY: true,
            swapXY: false,
            rotate90Clockwise: false,
            rotate90CounterClockwise: false);

    public static FaceCoordinateTransformSettings Back2DDefault =>
        new FaceCoordinateTransformSettings(
            mirrorX: true,
            invertY: true,
            swapXY: false,
            rotate90Clockwise: false,
            rotate90CounterClockwise: false);

    public override string ToString()
    {
        return $"mirrorX:{mirrorX},invertY:{invertY},swapXY:{swapXY},rotate90Clockwise:{rotate90Clockwise},rotate90CounterClockwise:{rotate90CounterClockwise}";
    }
}

public static class FaceCoordinateTransform
{
    public static Vector2 TransformPoint(Vector2 point, FaceCoordinateTransformSettings settings)
    {
        return TransformPoint(
            point,
            settings.mirrorX,
            settings.invertY,
            settings.swapXY,
            settings.rotate90Clockwise,
            settings.rotate90CounterClockwise);
    }

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

    public static Rect TransformRect(Rect rect, FaceCoordinateTransformSettings settings)
    {
        return TransformRect(
            rect,
            settings.mirrorX,
            settings.invertY,
            settings.swapXY,
            settings.rotate90Clockwise,
            settings.rotate90CounterClockwise);
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
