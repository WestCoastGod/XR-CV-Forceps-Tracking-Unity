using OpenCVForUnity.Calib3dModule;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.ObjdetectModule;
using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.UnityUtils.Helper;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TryAR.MarkerTracking
{
    /// <summary>
    /// ArUco marker detection and tracking component.
    /// Handles detection of ArUco markers in camera frames and provides pose estimation.
    /// </summary>
    public class ArUcoMarkerTracking : MonoBehaviour
    {
    /// <summary>
    /// The ArUco dictionary to use for marker detection. Set to match your printed markers.
    /// Default updated to DICT_4X4_100 to match user setup (IDs 0-5).
    /// </summary>
    [SerializeField] private ArUcoDictionary _dictionaryId = ArUcoDictionary.DICT_4X4_100;

        [Space(10)]

        /// <summary>
        /// The length of the markers' side in meters.
        /// </summary>
        [SerializeField] private float _markerLength = 0.3f;

        /// <summary>
        /// Coefficient for low-pass filter (0-1). Higher values mean more smoothing.
        /// </summary>
        [Range(0, 1)]
        [SerializeField] private float _poseFilterCoefficient = 0.5f;

        /// <summary>
        /// Division factor for input image resolution. Higher values improve performance but reduce detection accuracy.
        /// </summary>
        [SerializeField] private int _divideNumber = 1;
        
        /// <summary>
        /// Read-only access to the divide number value
        /// </summary>
        public int DivideNumber => _divideNumber;

        /// <summary>
        /// Show corner numbers and enhance corner 0 visualization for calibration
        /// </summary>
        [SerializeField] private bool enhancedVisualization = false;

        // OpenCV matrices for image processing
    /// <summary>
    /// RGB format mat for result display.
    /// </summary>
    private Mat _processingRgbMat;

    /// <summary>
    /// Grayscale mat for robust marker detection.
    /// </summary>
    private Mat _processingGrayMat;

        /// <summary>
        /// Full-size RGBA mat from original webcam image.
        /// </summary>
        private Mat _originalWebcamMat;
        
        /// <summary>
        /// Resized mat for intermediate processing.
        /// </summary>
        private Mat _halfSizeMat;

        /// <summary>
        /// The camera intrinsic parameters matrix.
        /// </summary>
        private Mat _cameraIntrinsicMatrix;

        /// <summary>
        /// The camera distortion coefficients.
        /// </summary>
        private MatOfDouble _cameraDistortionCoeffs;

        // ArUco detection related mats and variables
        private Mat _detectedMarkerIds;
        private List<Mat> _detectedMarkerCorners;
        private List<Mat> _rejectedMarkerCandidates;
        private Dictionary markerDictionary;
        private Mat recoveredMarkerIndices;
        private ArucoDetector arucoDetector;

        private bool _isReady = false;
        
        /// <summary>
        /// Read-only access to determine if tracking is ready
        /// </summary>
        public bool IsReady => _isReady;

        /// <summary>
        /// Read-only access to marker length for debugging
        /// </summary>
        public float GetMarkerLength() => _markerLength;

        /// <summary>
        /// Set the physical marker side length in meters (must match your printed markers)
        /// </summary>
        public void SetMarkerLength(float meters)
        {
            _markerLength = Mathf.Max(0.001f, meters);
        }

        /// <summary>
        /// Set the ArUco dictionary to match the printed markers. Call before Initialize for best results.
        /// If called after Initialize, the detector will be recreated with the new dictionary.
        /// </summary>
        public void SetDictionary(ArUcoDictionary dict)
        {
            _dictionaryId = dict;
            if (_isReady)
            {
                // Recreate the dictionary and detector with same tuned parameters
                markerDictionary = Objdetect.getPredefinedDictionary((int)_dictionaryId);
                RecreateArucoDetector();
            }
        }

        /// <summary>
        /// Dictionary storing previous pose data for each marker ID for smoothing
        /// </summary>
        private Dictionary<int, PoseData> _prevPoseDataDictionary = new Dictionary<int, PoseData>();

        /// <summary>
        /// Initialize the marker tracking system with camera parameters
        /// </summary>
        /// <param name="imageWidth">Camera image width in pixels</param>
        /// <param name="imageHeight">Camera image height in pixels</param>
        /// <param name="cx">Principal point X coordinate</param>
        /// <param name="cy">Principal point Y coordinate</param>
        /// <param name="fx">Focal length X</param>
        /// <param name="fy">Focal length Y</param>
        public void Initialize(int imageWidth, int imageHeight, float cx, float cy, float fx, float fy)
        {
            InitializeMatrices(imageWidth, imageHeight, cx, cy, fx, fy);        
        }

        /// <summary>
        /// Initialize all OpenCV matrices and detector parameters
        /// </summary>
        private void InitializeMatrices(int originalWidth, int originalHeight, float cX, float cY, float fX, float fY)
        {            
            // Processing dimensions (scaled by divide number)
            int processingWidth = originalWidth / _divideNumber;
            int processingHeight = originalHeight / _divideNumber;
            fX = fX / _divideNumber;
            fY = fY / _divideNumber;
            cX = cX / _divideNumber;
            cY = cY / _divideNumber;

            // Create camera intrinsic matrix
            _cameraIntrinsicMatrix = new Mat(3, 3, CvType.CV_64FC1);
            _cameraIntrinsicMatrix.put(0, 0, fX);
            _cameraIntrinsicMatrix.put(0, 1, 0);
            _cameraIntrinsicMatrix.put(0, 2, cX);
            _cameraIntrinsicMatrix.put(1, 0, 0);
            _cameraIntrinsicMatrix.put(1, 1, fY);
            _cameraIntrinsicMatrix.put(1, 2, cY);
            _cameraIntrinsicMatrix.put(2, 0, 0);
            _cameraIntrinsicMatrix.put(2, 1, 0);
            _cameraIntrinsicMatrix.put(2, 2, 1.0f);

            // No distortion coefficients for Quest cameras
            _cameraDistortionCoeffs = new MatOfDouble(0, 0, 0, 0);

            // Initialize all processing mats
            _originalWebcamMat = new Mat(originalHeight, originalWidth, CvType.CV_8UC4);
            _halfSizeMat = new Mat(processingHeight, processingWidth, CvType.CV_8UC4);
            _processingRgbMat = new Mat(processingHeight, processingWidth, CvType.CV_8UC3);
            _processingGrayMat = new Mat(processingHeight, processingWidth, CvType.CV_8UC1);

            // Create ArUco detection mats
            _detectedMarkerIds = new Mat();
            _detectedMarkerCorners = new List<Mat>();
            _rejectedMarkerCandidates = new List<Mat>();
            markerDictionary = Objdetect.getPredefinedDictionary((int)_dictionaryId);
            recoveredMarkerIndices = new Mat();

            // Create or recreate ArUco detector with tuned parameters
            RecreateArucoDetector();

            _isReady = true;
        }

        /// <summary>
        /// Helper to create the ArUco detector with tuned parameters. Safe to call multiple times.
        /// </summary>
        private void RecreateArucoDetector()
        {
            // Dispose previous
            if (arucoDetector != null)
            {
                arucoDetector.Dispose();
                arucoDetector = null;
            }
            // Configure detector parameters for improved robustness on device
            DetectorParameters detectorParams = new DetectorParameters();
            detectorParams.set_minDistanceToBorder(1);
            detectorParams.set_useAruco3Detection(true);
            detectorParams.set_cornerRefinementMethod(Objdetect.CORNER_REFINE_SUBPIX);
            detectorParams.set_minSideLengthCanonicalImg(10);
            detectorParams.set_errorCorrectionRate(0.8);
            detectorParams.set_adaptiveThreshWinSizeMin(3);
            detectorParams.set_adaptiveThreshWinSizeStep(2);
            detectorParams.set_adaptiveThreshWinSizeMax(23);
            detectorParams.set_adaptiveThreshConstant(7);
            detectorParams.set_minMarkerPerimeterRate(0.01f);
            detectorParams.set_maxMarkerPerimeterRate(4.0f);
            detectorParams.set_minCornerDistanceRate(0.02f);
            detectorParams.set_detectInvertedMarker(true);
            RefineParameters refineParameters = new RefineParameters(10f, 3f, true);
            // Create the ArUco detector
            arucoDetector = new ArucoDetector(markerDictionary, detectorParams, refineParameters);
        }

        /// <summary>
        /// Release all OpenCV resources
        /// </summary>
        private void ReleaseResources()
        {
            Debug.Log("Releasing ArUco tracking resources");

            if (_processingRgbMat != null)
                _processingRgbMat.Dispose();

            if (_originalWebcamMat != null)
                _originalWebcamMat.Dispose();
                
            if (_halfSizeMat != null)
                _halfSizeMat.Dispose();
  
            if (arucoDetector != null)
                arucoDetector.Dispose();

            if (_detectedMarkerIds != null)
                _detectedMarkerIds.Dispose();
            
            foreach (var corner in _detectedMarkerCorners)
            {
                corner.Dispose();
            }
            _detectedMarkerCorners.Clear();
            
            foreach (var rejectedCorner in _rejectedMarkerCandidates)
            {
                rejectedCorner.Dispose();
            }
            _rejectedMarkerCandidates.Clear();

            if (recoveredMarkerIndices != null)
                recoveredMarkerIndices.Dispose();

            if (_processingGrayMat != null)
                _processingGrayMat.Dispose();
        }

        /// <summary>
        /// Handle errors that occur during tracking operations
        /// </summary>
        /// <param name="errorCode">Error code</param>
        /// <param name="message">Error message</param>
        public void HandleError(Source2MatHelperErrorCode errorCode, string message)
        {
            Debug.Log("ArUco tracking error: " + errorCode + ":" + message);
        }

        /// <summary>
        /// Detect ArUco markers in the provided webcam texture
        /// </summary>
        /// <param name="webCamTexture">Input webcam texture</param>
        /// <param name="resultTexture">Optional output texture for visualization</param>
        /// <param name="contrast">Contrast adjustment (1.0 = no change)</param>
        /// <param name="brightness">Brightness adjustment (0 = no change)</param>
        public void DetectMarker(WebCamTexture webCamTexture, Texture2D resultTexture = null, float contrast = 1.0f, float brightness = 0.0f)
        {
            if (_isReady)
            {
                if (webCamTexture == null)
                {
                    return;
                }
                
                // Get image from webcam at full size
                Utils.webCamTextureToMat(webCamTexture, _originalWebcamMat);
                
                // Resize for processing
                Imgproc.resize(_originalWebcamMat, _halfSizeMat, _halfSizeMat.size());
                
                // Convert to RGB for visualization and to GRAY for detection
                Imgproc.cvtColor(_halfSizeMat, _processingRgbMat, Imgproc.COLOR_RGBA2RGB);
                
                // Apply brightness and contrast adjustment if needed
                if (contrast != 1.0f || brightness != 0.0f)
                {
                    _processingRgbMat.convertTo(_processingRgbMat, -1, contrast, brightness);
                }
                
                Imgproc.cvtColor(_halfSizeMat, _processingGrayMat, Imgproc.COLOR_RGBA2GRAY);

              
                // Reset detection containers
                _detectedMarkerIds.create(0, 1, CvType.CV_32S);
                _detectedMarkerCorners.Clear();
                _rejectedMarkerCandidates.Clear();
                
                // Detect markers on grayscale for improved robustness
                arucoDetector.detectMarkers(_processingGrayMat, _detectedMarkerCorners, _detectedMarkerIds, _rejectedMarkerCandidates);
                if (Debug.isDebugBuild)
                {
                    int idCount = (int)_detectedMarkerIds.total();
                    int rejCount = _rejectedMarkerCandidates != null ? _rejectedMarkerCandidates.Count : 0;
                    if (idCount == 0 && rejCount > 0)
                        Debug.Log($"ArUco: 0 ids, rejected={rejCount}. Try DivideNumber=1, improve lighting, move closer, or check dictionary.");
                }
                
                // Draw detected markers for visualization (always when any ids > 0)
                int idCountNow = (int)_detectedMarkerIds.total();
                if (idCountNow > 0)
                {
                    Objdetect.drawDetectedMarkers(_processingRgbMat, _detectedMarkerCorners, _detectedMarkerIds, new Scalar(0, 255, 0));

                    // Enhanced visualization for corner alignment
                    if (enhancedVisualization)
                    {
                        DrawEnhancedMarkerCorners();
                    }
                }
                // Commented out to remove blue edges when no markers detected
                // else if (_rejectedMarkerCandidates != null && _rejectedMarkerCandidates.Count > 0)
                // {
                //     // When nothing decoded, visualize rejected candidates in red to aid debugging
                //     DrawRejectedCandidatesInRed();
                // }
                        
                 

                // Update result texture for visualization
                if (resultTexture != null)
                {
                    Utils.matToTexture2D(_processingRgbMat, resultTexture);
                }
            }
        }

        /// <summary>
        /// Draws red outlines for rejected marker candidates to help debugging when no ids are detected.
        /// </summary>
        private void DrawRejectedCandidatesInRed()
        {
            if (_processingRgbMat == null || _rejectedMarkerCandidates == null) return;
            Scalar red = new Scalar(0, 0, 255);
            for (int k = 0; k < _rejectedMarkerCandidates.Count; k++)
            {
                Mat c = _rejectedMarkerCandidates[k];
                // Expecting a 4-corner contour (1x4x2 or 4x1x2). Sample points robustly.
                List<Point> pts = new List<Point>(4);
                int total = (int)(c.total());
                for (int i = 0; i < total; i++)
                {
                    double[] xy = c.get(i, 0);
                    if (xy == null || xy.Length < 2) continue;
                    pts.Add(new Point(xy[0], xy[1]));
                }
                if (pts.Count < 4)
                {
                    // Try alternate layout (1xN)
                    for (int i = 0; i < c.cols(); i++)
                    {
                        double[] xy = c.get(0, i);
                        if (xy == null || xy.Length < 2) continue;
                        pts.Add(new Point(xy[0], xy[1]));
                    }
                }
                if (pts.Count >= 2)
                {
                    for (int i = 0; i < pts.Count; i++)
                    {
                        Point a = pts[i];
                        Point b = pts[(i + 1) % pts.Count];
                        Imgproc.line(_processingRgbMat, a, b, red, 1);
                    }
                }
            }
        }

        /// <summary>
        /// Estimate pose for each detected marker and update corresponding game objects
        /// </summary>
        /// <param name="arObjects">Dictionary mapping marker IDs to game objects</param>
        /// <param name="camTransform">Camera transform for world-space positioning</param>
        public void EstimatePoseCanonicalMarker(Dictionary<int, GameObject> arObjects, Transform camTransform)
        {
            // Skip if not ready or no markers detected
            if (!_isReady || _detectedMarkerCorners == null || _detectedMarkerCorners.Count == 0)
            {
                return;
            }

            // Define 3D coordinates of marker corners (marker center is at origin)
            using (MatOfPoint3f objectPoints = new MatOfPoint3f(
                new Point3(-_markerLength / 2f, _markerLength / 2f, 0),
                new Point3(_markerLength / 2f, _markerLength / 2f, 0),
                new Point3(_markerLength / 2f, -_markerLength / 2f, 0),
                new Point3(-_markerLength / 2f, -_markerLength / 2f, 0)
            ))
            {
                // Process each detected marker
                for (int i = 0; i < _detectedMarkerCorners.Count; i++)
                {
                    // Get marker ID
                    int currentMarkerId = (int)_detectedMarkerIds.get(i, 0)[0];
                    
                    // Check if this marker has a corresponding game object
                    if (!arObjects.TryGetValue(currentMarkerId, out GameObject targetObject) || targetObject == null)
                        continue;
                    
                    using (Mat rotationVec = new Mat(1, 1, CvType.CV_64FC3))
                    using (Mat translationVec = new Mat(1, 1, CvType.CV_64FC3))
                    using (Mat corner_4x1 = _detectedMarkerCorners[i].reshape(2, 4))
                    using (MatOfPoint2f imagePoints = new MatOfPoint2f(corner_4x1))
                    {
                        // Solve PnP to get marker pose
                        Calib3d.solvePnP(objectPoints, imagePoints, _cameraIntrinsicMatrix, _cameraDistortionCoeffs, rotationVec, translationVec);
                        
                        // Convert to Unity coordinate system
                        double[] rvecArr = new double[3];
                        rotationVec.get(0, 0, rvecArr);
                        double[] tvecArr = new double[3];
                        translationVec.get(0, 0, tvecArr);
                        PoseData poseData = ARUtils.ConvertRvecTvecToPoseData(rvecArr, tvecArr);

                        // Get previous pose for this marker (or create new)
                        if (!_prevPoseDataDictionary.TryGetValue(currentMarkerId, out PoseData prevPose))
                        {
                            prevPose = new PoseData();
                            _prevPoseDataDictionary[currentMarkerId] = prevPose;
                        }

                        // Apply low-pass filter if we have previous pose data
                        if (prevPose.pos != Vector3.zero)
                        {
                            float t = _poseFilterCoefficient;
                            
                            // Filter position with linear interpolation
                            poseData.pos = Vector3.Lerp(poseData.pos, prevPose.pos, t);
                            
                            // Filter rotation with spherical interpolation
                            poseData.rot = Quaternion.Slerp(poseData.rot, prevPose.rot, t);
                        }
                        
                        // Store current pose for next frame
                        _prevPoseDataDictionary[currentMarkerId] = poseData;

                        // Convert pose to matrix and apply to game object
                        var arMatrix = ARUtils.ConvertPoseDataToMatrix(ref poseData, true);
                        arMatrix = camTransform.localToWorldMatrix * arMatrix;
                        ARUtils.SetTransformFromMatrix(targetObject.transform, ref arMatrix);
                    }
                }

                // Optional feature to deactivate objects for markers that weren't detected
                // (Use only if required by your application)
                // foreach (var kvp in arObjects)
                // {
                //     int markerId = kvp.Key;
                //     GameObject obj = kvp.Value;
                //     
                //     // Check if this marker was detected in this frame
                //     bool markerDetectedThisFrame = false;
                //     for (int i = 0; i < _detectedMarkerIds.total(); i++)
                //     {
                //         if ((int)_detectedMarkerIds.get(i, 0)[0] == markerId)
                //         {
                //             markerDetectedThisFrame = true;
                //             break;
                //         }
                //     }
                //     
                //     // Deactivate the object if the marker wasn't detected
                //     if (!markerDetectedThisFrame && obj != null)
                //     {
                //         obj.SetActive(false);
                //     }
                // }
            }
        }

        /// <summary>
        /// Explicitly release resources when the object is disposed
        /// </summary>
        public void Dispose()
        {
            ReleaseResources();
        }

        /// <summary>
        /// Clean up when object is destroyed
        /// </summary>
        void OnDestroy()
        {
            ReleaseResources();
        }

        /// <summary>
        /// Draw enhanced corner visualization to help with marker alignment
        /// </summary>
        private void DrawEnhancedMarkerCorners()
        {
            for (int i = 0; i < _detectedMarkerCorners.Count; i++)
            {
                int markerId = (int)_detectedMarkerIds.get(i, 0)[0];
                Mat corners = _detectedMarkerCorners[i];
                
                // Get corner points
                Point[] cornerPoints = new Point[4];
                for (int j = 0; j < 4; j++)
                {
                    double[] point = corners.get(0, j);
                    cornerPoints[j] = new Point(point[0], point[1]);
                }
                
                // Draw corner 0 (blue square) larger and more prominent
                Imgproc.circle(_processingRgbMat, cornerPoints[0], 8, new Scalar(255, 0, 0), -1); // Blue filled circle
                Imgproc.circle(_processingRgbMat, cornerPoints[0], 10, new Scalar(255, 255, 255), 2); // White border
                
                // Draw other corners in different colors
                Imgproc.circle(_processingRgbMat, cornerPoints[1], 5, new Scalar(0, 255, 255), -1); // Yellow
                Imgproc.circle(_processingRgbMat, cornerPoints[2], 5, new Scalar(255, 0, 255), -1); // Magenta  
                Imgproc.circle(_processingRgbMat, cornerPoints[3], 5, new Scalar(0, 255, 0), -1); // Green
                
                // Draw marker ID and corner labels
                Point textPos = new Point(cornerPoints[0].x - 20, cornerPoints[0].y - 15);
                Imgproc.putText(_processingRgbMat, $"ID:{markerId}", textPos, Imgproc.FONT_HERSHEY_SIMPLEX, 0.5, new Scalar(255, 255, 255), 1);
                
                // Label corners
                for (int j = 0; j < 4; j++)
                {
                    Point labelPos = new Point(cornerPoints[j].x + 5, cornerPoints[j].y - 5);
                    Scalar color = j == 0 ? new Scalar(255, 255, 255) : new Scalar(0, 0, 0);
                    Imgproc.putText(_processingRgbMat, j.ToString(), labelPos, Imgproc.FONT_HERSHEY_SIMPLEX, 0.4, color, 1);
                }
            }
        }

        /// <summary>
        /// Enable or disable enhanced corner visualization
        /// </summary>
        public void SetEnhancedVisualization(bool enabled)
        {
            enhancedVisualization = enabled;
        }

        /// <summary>
        /// Get the detected marker corner positions in world space for a specific marker ID.
        /// Uses solvePnP with stored camera intrinsics to compute accurate 3D corners.
        /// Corner order: [0] Top-Left, [1] Top-Right, [2] Bottom-Right, [3] Bottom-Left
        /// </summary>
        public Vector3[] GetDetectedMarkerCorners(int markerId, Transform cameraTransform)
        {
            if (!_isReady || _detectedMarkerCorners == null || _detectedMarkerCorners.Count == 0)
                return null;

            // Find the marker in detected markers
            for (int i = 0; i < _detectedMarkerIds.total(); i++)
            {
                int currentMarkerId = (int)_detectedMarkerIds.get(i, 0)[0];
                if (currentMarkerId == markerId)
                {
                    try
                    {
                        // Define 3D coordinates of marker corners in marker local frame (center at origin)
                        using (MatOfPoint3f objectPoints = new MatOfPoint3f(
                            new Point3(-_markerLength / 2f, _markerLength / 2f, 0),  // TL
                            new Point3( _markerLength / 2f, _markerLength / 2f, 0),  // TR
                            new Point3( _markerLength / 2f,-_markerLength / 2f, 0),  // BR
                            new Point3(-_markerLength / 2f,-_markerLength / 2f, 0)   // BL
                        ))
                        using (Mat rvec = new Mat(1, 1, CvType.CV_64FC3))
                        using (Mat tvec = new Mat(1, 1, CvType.CV_64FC3))
                        using (Mat corner_4x1 = _detectedMarkerCorners[i].reshape(2, 4))
                        using (MatOfPoint2f imagePoints = new MatOfPoint2f(corner_4x1))
                        {
                            // Solve for marker pose relative to camera
                            bool ok = Calib3d.solvePnP(objectPoints, imagePoints, _cameraIntrinsicMatrix, _cameraDistortionCoeffs, rvec, tvec);
                            if (!ok)
                                return null;

                            // Convert to Unity pose
                            double[] rvecArr = new double[3];
                            double[] tvecArr = new double[3];
                            rvec.get(0, 0, rvecArr);
                            tvec.get(0, 0, tvecArr);
                            PoseData poseData = ARUtils.ConvertRvecTvecToPoseData(rvecArr, tvecArr);

                            // Build marker local-to-world matrix
                            var markerLocalToCam = ARUtils.ConvertPoseDataToMatrix(ref poseData, true);
                            var markerLocalToWorld = cameraTransform.localToWorldMatrix * markerLocalToCam;

                            // Transform canonical local corners to world space
                            // Order must match ArUco convention: [TopLeft, TopRight, BottomRight, BottomLeft]
                            float h = _markerLength * 0.5f;
                            Vector3[] localCorners = new Vector3[]
                            {
                                new Vector3(-h,  h, 0), // TL
                                new Vector3( h,  h, 0), // TR
                                new Vector3( h, -h, 0), // BR
                                new Vector3(-h, -h, 0)  // BL
                            };

                            Vector3[] worldCorners = new Vector3[4];
                            for (int j = 0; j < 4; j++)
                            {
                                worldCorners[j] = markerLocalToWorld.MultiplyPoint3x4(localCorners[j]);
                            }

                            return worldCorners;
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"Failed to get corners for marker {markerId}: {e.Message}");
                    }
                }
            }

            return null; // Marker not found
        }
        
        /// <summary>
        /// Get the raw 2D pixel coordinates of detected marker corners for pose estimation.
        /// This is different from GetDetectedMarkerCorners which returns 3D world coordinates.
        /// </summary>
        public Vector2[] GetDetectedMarkerPixelCorners(int markerId)
        {
            if (!_isReady || _detectedMarkerCorners == null || _detectedMarkerCorners.Count == 0)
                return null;

            // Find the marker in detected markers
            for (int i = 0; i < _detectedMarkerIds.total(); i++)
            {
                int currentMarkerId = (int)_detectedMarkerIds.get(i, 0)[0];
                if (currentMarkerId == markerId)
                {
                    try
                    {
                        // Get the raw pixel coordinates of the detected corners
                        Vector2[] corners = new Vector2[4];
                        using (Mat corner_4x1 = _detectedMarkerCorners[i].reshape(2, 4))
                        {
                            for (int j = 0; j < 4; j++)
                            {
                                double[] point = corner_4x1.get(j, 0);
                                corners[j] = new Vector2((float)point[0], (float)point[1]);
                            }
                        }
                        
                        return corners;
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"Failed to get pixel corners for marker {markerId}: {e.Message}");
                    }
                }
            }
            
            return null; // Marker not found
        }
        
        /// <summary>
        /// Estimate pose using OpenCV board approach for multiple markers as a rigid body.
        /// This is similar to the cube tracking approach from the GitHub example.
        /// </summary>
        /// <param name="boardCorners">3D coordinates of all marker corners on the dice</param>
        /// <param name="boardIds">Marker IDs corresponding to the corners</param>
        /// <param name="detectedCorners">Currently detected marker corners</param>
        /// <param name="detectedIds">Currently detected marker IDs</param>
        /// <returns>PoseData for the entire dice, or null if estimation failed</returns>
        public PoseData? EstimateBoardPose(Vector3[][] boardCorners, int[] boardIds, List<Mat> detectedCorners, List<int> detectedIds)
        {
            if (!IsReady || detectedCorners == null || detectedIds == null || detectedCorners.Count == 0)
            {
                return null;
            }
            
            try
            {
                // Use traditional solvePnP with all detected marker corners
                // This achieves similar results to estimatePoseBoard by treating all corners as one object
                List<Vector3> objectPoints = new List<Vector3>();
                List<Vector2> imagePoints = new List<Vector2>();
                
                // Collect all corner correspondences from detected markers
                for (int i = 0; i < detectedIds.Count; i++)
                {
                    int detectedId = detectedIds[i];
                    
                    // Find this marker in the board definition
                    int boardIndex = -1;
                    for (int j = 0; j < boardIds.Length; j++)
                    {
                        if (boardIds[j] == detectedId)
                        {
                            boardIndex = j;
                            break;
                        }
                    }
                    
                    if (boardIndex >= 0 && boardIndex < boardCorners.Length)
                    {
                        // Get the 3D corners from board definition
                        Vector3[] board3DCorners = boardCorners[boardIndex];
                        
                        // Get the 2D pixel corners directly from detection
                        Vector2[] pixel2DCorners = GetDetectedMarkerPixelCorners(detectedId);
                        
                        if (pixel2DCorners != null && pixel2DCorners.Length == 4)
                        {
                            // Add all 4 corner correspondences
                            for (int j = 0; j < 4; j++)
                            {
                                objectPoints.Add(board3DCorners[j]);
                                imagePoints.Add(pixel2DCorners[j]);
                            }
                            
                            if (Debug.isDebugBuild)
                            {
                                Debug.Log($"Marker {detectedId}: 3D={board3DCorners[0]:F4} -> 2D={pixel2DCorners[0]:F1}");
                            }
                        }
                    }
                }
                
                if (objectPoints.Count < 4) // Need at least 4 points for pose estimation
                {
                    return null;
                }
                
                // Convert to OpenCV format
                using (MatOfPoint3f objectPointsMat = new MatOfPoint3f())
                using (MatOfPoint2f imagePointsMat = new MatOfPoint2f())
                using (Mat rvec = new Mat(1, 1, CvType.CV_64FC3))
                using (Mat tvec = new Mat(1, 1, CvType.CV_64FC3))
                {
                    // Convert Vector3 list to Point3 array for MatOfPoint3f
                    Point3[] objPts = new Point3[objectPoints.Count];
                    for (int i = 0; i < objectPoints.Count; i++)
                    {
                        Vector3 pt = objectPoints[i];
                        objPts[i] = new Point3(pt.x, pt.y, pt.z);
                    }
                    objectPointsMat.fromArray(objPts);
                    
                    // Convert Vector2 list to Point array for MatOfPoint2f
                    Point[] imgPts = new Point[imagePoints.Count];
                    for (int i = 0; i < imagePoints.Count; i++)
                    {
                        Vector2 pt = imagePoints[i];
                        imgPts[i] = new Point(pt.x, pt.y);
                    }
                    imagePointsMat.fromArray(imgPts);
                    
                    // Solve PnP for all points together (rigid body constraint)
                    bool success = Calib3d.solvePnP(objectPointsMat, imagePointsMat, _cameraIntrinsicMatrix, _cameraDistortionCoeffs, rvec, tvec);
                    
                    if (success)
                    {
                        // Convert to Unity format
                        double[] rvecArr = new double[3];
                        rvec.get(0, 0, rvecArr);
                        double[] tvecArr = new double[3];
                        tvec.get(0, 0, tvecArr);
                        
                        PoseData boardPose = ARUtils.ConvertRvecTvecToPoseData(rvecArr, tvecArr);
                        
                        if (Debug.isDebugBuild)
                        {
                            Debug.Log($"Board pose estimated with {detectedIds.Count} markers ({objectPoints.Count} points): pos={boardPose.pos:F3}, rot={boardPose.rot.eulerAngles:F1}°");
                        }
                        
                        return boardPose;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Board pose estimation failed: {ex.Message}");
            }
            
            return null;
        }

        /// <summary>
        /// Convenience wrapper to estimate board pose and return world-space position/rotation.
        /// </summary>
        public bool EstimateBoardPoseWorld(Vector3[][] boardCorners, int[] boardIds, Transform cameraTransform, out Vector3 worldPos, out Quaternion worldRot)
        {
            worldPos = Vector3.zero;
            worldRot = Quaternion.identity;

            var (corners, ids) = GetDetectedMarkersForBoard();
            if (ids == null || ids.Count == 0)
                return false;

            var pose = EstimateBoardPose(boardCorners, boardIds, corners, ids);
            if (pose.HasValue)
            {
                // Convert to world transform
                var pd = pose.Value;
                var localMatrix = ARUtils.ConvertPoseDataToMatrix(ref pd, true);
                var worldMatrix = cameraTransform.localToWorldMatrix * localMatrix;

                // Extract pos/rot from world matrix
                worldPos = worldMatrix.GetColumn(3);
                worldRot = Quaternion.LookRotation(worldMatrix.GetColumn(2), worldMatrix.GetColumn(1));
                return true;
            }

            return false;
        }
        
        /// <summary>
        /// Get currently detected marker corners and IDs for board estimation
        /// </summary>
        public (List<Mat> corners, List<int> ids) GetDetectedMarkersForBoard()
        {
            List<Mat> corners = new List<Mat>();
            List<int> ids = new List<int>();
            
            if (_detectedMarkerCorners != null && _detectedMarkerIds != null && _detectedMarkerIds.total() > 0)
            {
                // Copy corners
                corners.AddRange(_detectedMarkerCorners);
                
                // Convert MatOfInt to List<int>
                int[] idsArray = new int[(int)_detectedMarkerIds.total()];
                _detectedMarkerIds.get(0, 0, idsArray);
                ids.AddRange(idsArray);
            }
            
            return (corners, ids);
        }

        /// <summary>
        /// Draws an XYZ axis tripod (R,G,B) for a given world pose onto the current result image.
        /// </summary>
        public void DrawWorldAxesOnImage(Vector3 worldPos, Quaternion worldRot, Transform cameraTransform, float axisLength, Texture2D targetTexture)
        {
            if (!_isReady || targetTexture == null) return;

            // Define axis endpoints in world space
            Vector3 p0 = worldPos;
            Vector3 px = worldPos + worldRot * (Vector3.right * axisLength);
            Vector3 py = worldPos + worldRot * (Vector3.up * axisLength);
            Vector3 pz = worldPos + worldRot * (Vector3.forward * axisLength);

            // Project a world point to pixel using current intrinsics
            bool Project(Vector3 w, out Point ip)
            {
                // Convert to camera local
                Vector3 c = cameraTransform.worldToLocalMatrix.MultiplyPoint3x4(w);
                if (c.z <= 0.001f) { ip = new Point(); return false; }
                double fx = _cameraIntrinsicMatrix.get(0, 0)[0];
                double fy = _cameraIntrinsicMatrix.get(1, 1)[0];
                double cx = _cameraIntrinsicMatrix.get(0, 2)[0];
                double cy = _cameraIntrinsicMatrix.get(1, 2)[0];
                // OpenCV pixel coords: origin top-left, y down. Unity camera local has y up, so negate y.
                double u = fx * (c.x / c.z) + cx;
                double v = fy * (-c.y / c.z) + cy;
                ip = new Point(u, v);
                return true;
            }

            if (!Project(p0, out var p0i)) return;
            if (Project(px, out var pxi)) DrawLineOnMat(p0i, pxi, new Scalar(255, 0, 0));
            if (Project(py, out var pyi)) DrawLineOnMat(p0i, pyi, new Scalar(0, 255, 0));
            if (Project(pz, out var pzi)) DrawLineOnMat(p0i, pzi, new Scalar(0, 0, 255));
            // Upload once after all lines are drawn
            if (_processingRgbMat != null)
            {
                Utils.matToTexture2D(_processingRgbMat, targetTexture);
            }
        }

        private void DrawLineOnMat(Point a, Point b, Scalar color)
        {
            // Draw in the processing Mat and update texture for simplicity
            if (_processingRgbMat == null) return;
            int w = _processingRgbMat.width();
            int h = _processingRgbMat.height();
            Point p0 = new Point(Mathf.Clamp((float)a.x, 0, w - 1), Mathf.Clamp((float)a.y, 0, h - 1));
            Point p1 = new Point(Mathf.Clamp((float)b.x, 0, w - 1), Mathf.Clamp((float)b.y, 0, h - 1));
            Imgproc.line(_processingRgbMat, p0, p1, color, 2);
        }
        
        /// <summary>
        /// Type of ArUco marker to detect
        /// </summary>
        public enum MarkerType
        {
            CanonicalMarker,
            GridBoard,
            ChArUcoBoard,
            ChArUcoDiamondMarker
        }

        /// <summary>
        /// Available ArUco dictionaries for marker detection
        /// </summary>
        public enum ArUcoDictionary
        {
            DICT_4X4_50 = Objdetect.DICT_4X4_50,
            DICT_4X4_100 = Objdetect.DICT_4X4_100,
            DICT_4X4_250 = Objdetect.DICT_4X4_250,
            DICT_4X4_1000 = Objdetect.DICT_4X4_1000,
            DICT_5X5_50 = Objdetect.DICT_5X5_50,
            DICT_5X5_100 = Objdetect.DICT_5X5_100,
            DICT_5X5_250 = Objdetect.DICT_5X5_250,
            DICT_5X5_1000 = Objdetect.DICT_5X5_1000,
            DICT_6X6_50 = Objdetect.DICT_6X6_50,
            DICT_6X6_100 = Objdetect.DICT_6X6_100,
            DICT_6X6_250 = Objdetect.DICT_6X6_250,
            DICT_6X6_1000 = Objdetect.DICT_6X6_1000,
            DICT_7X7_50 = Objdetect.DICT_7X7_50,
            DICT_7X7_100 = Objdetect.DICT_7X7_100,
            DICT_7X7_250 = Objdetect.DICT_7X7_250,
            DICT_7X7_1000 = Objdetect.DICT_7X7_1000,
            DICT_ARUCO_ORIGINAL = Objdetect.DICT_ARUCO_ORIGINAL,
        }
    }
}