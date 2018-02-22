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
            ParallelBackprojectDistance = 1000;
            BadProjectionRatio = 0.4;
            MahalanobisThreshold = 4;
            FixedErrorThreshold = 20;
            MajorAxisThreshold = 100;
        }
        private ImageNodeDelegate ImageToNode;

        /// <summary>
        /// When two camera rays are parallel, try backprojecting from this distance.
        /// </summary>
        public double ParallelBackprojectDistance;
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
        /// <summary>
        /// Maximum uncertainty
        /// </summary>
        public double MajorAxisThreshold;

        internal struct ProjectionResult
        {
            public bool intersection;
            public double modelT, dataT;
            public Vector2 error;
        }

        public ImagePairCorrespondence Filter(MatchingContext context, ImagePairCorrespondence matches)
        {
            SceneNode modelNode = ImageToNode(matches.ModelImage);
            SceneNode dataNode = ImageToNode(matches.DataImage);
            ImageFeature[] modelFeatures = context.DetectedFeatures[matches.ModelImage];
            ImageFeature[] dataFeatures = context.DetectedFeatures[matches.DataImage];

            if (modelNode == null || dataNode == null) return matches;

            var dataToWorld = dataNode.GetOrAddComponent<NodeUncertainTransform>().LocalToWorld;
            var modelToWorld = modelNode.GetOrAddComponent<NodeUncertainTransform>().LocalToWorld;
            var dataToModelOld = dataToWorld.TimesInverse(modelToWorld);
            UncertainRigidTransform dataToModel = dataNode.GetOrAddComponent<NodeUncertainTransform>().To(modelNode);

            var modelImg = GetImage(matches.ModelImage);
            var dataImg = GetImage(matches.DataImage);
            var modelCam = modelImg.CameraModel;
            var dataCam = dataImg.CameraModel;

            // if 'data' node has a convex hull, compute it (uncertainty-inflated) in model space
            ConvexHull dataHullInModel = null;
            var ch = dataNode.GetComponent<NodeConvexHull>();
            if (ch != null)
            {
                dataHullInModel = ConvexHull.Transformed(ch.Hull, dataToModel);
            }

            // Cache result of model ray -> data frustum intersection, because model rays
            // can be repeated
            Dictionary<int, bool> modelRayIntersects = new Dictionary<int, bool>();
            List<KeyValuePair<int, int>> goodMatches = new List<KeyValuePair<int, int>>();

            /*var result = new Emgu.CV.Image<Emgu.CV.Structure.Bgr, byte>(
                modelImg.Width + dataImg.Width,
                Math.Max(modelImg.Height, dataImg.Height));
            result.ROI = new System.Drawing.Rectangle(0, 0, modelImg.Width, modelImg.Height);
            modelImg.ToEmgu<Emgu.CV.Structure.Bgr>().CopyTo(result);
            result.ROI = new System.Drawing.Rectangle(modelImg.Width, 0, dataImg.Width, dataImg.Height);
            dataImg.ToEmgu<Emgu.CV.Structure.Bgr>().CopyTo(result);
            result.ROI = new System.Drawing.Rectangle(0, 0, modelImg.Width + dataImg.Width, Math.Max(modelImg.Height, dataImg.Height));*/

            int rejectedHull = 0;
            int rejectedSigma = 0;
            int rejectedInvalid = 0;
            int rejectedError = 0;

            foreach (var pair in matches.DataToModel)
            {
                var modelFeature = modelFeatures[pair.Value];
                var dataFeature = dataFeatures[pair.Key];

                var modelRay = modelCam.ProjectRay(modelFeature.Location);
                var dataRay = dataCam.ProjectRay(dataFeature.Location);

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
                    double range;
                    if (!RayExtensions.ClosestIntersection(modelRay, mdrm, out modelT, out dataT))
                    {
                        // Rays are parallel or very close to parallel - try backprojecting from ~infinity
                        dataT = ParallelBackprojectDistance;
                        modelT = 0;
                        res.intersection = false;
                    }
                    else
                    {
                        res.intersection = true;
                    }
                    Vector3 dataPt = mdrm.Position + mdrm.Direction * dataT;
                    Vector3 modelPt = modelRay.Position + modelRay.Direction * modelT;

                    Vector2 modelInData = dataCam.Backproject(Vector3.Transform(modelPt, Matrix.Invert(mat)), out range);
                    Vector2 dataInModel = modelCam.Backproject(dataPt, out range);
                    Vector2 midErr = modelInData - dataFeature.Location;
                    Vector2 dimErr = dataInModel - modelFeature.Location;

                    if (midErr.LengthSquared() < dimErr.LengthSquared())
                    {
                        res.error = midErr;
                    }
                    else
                    {
                        res.error = dimErr;
                    }
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
                        if (badPoints > 3) return CreateVector.Dense<double>(2);
                        totalPoints++;
                        ProjectionResult res;
                        try
                        {
                            res = Reproject(mat);
                        }
                        catch (Exception e)
                        {
                            badPoints++;
                            return CreateVector.Dense<double>(2);
                        }
                        // Mark projection as bad if rays are parallel or the point is behind
                        // either camera
                        if (res.dataT < -0.01 || res.modelT < -0.01)
                        {
                            badPoints++;
                        }
                        return res.error.ToMathNet();
                    });
                    // If any points failed to meaningfully project, skip match
                    if (badPoints > 3)
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

                    var eig = error.Covariance.Evd(Symmetricity.Symmetric);
                    double majorAxis = Math.Sqrt(Math.Max(eig.EigenValues[0].Real, eig.EigenValues[1].Real));
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
                        var res = Reproject(dataToModel.Mean);
                        if (res.modelT < -0.01 || res.dataT < -0.01 || res.error.LengthSquared() > FixedErrorThreshold * FixedErrorThreshold)
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
                return null;
            }
            return new ImagePairCorrespondence(matches.ModelImage, matches.DataImage, goodMatches);
        }
    }
}
