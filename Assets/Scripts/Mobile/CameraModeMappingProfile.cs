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

    public CameraModeMappingProfile(
        bool previewMirrorX,
        int previewRotationDegrees,
        FaceCoordinateTransformSettings faceCoordinateTransform)
    {
        this.previewMirrorX = previewMirrorX;
        this.previewRotationDegrees = previewRotationDegrees;
        this.faceCoordinateTransform = faceCoordinateTransform;
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
}
