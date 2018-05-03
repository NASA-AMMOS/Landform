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

namespace OPS.Alignment
{
    /// <summary>
    /// Filter for pruning feature matches based on a priori known geometry of a scene. 
    /// Takes as input a scene graph with (optional) uncertainty information on transforms.
    /// </summary>
    public class KnownGeometryFilter : IMatchFilter
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(KnownGeometryFilter));

        public delegate SceneNode ImageNodeDelegate(ImageRef image);

        /// <summary>
        /// Construct with a function mapping image references to nodes.
        /// </summary>
        /// <param name="imageToNode">Should return the scene node associated with a given image</param>
        public KnownGeometryFilter(ImageNodeDelegate imageToNode)
        {
            ImageToNode = imageToNode;
            ParallelProjectionDistance = 1000;
            BadProjectionRatio = 0.4;
            MahalanobisThreshold = 4;
            FixedErrorThreshold = 20;
        }
        private ImageNodeDelegate ImageToNode;

        /// <summary>
        /// When two camera rays are parallel, try projecting from this distance.
        /// </summary>
        public double ParallelProjectionDistance;
        /// <summary>
        /// Ratio of bad to good projections to reject an uncertain match.
        /// </summary>
        public double BadProjectionRatio;
        /// <summary>
        /// Maximum Mahalanobis distance to accept. Conceptually similar to number of standard deviations.
        /// </summary>
        public double MahalanobisThreshold;
        /// <summary>
        /// Error threshold (in pixels) for matches with no transform uncertainty information.
        /// </summary>
        public double FixedErrorThreshold;

        internal struct ProjectionResult
        {
            public bool intersection;
            public double modelT, dataT;
            public Vector2 error;
        }

        public ImagePairCorrespondence Filter(ImagePairCorrespondence matches)
        {
            SceneNode modelNode = ImageToNode(matches.ModelImage);
            SceneNode dataNode = ImageToNode(matches.DataImage);

            if (modelNode == null || dataNode == null) return matches;

            var dataToWorld = dataNode.GetOrAddComponent<NodeUncertainTransform>().LocalToWorld;
            var modelToWorld = modelNode.GetOrAddComponent<NodeUncertainTransform>().LocalToWorld;
            UncertainRigidTransform dataToModel = dataToWorld.TimesInverse(modelToWorld);

            var modelCam = matches.ModelImage.Image.CameraModel;
            var dataCam = matches.DataImage.Image.CameraModel;

            // if 'data' node has a convex hull, compute it (uncertainty-inflated) in model space
            ConvexHull dataHullInModel = null;
            var ch = dataNode.GetComponent<NodeConvexHull>();
            if (ch != null)
            {
                dataHullInModel = ConvexHull.Transformed(ch.hull, dataToModel);
            }

            // Cache result of model ray -> data frustum intersection, because model rays
            // can be repeated
            Dictionary<int, bool> modelRayIntersects = new Dictionary<int, bool>();
            List<KeyValuePair<int, int>> goodMatches = new List<KeyValuePair<int, int>>();

            int rejectedHull = 0;
            int rejectedSigma = 0;
            int rejectedInvalid = 0;
            int rejectedError = 0;

            foreach (var pair in matches.DataToModel)
            {
                var modelFeature = matches.ModelFeatures[pair.Value];
                var dataFeature = matches.DataFeatures[pair.Key];

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

                Func<Matrix, ProjectionResult> Reproject = (mat) =>
                {
                    ProjectionResult res = new ProjectionResult();

                    var mdrm = RayExtensions.Transform(dataRay, mat);
                    double modelT, dataT;
                    Vector2 projected;
                    double range;
                    if (!RayExtensions.ClosestIntersection(modelRay, mdrm, out modelT, out dataT))
                    {
                        // Rays are parallel or very close to parallel - try projecting from ~infinity
                        dataT = ParallelProjectionDistance;
                        modelT = 0;
                        res.intersection = false;
                    }
                    else
                    {
                        res.intersection = true;
                    }
                    Vector3 dataPt = mdrm.Position + mdrm.Direction * dataT;
                    projected = modelCam.Project(dataPt, out range);
                    res.error = projected - modelFeature.Location;
                    res.modelT = modelT;
                    res.dataT = dataT;
                    return res;
                };

                if (dataToModel.Uncertain)
                {
                    // Compute probability distribution of reprojection error for closest point
                    int badPoints = 0;
                    int totalPoints = 0;
                    var error = dataToModel.UnscentedTransform((mat) =>
                    {
                        totalPoints++;
                        var res = Reproject(mat);
                        // Mark projection as bad if rays are parallel or the point is behind
                        // either camera
                        if (!res.intersection || res.dataT < -0.01 || res.modelT < -0.01)
                        {
                            badPoints++;
                        }
                        return res.error.ToMathNet();
                    });
                    // If more than 40% of points failed to meaningfully project, skip match
                    if (badPoints / (double)totalPoints > BadProjectionRatio)
                    {
                        rejectedInvalid++;
                        continue;
                    }
                    // If zero error is >n sigma away from mean, skip match
                    double mhDistSqr = error.MahalanobisDistanceSquared(Vector2.Zero.ToMathNet());
                    if (mhDistSqr > MahalanobisThreshold * MahalanobisThreshold)
                    {
                        rejectedSigma++;
                        continue;
                    }
                }
                else
                {
                    // Transform is exact-ish, just make sure it's close
                    var res = Reproject(dataToModel.Mean);
                    if (res.modelT < -0.01 || res.dataT < -0.01 || res.error.LengthSquared() > FixedErrorThreshold * FixedErrorThreshold)
                    {
                        rejectedError++;
                        continue;
                    }
                }

                // we peachy
                goodMatches.Add(pair);
            }

            logger.Debug(string.Format("Rejected: {0} for hull intersection, {1} for bad projection, {2} for sigma threshold, {3} for error", rejectedHull, rejectedInvalid, rejectedSigma, rejectedError));

            if (goodMatches.Count == 0)
            {
                return null;
            }
            matches = new ImagePairCorrespondence(matches.ModelImage, matches.DataImage, matches.ModelFeatures, matches.DataFeatures, goodMatches);
            matches.Compact();
            return matches;
        }
    }
}
