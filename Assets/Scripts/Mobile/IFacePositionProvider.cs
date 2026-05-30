using UnityEngine;

public interface IFacePositionProvider
{
    bool HasFace { get; }
    Vector2 NormalizedFaceCenter { get; }
    Rect NormalizedFaceRect { get; }
    string SourceName { get; }
}
