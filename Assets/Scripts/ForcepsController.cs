using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class ForcepsController : MonoBehaviour
{
    [Header("Input Actions")]
    [SerializeField]
    private InputActionReference _gripAction;

    [Header("Forceps Parts")]
    [SerializeField]
    private Transform _upperClamp;
    [SerializeField]
    private Transform _lowerClamp;

    [Header("XR Interactors")]
    [SerializeField]
    [Tooltip("CustomXRDirectInteractor on upper clamp for grabbing")]
    private CustomXRDirectInteractor _upperInteractor;
    [SerializeField]
    [Tooltip("CustomXRDirectInteractor on lower clamp for grabbing")]
    private CustomXRDirectInteractor _lowerInteractor;
    
    [System.Serializable]
    public class BallSizeMapping
    {
        public string ballName;  // Object name (e.g., "Sphere S1", "Sphere M2")
        public float radius;     // Physical radius in meters
    }
    
    [Header("Ball Size Configuration")]
    [Tooltip("Object name to radius mapping for different ball sizes")]
    [SerializeField]
    private List<BallSizeMapping> ballSizeMapping = new List<BallSizeMapping>
    {
        new BallSizeMapping { ballName = "Sphere L4", radius = 0.02f },  // Large ball - 2cm radius
        new BallSizeMapping { ballName = "Sphere M2", radius = 0.015f }, // Medium ball - 1.5cm radius
        new BallSizeMapping { ballName = "Sphere M1", radius = 0.0125f }, // Medium-small ball
        new BallSizeMapping { ballName = "Sphere S1", radius = 0.01f },  // Small ball - 1cm radius
        new BallSizeMapping { ballName = "Sphere L1", radius = 0.01f }   // Small ball - 1cm radius
    };

    [Header("ArUco Control")]
    [SerializeField]
    [Tooltip("RigidCubeAxesMinimal component that controls clamp state via marker visibility")]
    private RigidCubeAxesMinimal _rigidCubeController;

    [Header("Interaction Settings")]
    [SerializeField]
    [Tooltip("List of tags that this forceps can interact with. If empty, interacts with all objects.")]
    private List<string> _interactableTags = new List<string> { "GrabbableSphere" };

    [SerializeField]
    [Tooltip("Enable debug logs for tag checking")]
    private bool _showTagDebugInfo = false;

    [Header("Animation Settings")]
    [SerializeField]
    [Range(0.1f, 2.0f)]
    [Tooltip("Duration of the open/close animation in seconds")]
    private float _animationDuration = 0.3f;

    [SerializeField]
    [Tooltip("Animation curve for easing the transition")]
    private AnimationCurve _animationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private bool _isGripPressed = false;
    private bool _isAnimating = false;

    private Quaternion _upperClampDefaultRot;
    private Quaternion _lowerClampDefaultRot;

    private Coroutine _currentAnimation;

    private bool _isObjectInUpperTrigger = false;
    private bool _isObjectInLowerTrigger = false;

    // Track grabbed objects for release
    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable _upperGrabbedObject = null;
    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable _lowerGrabbedObject = null;
    
    // Track previous clamp state to detect changes
    private bool _wasClampsClosedLastFrame = false;
    
    // Store objects in trigger for grabbing when clamps close
    private GameObject _objectInUpperTriggerToGrab = null;
    private GameObject _objectInLowerTriggerToGrab = null;
    
    // Track recently released objects to prevent immediate re-grab
    private GameObject _recentlyReleasedObject = null;
    private float _releaseTime = 0f;
    private const float RELEASE_COOLDOWN = 0.5f; // 0.5 seconds cooldown after release

    /// <summary>
    /// Check if the object has an interactable tag
    /// </summary>
    /// <param name="obj">GameObject to check</param>
    /// <returns>True if the object can be interacted with</returns>
    private bool IsObjectInteractable(GameObject obj)
    {
        if (obj == null) return false;

        // If no tags are specified, allow interaction with all objects
        if (_interactableTags == null || _interactableTags.Count == 0)
        {
            if (_showTagDebugInfo)
                Debug.Log($"No tag restrictions - allowing interaction with {obj.name}");
            return true;
        }

        // Check if object has any of the allowed tags
        bool isInteractable = _interactableTags.Contains(obj.tag);

        if (_showTagDebugInfo)
        {
            if (isInteractable)
                Debug.Log($"Object {obj.name} with tag '{obj.tag}' is interactable");
            else
                Debug.Log($"Object {obj.name} with tag '{obj.tag}' is NOT interactable. Allowed tags: [{string.Join(", ", _interactableTags)}]");
        }

        return isInteractable;
    }

    public void OnUpperTriggerEnter(GameObject other)
    {
        // Check if the object is interactable based on its tag
        if (!IsObjectInteractable(other))
        {
            if (_showTagDebugInfo)
                Debug.Log($"Upper clamp ignoring object {other.name} - not interactable");
            return;
        }

        // Handle upper clamp trigger enter logic
        Debug.Log($"Upper clamp triggered by interactable object: {other.name} (tag: {other.tag})");
        _isObjectInUpperTrigger = true;
        
        // Store object for potential grabbing when clamps close
        // Only store if not already grabbed AND not recently released
        if (_upperGrabbedObject == null)
        {
            // Check if this object was recently released (within cooldown period)
            if (_recentlyReleasedObject == other && (Time.time - _releaseTime) < RELEASE_COOLDOWN)
            {
                Debug.Log($"[Trigger] {other.name} was recently released ({Time.time - _releaseTime:F2}s ago) - ignoring for grab");
                return;
            }
            
            _objectInUpperTriggerToGrab = other;
        }
    }

    public void OnUpperTriggerExit(GameObject other)
    {
        // Check if the object was interactable
        if (!IsObjectInteractable(other))
        {
            return;
        }

        // Handle upper clamp trigger exit logic
        Debug.Log($"Upper clamp exited by interactable object: {other.name} (tag: {other.tag})");
        _isObjectInUpperTrigger = false;
        
        // DON'T clear stored object - let it persist until grabbed or replaced by another object
        // This prevents physics jitter from clearing the grab target
    }

    public void OnLowerTriggerEnter(GameObject other)
    {
        // Check if the object is interactable based on its tag
        if (!IsObjectInteractable(other))
        {
            if (_showTagDebugInfo)
                Debug.Log($"Lower clamp ignoring object {other.name} - not interactable");
            return;
        }

        // Handle lower clamp trigger enter logic
        Debug.Log($"Lower clamp triggered by interactable object: {other.name} (tag: {other.tag})");
        _isObjectInLowerTrigger = true;
        
        // Store object for potential grabbing when clamps close
        // Only store if not already grabbed AND not recently released
        if (_lowerGrabbedObject == null)
        {
            // Check if this object was recently released (within cooldown period)
            if (_recentlyReleasedObject == other && (Time.time - _releaseTime) < RELEASE_COOLDOWN)
            {
                Debug.Log($"[Trigger] {other.name} was recently released ({Time.time - _releaseTime:F2}s ago) - ignoring for grab");
                return;
            }
            
            _objectInLowerTriggerToGrab = other;
        }
    }

    public void OnLowerTriggerExit(GameObject other)
    {
        // Check if the object was interactable
        if (!IsObjectInteractable(other))
        {
            return;
        }

        // Handle lower clamp trigger exit logic
        Debug.Log($"Lower clamp exited by interactable object: {other.name} (tag: {other.tag})");
        _isObjectInLowerTrigger = false;
        
        // DON'T clear stored object - let it persist until grabbed or replaced by another object
        // This prevents physics jitter from clearing the grab target
    }

    /// <summary>
    /// Get ball radius from object name using the ballSizeMapping
    /// </summary>
    private float GetBallRadius(GameObject obj)
    {
        foreach (var mapping in ballSizeMapping)
        {
            // Check if object name contains the mapped ball name
            if (obj.name.Contains(mapping.ballName))
            {
                Debug.Log($"[BallSize] {obj.name} matches '{mapping.ballName}' -> radius {mapping.radius}m");
                return mapping.radius;
            }
        }
        
        // Default radius if name not found
        Debug.LogWarning($"[BallSize] {obj.name} not found in ballSizeMapping! Using default 0.015m");
        return 0.015f; // Default to medium ball size
    }
    
    /// <summary>
    /// Calculate the correct clamp rotation angle using GEOMETRIC calculation
    /// Based on groupmate's approach: calculates angle from clamp geometry and attach point position
    /// Returns the actual rotation angle in degrees that the clamps should close to
    /// </summary>
    private float CalculateClampRotationAngle(Transform attachPoint, float objectRadius)
    {
        if (attachPoint == null || _upperClamp == null || _lowerClamp == null)
        {
            Debug.LogWarning("[CalcAngle] Missing transforms, using default -67.5°");
            return -67.5f;  // Halfway between -45° (open) and -90° (closed)
        }
        
        // GEOMETRIC CALCULATION (based on groupmate's ForcepsControllerGeometric.cs)
        // Calculate the middle point between the two clamps
        Vector3 middle = (_upperClamp.position + _lowerClamp.position) * 0.5f;
        
        // The clamp line is the vector between upper and lower clamps
        Vector3 clampLine = (_upperClamp.position - _lowerClamp.position).normalized;
        
        // The attach direction is from the middle point to the attach point
        Vector3 attachDir = (attachPoint.position - middle).normalized;
        
        // Calculate the angle between attach direction and clamp line
        float geometricAngle = Vector3.Angle(attachDir, clampLine);
        
        Debug.Log($"[CalcAngle GEOMETRIC] AttachPoint: {attachPoint.name}");
        Debug.Log($"[CalcAngle GEOMETRIC] Middle: {middle}, ClampLine: {clampLine}, AttachDir: {attachDir}");
        Debug.Log($"[CalcAngle GEOMETRIC] Geometric angle between attachDir and clampLine: {geometricAngle:F1}°");
        
        // Convert geometric angle to clamp rotation angle
        // The geometric angle tells us how far the attach point is from the clamp line
        // We need to map this to the actual rotation angle of the clamps
        //
        // Typical mapping (based on your setup):
        // - Inner attach point (small geometric angle ~30°) → clamps close MORE → rotation angle ~-85° (near closed -90°)
        // - Middle attach point (medium geometric angle ~50°) → rotation angle ~-67.5° (halfway)
        // - Outer attach point (large geometric angle ~70°) → clamps close LESS → rotation angle ~-50° (near open -45°)
        //
        // DIRECT MAPPING: geometric angle maps to clamp rotation angle
        // Small geometric angle = large (more negative) rotation = more closed
        // Large geometric angle = small (less negative) rotation = more open
        
        float minGeometricAngle = 30f;  // Inner attach point
        float maxGeometricAngle = 70f;  // Outer attach point
        
        float openRotation = -45f;   // Fully open clamp angle
        float closedRotation = -90f; // Fully closed clamp angle
        
        // Map geometric angle to rotation angle
        // Smaller geometric angle → closer to closedRotation
        // Larger geometric angle → closer to openRotation
        float t = Mathf.Clamp01((geometricAngle - minGeometricAngle) / (maxGeometricAngle - minGeometricAngle));
        float targetRotationAngle = Mathf.Lerp(closedRotation, openRotation, t);
        
        Debug.Log($"[CalcAngle GEOMETRIC] Radius: {objectRadius}m, GeometricAngle: {geometricAngle:F1}°, " +
                  $"t: {t:F2}, TargetRotationAngle: {targetRotationAngle:F1}° (range: {closedRotation}° to {openRotation}°)");
        
        return targetRotationAngle;
    }

    /// <summary>
    /// Try to grab an object using the specified interactor
    /// </summary>
    private void TryGrabObject(GameObject obj, CustomXRDirectInteractor interactor)
    {
        Debug.Log($"[TryGrabObject] ENTRY - obj: {obj.name}, interactor: {(interactor != null ? interactor.name : "NULL")}");
        
        if (interactor == null)
        {
            Debug.LogWarning($"Interactor not assigned, cannot grab {obj.name}");
            return;
        }

        // Check if object has XRGrabInteractable
        var grabInteractable = obj.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        if (grabInteractable == null)
        {
            Debug.LogWarning($"Object {obj.name} doesn't have XRGrabInteractable component");
            return;
        }

        Debug.Log($"[TryGrabObject] About to call SelectEnter");
        
        // Manually trigger the select interaction
        var interactionManager = interactor.interactionManager;
        if (interactionManager != null)
        {
            interactionManager.SelectEnter((UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor)interactor, 
                                          (UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)grabInteractable);
            
            Debug.Log($"[TryGrabObject] SelectEnter completed, now calculating angles...");
            
            // ==== STEP 1: Get ball radius ====
            float ballRadius = GetBallRadius(obj);
            
            // ==== STEP 2: Get which attach point the CustomXRDirectInteractor selected ====
            // Don't search manually - ask the interactor which attach transform it used!
            Transform activeAttachPoint = null;
            
            // Cast to CustomXRDirectInteractor to access currentAttachTransform
            CustomXRDirectInteractor customInteractor = interactor as CustomXRDirectInteractor;
            if (customInteractor != null)
            {
                activeAttachPoint = customInteractor.currentAttachTransform;
                if (activeAttachPoint != null)
                {
                    Debug.Log($"[GRAB] CustomXRDirectInteractor selected attach point: {activeAttachPoint.name}");
                }
                else
                {
                    Debug.LogWarning($"[GRAB] CustomXRDirectInteractor.currentAttachTransform is NULL, using interactor transform");
                    activeAttachPoint = interactor.transform;
                }
            }
            else
            {
                Debug.LogWarning($"[GRAB] Interactor is not CustomXRDirectInteractor, using interactor transform");
                activeAttachPoint = interactor.transform;
            }
            
            // ==== STEP 3: Calculate correct clamp rotation angle based on attach point position ====
            float targetRotationAngle = CalculateClampRotationAngle(activeAttachPoint, ballRadius);
            
            // ==== STEP 4: Build target rotations directly from the calculated angle ====
            // Upper clamp: rotation around X-axis (first component of Euler angles)
            // Lower clamp: rotation around X-axis (first component of Euler angles)
            // Keep Y and Z the same as the closed rotation
            
            Quaternion upperClosedRot = Quaternion.Euler(-90f, -90f, 90f);  // Default closed rotation
            Quaternion lowerClosedRot = Quaternion.Euler(-90f, 90f, -90f);
            
            // Try to get actual closed rotations from RigidCubeAxesMinimal
            var rigidCubeType = typeof(RigidCubeAxesMinimal);
            var upperClosedField = rigidCubeType.GetField("upperClampClosedRot", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var lowerClosedField = rigidCubeType.GetField("lowerClampClosedRot", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (upperClosedField != null) upperClosedRot = (Quaternion)upperClosedField.GetValue(_rigidCubeController);
            if (lowerClosedField != null) lowerClosedRot = (Quaternion)lowerClosedField.GetValue(_rigidCubeController);
            
            Vector3 upperEuler = upperClosedRot.eulerAngles;
            Vector3 lowerEuler = lowerClosedRot.eulerAngles;
            
            Debug.Log($"[GRAB] Original Euler angles - Upper: {upperEuler}, Lower: {lowerEuler}");
            Debug.Log($"[GRAB] Target rotation angle from calculation: {targetRotationAngle:F1}°");
            
            // Convert targetRotationAngle to 0-360 range to match Unity's eulerAngles
            float targetAngle360 = targetRotationAngle;
            if (targetAngle360 < 0) targetAngle360 += 360f;
            
            Debug.Log($"[GRAB] Target angle converted to 0-360 range: {targetAngle360:F1}°");
            
            // Replace the X component (rotation angle) with our calculated angle
            upperEuler.x = targetAngle360;
            lowerEuler.x = targetAngle360;
            
            Quaternion targetUpperRot = Quaternion.Euler(upperEuler);
            Quaternion targetLowerRot = Quaternion.Euler(lowerEuler);
            
            Debug.Log($"[GRAB] Ball radius {ballRadius}m -> final Euler - Upper: {targetUpperRot.eulerAngles}, Lower: {targetLowerRot.eulerAngles}");
            Debug.Log($"[GRAB] Target rotations - Upper: {targetUpperRot.eulerAngles}, Lower: {targetLowerRot.eulerAngles}");
            
            // Track which object is grabbed by which interactor
            if (interactor == _upperInteractor)
            {
                _upperGrabbedObject = grabInteractable;
                _objectInUpperTriggerToGrab = null; // Clear stored reference after grabbing
                Debug.Log($"[GRAB] Upper clamp grabbed {obj.name} - Interactor: {interactor.name}");
            }
            else if (interactor == _lowerInteractor)
            {
                _lowerGrabbedObject = grabInteractable;
                _objectInLowerTriggerToGrab = null; // Clear stored reference after grabbing
                Debug.Log($"[GRAB] Lower clamp grabbed {obj.name} - Interactor: {interactor.name}");
            }
            
            // ==== STEP 6: CRITICAL - Set frozen target rotations FIRST ====
            if (_rigidCubeController != null)
            {
                // Set the calculated target rotations
                _rigidCubeController.SetFrozenClampRotations(targetUpperRot, targetLowerRot);
                
                // NOW freeze the animation - clamps will smoothly move to target angles
                Debug.Log($"[GRAB] Calling FreezeClampAnimation(true) on {_rigidCubeController.name}");
                _rigidCubeController.FreezeClampAnimation(true);
                
                // IMPORTANT: Also tell RigidCubeAxesMinimal to stop updating visibility
                // This prevents the marker visibility from forcing clamps to keep closing
                _rigidCubeController.SendMessage("PauseVisibilityControl", SendMessageOptions.DontRequireReceiver);
            }
            else
            {
                Debug.LogError("[GRAB] Cannot freeze clamps - _rigidCubeController is NULL!");
            }
            
            Debug.Log($"Successfully grabbed {obj.name} with {interactor.name}");
        }
        else
        {
            Debug.LogError("No XR Interaction Manager found in scene!");
        }
    }

    void Start()
    {
        Debug.Log("ForcepsController Start() beginning...");
        
        // Auto-find RigidCubeAxesMinimal FIRST (critical for ArUco-based control!)
        if (_rigidCubeController == null)
        {
            _rigidCubeController = FindObjectOfType<RigidCubeAxesMinimal>();
            if (_rigidCubeController == null)
            {
                Debug.LogError("[CRITICAL] RigidCubeAxesMinimal not found! ArUco-based grab/release won't work.");
            }
            else
            {
                Debug.Log($"[SUCCESS] Found RigidCubeAxesMinimal: {_rigidCubeController.name}");
            }
        }
        
        // Auto-find interactors
        if (_upperClamp != null)
        {
            if (_upperInteractor == null)
            {
                Debug.Log($"Attempting to find CustomXRDirectInteractor on {_upperClamp.name}...");
                _upperInteractor = _upperClamp.GetComponent<CustomXRDirectInteractor>();
                if (_upperInteractor == null)
                {
                    Debug.LogError($"FAILED: Upper clamp ({_upperClamp.name}) doesn't have CustomXRDirectInteractor! Ball grabbing won't work.");
                }
                else
                {
                    Debug.Log($"SUCCESS: Found CustomXRDirectInteractor on {_upperClamp.name}");
                }
            }
            else
            {
                Debug.Log($"Upper Interactor already assigned: {_upperInteractor.name}");
            }
        }

        if (_lowerClamp != null)
        {
            if (_lowerInteractor == null)
            {
                Debug.Log($"Attempting to find CustomXRDirectInteractor on {_lowerClamp.name}...");
                _lowerInteractor = _lowerClamp.GetComponent<CustomXRDirectInteractor>();
                if (_lowerInteractor == null)
                {
                    Debug.LogError($"FAILED: Lower clamp ({_lowerClamp.name}) doesn't have CustomXRDirectInteractor! Ball grabbing won't work.");
                }
                else
                {
                    Debug.Log($"SUCCESS: Found CustomXRDirectInteractor on {_lowerClamp.name}");
                }
            }
            else
            {
                Debug.Log($"Lower Interactor already assigned: {_lowerInteractor.name}");
            }
        }
        
        // Now do validation checks (but don't fail if grip action is null - we're using ArUco!)
        if (_upperClamp == null || _lowerClamp == null)
        {
            Debug.LogError("Forceps clamps not assigned!");
            return;
        }

        if (_gripAction == null)
        {
            Debug.LogWarning("Grip Action not assigned - using ArUco marker control instead.");
            // Don't return here - ArUco control doesn't need grip action!
        }
        else
        {
            // bind the grip action to the methods (only if grip action is assigned)
            _gripAction.action.performed += OnGripPressed;
            _gripAction.action.canceled += OnGripReleased;
        }

        // 1) upper opened: (-45, -90, 90)
        _upperClampDefaultRot = Quaternion.Euler(-45f, -90f, 90f);

        // 2) lower opened: (-45, 90, -90)
        _lowerClampDefaultRot = Quaternion.Euler(-45f, 90f, -90f);

        _upperClamp.localRotation = _upperClampDefaultRot;
        _lowerClamp.localRotation = _lowerClampDefaultRot;

        Debug.Log("ForcepsController initialized. Upper/Lower clamps set to default angles.");
    }

    void Update()
    {
        // Check clamp state from ArUco marker visibility (if RigidCubeAxesMinimal is available)
        if (_rigidCubeController != null)
        {
            CheckArUcoClampState();
            
            // Continuously check if clamps should be frozen (holding object)
            bool isHoldingObject = (_upperGrabbedObject != null || _lowerGrabbedObject != null);
            
            // Update freeze state based on whether we're holding something
            // This ensures clamps stay frozen as long as object is held
            if (isHoldingObject)
            {
                // Keep frozen while holding
                if (Time.frameCount % 120 == 0) // Log every 2 seconds
                {
                    Debug.Log("[Update] Object is grabbed - keeping clamps frozen");
                }
            }
        }
        else
        {
            // Only log once every 120 frames (about 2 seconds at 60fps)
            if (Time.frameCount % 120 == 0)
            {
                Debug.LogWarning("[ArUco] RigidCubeAxesMinimal is NULL - cannot check clamp state!");
            }
        }
    }

    /// <summary>
    /// Check ArUco marker visibility and grab/release accordingly
    /// </summary>
    private void CheckArUcoClampState()
    {
        // Access the visibility filtered value from RigidCubeAxesMinimal
        // > 0.5f means marker visible (clamps open), < 0.5f means marker hidden (clamps closed)
        bool areClampsClosed = !AreClamppsOpen();
        
        // Debug every 60 frames (about 1 second at 60fps)
        if (Time.frameCount % 60 == 0)
        {
            Debug.Log($"[ArUco Debug] Clamps closed: {areClampsClosed}, WasClosedLastFrame: {_wasClampsClosedLastFrame}");
        }
        
        // Detect state change from open to closed
        if (areClampsClosed && !_wasClampsClosedLastFrame)
        {
            // Clamps just closed - grab objects if we have stored references
            Debug.Log($"[ArUco] Clamps CLOSED - attempting to grab. Upper stored: {_objectInUpperTriggerToGrab != null}, Lower stored: {_objectInLowerTriggerToGrab != null}");
            
            // Trust trigger detection - if object entered trigger, it's close enough to grab
            // Distance check removed because ArUco tracking and Unity world space may have coordinate mismatches
            
            if (_objectInUpperTriggerToGrab != null)
            {
                Debug.Log($"[ArUco] Grabbing {_objectInUpperTriggerToGrab.name} with upper clamp");
                TryGrabObject(_objectInUpperTriggerToGrab, _upperInteractor);
            }
            
            if (_objectInLowerTriggerToGrab != null)
            {
                Debug.Log($"[ArUco] Grabbing {_objectInLowerTriggerToGrab.name} with lower clamp");
                TryGrabObject(_objectInLowerTriggerToGrab, _lowerInteractor);
            }
        }
        // Detect state change from closed to open
        else if (!areClampsClosed && _wasClampsClosedLastFrame)
        {
            // Clamps just opened - release grabbed objects
            Debug.Log("[ArUco] Clamps OPENED - releasing objects");
            ReleaseGrabbedObjects();
        }
        
        _wasClampsClosedLastFrame = areClampsClosed;
    }

    /// <summary>
    /// Check if clamps are currently open based on ArUco marker visibility
    /// </summary>
    private bool AreClamppsOpen()
    {
        if (_rigidCubeController == null) return true;
        
        // Use reflection to access the private visibilityFilteredValue field
        var field = typeof(RigidCubeAxesMinimal).GetField("visibilityFilteredValue", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (field != null)
        {
            float visibilityValue = (float)field.GetValue(_rigidCubeController);
            bool isOpen = visibilityValue > 0.5f;
            
            Debug.Log($"[ArUco Check] visibilityFilteredValue: {visibilityValue:F3} → isOpen: {isOpen} (threshold: 0.5)");
            
            return isOpen; // > 0.5f = open, < 0.5f = closed
        }
        
        Debug.LogError("[ArUco] Failed to access visibilityFilteredValue field via reflection!");
        return true; // Default to open if can't read value
    }

    private void OnGripPressed(InputAction.CallbackContext context)
    {
        _isGripPressed = true;
        StartSmoothAnimation(true);
        Debug.Log("Grip pressed - Forceps closing smoothly.");
    }

    private void OnGripReleased(InputAction.CallbackContext context)
    {
        _isGripPressed = false;
        
        // Release any grabbed objects
        ReleaseGrabbedObjects();
        
        StartSmoothAnimation(false);
        Debug.Log("Grip released - Forceps opening smoothly.");
    }

    /// <summary>
    /// Release all grabbed objects when grip is released
    /// </summary>
    private void ReleaseGrabbedObjects()
    {
        GameObject releasedObject = null;
        
        // Release upper clamp's object
        if (_upperInteractor != null && _upperGrabbedObject != null)
        {
            var interactionManager = _upperInteractor.interactionManager;
            if (interactionManager != null)
            {
                interactionManager.SelectExit((UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor)_upperInteractor,
                                             (UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)_upperGrabbedObject);
                Debug.Log($"Released {_upperGrabbedObject.name} from upper clamp");
                releasedObject = _upperGrabbedObject.gameObject;
            }
            _upperGrabbedObject = null;
        }
        
        // Release lower clamp's object
        if (_lowerInteractor != null && _lowerGrabbedObject != null)
        {
            var interactionManager = _lowerInteractor.interactionManager;
            if (interactionManager != null)
            {
                interactionManager.SelectExit((UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor)_lowerInteractor,
                                             (UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)_lowerGrabbedObject);
                Debug.Log($"Released {_lowerGrabbedObject.name} from lower clamp");
                releasedObject = _lowerGrabbedObject.gameObject;
            }
            _lowerGrabbedObject = null;
        }
        
        // Mark the released object and record release time to prevent immediate re-grab
        if (releasedObject != null)
        {
            _recentlyReleasedObject = releasedObject;
            _releaseTime = Time.time;
            Debug.Log($"[Release] Marked {releasedObject.name} as recently released - will ignore for {RELEASE_COOLDOWN}s");
        }
        
        // CRITICAL: Clear stored references after release to prevent auto-regrab
        // The ball has fallen away, so don't try to grab it on next close
        _objectInUpperTriggerToGrab = null;
        _objectInLowerTriggerToGrab = null;
        Debug.Log("[Release] Cleared all stored grab targets to prevent auto-regrab");
        
        // CRITICAL: Unfreeze clamp animation when object is released
        if (_rigidCubeController != null)
        {
            _rigidCubeController.FreezeClampAnimation(false);
        }
    }

    private void StartSmoothAnimation(bool closing)
    {
        // Stop any existing animation
        if (_currentAnimation != null)
        {
            StopCoroutine(_currentAnimation);
        }

        // Start new animation
        _currentAnimation = StartCoroutine(AnimateForceps(closing));
    }

    private IEnumerator AnimateForceps(bool closing)
    {
        _isAnimating = true;

        // Determine start rotations for clamps only
        Quaternion upperClampStartRot = _upperClamp.localRotation;
        Quaternion lowerClampStartRot = _lowerClamp.localRotation;

        if (closing)
        {
            // Define closed positions for clamps
            Quaternion upperClampTargetRot = Quaternion.Euler(-90f, -90f, 90f);  //close entirely
            Quaternion lowerClampTargetRot = Quaternion.Euler(-90f, 90f, -90f);  //close entirely

            float elapsedTime = 0f;

            // For closing animation, continue until object is detected OR animation duration is reached
            while (!(_isObjectInUpperTrigger || _isObjectInLowerTrigger) && elapsedTime < _animationDuration)
            {
                elapsedTime += Time.deltaTime;
                float normalizedTime = elapsedTime / _animationDuration;

                // Apply animation curve for easing
                float curveValue = _animationCurve.Evaluate(normalizedTime);

                // Interpolate clamp rotations toward closed position
                _upperClamp.localRotation = Quaternion.Slerp(upperClampStartRot, upperClampTargetRot, curveValue);
                _lowerClamp.localRotation = Quaternion.Slerp(lowerClampStartRot, lowerClampTargetRot, curveValue);

                yield return null;
            }

            if (_isObjectInUpperTrigger || _isObjectInLowerTrigger)
            {
                Debug.Log("Object detected in trigger - Forceps stopped closing");
            }
            else
            {
                Debug.Log("Forceps closing animation completed by duration");
            }
        }
        else
        {
            // For opening animation, return to default positions
            Quaternion upperClampEndRot = _upperClampDefaultRot;
            Quaternion lowerClampEndRot = _lowerClampDefaultRot;

            float elapsedTime = 0f;

            while (elapsedTime < _animationDuration)
            {
                elapsedTime += Time.deltaTime;
                float normalizedTime = elapsedTime / _animationDuration;

                // Apply animation curve for easing
                float curveValue = _animationCurve.Evaluate(normalizedTime);

                // Interpolate rotations back to default (open) positions
                _upperClamp.localRotation = Quaternion.Slerp(upperClampStartRot, upperClampEndRot, curveValue);
                _lowerClamp.localRotation = Quaternion.Slerp(lowerClampStartRot, lowerClampEndRot, curveValue);

                yield return null;
            }

            // Ensure final positions are exact
            _upperClamp.localRotation = upperClampEndRot;
            _lowerClamp.localRotation = lowerClampEndRot;

            Debug.Log("Forceps opened to default position");
        }

        _isAnimating = false;
        _currentAnimation = null;
    }

    public bool IsGripPressed => _isGripPressed;
    public bool IsAnimating => _isAnimating;

    /// <summary>
    /// Get the list of interactable tags
    /// </summary>
    public List<string> InteractableTags => new List<string>(_interactableTags);

    /// <summary>
    /// Add a new interactable tag
    /// </summary>
    /// <param name="tag">Tag to add</param>
    public void AddInteractableTag(string tag)
    {
        if (!string.IsNullOrEmpty(tag) && !_interactableTags.Contains(tag))
        {
            _interactableTags.Add(tag);
            if (_showTagDebugInfo)
                Debug.Log($"Added interactable tag: {tag}");
        }
    }

    /// <summary>
    /// Remove an interactable tag
    /// </summary>
    /// <param name="tag">Tag to remove</param>
    public void RemoveInteractableTag(string tag)
    {
        if (_interactableTags.Remove(tag))
        {
            if (_showTagDebugInfo)
                Debug.Log($"Removed interactable tag: {tag}");
        }
    }

    /// <summary>
    /// Clear all interactable tags (allows interaction with all objects)
    /// </summary>
    public void ClearInteractableTags()
    {
        _interactableTags.Clear();
        if (_showTagDebugInfo)
            Debug.Log("Cleared all interactable tags - now interacts with all objects");
    }

    /// <summary>
    /// Set the list of interactable tags
    /// </summary>
    /// <param name="tags">New list of tags</param>
    public void SetInteractableTags(List<string> tags)
    {
        _interactableTags = tags ?? new List<string>();
        if (_showTagDebugInfo)
            Debug.Log($"Set interactable tags to: [{string.Join(", ", _interactableTags)}]");
    }

    /// <summary>
    /// Check if a specific tag is in the interactable list
    /// </summary>
    /// <param name="tag">Tag to check</param>
    /// <returns>True if the tag is interactable</returns>
    public bool IsTagInteractable(string tag)
    {
        return _interactableTags.Contains(tag);
    }

    void OnDestroy()
    {
        if (_gripAction != null)
        {
            _gripAction.action.performed -= OnGripPressed;
            _gripAction.action.canceled -= OnGripReleased;
        }

        // Clean up any running animation
        if (_currentAnimation != null)
        {
            StopCoroutine(_currentAnimation);
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Validate configuration in editor
    /// </summary>
    private void OnValidate()
    {
        // Check if tags list contains invalid entries
        if (_interactableTags != null)
        {
            for (int i = _interactableTags.Count - 1; i >= 0; i--)
            {
                if (string.IsNullOrEmpty(_interactableTags[i]))
                {
                    Debug.LogWarning($"Forceps Controller has empty tag entry at index {i}. Consider removing it.", this);
                }
            }
        }

        // Warn if no interactable tags are set
        if (_interactableTags == null || _interactableTags.Count == 0)
        {
            Debug.LogWarning("Forceps Controller has no interactable tags set. It will interact with all objects.", this);
        }
    }
#endif
}
