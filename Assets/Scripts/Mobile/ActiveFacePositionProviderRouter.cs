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

    [Header("Coordinate Mapping")]
    [Tooltip("Front AR uses ARCamera screen projection coordinates. Tune this separately from Back 2D because origin, handedness, and mirroring can differ by camera/source.")]
    [SerializeField] private FaceCoordinateTransformSettings frontARTransform = FaceCoordinateTransformSettings.CurrentMobileDefault;

    [Tooltip("Back 2D provider rotates raw MediaPipe texture coordinates into preview orientation. This transform only converts top-left preview space into Unity bottom-left UI space.")]
    [SerializeField] private FaceCoordinateTransformSettings back2DTransform = FaceCoordinateTransformSettings.Back2DDefault;

    [Header("Fallback")]
    [Tooltip("If enabled, missing serialized references are found in the scene on Awake.")]
    [SerializeField] private bool autoBindOnAwake = true;

    private IFacePositionProvider frontProvider;
    private IFacePositionProvider backProvider;
    private MobileARModeController.MobileTrackingMode lastMode;
    private bool hasLastMode;
    private string lastCoordinateMappingState;
    private string lastActivePathState;

    public bool HasFace => ActiveProvider != null && IsActiveProviderEnabled() && ActiveProvider.HasFace;
    public Vector2 NormalizedFaceCenter => HasFace ? ActiveProvider.NormalizedFaceCenter : Vector2.zero;
    public Rect NormalizedFaceRect => HasFace ? ActiveProvider.NormalizedFaceRect : Rect.zero;
    public string SourceName => ActiveProvider != null && IsActiveProviderEnabled() ? ActiveProvider.SourceName : "No Active Face Provider";
    public MobileARModeController.MobileTrackingMode CurrentMode => modeController != null ? modeController.CurrentMode : MobileARModeController.MobileTrackingMode.FaceSubtitle;
    public FaceCoordinateTransformSettings CurrentTransformSettings => IsBackProviderActive() ? back2DTransform : frontARTransform;
    public string CurrentProviderName
    {
        get
        {
            IFacePositionProvider provider = ActiveProvider;
            return provider != null ? provider.SourceName : "None";
        }
    }

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
        LogActivePath("Update");
        LogCoordinateMapping();
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

    private bool IsBackProviderActive()
    {
        if (modeController == null)
            return backProvider != null && backProvider.HasFace && !(frontProvider != null && frontProvider.HasFace);

        return modeController.CurrentMode == MobileARModeController.MobileTrackingMode.BackFace2D;
    }

    private void TrackModeChange()
    {
        if (modeController == null)
            return;

        if (!hasLastMode)
        {
            lastMode = modeController.CurrentMode;
            hasLastMode = true;
            LogActivePath("InitialMode");
            return;
        }

        if (lastMode == modeController.CurrentMode)
            return;

        lastMode = modeController.CurrentMode;
        ClearInactiveProviderReferences();
        LogActivePath("ModeChanged");
    }

    private void ClearInactiveProviderReferences()
    {
        if (frontProviderBehaviour != GetActiveProviderBehaviour())
            frontProvider = null;

        if (backProviderBehaviour != GetActiveProviderBehaviour())
            backProvider = null;

        ResolveProviders();
    }

    private void LogCoordinateMapping()
    {
        IFacePositionProvider provider = ActiveProvider;
        bool hasFace = HasFace;
        Vector2 rawCenter = hasFace ? provider.NormalizedFaceCenter : Vector2.zero;
        Rect rawBounds = hasFace ? provider.NormalizedFaceRect : Rect.zero;
        FaceCoordinateTransformSettings settings = CurrentTransformSettings;
        Vector2 transformedCenter = hasFace ? FaceCoordinateTransform.TransformPoint(rawCenter, settings) : Vector2.zero;
        Rect transformedBounds = hasFace ? FaceCoordinateTransform.TransformRect(rawBounds, settings) : Rect.zero;
        bool boundsValid = rawBounds.width > 0.001f && rawBounds.height > 0.001f;
        string providerSource = provider != null ? provider.SourceName : "None";
        string state =
            $"activeMode={CurrentMode} provider={providerSource} transform={settings} hasFace={hasFace} boundsValid={boundsValid} " +
            $"rawCenter={rawCenter} " +
            $"rawBounds=min({rawBounds.xMin:0.000},{rawBounds.yMin:0.000}) max({rawBounds.xMax:0.000},{rawBounds.yMax:0.000}) size({rawBounds.width:0.000},{rawBounds.height:0.000}) " +
            $"transformedCenter={transformedCenter} " +
            $"transformedBounds=min({transformedBounds.xMin:0.000},{transformedBounds.yMin:0.000}) max({transformedBounds.xMax:0.000},{transformedBounds.yMax:0.000}) size({transformedBounds.width:0.000},{transformedBounds.height:0.000})";

        if (state == lastCoordinateMappingState)
            return;

        lastCoordinateMappingState = state;
        Debug.Log($"[FaceCoordinateMapping] consumer=Router {state}", this);
    }

    private void LogActivePath(string reason)
    {
        IFacePositionProvider provider = ActiveProvider;
        MonoBehaviour activeBehaviour = GetActiveProviderBehaviour();
        string state =
            $"reason={reason} " +
            $"mode={CurrentMode} " +
            $"CurrentProvider={CurrentProviderName} " +
            $"providerBehaviour={(activeBehaviour != null ? activeBehaviour.GetType().Name : "null")} " +
            $"providerEnabled={(activeBehaviour != null && activeBehaviour.isActiveAndEnabled)} " +
            $"transform={CurrentTransformSettings} " +
            $"frontTransform={frontARTransform} " +
            $"backTransform={back2DTransform} " +
            $"hasFace={(provider != null && provider.HasFace)}";

        if (state == lastActivePathState)
            return;

        lastActivePathState = state;
        Debug.Log($"[ActiveFaceProviderAudit] {state}", this);
    }
}
