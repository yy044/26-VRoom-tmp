using UnityEngine;

public class ActiveFacePositionProviderRouter : MonoBehaviour, IFacePositionProvider
{
    [Header("Mode")]
    [Tooltip("Mobile mode controller used to decide whether front AR or back 2D face data is active.")]
    [SerializeField] private MobileARModeController modeController;

    [Header("Providers")]
    [Tooltip("Provider used when the mobile mode is FaceSubtitle or FrontFaceAR.")]
    [SerializeField] private MonoBehaviour frontProviderBehaviour;

    [Tooltip("Provider used when the mobile mode is BackFace2D.")]
    [SerializeField] private MonoBehaviour backProviderBehaviour;

    [Header("Fallback")]
    [Tooltip("If enabled, missing serialized references are found in the scene on Awake.")]
    [SerializeField] private bool autoBindOnAwake = true;

    private IFacePositionProvider frontProvider;
    private IFacePositionProvider backProvider;
    private MobileARModeController.MobileTrackingMode lastMode;
    private bool hasLastMode;

    public bool HasFace => ActiveProvider != null && IsActiveProviderEnabled() && ActiveProvider.HasFace;
    public Vector2 NormalizedFaceCenter => HasFace ? ActiveProvider.NormalizedFaceCenter : Vector2.zero;
    public Rect NormalizedFaceRect => HasFace ? ActiveProvider.NormalizedFaceRect : Rect.zero;
    public string SourceName => ActiveProvider != null && IsActiveProviderEnabled() ? ActiveProvider.SourceName : "No Active Face Provider";

    private IFacePositionProvider ActiveProvider
    {
        get
        {
            ResolveProviders();

            if (modeController == null)
                return frontProvider != null && frontProvider.HasFace ? frontProvider : backProvider;

            switch (modeController.CurrentMode)
            {
                case MobileARModeController.MobileTrackingMode.FaceSubtitle:
                case MobileARModeController.MobileTrackingMode.FrontFaceAR:
                    return frontProvider;

                case MobileARModeController.MobileTrackingMode.BackFace2D:
                    return backProvider;

                default:
                    return null;
            }
        }
    }

    private void Awake()
    {
        if (autoBindOnAwake)
            AutoBind();

        ResolveProviders();
        TrackModeChange();
    }

    private void Update()
    {
        TrackModeChange();
    }

    private void OnValidate()
    {
        ResolveProviders();
    }

    private void AutoBind()
    {
        if (modeController == null)
            modeController = FindFirstObjectByType<MobileARModeController>(FindObjectsInactive.Include);

        if (frontProviderBehaviour == null)
            frontProviderBehaviour = FindFirstObjectByType<MobileARFaceTrackingRunner>(FindObjectsInactive.Include);

        if (backProviderBehaviour == null)
            backProviderBehaviour = FindFirstObjectByType<BackCameraFacePositionProvider>(FindObjectsInactive.Include);
    }

    private void ResolveProviders()
    {
        frontProvider = frontProviderBehaviour as IFacePositionProvider;
        backProvider = backProviderBehaviour as IFacePositionProvider;
    }

    private bool IsActiveProviderEnabled()
    {
        MonoBehaviour activeBehaviour = GetActiveProviderBehaviour();
        return activeBehaviour != null && activeBehaviour.isActiveAndEnabled;
    }

    private MonoBehaviour GetActiveProviderBehaviour()
    {
        if (modeController == null)
            return frontProviderBehaviour != null && frontProviderBehaviour.isActiveAndEnabled
                ? frontProviderBehaviour
                : backProviderBehaviour;

        switch (modeController.CurrentMode)
        {
            case MobileARModeController.MobileTrackingMode.FaceSubtitle:
            case MobileARModeController.MobileTrackingMode.FrontFaceAR:
                return frontProviderBehaviour;

            case MobileARModeController.MobileTrackingMode.BackFace2D:
                return backProviderBehaviour;

            default:
                return null;
        }
    }

    private void TrackModeChange()
    {
        if (modeController == null)
            return;

        if (!hasLastMode)
        {
            lastMode = modeController.CurrentMode;
            hasLastMode = true;
            return;
        }

        if (lastMode == modeController.CurrentMode)
            return;

        lastMode = modeController.CurrentMode;
        ClearInactiveProviderReferences();
    }

    private void ClearInactiveProviderReferences()
    {
        if (frontProviderBehaviour != GetActiveProviderBehaviour())
            frontProvider = null;

        if (backProviderBehaviour != GetActiveProviderBehaviour())
            backProvider = null;

        ResolveProviders();
    }
}
