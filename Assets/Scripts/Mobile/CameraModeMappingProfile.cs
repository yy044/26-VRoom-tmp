using System;
using UnityEngine;

[Serializable]
public struct CameraModeMappingProfile
{
    [Tooltip("Affects the camera RawImage preview only. Does not change face dots, boxes, or subtitle anchors. Defaults only initialize/reset profiles; serialized scene values control runtime.")]
    public bool previewMirrorX;

    [Tooltip("Additional RawImage preview rotation in degrees. Affects the camera RawImage preview only. Does not change face dots, boxes, or subtitle anchors. Defaults only initialize/reset profiles; serialized scene values control runtime.")]
    public int previewRotationDegrees;

    [Tooltip("Affects face dots, bounding boxes, and subtitle anchors only. Does not change the camera RawImage preview. Defaults only initialize/reset profiles; serialized scene values control runtime.")]
    public FaceCoordinateTransformSettings faceCoordinateTransform;

    [Tooltip("UI-only face rect adjustment applied after coordinate transform. Does not change MediaPipe/provider data.")]
    public bool useRectAdjustment;

    [Tooltip("UI-only width scale for transformed face rects. Scales around rect center.")]
    public float rectWidthMultiplier;

    [Tooltip("UI-only height scale for transformed face rects. Scales around rect center.")]
    public float rectHeightMultiplier;

    [Tooltip("UI-only normalized Y offset applied after rect scaling.")]
    public float rectYOffsetNormalized;

    public CameraModeMappingProfile(
        bool previewMirrorX,
        int previewRotationDegrees,
        FaceCoordinateTransformSettings faceCoordinateTransform,
        bool useRectAdjustment = false,
        float rectWidthMultiplier = 1f,
        float rectHeightMultiplier = 1f,
        float rectYOffsetNormalized = 0f)
    {
        this.previewMirrorX = previewMirrorX;
        this.previewRotationDegrees = previewRotationDegrees;
        this.faceCoordinateTransform = faceCoordinateTransform;
        this.useRectAdjustment = useRectAdjustment;
        this.rectWidthMultiplier = rectWidthMultiplier;
        this.rectHeightMultiplier = rectHeightMultiplier;
        this.rectYOffsetNormalized = rectYOffsetNormalized;
    }

    public static CameraModeMappingProfile CreateBack2DDefault()
    {
        // Defaults seed new profiles and explicit resets. Existing serialized scenes keep
        // their stored values until the profile is reset or migrated.
        return new CameraModeMappingProfile(
            previewMirrorX: false,
            previewRotationDegrees: 0,
            faceCoordinateTransform: FaceCoordinateTransformSettings.Back2DDefault);
    }

    public static CameraModeMappingProfile CreateFrontARDefault()
    {
        // Provided for symmetry if front camera mapping is moved to profiles later.
        // It is not a runtime source until a serialized component chooses to use it.
        return new CameraModeMappingProfile(
            previewMirrorX: false,
            previewRotationDegrees: 0,
            faceCoordinateTransform: FaceCoordinateTransformSettings.CurrentMobileDefault);
    }

    public void ResetToBack2DDefault()
    {
        this = CreateBack2DDefault();
    }

    public Rect ApplyRectAdjustment(Rect rect)
    {
        if (!useRectAdjustment)
            return rect;

        float width = Mathf.Max(0f, rect.width * Mathf.Max(0f, rectWidthMultiplier));
        float height = Mathf.Max(0f, rect.height * Mathf.Max(0f, rectHeightMultiplier));
        Vector2 center = rect.center;
        center.y += rectYOffsetNormalized;

        float halfWidth = width * 0.5f;
        float halfHeight = height * 0.5f;
        return Rect.MinMaxRect(
            Mathf.Clamp01(center.x - halfWidth),
            Mathf.Clamp01(center.y - halfHeight),
            Mathf.Clamp01(center.x + halfWidth),
            Mathf.Clamp01(center.y + halfHeight));
    }

    public override string ToString()
    {
        return
            $"previewMirrorX:{previewMirrorX},previewRotationDegrees:{previewRotationDegrees}," +
            $"faceCoordinateTransform:{faceCoordinateTransform}," +
            $"useRectAdjustment:{useRectAdjustment},rectWidthMultiplier:{rectWidthMultiplier}," +
            $"rectHeightMultiplier:{rectHeightMultiplier},rectYOffsetNormalized:{rectYOffsetNormalized}";
    }
}
