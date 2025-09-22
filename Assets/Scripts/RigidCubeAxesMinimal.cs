using System.Collections.Generic;
using UnityEngine;
using PassthroughCameraSamples;
using TryAR.MarkerTracking;
using UnityEngine.UI;

// RigidCubeAxesMinimal
// Detect ArUco markers on the cube, estimate one rigid-body pose from all visible corners,
// and render simple 3D XYZ axes at the cube center. No smoothing, no UI canvas, just the core idea.
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

    private MarkerCornerExtractor cornerExtractor;
    private Texture2D resultTexture;

    private struct PoseData { public Vector3 position; public Quaternion rotation; }
    private PoseData? lastValidPose;

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
            arucoTracker.DetectMarker(webCamManager.WebCamTexture, resultTexture); 
            Debug.Log($"DetectMarker called - resultTexture is {(resultTexture != null ? "not null" : "null")}");
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

        // Apply smoothing for stability
        if (lastValidPose.HasValue)
        {
            float posSmooth = 0.7f; // Strong smoothing for stability
            float rotSmooth = 0.8f;
            cubePos = Vector3.Lerp(cubePos, lastValidPose.Value.position, posSmooth);
            cubeRot = Quaternion.Slerp(cubeRot, lastValidPose.Value.rotation, rotSmooth);
        }
        lastValidPose = new PoseData { position = cubePos, rotation = cubeRot };

        // Update cube model with adjustments
        if (cubeModel != null && trackCubeModel)
        {
            // Apply position offset (in local cube space)
            Vector3 adjustedPos = cubePos + cubeRot * cubePositionOffset;
            
            // Apply rotation offset
            Quaternion rotOffset = Quaternion.Euler(cubeRotationOffset);
            Quaternion adjustedRot = cubeRot * rotOffset;
            
            // Apply scale (affects both cube and child forceps)
            Vector3 adjustedScale = Vector3.one * cubeScaleMultiplier;
            
            cubeModel.position = adjustedPos;
            cubeModel.rotation = adjustedRot;
            cubeModel.localScale = adjustedScale;
        }        // Draw 2D projected axes on the canvas texture
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
}
