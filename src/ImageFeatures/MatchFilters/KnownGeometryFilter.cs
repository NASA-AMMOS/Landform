using System;
using System.Collections.Generic;
using MathNet.Numerics.LinearAlgebra;
using JPLOPS.Util;
using JPLOPS.Geometry;
using JPLOPS.Imaging;

namespace JPLOPS.ImageFeatures
{
    public class KnownGeometryFilter : IMatchFilter
    {
        public const double DEF_MAHALANOBIS_THRESHOLD = 4;
        public const double DEF_MAJOR_AXIS_THRESHOLD = 100;

        /// <summary>
        /// When two camera rays are parallel, try projecting from this distance.
        /// </summary>
        public double ParallelProjectionDistance = 1000;

        /// <summary>
        /// Number of bad projections to reject an uncertain match.
        /// </summary>
        public int MaxBadProjections = 3;

        /// <summary>
        /// Maximum Mahalanobis distance to accept. Conceptually similar to number of standard deviations.
        /// </summary>
        public double MahalanobisThreshold = DEF_MAHALANOBIS_THRESHOLD;

        /// <summary>
        /// Maximum uncertainty
        /// </summary>
        public double MajorAxisThreshold = DEF_MAJOR_AXIS_THRESHOLD;

        /// <summary>
        /// Error threshold (in pixels) for matches with no transform uncertainty information.
        /// </summary>
        public double FixedErrorThreshold = 20;

        public delegate SceneNode ImageNodeDelegate(string imageUrl);

        private readonly ImageNodeDelegate imageToNode;
        private readonly ILogger logger;

        /// <summary>
        /// Construct with a function mapping image references to nodes.
        /// </summary>
        /// <param name="imageToNode">Should return the scene node associated with a given image</param>
        public KnownGeometryFilter(ILogger logger = null, ImageNodeDelegate imageToNode = null)
        {
            this.logger = logger;
            this.imageToNode = imageToNode;
        }

        public ImagePairCorrespondence Filter(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures,
                                              ImagePairCorrespondence matches)
        {
            var modelNode = imageToNode(matches.ModelImageUrl);
            var dataNode = imageToNode(matches.DataImageUrl);
            return Filter(modelFeatures, dataFeatures, matches, modelNode, dataNode);
        }

        public ImagePairCorrespondence Filter(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures,
                                              ImagePairCorrespondence matches,
                                              SceneNode modelNode, SceneNode dataNode)
        {
            UncertainRigidTransform dataToModel = dataNode.GetComponent<NodeUncertainTransform>().To(modelNode);
            UncertainRigidTransform modelToData = modelNode.GetComponent<NodeUncertainTransform>().To(dataNode);

            var modelCam = modelNode.GetComponent<NodeImage>();
            var dataCam = dataNode.GetComponent<NodeImage>();

            if (modelCam == null || modelCam.CameraModel == null || dataCam == null || dataCam.CameraModel == null)
            {
                throw new ArgumentException("KnownGeometryFilter requires camera models");
            }

            // if data node has a convex hull, compute it (uncertainty-inflated) in model space
            ConvexHull dataHullInModel = null;
            var hullComp = dataNode.GetComponent<NodeConvexHull>();
            if (hullComp != null && hullComp.Hull != null)
            {
                dataHullInModel = ConvexHull.Transformed(hullComp.Hull, dataToModel);
            }

            // if model node has a convex hull, compute it (uncertainty-inflated) in data space
            ConvexHull modelHullInData = null;
            hullComp = modelNode.GetComponent<NodeConvexHull>();
            if (hullComp != null && hullComp.Hull != null)
            {
                modelHullInData = ConvexHull.Transformed(hullComp.Hull, modelToData);
            }

            return Filter(modelFeatures, dataFeatures, matches,
                          modelCam.CameraModel, dataCam.CameraModel, modelToData, dataToModel,
                          modelHullInData, dataHullInModel);
        }

        public ImagePairCorrespondence Filter(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures,
                                              ImagePairCorrespondence matches,
                                              CameraModel modelCam, CameraModel dataCam,
                                              UncertainRigidTransform modelToData, UncertainRigidTransform dataToModel,
                                              ConvexHull modelHullInData = null, ConvexHull dataHullInModel = null)
        {
            // Cache result of model ray -> data frustum intersection, because model rays can be repeated
            var modelRayIntersects = new Dictionary<int, bool>();
            var dataRayIntersects = new Dictionary<int, bool>();
            var goodMatches = new List<FeatureMatch>();

            int rejectedHull = 0;
            int rejectedSigma = 0;
            int rejectedInvalid = 0;
            int rejectedError = 0;

            var epiFinder = new EpipolarLineFinder();
            epiFinder.ParallelProjectionDistance = ParallelProjectionDistance;

            for (int i = 0; i < matches.DataToModel.Length; i++)
            {
                int dataFeatureIndex = matches.DataToModel[i].Key;
                int modelFeatureIndex = matches.DataToModel[i].Value;

                var modelFeature = modelFeatures[modelFeatureIndex];
                var dataFeature = dataFeatures[dataFeatureIndex];

                var modelRay = modelCam.Unproject(modelFeature.Location);
                var dataRay = dataCam.Unproject(dataFeature.Location);

                // if we have a convex hull, check if model ray intersects it at all
                if (dataHullInModel != null)
                {
                    if (!modelRayIntersects.ContainsKey(modelFeatureIndex))
                    {
                        bool intersects = dataHullInModel.Intersects(modelRay);
                        modelRayIntersects[modelFeatureIndex] = intersects;
                    }

                    if (!modelRayIntersects[modelFeatureIndex])
                    {
                        rejectedHull++;
                        continue;
                    }
                }

                if (modelHullInData != null)
                {
                    if (!dataRayIntersects.ContainsKey(dataFeatureIndex))
                    {
                        bool intersects = modelHullInData.Intersects(dataRay);
                        dataRayIntersects[dataFeatureIndex] = intersects;
                    }

                    if (!dataRayIntersects[dataFeatureIndex])
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
                        var epi = epiFinder.Find(modelCam, dataCam, d2m, dataFeature, modelFeature);

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
                        var epi = epiFinder.Find(modelCam, dataCam, dataToModel.Mean, dataFeature, modelFeature);
                        if (!epi.Success)
                        {
                            rejectedInvalid++;
                            continue;
                        }
                        if (epi.ModelT < -0.01 || epi.DataT < -0.01 ||
                            Math.Abs(epi.SignedDistance(modelFeature.Location)) > FixedErrorThreshold)
                        {
                            rejectedError++;
                            continue;
                        }

                        epi = epiFinder.Find(dataCam, modelCam, modelToData.Mean, modelFeature, dataFeature);
                        if (!epi.Success)
                        {
                            rejectedInvalid++;
                            continue;
                        }
                        if (epi.ModelT < -0.01 || epi.DataT < -0.01 ||
                            Math.Abs(epi.SignedDistance(dataFeature.Location)) > FixedErrorThreshold)
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

                goodMatches.Add(new FeatureMatch()
                                {
                                    DataIndex = dataFeatureIndex,
                                    ModelIndex = modelFeatureIndex,
                                    DescriptorDistance = matches.DescriptorDistance[i]
                                });
            }

            if (logger != null)
            {
                logger.LogVerbose("{0} KnownGeometryFilter: rejected {1} for hull intersection, " +
                                  "{2} for bad projection, {3} for sigma threshold, {4} for error",
                                  (new URLPair(matches.ModelImageUrl, matches.DataImageUrl)).ToStringShort(),
                                  rejectedHull, rejectedInvalid, rejectedSigma, rejectedError);
            }

            if (goodMatches.Count == 0)
            {
                return ImagePairCorrespondence.Empty;
            }

            return new ImagePairCorrespondence(matches.ModelImageUrl, matches.DataImageUrl, goodMatches,
                                               matches.FundamentalMatrix, matches.BestTransformEstimate);
        }
    }
}
