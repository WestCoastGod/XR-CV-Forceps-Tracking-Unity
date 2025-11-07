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

    [Header("Camera Display Settings")]
    [Tooltip("Adjust camera brightness (0=no change, positive=brighter, negative=darker)")]
    [SerializeField] private float cameraBrightness = 0.0f;
    [Tooltip("Adjust camera contrast (1.0=no change, >1.0=more contrast, <1.0=less contrast)")]
    [SerializeField] private float cameraContrast = 1.0f;

    [Header("Smoothing Settings")]
    [Tooltip("One Euro Filter minimum cutoff frequency for position - USE 0.05 FOR SMOOTH TRACKING! (1.0 = jittery)")]
    [SerializeField] private float positionMinCutoff = 0.05f;
    [Tooltip("One Euro Filter beta (speed coefficient) for position - USE 0.0 FOR SMOOTH TRACKING! (0.1 = jittery)")]  
    [SerializeField] private float positionBeta = 0.0f;
    [Tooltip("One Euro Filter minimum cutoff frequency for rotation - USE 0.05 FOR SMOOTH TRACKING! (1.0 = jittery)")]
    [SerializeField] private float rotationMinCutoff = 0.05f;
    [Tooltip("One Euro Filter beta (speed coefficient) for rotation - USE 0.0 FOR SMOOTH TRACKING! (0.1 = jittery)")]
    [SerializeField] private float rotationBeta = 0.0f;

    [Header("Clamp Control (Marker Visibility)")]
    [SerializeField] private bool enableClampControl = true;
    [SerializeField] private Transform upperClamp;
    [SerializeField] private Transform lowerClamp;
    
    [Header("Marker Visibility Detection (Method 13)")]
    [Tooltip("Use marker visibility detection - clamps CLOSE when ANY marker is VISIBLE, OPEN when ALL are hidden (OR logic)")]
    [SerializeField] private bool useMarkerVisibility = true;
    [Tooltip("Marker IDs to monitor - clamps CLOSE when ANY of these markers is VISIBLE")]
    [SerializeField] private List<int> visibilityMarkerIDs = new List<int> { 8 };
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
    
    // Flag to prevent clamp animation when object is grabbed
    private bool freezeClampAnimation = false;
    
    // Target clamp rotations when frozen (for precise ball holding)
    private Quaternion? frozenUpperClampRotation = null;
    private Quaternion? frozenLowerClampRotation = null;

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

        // METHOD 13: Simple marker visibility check for clamp control
        if (enableClampControl && clampInitialized && useMarkerVisibility)
        {
            CheckMarkerVisibilityAndControlClamp();
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
        // Draw 2D projected axes on the canvas texture
        if (drawCanvasAxes && resultTexture != null && cameraAnchor != null)
        {
            arucoTracker.DrawWorldAxesOnImage(cubePos, cubeRot, cameraAnchor, axesLength, resultTexture);
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
    /// METHOD 13: Simple marker visibility check for clamp control.
    /// Checks if specified markers (visibilityMarkerIDs) are ALL visible to control clamps.
    /// No second cube tracking needed - just single marker detection.
    /// </summary>
    private void CheckMarkerVisibilityAndControlClamp()
    {
        // Check if any markers 6-11 are visible
        List<int> detectedMarkerIds;
        List<Vector3[]> detectedMarkerCorners;
        
        bool anyMarkerDetected = cornerExtractor.GetDetectedMarkerCorners(
            new List<int> { 6, 7, 8, 9, 10, 11 }, 
            out detectedMarkerIds, 
            out detectedMarkerCorners) && detectedMarkerIds.Count > 0;
        
        if (anyMarkerDetected)
        {
            // OR logic: Check if ANY specified marker is visible
            // Clamps CLOSE when ANY marker is VISIBLE, OPEN when ALL are hidden
            bool anyMarkerVisible = false;
            List<int> visibleMarkers = new List<int>();
            List<int> hiddenMarkers = new List<int>();
            
            foreach (int markerID in visibilityMarkerIDs)
            {
                if (detectedMarkerIds.Contains(markerID))
                {
                    visibleMarkers.Add(markerID);
                    anyMarkerVisible = true;
                }
                else
                {
                    hiddenMarkers.Add(markerID);
                }
            }
            
            Debug.Log($"[DETECTION] {detectedMarkerIds.Count} markers detected: [{string.Join(", ", detectedMarkerIds)}] - Visible: [{string.Join(", ", visibleMarkers)}], Hidden: [{string.Join(", ", hiddenMarkers)}], Any visible: {anyMarkerVisible}");
            
            // anyMarkerVisible=true means at least one marker visible → close clamps → pass true
            UpdateMarkerVisibility(anyMarkerVisible);
        }
        else
        {
            // No markers detected - all markers are hidden
            Debug.Log($"[DETECTION] NO markers detected (6-11) - All markers hidden");
            // All markers hidden means OPEN → pass false (markers not visible → visibilityFilteredValue→1.0→open)
            UpdateMarkerVisibility(false);
        }
        
        // Control clamps based on visibility
        ControlClampByVisibility();
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
            
            Debug.Log($"[VISIBILITY] Markers [{string.Join(", ", visibilityMarkerIDs)}] DETECTED (any visible) - ConsecVisible: {consecutiveVisibleFrames}, ConsecInvisible: 0, FilteredValue: {visibilityFilteredValue:F3}");
            
            // Require multiple frames to confirm visibility (prevents jitter)
            if (consecutiveVisibleFrames >= VISIBILITY_CONFIRMATION_FRAMES)
            {
                markerWasVisible = true;
                float oldValue = visibilityFilteredValue;
                // Markers visible → CLOSE clamps → visibilityFilteredValue = 0.0
                visibilityFilteredValue = Mathf.Lerp(visibilityFilteredValue, 0.0f, visibilitySmoothing);
                Debug.Log($"[VISIBILITY] Confirmed MARKERS VISIBLE (>={VISIBILITY_CONFIRMATION_FRAMES} frames) - CLOSING clamps - Lerping {oldValue:F3} → {visibilityFilteredValue:F3} (smoothing: {visibilitySmoothing})");
            }
        }
        else
        {
            consecutiveInvisibleFrames++;
            consecutiveVisibleFrames = 0;
            
            Debug.Log($"[VISIBILITY] Markers [{string.Join(", ", visibilityMarkerIDs)}] NOT DETECTED (all hidden) - ConsecVisible: 0, ConsecInvisible: {consecutiveInvisibleFrames}, FilteredValue: {visibilityFilteredValue:F3}");
            
            // Require multiple frames to confirm invisibility
            if (consecutiveInvisibleFrames >= VISIBILITY_CONFIRMATION_FRAMES)
            {
                markerWasVisible = false;
                float oldValue = visibilityFilteredValue;
                // Markers hidden → OPEN clamps → visibilityFilteredValue = 1.0
                visibilityFilteredValue = Mathf.Lerp(visibilityFilteredValue, 1.0f, visibilitySmoothing);
                Debug.Log($"[VISIBILITY] Confirmed MARKERS HIDDEN (>={VISIBILITY_CONFIRMATION_FRAMES} frames) - OPENING clamps - Lerping {oldValue:F3} → {visibilityFilteredValue:F3} (smoothing: {visibilitySmoothing})");
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
        
        // Don't animate if clamps are frozen (object grabbed)
        if (freezeClampAnimation)
        {
            // If we have frozen target rotations, move smoothly to them
            if (frozenUpperClampRotation.HasValue && frozenLowerClampRotation.HasValue)
            {
                float freezeAnimSpeed = 10.0f; // Fast movement to target
                upperClamp.localRotation = Quaternion.Slerp(upperClamp.localRotation, frozenUpperClampRotation.Value, Time.deltaTime * freezeAnimSpeed);
                lowerClamp.localRotation = Quaternion.Slerp(lowerClamp.localRotation, frozenLowerClampRotation.Value, Time.deltaTime * freezeAnimSpeed);
                
                // Log freeze status periodically
                if (Time.frameCount % 120 == 0)
                {
                    Debug.Log($"[RigidCube] Clamp animation FROZEN - Target: Upper={frozenUpperClampRotation.Value.eulerAngles}, Lower={frozenLowerClampRotation.Value.eulerAngles}");
                }
            }
            return; // Don't run visibility-based animation
        }

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
    
    /// <summary>
    /// PUBLIC API: Freeze clamp animation (called when object is grabbed)
    /// </summary>
    public void FreezeClampAnimation(bool freeze)
    {
        freezeClampAnimation = freeze;
        if (freeze)
        {
            // Store current rotations as frozen target
            if (upperClamp != null && lowerClamp != null)
            {
                frozenUpperClampRotation = upperClamp.localRotation;
                frozenLowerClampRotation = lowerClamp.localRotation;
                Debug.Log($"[RigidCube] Clamp animation FROZEN at current angle - Upper: {frozenUpperClampRotation.Value.eulerAngles}, Lower: {frozenLowerClampRotation.Value.eulerAngles}");
            }
            else
            {
                Debug.Log("[RigidCube] Clamp animation FROZEN - object grabbed");
            }
        }
        else
        {
            frozenUpperClampRotation = null;
            frozenLowerClampRotation = null;
            Debug.Log("[RigidCube] Clamp animation UNFROZEN - object released");
        }
    }
    
    /// <summary>
    /// PUBLIC API: Set specific target angle for frozen clamps (for precise ball holding)
    /// </summary>
    public void SetFrozenClampRotations(Quaternion upperRot, Quaternion lowerRot)
    {
        frozenUpperClampRotation = upperRot;
        frozenLowerClampRotation = lowerRot;
        Debug.Log($"[RigidCube] Set frozen clamp target rotations - Upper: {upperRot.eulerAngles}, Lower: {lowerRot.eulerAngles}");
    }
}
