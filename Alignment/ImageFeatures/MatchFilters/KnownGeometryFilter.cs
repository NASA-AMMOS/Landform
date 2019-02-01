using OPS.Geometry;
using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.MathExtensions;
using MathNet.Numerics.LinearAlgebra;
using log4net;
using OPS.Plumbing;
using OPS.Imaging.Emgu;

namespace OPS.Alignment
{
    /// <summary>
    /// Filter for pruning feature matches based on a priori known geometry of a scene. 
    /// Takes as input a scene graph with (optional) uncertainty information on transforms.
    /// </summary>
    public class KnownGeometryFilter : PipelineRoutine, IMatchFilter
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(KnownGeometryFilter));

        public delegate SceneNode ImageNodeDelegate(ImageRef image);

        /// <summary>
        /// Construct with a function mapping image references to nodes.
        /// </summary>
        /// <param name="imageToNode">Should return the scene node associated with a given image</param>
        public KnownGeometryFilter(PipelineCore pipeline, ImageNodeDelegate imageToNode)
            : base(pipeline)
        {
            ImageToNode = imageToNode;
            ParallelProjectionDistance = 1000;
            MaxBadProjections = 3;
            MahalanobisThreshold = 4;
            FixedErrorThreshold = 20;
            MajorAxisThreshold = 100;
        }
        private ImageNodeDelegate ImageToNode;

        /// <summary>
        /// When two camera rays are parallel, try projecting from this distance.
        /// </summary>
        public double ParallelProjectionDistance;
        /// <summary>
        /// Number of bad projections to reject an uncertain match.
        /// </summary>
        public int MaxBadProjections;
        /// <summary>
        /// Maximum Mahalanobis distance to accept. Conceptually similar to number of standard deviations.
        /// </summary>
        public double MahalanobisThreshold;
        /// <summary>
        /// Error threshold (in pixels) for matches with no transform uncertainty information.
        /// </summary>
        public double FixedErrorThreshold;
        /// <summary>
        /// Maximum uncertainty
        /// </summary>
        public double MajorAxisThreshold;

        public ImagePairCorrespondence Filter(AlignmentScene scene, ImagePairCorrespondence matches)
        {
            SceneNode modelNode = ImageToNode(matches.ModelImage);
            SceneNode dataNode = ImageToNode(matches.DataImage);
            ImageFeature[] modelFeatures = scene.DetectedFeatures[matches.ModelImage];
            ImageFeature[] dataFeatures = scene.DetectedFeatures[matches.DataImage];

            if (modelNode == null || dataNode == null) return matches;

            UncertainRigidTransform dataToModel = dataNode.GetOrAddComponent<NodeUncertainTransform>().To(modelNode);
            UncertainRigidTransform modelToData = modelNode.GetOrAddComponent<NodeUncertainTransform>().To(dataNode);

 			var modelImg = GetImage(matches.ModelImage);
            var dataImg = GetImage(matches.DataImage);
            var modelCam = modelImg.CameraModel;
            var dataCam = dataImg.CameraModel;

            // if 'data' node has a convex hull, compute it (uncertainty-inflated) in model space
            ConvexHull dataHullInModel = null;
            ConvexHull modelHullInData = null;
            var modelHull = modelNode.GetComponent<NodeConvexHull>();
            var dataHull = dataNode.GetComponent<NodeConvexHull>();
            if (dataHull != null)
            {
                dataHullInModel = ConvexHull.Transformed(dataHull.Hull, dataToModel);
            }
            if (modelHull != null)
            {
                modelHullInData= ConvexHull.Transformed(modelHull.Hull, modelToData);
            }

            // Cache result of model ray -> data frustum intersection, because model rays
            // can be repeated
            Dictionary<int, bool> modelRayIntersects = new Dictionary<int, bool>();
            Dictionary<int, bool> dataRayIntersects = new Dictionary<int, bool>();
            List<KeyValuePair<int, int>> goodMatches = new List<KeyValuePair<int, int>>();

            int rejectedHull = 0;
            int rejectedSigma = 0;
            int rejectedInvalid = 0;
            int rejectedError = 0;

            var epiFinder = new EpipolarLineFinder(Pipeline);
            epiFinder.ParallelProjectionDistance = ParallelProjectionDistance;

            foreach (var pair in matches.DataToModel)
            {
                var modelFeature = modelFeatures[pair.Value];
                var dataFeature = dataFeatures[pair.Key];

                var modelRay = modelCam.Unproject(modelFeature.Location);
                var dataRay = dataCam.Unproject(dataFeature.Location);

                // if we have a convex hull, check if model ray intersects it at all
                if (dataHullInModel != null)
                {
                    if (!modelRayIntersects.ContainsKey(pair.Value))
                    {
                        bool intersects = dataHullInModel.Intersects(modelRay);
                        modelRayIntersects[pair.Value] = intersects;
                    }

                    if (!modelRayIntersects[pair.Value])
                    {
                        rejectedHull++;
                        continue;
                    }
                }
                if (modelHullInData != null)
                {
                    if (!dataRayIntersects.ContainsKey(pair.Key))
                    {
                        bool intersects = modelHullInData.Intersects(dataRay);
                        dataRayIntersects[pair.Key] = intersects;
                    }

                    if (!dataRayIntersects[pair.Key])
                    {
                        rejectedHull++;
                        continue;
                    }
                }


                if (dataToModel.Uncertain)
                {
                    // Compute probability distribution of epipolar error
                    int badPoints = 0;
                    int totalPoints = 0;
                    var error = dataToModel.UnscentedTransform(d2m =>
                    {
                        // If we already know the match will be rejected bail out early
                        if (badPoints >= MaxBadProjections)
                        {
                            return CreateVector.DenseOfArray(new[] { MajorAxisThreshold });
                        }

                        totalPoints++;
                        // Find epipolar line in model image corresponding to data point
                        var epi = epiFinder.Find(matches.ModelImage, matches.DataImage, d2m, dataFeature, modelFeature);

                        if (!epi.Success)
                        {
                            badPoints++;
                            return CreateVector.DenseOfArray(new[] { MajorAxisThreshold });
                        }

                        // Mark projection as bad if rays are parallel or the point is behind
                        // either camera, but still use computed error
                        if (epi.DataT < -0.01 || epi.ModelT < -0.01)
                        {
                            badPoints++;
                        }
                        return CreateVector.DenseOfArray(new[] { epi.SignedDistance(modelFeature.Location) });
                    });
                    // If too many points failed to meaningfully project, skip match
                    if (badPoints >= MaxBadProjections)
                    {
                        rejectedInvalid++;
                        continue;
                    }
                    // If zero error is >n sigma away from mean, skip match
                    double mhDistSqr = error.MahalanobisDistanceSquared(CreateVector.DenseOfArray(new[] { 0.0 }));
                    if (mhDistSqr > MahalanobisThreshold * MahalanobisThreshold)
                    {
                        rejectedSigma++;
                        continue;
                    }
                    
                    double majorAxis = Math.Sqrt(error.Covariance[0, 0]);
                    if (majorAxis > MajorAxisThreshold)
                    {
                        rejectedError++;
                        continue;
                    }
                }
                else
                {
                    // Transform is exact-ish, just make sure it's close
                    try
                    {
                        var epi = epiFinder.Find(matches.ModelImage, matches.DataImage, dataToModel.Mean, dataFeature, modelFeature);
                        if (!epi.Success)
                        {
                            rejectedInvalid++;
                            continue;
                        }
                        if (epi.ModelT < -0.01 || epi.DataT < -0.01 || Math.Abs(epi.SignedDistance(modelFeature.Location)) > FixedErrorThreshold)
                        {
                            rejectedError++;
                            continue;
                        }

                        epi = epiFinder.Find(dataImg.CameraModel, modelImg.CameraModel, modelToData.Mean, modelFeature, dataFeature);
                        if (!epi.Success)
                        {
                            rejectedInvalid++;
                            continue;
                        }
                        if (epi.ModelT < -0.01 || epi.DataT < -0.01 || Math.Abs(epi.SignedDistance(dataFeature.Location)) > FixedErrorThreshold)
                        {
                            rejectedError++;
                            continue;
                        }
                    }
                    catch (Exception)
                    {
                        rejectedInvalid++;
                        continue;
                    }
                }

                // we peachy
                goodMatches.Add(pair);
            }

            logger.Debug(string.Format("Rejected: {0} for hull intersection, {1} for bad projection, {2} for sigma threshold, {3} for error", rejectedHull, rejectedInvalid, rejectedSigma, rejectedError));

            if (goodMatches.Count == 0)
            {
                return ImagePairCorrespondence.Empty;
            }
            return new ImagePairCorrespondence(matches.ModelImage, matches.DataImage, goodMatches, matches.FundamentalMatrix, matches.BestTransformEstimate);
        }
    }
}
