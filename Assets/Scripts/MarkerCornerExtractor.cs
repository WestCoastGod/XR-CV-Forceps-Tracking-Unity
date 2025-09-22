using System.Collections.Generic;
using UnityEngine;
using TryAR.MarkerTracking;
using PassthroughCameraSamples;

/// <summary>
/// Extracts raw marker corner data from ArUco detection for rigid body pose estimation
/// This bypasses the individual marker pose processing to get the raw corner coordinates
/// </summary>
public class MarkerCornerExtractor : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private ArUcoMarkerTracking arucoTracker;
    [SerializeField] private WebCamTextureManager webCamManager;
    [SerializeField] private Transform cameraAnchor;

    private float _lastWarnTime;

    public void Configure(ArUcoMarkerTracking tracker, WebCamTextureManager webcam, Transform camAnchor)
    {
        arucoTracker = tracker;
        webCamManager = webcam;
        cameraAnchor = camAnchor;
    }
    
    /// <summary>
    /// Extract raw marker corners from the last ArUco detection.
    /// Returns corners in world space coordinates (camera-relative) using solvePnP.
    /// </summary>
    /// <param name="markerIDs">List of marker IDs to extract corners for</param>
    /// <returns>Dictionary mapping marker ID to its 4 corners [TL, TR, BR, BL]</returns>
    public Dictionary<int, Vector3[]> ExtractMarkerCorners(List<int> markerIDs)
    {
        // Use provided dependencies; do not auto-create anchors to avoid mismatched frames of reference

        Dictionary<int, Vector3[]> markerCorners = new Dictionary<int, Vector3[]>();
        
        if (arucoTracker == null || webCamManager?.WebCamTexture == null || cameraAnchor == null)
        {
            // Throttle warnings to avoid log spam
            if (Time.unscaledTime - _lastWarnTime > 1f)
            {
                Debug.LogWarning("MarkerCornerExtractor: Missing dependencies");
                _lastWarnTime = Time.unscaledTime;
            }
            return markerCorners;
        }

        foreach (int markerID in markerIDs)
        {
            var corners = arucoTracker.GetDetectedMarkerCorners(markerID, cameraAnchor);
            if (corners != null && corners.Length == 4)
            {
                markerCorners[markerID] = corners;
            }
        }

        return markerCorners;
    }
    
    /// <summary>
    /// Get corners for multiple detected markers
    /// </summary>
    public bool GetDetectedMarkerCorners(List<int> requestedMarkerIDs, 
                                        out List<int> detectedIDs,
                                        out List<Vector3[]> detectedCorners)
    {
        detectedIDs = new List<int>();
        detectedCorners = new List<Vector3[]>();
        
        Dictionary<int, Vector3[]> markerCorners = ExtractMarkerCorners(requestedMarkerIDs);
        
        foreach (var kvp in markerCorners)
        {
            detectedIDs.Add(kvp.Key);
            detectedCorners.Add(kvp.Value);
        }
        
        return detectedIDs.Count > 0;
    }
}