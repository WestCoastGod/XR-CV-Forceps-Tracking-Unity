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
    }

    void Start()
    {
        if (_upperClamp == null || _lowerClamp == null)
        {
            Debug.LogError("Forceps clamps not assigned!");
            return;
        }

        if (_gripAction == null)
        {
            Debug.LogError("Grip Action not assigned!");
            return;
        }

        // bind the grip action to the methods
        _gripAction.action.performed += OnGripPressed;
        _gripAction.action.canceled += OnGripReleased;

        // 1) upper opened: (-45, -90, 90)
        _upperClampDefaultRot = Quaternion.Euler(-45f, -90f, 90f);

        // 2) lower opened: (-45, 90, -90)
        _lowerClampDefaultRot = Quaternion.Euler(-45f, 90f, -90f);

        _upperClamp.localRotation = _upperClampDefaultRot;
        _lowerClamp.localRotation = _lowerClampDefaultRot;

        Debug.Log("ForcepsController initialized. Upper/Lower clamps set to default angles.");
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
        StartSmoothAnimation(false);
        Debug.Log("Grip released - Forceps opening smoothly.");
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
