using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Proper rigid body pose estimation for multi-marker objects
/// Implements the mathematical approach equivalent to OpenCV's solvePnP for ArUco boards
/// </summary>
public static class RigidBodyPoseEstimator
{
    public struct PoseResult
    {
        public Vector3 position;
        public Quaternion rotation;
        public float reprojectionError;
        public int markerCount;
        public bool isValid;
        
        public PoseResult(Vector3 pos, Quaternion rot, float error, int count, bool valid)
        {
            position = pos;
            rotation = rot;
            reprojectionError = error;
            markerCount = count;
            isValid = valid;
        }
    }
    
    /// <summary>
    /// Estimate pose of a rigid object from marker corner correspondences
    /// This is the correct approach - equivalent to cv::solvePnP with ArUco board
    /// </summary>
    /// <param name="detectedMarkerIDs">IDs of detected markers</param>
    /// <param name="detectedCorners">4 corners per marker in world space [TL, TR, BR, BL]</param>
    /// <param name="objectCorners">4 corners per marker in object space [TL, TR, BR, BL]</param>
    /// <returns>Estimated pose of the object</returns>
    public static PoseResult EstimateRigidBodyPose(
        List<int> detectedMarkerIDs,
        List<Vector3[]> detectedCorners,
        List<Vector3[]> objectCorners)
    {
        if (detectedMarkerIDs.Count == 0 || detectedCorners.Count != detectedMarkerIDs.Count)
        {
            return new PoseResult(Vector3.zero, Quaternion.identity, float.MaxValue, 0, false);
        }
        
        // Collect all corner correspondences
        List<Vector3> allObjectPoints = new List<Vector3>();
        List<Vector3> allImagePoints = new List<Vector3>();
        
        for (int i = 0; i < detectedMarkerIDs.Count; i++)
        {
            if (detectedCorners[i].Length == 4 && objectCorners[i].Length == 4)
            {
                for (int j = 0; j < 4; j++)
                {
                    allObjectPoints.Add(objectCorners[i][j]);
                    allImagePoints.Add(detectedCorners[i][j]);
                }
            }
        }
        
        if (allObjectPoints.Count < 3) // Need at least 3 non-collinear points (one marker has 4 corners)
        {
            return new PoseResult(Vector3.zero, Quaternion.identity, float.MaxValue, 0, false);
        }
        
    // Estimate pose using point correspondences (Horn/Davenport quaternion method)
    Quaternion estimatedRotation;
    Vector3 estimatedPosition;

    EstimatePoseHorn(allObjectPoints, allImagePoints, out estimatedRotation, out estimatedPosition);
        
        // Calculate reprojection error
        float error = CalculateReprojectionError(allObjectPoints, allImagePoints, estimatedPosition, estimatedRotation);
        
        return new PoseResult(estimatedPosition, estimatedRotation, error, detectedMarkerIDs.Count, true);
    }
    
    private static void EstimatePoseHorn(List<Vector3> objectPoints, List<Vector3> imagePoints, out Quaternion rotation, out Vector3 translation)
    {
        // Preconditions
        rotation = Quaternion.identity;
        translation = Vector3.zero;
        int n = Mathf.Min(objectPoints.Count, imagePoints.Count);
        if (n < 3) return;

        // Compute centroids
        Vector3 muP = Vector3.zero; // object centroid
        Vector3 muQ = Vector3.zero; // image centroid
        for (int i = 0; i < n; i++) { muP += objectPoints[i]; muQ += imagePoints[i]; }
        muP /= n; muQ /= n;

        // Build cross-covariance matrix S = sum (P_i - muP) (Q_i - muQ)^T
        float Sxx=0, Sxy=0, Sxz=0, Syx=0, Syy=0, Syz=0, Szx=0, Szy=0, Szz=0;
        for (int i = 0; i < n; i++)
        {
            Vector3 P = objectPoints[i] - muP;
            Vector3 Q = imagePoints[i] - muQ;
            Sxx += P.x * Q.x; Sxy += P.x * Q.y; Sxz += P.x * Q.z;
            Syx += P.y * Q.x; Syy += P.y * Q.y; Syz += P.y * Q.z;
            Szx += P.z * Q.x; Szy += P.z * Q.y; Szz += P.z * Q.z;
        }

        // Davenport matrix K (4x4) for quaternion eigen problem
        // K = [ trace(S)    Syz-Szy   Szx-Sxz   Sxy-Syx ]
        //     [ Syz-Szy   Sxx-Syy-Szz  Sxy+Syx  Szx+Sxz ]
        //     [ Szx-Sxz   Sxy+Syx   -Sxx+Syy-Szz  Syz+Szy ]
        //     [ Sxy-Syx   Szx+Sxz    Syz+Szy   -Sxx-Syy+Szz ]
        float trace = Sxx + Syy + Szz;
        float k00 = trace;
        float k01 = Syz - Szy;
        float k02 = Szx - Sxz;
        float k03 = Sxy - Syx;

        float k11 = Sxx - Syy - Szz;
        float k12 = Sxy + Syx;
        float k13 = Szx + Sxz;

        float k22 = -Sxx + Syy - Szz;
        float k23 = Syz + Szy;

        float k33 = -Sxx - Syy + Szz;

        // Power iteration to find dominant eigenvector of symmetric K
        Vector4 q = new Vector4(1, 0, 0, 0); // (w,x,y,z)
        for (int iter = 0; iter < 30; iter++)
        {
            Vector4 qn = new Vector4(
                k00*q.x + k01*q.y + k02*q.z + k03*q.w,
                k01*q.x + k11*q.y + k12*q.z + k13*q.w,
                k02*q.x + k12*q.y + k22*q.z + k23*q.w,
                k03*q.x + k13*q.y + k23*q.z + k33*q.w
            );
            float norm = Mathf.Sqrt(qn.x*qn.x + qn.y*qn.y + qn.z*qn.z + qn.w*qn.w) + 1e-9f;
            q = qn / norm;
        }

        // Normalize and convert to Quaternion (Unity uses x,y,z,w)
        rotation = new Quaternion(q.y, q.z, q.w, q.x);

        // Ensure we pick the hemisphere that minimizes reflection
        if (rotation.w < 0) rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);

        // Translation t = muQ - R * muP
        translation = muQ - (rotation * muP);
    }
    
    private static float CalculateReprojectionError(List<Vector3> objectPoints, List<Vector3> imagePoints, 
                                                   Vector3 position, Quaternion rotation)
    {
        float totalError = 0f;
        
        for (int i = 0; i < objectPoints.Count; i++)
        {
            // Project object point to image space using estimated pose
            Vector3 projectedPoint = rotation * objectPoints[i] + position;
            
            // Calculate distance to actual image point
            float error = Vector3.Distance(projectedPoint, imagePoints[i]);
            totalError += error * error;
        }
        
        return Mathf.Sqrt(totalError / objectPoints.Count);
    }
}