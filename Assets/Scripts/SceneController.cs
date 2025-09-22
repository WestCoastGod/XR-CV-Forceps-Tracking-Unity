using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARPlaneManager))]
public class SceneController : MonoBehaviour
{
    [Header("Input Actions")]
    [SerializeField]
    private InputActionReference _togglePlanesAction;

    [SerializeField]
    private InputActionReference _activateAction;

    [Header("Prefabs")]
    [SerializeField]
    private GameObject _grabbableSphere;

    private ARPlaneManager _planeManager;
    private bool _isVisible = true;
    private int _numPlanesAddedOccurred = 0;

    // Start is called before the first frame update
    void Start()
    {
        Debug.Log("-> SceneController::Start()");

        _planeManager = GetComponent<ARPlaneManager>();

        if (_planeManager == null)
        {
            Debug.LogError("-> Can't find 'ARPlaneManager' :( ");
            return;
        }

        // Check if the plane manager is initialized
        if (_togglePlanesAction != null)
        {
            _togglePlanesAction.action.performed += OnTogglePlanesAction;
        }
        else
        {
            Debug.LogWarning("-> _togglePlanesAction is not assigned!");
        }

        if (_activateAction != null)
        {
            _activateAction.action.performed += OnActivateAction;
        }
        else
        {
            Debug.LogWarning("-> _activateAction is not assigned!");
        }

        _planeManager.planesChanged += OnPlanesChanged;
    }

    private void OnActivateAction(InputAction.CallbackContext obj)
    {
        SpawnGrabbableSphere();
    }

    private void SpawnGrabbableSphere()
    {
        Debug.Log("-> SceneController::SpawnGrabbableSphere()");

        if (_grabbableSphere == null)
        {
            Debug.LogWarning("-> GrabbableSphere prefab is not assigned.");
            return;
        }

        Vector3 spawnPosition;

        foreach (var plane in _planeManager.trackables)
        {
            if (plane.classification == PlaneClassification.Table)
            {
                spawnPosition = plane.transform.position;
                spawnPosition.y += 0.3f;
                Instantiate(_grabbableSphere, spawnPosition, Quaternion.identity);
                Debug.Log($"-> Spawned sphere at position: {spawnPosition}");
            }
        }
    }

    // Update is called once per frame
    void Update()
    {

    }

    private void OnTogglePlanesAction(InputAction.CallbackContext obj)
    {
        _isVisible = !_isVisible;
        float fillAlpha = _isVisible ? 0.3f : 0f;
        float lineAlpha = _isVisible ? 1.0f : 0f;

        Debug.Log("-> OnTogglePlanesAction() - trackables.count: " + _planeManager.trackables.count);

        foreach (var plane in _planeManager.trackables)
        {
            SetPlaneAlpha(plane, fillAlpha, lineAlpha);
        }
    }

    private void SetPlaneAlpha(ARPlane plane, float fillAlpha, float lineAlpha)
    {
        var meshRenderer = plane.GetComponent<MeshRenderer>();
        var lineRenderer = plane.GetComponent<LineRenderer>();

        if (meshRenderer != null)
        {
            Color color = meshRenderer.material.color;
            color.a = fillAlpha;
            meshRenderer.material.color = color;
        }

        if (lineRenderer != null)
        {
            Color startColor = lineRenderer.startColor;
            Color endColor = lineRenderer.endColor;

            startColor.a = lineAlpha;
            endColor.a = lineAlpha;

            lineRenderer.startColor = startColor;
            lineRenderer.endColor = endColor;
        }
    }

    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        if (args.added.Count > 0)
        {
            _numPlanesAddedOccurred++;

            foreach (var plane in _planeManager.trackables)
            {
                PrintPlaneLabel(plane);
            }

            Debug.Log("-> Number of planes: " + _planeManager.trackables.count);
            Debug.Log("-> Num Planes Added Occurred: " + _numPlanesAddedOccurred);
        }
    }

    private void PrintPlaneLabel(ARPlane plane)
    {
        string label = plane.classification.ToString();
        string log = $"Plane ID: {plane.trackableId}, Label: {label}";
        Debug.Log(log);
    }

    void OnDestroy()
    {
        Debug.Log("-> SceneController::OnDestroy()");
        
        if (_togglePlanesAction != null)
            _togglePlanesAction.action.performed -= OnTogglePlanesAction;
        
        if (_planeManager != null)
            _planeManager.planesChanged -= OnPlanesChanged;
        
        if (_activateAction != null)
            _activateAction.action.performed -= OnActivateAction;
    }
}
