using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/* ============================================================= */
/*         This is an extension of XRDirectInteractor.cs         */
/*         from the Unity XR Interaction Toolkit package         */
/* ============================================================= */

[AddComponentMenu("XR/Custom XR Direct Interactor", 12)]
public class CustomXRDirectInteractor : UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor
{
    [Header("Multi Attach Transform")]
    [SerializeField]
    [Tooltip("Enable multiple attach transforms. When disabled, uses default single attach transform behavior.")]
    private bool m_UseMultipleAttachTransforms = false;

    [SerializeField]
    [Tooltip("List of attach transforms. The closest one to target will be selected automatically.")]
    private List<Transform> m_AttachTransforms = new List<Transform>();

    [SerializeField]
    [Range(0.01f, 1.0f)]
    [Tooltip("Update frequency for recalculating closest attach transform (in seconds).")]
    private float m_AttachTransformUpdateFrequency = 0.1f;

    [SerializeField]
    [Tooltip("Show debug information in console")]
    private bool m_ShowDebugInfo = false;

    /// <summary>
    /// Whether to use multiple attach transforms instead of the default single attach transform.
    /// </summary>
    public bool useMultipleAttachTransforms
    {
        get => m_UseMultipleAttachTransforms;
        set => m_UseMultipleAttachTransforms = value;
    }

    /// <summary>
    /// List of attach transforms used when multiple attach transforms is enabled.
    /// </summary>
    public List<Transform> attachTransforms
    {
        get => m_AttachTransforms;
        set => m_AttachTransforms = value ?? new List<Transform>();
    }

    /// <summary>
    /// Update frequency for recalculating the closest attach transform.
    /// </summary>
    public float attachTransformUpdateFrequency
    {
        get => m_AttachTransformUpdateFrequency;
        set => m_AttachTransformUpdateFrequency = Mathf.Max(0.01f, value);
    }

    /// <summary>
    /// Get the currently selected attach transform.
    /// </summary>
    public Transform currentAttachTransform => m_CurrentClosestAttachTransform;

    // Cache for multi-attach functionality
    private Transform m_CurrentClosestAttachTransform;
    private float m_LastAttachTransformUpdateTime;
    private Dictionary<UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable, Transform> m_InteractableAttachCache = new Dictionary<UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable, Transform>();
    
    // Track currently grabbed interactables to prevent recalculation during grab
    private Dictionary<UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable, Transform> m_GrabbedInteractableAttachTransforms = new Dictionary<UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable, Transform>();

    /// <summary>
    /// Initialize multi-attach functionality
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        // Initialize multi-attach cache
        if (m_InteractableAttachCache == null)
            m_InteractableAttachCache = new Dictionary<UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable, Transform>();

        // Initialize grabbed interactables tracking
        if (m_GrabbedInteractableAttachTransforms == null)
            m_GrabbedInteractableAttachTransforms = new Dictionary<UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable, Transform>();

        if (m_ShowDebugInfo)
            Debug.Log($"CustomXRDirectInteractor initialized with {m_AttachTransforms.Count} attach transforms.");
    }

    /// <summary>
    /// Override GetAttachTransform to support multiple attach transforms
    /// </summary>
    /// <param name="interactable">The interactable to get attach transform for</param>
    /// <returns>The closest attach transform or base attach transform</returns>
    public override Transform GetAttachTransform(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable interactable)
    {
        // If multiple attach transforms is disabled, use base behavior
        if (!m_UseMultipleAttachTransforms || m_AttachTransforms == null || m_AttachTransforms.Count == 0)
        {
            return base.GetAttachTransform(interactable);
        }

        // If only one attach transform, return it
        if (m_AttachTransforms.Count == 1)
        {
            var singleTransform = m_AttachTransforms[0];
            m_CurrentClosestAttachTransform = singleTransform;
            return singleTransform ?? base.GetAttachTransform(interactable);
        }

        // Check if this interactable is currently being grabbed
        // If so, return the locked attach transform to prevent switching during grab
        if (interactable != null && m_GrabbedInteractableAttachTransforms.TryGetValue(interactable, out var grabbedTransform))
        {
            if (m_ShowDebugInfo)
                Debug.Log($"Using locked attach transform during grab: {grabbedTransform.name} for {interactable.transform.name}");
            
            m_CurrentClosestAttachTransform = grabbedTransform;
            return grabbedTransform ?? base.GetAttachTransform(interactable);
        }

        // Check if we need to update based on frequency (only for non-grabbed interactables)
        bool shouldUpdate = Time.time - m_LastAttachTransformUpdateTime >= m_AttachTransformUpdateFrequency;

        // Try to get cached result for this specific interactable
        if (!shouldUpdate && interactable != null && m_InteractableAttachCache.TryGetValue(interactable, out var cachedTransform))
        {
            if (m_ShowDebugInfo && cachedTransform != null)
                Debug.Log($"Using cached attach transform: {cachedTransform.name} for {interactable.transform.name}");

            return cachedTransform ?? base.GetAttachTransform(interactable);
        }

        // Find the closest attach transform
        Transform closestTransform = FindClosestAttachTransform(interactable);

        // Cache the result
        if (interactable != null && closestTransform != null)
        {
            m_InteractableAttachCache[interactable] = closestTransform;

            if (m_ShowDebugInfo)
                Debug.Log($"Selected closest attach transform: {closestTransform.name} for {interactable.transform.name}");
        }

        m_CurrentClosestAttachTransform = closestTransform;
        m_LastAttachTransformUpdateTime = Time.time;

        return closestTransform ?? base.GetAttachTransform(interactable);
    }

    /// <summary>
    /// Find the closest attach transform to the given interactable
    /// </summary>
    /// <param name="interactable">Target interactable</param>
    /// <returns>Closest attach transform</returns>
    private Transform FindClosestAttachTransform(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable interactable)
    {
        if (m_AttachTransforms == null || m_AttachTransforms.Count == 0)
            return null;

        Transform closestTransform = null;
        float closestDistance = float.MaxValue;

        Vector3 referencePosition = GetReferencePositionForAttach(interactable);

        foreach (var attachTransform in m_AttachTransforms)
        {
            if (attachTransform == null) continue;

            float distance = Vector3.Distance(attachTransform.position, referencePosition);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestTransform = attachTransform;
            }
        }

        return closestTransform;
    }

    /// <summary>
    /// Get reference position for attach transform distance calculation
    /// </summary>
    /// <param name="interactable">Target interactable</param>
    /// <returns>Reference position for distance calculation</returns>
    private Vector3 GetReferencePositionForAttach(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable interactable)
    {
        // If we have a specific interactable, use its position
        if (interactable?.transform != null)
        {
            return interactable.transform.position;
        }

        // Fallback: use average position of all current valid targets
        var validTargets = new List<UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable>();
        GetValidTargets(validTargets);

        if (validTargets.Count > 0)
        {
            Vector3 averagePosition = Vector3.zero;
            int validCount = 0;

            foreach (var target in validTargets)
            {
                if (target?.transform != null)
                {
                    averagePosition += target.transform.position;
                    validCount++;
                }
            }

            if (validCount > 0)
            {
                return averagePosition / validCount;
            }
        }

        // Final fallback: use this transform's position
        return transform.position;
    }

    /// <summary>
    /// Clear attach transform cache for specific interactable
    /// </summary>
    /// <param name="interactable">Interactable to clear from cache</param>
    private void ClearAttachTransformCache(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable interactable)
    {
        if (interactable != null && m_InteractableAttachCache != null)
        {
            m_InteractableAttachCache.Remove(interactable);
        }
    }

    /// <summary>
    /// Handle when grab starts - lock the attach transform for this interactable
    /// </summary>
    /// <param name="args">Selection event arguments</param>
    protected override void OnSelectEntered(SelectEnterEventArgs args)
    {
        base.OnSelectEntered(args);
        
        var interactable = args.interactableObject;
        if (interactable != null && m_UseMultipleAttachTransforms)
        {
            // Get the current attach transform and lock it for this grab session
            var currentTransform = GetAttachTransform(interactable);
            if (currentTransform != null)
            {
                m_GrabbedInteractableAttachTransforms[interactable] = currentTransform;
                
                if (m_ShowDebugInfo)
                    Debug.Log($"Locked attach transform {currentTransform.name} for grabbed interactable {interactable.transform.name}");
            }
        }
    }

    /// <summary>
    /// Handle when grab ends - unlock the attach transform for this interactable
    /// </summary>
    /// <param name="args">Selection event arguments</param>
    protected override void OnSelectExited(SelectExitEventArgs args)
    {
        base.OnSelectExited(args);
        
        var interactable = args.interactableObject;
        if (interactable != null)
        {
            // Remove the locked attach transform
            if (m_GrabbedInteractableAttachTransforms.Remove(interactable))
            {
                if (m_ShowDebugInfo)
                    Debug.Log($"Unlocked attach transform for released interactable {interactable.transform.name}");
            }
            
            // Clear cache so it will recalculate on next interaction
            ClearAttachTransformCache(interactable);
        }
    }

    /// <summary>
    /// Clear all attach transform cache
    /// </summary>
    public void ClearAllAttachTransformCache()
    {
        if (m_InteractableAttachCache != null)
        {
            m_InteractableAttachCache.Clear();
        }
        
        if (m_GrabbedInteractableAttachTransforms != null)
        {
            m_GrabbedInteractableAttachTransforms.Clear();
        }
        
        m_CurrentClosestAttachTransform = null;

        if (m_ShowDebugInfo)
            Debug.Log("Cleared all attach transform cache and grab locks.");
    }

    /// <summary>
    /// Add an attach transform at runtime
    /// </summary>
    /// <param name="attachTransform">Transform to add</param>
    public void AddAttachTransform(Transform attachTransform)
    {
        if (attachTransform != null && !m_AttachTransforms.Contains(attachTransform))
        {
            m_AttachTransforms.Add(attachTransform);
            ClearAllAttachTransformCache();

            if (m_ShowDebugInfo)
                Debug.Log($"Added attach transform: {attachTransform.name}");
        }
    }

    /// <summary>
    /// Remove an attach transform at runtime
    /// </summary>
    /// <param name="attachTransform">Transform to remove</param>
    public void RemoveAttachTransform(Transform attachTransform)
    {
        if (m_AttachTransforms.Remove(attachTransform))
        {
            ClearAllAttachTransformCache();

            if (m_ShowDebugInfo)
                Debug.Log($"Removed attach transform: {attachTransform.name}");
        }
    }

    /// <summary>
    /// Get distance to closest attach transform
    /// </summary>
    /// <param name="targetPosition">Target position</param>
    /// <returns>Distance to closest attach transform</returns>
    public float GetDistanceToClosestAttachTransform(Vector3 targetPosition)
    {
        if (!m_UseMultipleAttachTransforms || m_AttachTransforms.Count == 0)
            return Vector3.Distance(transform.position, targetPosition);

        float closestDistance = float.MaxValue;
        foreach (var attachTransform in m_AttachTransforms)
        {
            if (attachTransform == null) continue;

            float distance = Vector3.Distance(attachTransform.position, targetPosition);
            if (distance < closestDistance)
                closestDistance = distance;
        }

        return closestDistance;
    }

    /// <summary>
    /// Check if an interactable is currently being grabbed (has locked attach transform)
    /// </summary>
    /// <param name="interactable">Interactable to check</param>
    /// <returns>True if the interactable is currently grabbed</returns>
    public bool IsInteractableGrabbed(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable interactable)
    {
        return interactable != null && m_GrabbedInteractableAttachTransforms.ContainsKey(interactable);
    }

    /// <summary>
    /// Get the locked attach transform for a grabbed interactable
    /// </summary>
    /// <param name="interactable">Grabbed interactable</param>
    /// <returns>Locked attach transform, or null if not grabbed</returns>
    public Transform GetLockedAttachTransform(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable interactable)
    {
        if (interactable != null && m_GrabbedInteractableAttachTransforms.TryGetValue(interactable, out var lockedTransform))
        {
            return lockedTransform;
        }
        return null;
    }

    /// <summary>
    /// Force unlock an interactable's attach transform (useful for manual release)
    /// </summary>
    /// <param name="interactable">Interactable to unlock</param>
    public void ForceUnlockInteractable(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable interactable)
    {
        if (interactable != null && m_GrabbedInteractableAttachTransforms.Remove(interactable))
        {
            ClearAttachTransformCache(interactable);
            
            if (m_ShowDebugInfo)
                Debug.Log($"Force unlocked attach transform for interactable {interactable.transform.name}");
        }
    }

    /// <summary>
    /// Clear cache when component is enabled
    /// </summary>
    protected override void OnEnable()
    {
        base.OnEnable();
        ClearAllAttachTransformCache();
    }

    /// <summary>
    /// Clear cache when component is disabled
    /// </summary>
    protected override void OnDisable()
    {
        base.OnDisable();
        ClearAllAttachTransformCache();
    }

#if UNITY_EDITOR
    /// <summary>
    /// Draw gizmos for multi-attach transforms in Scene view
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!m_UseMultipleAttachTransforms || m_AttachTransforms == null) return;

        // Draw all attach transforms
        UnityEditor.Handles.color = Color.cyan;
        foreach (var attachTransform in m_AttachTransforms)
        {
            if (attachTransform != null)
            {
                UnityEditor.Handles.DrawWireDisc(attachTransform.position, attachTransform.up, 0.003f);
            }
        }

        // Highlight current closest attach transform
        if (m_CurrentClosestAttachTransform != null)
        {
            UnityEditor.Handles.color = Color.red;
            UnityEditor.Handles.DrawWireDisc(m_CurrentClosestAttachTransform.position, m_CurrentClosestAttachTransform.up, 0.003f);
        }
    }

    /// <summary>
    /// Validate configuration in editor
    /// </summary>
    private void OnValidate()
    {
        if (m_UseMultipleAttachTransforms && (m_AttachTransforms == null || m_AttachTransforms.Count == 0))
        {
            Debug.LogWarning("Multi-attach is enabled but no attach transforms are assigned!", this);
        }
    }
#endif
}
