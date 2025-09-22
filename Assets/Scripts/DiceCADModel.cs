using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Contains the precise 3D coordinates of marker corners from the CAD model.
/// This is the "blueprint" for the multi-marker PnP solving.
/// </summary>
public static class DiceCADModel
{
    // Marker IDs on the dice
    public static readonly int[] MarkerIDs = { 0, 4, 6, 7, 8 };
    
    // Cube marker IDs (if using cube instead)
    public static readonly int[] CubeMarkerIDs = { 0, 1, 2, 3, 4, 5 };
    
    // Set this to true to use simple cube coordinates instead of complex dice
    public static bool USE_SIMPLE_CUBE = true;
    
    // Cube size in meters (7cm cube with 6.5cm markers to match Python reference)
    private static readonly float CUBE_SIZE = 0.07f; // 7cm cube
    private static readonly float MARKER_SIZE = 0.065f; // 6.5cm markers (matches Python)
    
    /// <summary>
    /// Get OpenCV board definition for the dice.
    /// Returns marker corners organized for OpenCV's ArUco board creation.
    /// Similar to the cube example but for pentagonal dice geometry.
    /// </summary>
    public static (List<Vector3[]> boardCorners, List<int> boardIds) GetOpenCVBoardDefinition()
    {
        if (USE_SIMPLE_CUBE)
        {
            return GetCubeBoardDefinition();
        }
        else
        {
            return GetDiceBoardDefinition();
        }
    }
    
    /// <summary>
    /// Get simple cube board definition with known good coordinates
    /// </summary>
    private static (List<Vector3[]> boardCorners, List<int> boardIds) GetCubeBoardDefinition()
    {
        List<Vector3[]> boardCorners = new List<Vector3[]>();
        List<int> boardIds = new List<int>();
        
        foreach (int markerID in CubeMarkerIDs)
        {
            Vector3[] corners = GetCubeMarkerCorners(markerID);
            boardCorners.Add(corners);
            boardIds.Add(markerID);
        }
        
        return (boardCorners, boardIds);
    }
    
    /// <summary>
    /// Get dice board definition with complex coordinates
    /// </summary>
    private static (List<Vector3[]> boardCorners, List<int> boardIds) GetDiceBoardDefinition()
    {
        List<Vector3[]> boardCorners = new List<Vector3[]>();
        List<int> boardIds = new List<int>();
        
        foreach (int markerID in MarkerIDs)
        {
            Vector3[] corners = GetMarkerCornersInMeters(markerID); // Already scaled correctly
            
            // No additional scaling needed - GetMarkerCornersInMeters already handles this
            boardCorners.Add(corners);
            boardIds.Add(markerID);
        }
        
        return (boardCorners, boardIds);
    }
    
    /// <summary>
    /// Get board corners in the format expected by OpenCV ArUco board.
    /// Each marker has 4 corners: [TopLeft, TopRight, BottomRight, BottomLeft]
    /// </summary>
    public static Vector3[][] GetBoardCornersForOpenCV()
    {
        var (boardCorners, boardIds) = GetOpenCVBoardDefinition();
        return boardCorners.ToArray();
    }
    
    /// <summary>
    /// Get board marker IDs in the order corresponding to the corners.
    /// </summary>
    public static int[] GetBoardMarkerIDs()
    {
        var (boardCorners, boardIds) = GetOpenCVBoardDefinition();
        return boardIds.ToArray();
    }
    
    /// <summary>
    /// Get the 4 corner positions for a specific marker in dice local space.
    /// Order: TopLeft, TopRight, BottomRight, BottomLeft
    /// Units: meters (scaled to match physical 7cm×5.5cm dice with 25mm markers)
    /// </summary>
    public static Vector3[] GetMarkerCorners(int markerID)
    {
        switch (markerID)
        {
            case 7:
                return new Vector3[]
                {
                    new Vector3(0.00279f, 0.00042f, 0.00283f),   // TopLeft (back to original scale)
                    new Vector3(0.00278f, 0.00276f, 0.00172f),   // TopRight
                    new Vector3(0.00011f, 0.00267f, 0.00172f),   // BottomRight
                    new Vector3(0.00012f, 0.00033f, 0.00283f)    // BottomLeft
                };
                
            case 0:
                return new Vector3[]
                {
                    new Vector3(0.00323f, 0.00277f, 0.00132f),   // TopLeft (back to original scale)
                    new Vector3(0.00429f, 0.00036f, 0.00135f),   // TopRight
                    new Vector3(0.00423f, 0.00041f, -0.00133f),  // BottomRight
                    new Vector3(0.00317f, 0.00282f, -0.00136f)   // BottomLeft
                };
                
            case 4:
                return new Vector3[]
                {
                    new Vector3(0.00012f, 0.00276f, -0.00175f),  // TopLeft (back to original scale)
                    new Vector3(0.00281f, 0.00276f, -0.00171f),  // TopRight
                    new Vector3(0.00284f, 0.00034f, -0.00283f),  // BottomRight
                    new Vector3(0.00015f, 0.00034f, -0.00287f)   // BottomLeft
                };
                
            case 6:
                return new Vector3[]
                {
                    new Vector3(-0.00141f, 0.000320f, 0.001300f), // TopLeft (back to original scale)
                    new Vector3(-0.00027f, 0.00271f, 0.00135f),   // TopRight
                    new Vector3(-0.00023f, 0.00270f, -0.00127f),  // BottomRight
                    new Vector3(-0.00137f, 0.00031f, -0.00132f)   // BottomLeft
                };
                
            case 8:
                return new Vector3[]
                {
                    new Vector3(0.00278f, 0.00303f, 0.00134f),   // TopLeft (back to original scale)
                    new Vector3(0.00282f, 0.00298f, -0.00132f),  // TopRight
                    new Vector3(0.00018f, 0.00295f, -0.00130f),  // BottomRight
                    new Vector3(0.00014f, 0.00300f, 0.00136f)    // BottomLeft
                };
                
            default:
                Debug.LogError($"Unknown marker ID: {markerID}");
                return new Vector3[4];
        }
    }
    
    /// <summary>
    /// Get marker corners in Unity coordinate system (meters)
    /// </summary>
    public static Vector3[] GetMarkerCornersInMeters(int markerID)
    {
        if (USE_SIMPLE_CUBE)
        {
            // For cube: use the cube coordinates directly (already in meters)
            return GetCubeMarkerCorners(markerID);
        }
        else
        {
            // For dice: scale the tiny CAD coordinates
            Vector3[] corners = GetMarkerCorners(markerID);
            
            // COORDINATE SCALING: Your raw coordinates are ~0.004m (4mm) max
            // Physical dice is 7cm tall, so we need appropriate scaling
            // Target: ~5-7cm range for proper spatial relationships with 25mm markers
            float scaleFactor = 10.0f; // Scale from 4mm to 4cm range (more reasonable)
            
            for (int i = 0; i < corners.Length; i++)
            {
                corners[i] *= scaleFactor;
            }
            
            return corners;
        }
    }
    
    /// <summary>
    /// Get the center position of a marker in dice local coordinate system.
    /// This calculates the center from the 4 corner positions.
    /// </summary>
    public static Vector3 GetMarkerCenterPosition(int markerID)
    {
        if (USE_SIMPLE_CUBE)
        {
            // For cube: calculate center from cube corners (already in meters)
            Vector3[] corners = GetCubeMarkerCorners(markerID);
            if (corners.Length != 4) return Vector3.zero;
            
            // Calculate center as average of 4 corners
            Vector3 center = Vector3.zero;
            foreach (Vector3 corner in corners)
            {
                center += corner;
            }
            center /= corners.Length;
            return center;
        }
        else
        {
            // For dice: use original scale corners
            Vector3[] corners = GetMarkerCorners(markerID);
            if (corners.Length != 4) return Vector3.zero;
            
            // Calculate center as average of 4 corners
            Vector3 center = Vector3.zero;
            for (int i = 0; i < corners.Length; i++)
            {
                center += corners[i];
            }
            center /= corners.Length;
            
            // This gives us the marker center position in dice local coordinates
            // No additional scaling needed - your coordinates are the actual positions
            return center;
        }
    }
    
    /// <summary>
    /// Get all marker center positions for spatial relationship calculations
    /// </summary>
    public static Dictionary<int, Vector3> GetAllMarkerCenterPositions()
    {
        Dictionary<int, Vector3> markerCenters = new Dictionary<int, Vector3>();
        
        foreach (int markerID in MarkerIDs)
        {
            markerCenters[markerID] = GetMarkerCenterPosition(markerID);
        }
        
        return markerCenters;
    }
    
    /// <summary>
    /// Get all corner points for all markers in one list (for PnP solving)
    /// </summary>
    public static List<Vector3> GetAllCornerPoints(List<int> detectedMarkerIDs)
    {
        List<Vector3> allCorners = new List<Vector3>();
        
        foreach (int markerID in detectedMarkerIDs)
        {
            Vector3[] corners = GetMarkerCornersInMeters(markerID);
            allCorners.AddRange(corners);
        }
        
        return allCorners;
    }
    
    /// <summary>
    /// Get cube marker corners for a simple 6-sided cube.
    /// Cube faces: 0=Front, 1=Back, 2=Left, 3=Right, 4=Top, 5=Bottom
    /// Corner order: [TopLeft, TopRight, BottomRight, BottomLeft] (consistent with ArUco)
    /// </summary>
    public static Vector3[] GetCubeMarkerCorners(int markerID)
    {
        float half = CUBE_SIZE * 0.5f; // Half cube size
        float markerHalf = MARKER_SIZE * 0.5f; // Half marker size
        
        switch (markerID)
        {
            case 0: // Front face (+Z)
                return new Vector3[]
                {
                    new Vector3(-markerHalf,  markerHalf, half), // TopLeft
                    new Vector3( markerHalf,  markerHalf, half), // TopRight
                    new Vector3( markerHalf, -markerHalf, half), // BottomRight
                    new Vector3(-markerHalf, -markerHalf, half)  // BottomLeft
                };
                
            case 1: // Back face (-Z)
                return new Vector3[]
                {
                    new Vector3( markerHalf,  markerHalf, -half), // TopLeft
                    new Vector3(-markerHalf,  markerHalf, -half), // TopRight
                    new Vector3(-markerHalf, -markerHalf, -half), // BottomRight
                    new Vector3( markerHalf, -markerHalf, -half)  // BottomLeft
                };
                
            case 2: // Left face (-X)
                return new Vector3[]
                {
                    new Vector3(-half,  markerHalf, -markerHalf), // TopLeft
                    new Vector3(-half,  markerHalf,  markerHalf), // TopRight
                    new Vector3(-half, -markerHalf,  markerHalf), // BottomRight
                    new Vector3(-half, -markerHalf, -markerHalf)  // BottomLeft
                };
                
            case 3: // Right face (+X)
                return new Vector3[]
                {
                    new Vector3(half,  markerHalf,  markerHalf), // TopLeft
                    new Vector3(half,  markerHalf, -markerHalf), // TopRight
                    new Vector3(half, -markerHalf, -markerHalf), // BottomRight
                    new Vector3(half, -markerHalf,  markerHalf)  // BottomLeft
                };
                
            case 4: // Top face (+Y)
                return new Vector3[]
                {
                    new Vector3(-markerHalf, half, -markerHalf), // TopLeft
                    new Vector3( markerHalf, half, -markerHalf), // TopRight
                    new Vector3( markerHalf, half,  markerHalf), // BottomRight
                    new Vector3(-markerHalf, half,  markerHalf)  // BottomLeft
                };
                
            case 5: // Bottom face (-Y)
                return new Vector3[]
                {
                    new Vector3(-markerHalf, -half,  markerHalf), // TopLeft
                    new Vector3( markerHalf, -half,  markerHalf), // TopRight
                    new Vector3( markerHalf, -half, -markerHalf), // BottomRight
                    new Vector3(-markerHalf, -half, -markerHalf)  // BottomLeft
                };
                
            default:
                Debug.LogError($"Unknown cube marker ID: {markerID}");
                return new Vector3[4];
        }
    }
}
