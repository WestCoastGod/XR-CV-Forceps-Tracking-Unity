using System.Collections.Generic;
using UnityEngine;
using PassthroughCameraSamples;
using TryAR.MarkerTracking;
using UnityEngine.UI;
using UnityEngine.XR;

// RigidCubeAxesMinimal
// Detect ArUco markers on the cube, estimate one rigid-body pose from all visible corners,
// and render simple 3D XYZ axes at the cube center. Uses marker visibility detection for clamp control.
public class RigidCubeAxesMinimal : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private WebCamTextureManager webCamManager;
    [SerializeField] private Transform cameraAnchor;
    [SerializeField] private ArUcoMarkerTracking arucoTracker;
    [SerializeField] private RawImage resultRawImage; // optional 2D output

    [Header("Cube Model (for stereo alignment testing)")]
    [SerializeField] private Transform cubeModel; // drag cube model transform here
    [SerializeField] private bool showCubeModel = true;
    [SerializeField] private bool trackCubeModel = true;

    [Header("Orientation Adjustment")]
    [Tooltip("Adjust cube model orientation relative to tracked pose")]
    [SerializeField] private Vector3 cubeRotationOffset = Vector3.zero; // Euler angles
    [Tooltip("Adjust cube model position relative to tracked pose")]
    [SerializeField] private Vector3 cubePositionOffset = Vector3.zero; // Local offset
    [Tooltip("Scale adjustment for cube model and attached forceps")]
    [SerializeField] private float cubeScaleMultiplier = 1.0f;

    [Header("ArUco Setup")]
    [SerializeField] private ArUcoMarkerTracking.ArUcoDictionary dictionary = ArUcoMarkerTracking.ArUcoDictionary.DICT_4X4_50;
    [Tooltip("Marker side length in meters. Must match your printed markers")]
    [SerializeField] private float markerLengthMeters = 0.065f;

    [Header("Axes 2D Canvas Only")]
    [SerializeField] private float axesLength = 0.05f; // 5 cm
    [Tooltip("Show axes projected on 2D canvas only (no 3D world axes)")]
    [SerializeField] private bool drawCanvasAxes = true;
    [Tooltip("Show axes for second cube (markers 6-11) on canvas")]
    [SerializeField] private bool drawSecondCubeAxes = true;

    [Header("Camera Display Settings")]
    [Tooltip("Adjust camera brightness (0=no change, positive=brighter, negative=darker)")]
    [SerializeField] private float cameraBrightness = 0.0f;
    [Tooltip("Adjust camera contrast (1.0=no change, >1.0=more contrast, <1.0=less contrast)")]
    [SerializeField] private float cameraContrast = 1.0f;

    [Header("Smoothing Settings")]
    [Tooltip("One Euro Filter minimum cutoff frequency for position")]
    [SerializeField] private float positionMinCutoff = 1.0f;
    [Tooltip("One Euro Filter beta (speed coefficient) for position")]  
    [SerializeField] private float positionBeta = 0.1f;
    [Tooltip("One Euro Filter minimum cutoff frequency for rotation")]
    [SerializeField] private float rotationMinCutoff = 1.0f;
    [Tooltip("One Euro Filter beta (speed coefficient) for rotation")]
    [SerializeField] private float rotationBeta = 0.1f;

    [Header("Clamp Control (Second Cube)")]
    [SerializeField] private bool enableClampControl = true;
    [SerializeField] private Transform upperClamp;
    [SerializeField] private Transform lowerClamp;
    
    [Header("Marker Visibility Detection (Method 13)")]
    [Tooltip("Use marker visibility detection - clamps close when marker is NOT visible")]
    [SerializeField] private bool useMarkerVisibility = true;
    [Tooltip("Marker ID to monitor for visibility (typically 6 - the marker that gets occluded when squeezing)")]
    [SerializeField] private int visibilityMarkerID = 6;
    [Tooltip("Smoothing for visibility-based control (prevents jitter from detection dropouts)")]
    [SerializeField] private float visibilitySmoothing = 0.5f;
    [SerializeField] private float clampAnimationDuration = 0.3f;
    [SerializeField] private AnimationCurve clampAnimationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private MarkerCornerExtractor cornerExtractor;
    private Texture2D resultTexture;

    // One Euro Filters for smooth tracking
    private OneEuroFilterVec3 positionFilter;
    private OneEuroFilterQuat rotationFilter;
    private OneEuroFilter scaleFilter; // Add scale smoothing
    private float lastUpdateTime;

    private struct PoseData { public Vector3 position; public Quaternion rotation; }
    private PoseData? lastValidPose;

    // Second cube tracking
    private Vector3 lastSecondCubeCenter = Vector3.zero;
    private Quaternion lastSecondCubeRotation = Quaternion.identity;
    private OneEuroFilterVec3 secondCubePositionFilter;
    private OneEuroFilterQuat secondCubeRotationFilter;
    private bool secondCubeTrackingInitialized = false;

    // Method 13: Marker visibility tracking
    private bool markerWasVisible = true; // Start assuming visible
    private float visibilityFilteredValue = 1.0f; // 1.0 = visible, 0.0 = not visible
    private int consecutiveVisibleFrames = 0;
    private int consecutiveInvisibleFrames = 0;
    private const int VISIBILITY_CONFIRMATION_FRAMES = 3; // Require N frames to confirm state change

    // Clamp control variables
    private Quaternion upperClampOpenRot;
    private Quaternion lowerClampOpenRot;
    private Quaternion upperClampClosedRot;
    private Quaternion lowerClampClosedRot;
    private Coroutine currentClampAnimation;
    private bool clampInitialized = false;

    private void Start()
    {
        // Ensure corner extractor exists and configure later
        cornerExtractor = GetComponent<MarkerCornerExtractor>();
        if (cornerExtractor == null) cornerExtractor = gameObject.AddComponent<MarkerCornerExtractor>();
        StartCoroutine(Init());
    }

    private System.Collections.IEnumerator Init()
    {
        // Wait camera permission
        while (PassthroughCameraPermissions.HasCameraPermission != true)
            yield return new WaitForSeconds(0.05f);

        // Init camera
        var eye = webCamManager.Eye;
        var intr = PassthroughCameraUtils.GetCameraIntrinsics(eye);
        webCamManager.RequestedResolution = intr.Resolution;
        webCamManager.enabled = true;
        while (webCamManager.WebCamTexture == null) yield return null;
        while (webCamManager.WebCamTexture.width < 32) yield return null;

        // Init ArUco
        arucoTracker.SetDictionary(dictionary);
        arucoTracker.SetMarkerLength(Mathf.Max(0.005f, markerLengthMeters));
        arucoTracker.Initialize(intr.Resolution.x, intr.Resolution.y, intr.PrincipalPoint.x, intr.PrincipalPoint.y, intr.FocalLength.x, intr.FocalLength.y);

        // Configure extractor to share frames/poses
        cornerExtractor.Configure(arucoTracker, webCamManager, cameraAnchor);

        // Initialize One Euro Filters
        positionFilter = new OneEuroFilterVec3(positionMinCutoff, positionBeta);
        rotationFilter = new OneEuroFilterQuat(rotationMinCutoff, rotationBeta);
        scaleFilter = new OneEuroFilter(); // Initialize scale filter with default params
        lastUpdateTime = Time.time;

        // Initialize filters for second cube
        secondCubePositionFilter = new OneEuroFilterVec3(positionMinCutoff, positionBeta);
        secondCubeRotationFilter = new OneEuroFilterQuat(rotationMinCutoff, rotationBeta);

        // Prepare optional 2D output texture
        if (resultRawImage != null)
        {
            int div = Mathf.Max(1, arucoTracker.DivideNumber);
            resultTexture = new Texture2D(intr.Resolution.x / div, intr.Resolution.y / div, TextureFormat.RGB24, false);
            resultRawImage.texture = resultTexture;
        }

        // Note: Only using 2D canvas axes - no 3D world axes created
        Debug.Log("RigidCubeAxesMinimal initialized - 2D canvas axes only");

        // Configure cube model
        if (cubeModel != null)
        {
            cubeModel.gameObject.SetActive(showCubeModel);
            Debug.Log($"Cube model configured: visible={showCubeModel}, tracking={trackCubeModel}");
        }
        else
        {
            Debug.LogWarning("No cube model assigned - only 3D axes will be visible");
        }

        // Initialize clamp rotations
        if (enableClampControl)
        {
            InitializeClampRotations();
        }
    }

    private void Update()
    {
        if (webCamManager.WebCamTexture == null || !arucoTracker.IsReady) return;

        // Update camera anchor from passthrough
        var eye = webCamManager.Eye;
        var camPose = PassthroughCameraUtils.GetCameraPoseInWorld(eye);
        if (cameraAnchor != null) { cameraAnchor.position = camPose.position; cameraAnchor.rotation = camPose.rotation; }

        // Run detection (writes into optional UI texture)
        try 
        { 
            arucoTracker.DetectMarker(webCamManager.WebCamTexture, resultTexture, cameraContrast, cameraBrightness); 
        }
        catch (System.Exception ex) { Debug.LogError($"DetectMarker failed: {ex.Message}"); return; }

        // Extract corners/ids for our cube markers (0..5)
        List<int> ids; List<Vector3[]> worldCorners;
        if (!cornerExtractor.GetDetectedMarkerCorners(new List<int> { 0, 1, 2, 3, 4, 5 }, out ids, out worldCorners) || ids.Count == 0)
            return;

        // Map to cube CAD corners (object space) from DiceCADModel
        var (boardCorners, boardIds) = DiceCADModel.GetOpenCVBoardDefinition();
        var objCorners = new List<Vector3[]>(); var detCorners = new List<Vector3[]>(); var usedIds = new List<int>();
        for (int i = 0; i < ids.Count; i++)
        {
            int idx = boardIds.IndexOf(ids[i]);
            if (idx >= 0)
            {
                objCorners.Add(boardCorners[idx]);
                detCorners.Add(worldCorners[i]);
                usedIds.Add(ids[i]);
            }
        }
        if (usedIds.Count == 0) return;

        // Estimate rigid pose using board approach (more stable than Horn)
        Vector3 cubePos; Quaternion cubeRot;
        if (!arucoTracker.EstimateBoardPoseWorld(boardCorners.ToArray(), boardIds.ToArray(), cameraAnchor, out cubePos, out cubeRot))
            return;

        // Apply One Euro Filter smoothing with adaptive parameters based on marker count
        float currentTime = Time.time;
        float deltaTime = currentTime - lastUpdateTime;
        lastUpdateTime = currentTime;
        
        if (deltaTime > 0.001f) // Avoid division by zero
        {
            // Adaptive smoothing: stronger filtering with fewer markers
            float adaptiveMinCutoff = positionMinCutoff;
            float adaptiveBeta = positionBeta;
            
            if (usedIds.Count <= 2)
            {
                // Fewer markers = more smoothing (lower cutoff, lower beta)
                adaptiveMinCutoff *= 0.5f;
                adaptiveBeta *= 0.5f;
            }
            
            // Update filter parameters dynamically
            positionFilter.minCutoff = adaptiveMinCutoff;
            positionFilter.beta = adaptiveBeta;
            rotationFilter.minCutoff = rotationMinCutoff * (usedIds.Count <= 2 ? 0.5f : 1.0f);
            rotationFilter.beta = rotationBeta * (usedIds.Count <= 2 ? 0.5f : 1.0f);
            
            cubePos = positionFilter.Filter(cubePos, deltaTime);
            cubeRot = rotationFilter.Filter(cubeRot, deltaTime);
        }
        
        lastValidPose = new PoseData { position = cubePos, rotation = cubeRot };

        // Track second cube (markers 6-11) for clamp control
        if (enableClampControl && clampInitialized)
        {
            TrackSecondCubeAndControlClamp(cubePos, cubeRot);
        }

        // Update cube model with adjustments
        if (cubeModel != null && trackCubeModel)
        {
            // Apply position offset (in local cube space)
            Vector3 adjustedPos = cubePos + cubeRot * cubePositionOffset;
            
            // Apply rotation offset
            Quaternion rotOffset = Quaternion.Euler(cubeRotationOffset);
            Quaternion adjustedRot = cubeRot * rotOffset;
            
            // Apply scale with smoothing
            float targetScale = cubeScaleMultiplier;
            float smoothedScale = scaleFilter.Filter(targetScale, deltaTime);
            Vector3 adjustedScale = Vector3.one * smoothedScale;
            
            cubeModel.position = adjustedPos;
            cubeModel.rotation = adjustedRot;
            cubeModel.localScale = adjustedScale;
        }        // Draw 2D projected axes on the canvas texture
        if (drawCanvasAxes && resultTexture != null && cameraAnchor != null)
        {
            arucoTracker.DrawWorldAxesOnImage(cubePos, cubeRot, cameraAnchor, axesLength, resultTexture);
            
            // Draw second cube axes if enabled and detected
            if (drawSecondCubeAxes && lastSecondCubeCenter != Vector3.zero)
            {
                arucoTracker.DrawWorldAxesOnImage(lastSecondCubeCenter, lastSecondCubeRotation, cameraAnchor, axesLength, resultTexture);
            }
        }
    }

    private static Vector3 ComputeCadCenterOffset(List<Vector3[]> boardCorners)
    {
        if (boardCorners == null || boardCorners.Count == 0) return Vector3.zero;
        Vector3 sum = Vector3.zero; int n = 0;
        for (int i = 0; i < boardCorners.Count; i++)
        {
            var c = boardCorners[i]; if (c == null || c.Length != 4) continue;
            sum += (c[0] + c[1] + c[2] + c[3]) / 4f; n++;
        }
        return n > 0 ? (sum / n) : Vector3.zero;
    }

    /// <summary>
    /// Initialize clamp rotations following ForcepsController pattern
    /// </summary>
    private void InitializeClampRotations()
    {
        if (upperClamp == null || lowerClamp == null)
        {
            Debug.LogWarning("Upper or lower clamp not assigned - clamp control disabled");
            enableClampControl = false;
            return;
        }

        // Define clamp rotations exactly like ForcepsController.cs
        // Open positions (default)
        upperClampOpenRot = Quaternion.Euler(-45f, -90f, 90f);
        lowerClampOpenRot = Quaternion.Euler(-45f, 90f, -90f);
        
        // Closed positions  
        upperClampClosedRot = Quaternion.Euler(-90f, -90f, 90f);
        lowerClampClosedRot = Quaternion.Euler(-90f, 90f, -90f);
        
        // Set initial open position
        upperClamp.localRotation = upperClampOpenRot;
        lowerClamp.localRotation = lowerClampOpenRot;
        
        clampInitialized = true;
        Debug.Log("Clamp rotations initialized to open position");
    }

    /// <summary>
    /// Track second cube (markers 6-11) and control clamp angle based on rotation-validated distance.
    /// Uses rotation alignment to validate both cubes are tracking the same rigid object,
    /// then measures distance in a shared local coordinate frame for stability.
    /// METHOD 13: Can also use marker visibility detection (if useMarkerVisibility=true).
    /// </summary>
    private void TrackSecondCubeAndControlClamp(Vector3 trackingCubePos, Quaternion trackingCubeRot)
    {
        // Extract corners for second cube markers (6-11)
        List<int> secondCubeIds;
        List<Vector3[]> secondCubeCorners;
        
        if (!cornerExtractor.GetDetectedMarkerCorners(new List<int> { 6, 7, 8, 9, 10, 11 }, 
            out secondCubeIds, out secondCubeCorners) || secondCubeIds.Count == 0)
        {
            // Second cube not detected - keep current clamp state
            lastSecondCubeCenter = Vector3.zero;
            lastSecondCubeRotation = Quaternion.identity;
            secondCubeTrackingInitialized = false;
            
            // Method 13: If using visibility detection, marker not visible = closed
            if (useMarkerVisibility)
            {
                UpdateMarkerVisibility(false);
            }
            return;
        }

        // METHOD 13: Marker Visibility Detection
        // Check if the specific marker is visible and use that for clamp control
        if (useMarkerVisibility)
        {
            bool markerVisible = secondCubeIds.Contains(visibilityMarkerID);
            UpdateMarkerVisibility(markerVisible);
            
            // Control clamps based on visibility (visible = open, not visible = closed)
            ControlClampByVisibility();
            
            // Still track position for visualization, but don't use distance-based control
            if (Time.frameCount % 60 == 0)
            {
                Debug.Log($"[METHOD 13] Marker {visibilityMarkerID} visible: {markerVisible}, Clamp state: {(visibilityFilteredValue > 0.5f ? "OPEN" : "CLOSED")}, Detected markers: {secondCubeIds.Count}");
            }
        }

        // Get board definition for second cube (markers 6-11 use same geometry as 0-5)
        var (boardCorners, boardIds) = DiceCADModel.GetOpenCVBoardDefinition();
        
        // Map detected markers to board corners
        var objCorners = new List<Vector3[]>();
        var detCorners = new List<Vector3[]>();
        var usedIds = new List<int>();
        
        for (int i = 0; i < secondCubeIds.Count; i++)
        {
            // Map marker IDs 6-11 to board definition 0-5
            int mappedId = secondCubeIds[i] - 6; // 6->0, 7->1, 8->2, 9->3, 10->4, 11->5
            int idx = boardIds.IndexOf(mappedId);
            
            if (idx >= 0)
            {
                objCorners.Add(boardCorners[idx]);
                detCorners.Add(secondCubeCorners[i]);
                usedIds.Add(secondCubeIds[i]);
            }
        }
        
        if (usedIds.Count == 0)
        {
            lastSecondCubeCenter = Vector3.zero;
            lastSecondCubeRotation = Quaternion.identity;
            secondCubeTrackingInitialized = false;
            return;
        }

        // Estimate rigid pose using board approach (same as tracking cube)
        Vector3 secondCubePos;
        Quaternion secondCubeRot;
        
        if (!arucoTracker.EstimateBoardPoseWorld(objCorners.ToArray(), usedIds.ToArray(), cameraAnchor, out secondCubePos, out secondCubeRot))
        {
            lastSecondCubeCenter = Vector3.zero;
            lastSecondCubeRotation = Quaternion.identity;
            secondCubeTrackingInitialized = false;
            return;
        }

        // Apply One Euro Filter smoothing to second cube
        float currentTime = Time.time;
        float deltaTime = currentTime - lastUpdateTime;
        
        if (deltaTime > 0.001f) // Avoid division by zero
        {
            // Adaptive smoothing for second cube based on marker count
            float adaptiveMinCutoff = positionMinCutoff;
            float adaptiveBeta = positionBeta;
            
            if (usedIds.Count <= 2)
            {
                // Stronger smoothing with fewer markers
                adaptiveMinCutoff *= 0.5f;
                adaptiveBeta *= 0.5f;
            }
            
            if (secondCubeTrackingInitialized)
            {
                // Update filter parameters and apply smoothing
                secondCubePositionFilter.minCutoff = adaptiveMinCutoff;
                secondCubePositionFilter.beta = adaptiveBeta;
                secondCubeRotationFilter.minCutoff = rotationMinCutoff * (usedIds.Count <= 2 ? 0.5f : 1.0f);
                secondCubeRotationFilter.beta = rotationBeta * (usedIds.Count <= 2 ? 0.5f : 1.0f);
                
                secondCubePos = secondCubePositionFilter.Filter(secondCubePos, deltaTime);
                secondCubeRot = secondCubeRotationFilter.Filter(secondCubeRot, deltaTime);
            }
            else
            {
                // First frame - initialize filters
                secondCubePositionFilter.Filter(secondCubePos, deltaTime);
                secondCubeRotationFilter.Filter(secondCubeRot, deltaTime);
                secondCubeTrackingInitialized = true;
            }
        }
        
        // Store smoothed pose
        lastSecondCubeCenter = secondCubePos;
        lastSecondCubeRotation = secondCubeRot;
    }

    /// <summary>
    /// Calculate center position from multiple marker corners
    /// </summary>
    private Vector3 CalculateCenterFromCorners(List<Vector3[]> corners)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        
        foreach (var markerCorners in corners)
        {
            if (markerCorners != null && markerCorners.Length == 4)
            {
                // Average the 4 corners of each marker
                foreach (var corner in markerCorners)
                {
                    sum += corner;
                    count++;
                }
            }
        }
        
        return count > 0 ? sum / count : Vector3.zero;
    }

    /// <summary>
    /// METHOD 13: Update marker visibility state with smoothing to prevent jitter.
    /// Uses frame confirmation to avoid false positives from temporary detection failures.
    /// </summary>
    /// <param name="isVisible">Whether the target marker is currently visible</param>
    private void UpdateMarkerVisibility(bool isVisible)
    {
        if (isVisible)
        {
            consecutiveVisibleFrames++;
            consecutiveInvisibleFrames = 0;
            
            // Require multiple frames to confirm visibility (prevents jitter)
            if (consecutiveVisibleFrames >= VISIBILITY_CONFIRMATION_FRAMES)
            {
                markerWasVisible = true;
                // Smooth transition to visible state
                visibilityFilteredValue = Mathf.Lerp(visibilityFilteredValue, 1.0f, visibilitySmoothing);
            }
        }
        else
        {
            consecutiveInvisibleFrames++;
            consecutiveVisibleFrames = 0;
            
            // Require multiple frames to confirm invisibility
            if (consecutiveInvisibleFrames >= VISIBILITY_CONFIRMATION_FRAMES)
            {
                markerWasVisible = false;
                // Smooth transition to invisible state
                visibilityFilteredValue = Mathf.Lerp(visibilityFilteredValue, 0.0f, visibilitySmoothing);
            }
        }
    }

    /// <summary>
    /// METHOD 13: Control clamp animation based on marker visibility.
    /// Visible marker = handles open = clamps open
    /// Hidden marker = handles squeezed = clamps closed
    /// </summary>
    private void ControlClampByVisibility()
    {
        if (upperClamp == null || lowerClamp == null) return;

        // Map visibility to clamp state: 1.0 (visible) = open, 0.0 (not visible) = closed
        float targetClampState = visibilityFilteredValue;
        
        // Interpolate clamp rotations
        Quaternion targetUpperRot = Quaternion.Slerp(upperClampClosedRot, upperClampOpenRot, targetClampState);
        Quaternion targetLowerRot = Quaternion.Slerp(lowerClampClosedRot, lowerClampOpenRot, targetClampState);
        
        // Smooth animation
        float animSpeed = 1.0f / clampAnimationDuration;
        upperClamp.localRotation = Quaternion.Slerp(upperClamp.localRotation, targetUpperRot, Time.deltaTime * animSpeed);
        lowerClamp.localRotation = Quaternion.Slerp(lowerClamp.localRotation, targetLowerRot, Time.deltaTime * animSpeed);
    }
}
