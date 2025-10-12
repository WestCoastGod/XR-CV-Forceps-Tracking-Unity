using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LowerClamp : MonoBehaviour
{
    [SerializeField]
    private ForcepsController parentForceps;

    [Header("Debug Settings")]
    [SerializeField]
    [Tooltip("Show debug messages for trigger events")]
    private bool showTriggerDebug = true;

    private ForcepsController cachedForcepsController;

    /// <summary>
    /// Get the forceps controller (with automatic fallback search)
    /// </summary>
    private ForcepsController ForcepsController
    {
        get
        {
            if (cachedForcepsController == null)
            {
                // Try manual reference first
                if (parentForceps != null)
                {
                    cachedForcepsController = parentForceps;
                }
                else
                {
                    // Fallback: try to find in parent hierarchy
                    cachedForcepsController = GetComponentInParent<ForcepsController>();
                    
                    // If still not found, try to find in root
                    if (cachedForcepsController == null)
                        cachedForcepsController = FindObjectOfType<ForcepsController>();
                }
            }
            return cachedForcepsController;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Debug message showing object tag
        if (showTriggerDebug)
        {
            UnityEngine.Debug.Log($"[LowerClamp] Object '{other.gameObject.name}' with tag '{other.gameObject.tag}' entered trigger", this);
        }

        if (ForcepsController != null)
        {
            ForcepsController.OnLowerTriggerEnter(other.gameObject);
        }
        else
        {
            UnityEngine.Debug.LogError("LowerClamp: No ForcepsController found! Please assign parentForceps in the inspector or ensure ForcepsController exists in the scene.", this);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        // Debug message showing object tag
        if (showTriggerDebug)
        {
            UnityEngine.Debug.Log($"[LowerClamp] Object '{other.gameObject.name}' with tag '{other.gameObject.tag}' exited trigger", this);
        }

        if (ForcepsController != null)
        {
            ForcepsController.OnLowerTriggerExit(other.gameObject);
        }
        else
        {
            UnityEngine.Debug.LogError("LowerClamp: No ForcepsController found! Please assign parentForceps in the inspector or ensure ForcepsController exists in the scene.", this);
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        // Validate ForcepsController reference on start
        if (ForcepsController == null)
        {
            UnityEngine.Debug.LogError("LowerClamp: Failed to find ForcepsController! Please assign parentForceps in the inspector.", this);
        }
        else
        {
            UnityEngine.Debug.Log($"LowerClamp: Successfully connected to ForcepsController: {ForcepsController.name}");
        }

        if (showTriggerDebug)
        {
            UnityEngine.Debug.Log($"[LowerClamp] Trigger debugging enabled on {gameObject.name}", this);
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
