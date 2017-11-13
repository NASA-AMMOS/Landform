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
    public class KnownGeometryFilter : IMatchFilter
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(KnownGeometryFilter));

        public delegate SceneNode ImageNodeDelegate(ImageRef image);

        public KnownGeometryFilter(ImageNodeDelegate imageToNode)
        {
            ImageToNode = imageToNode;
        }
        ImageNodeDelegate ImageToNode;

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
            var worldToModel = modelNode.GetOrAddComponent<NodeUncertainTransform>().WorldToLocal;
            UncertainRigidTransform dataToModel = dataToWorld * worldToModel;

            var modelCam = matches.ModelImage.Image.CameraModel;
            var dataCam = matches.DataImage.Image.CameraModel;

            // if 'data' node has a convex hull, compute it (uncertainty-inflated) in model space
            ConvexHull dataHullInModel = null;
            var ch = dataNode.GetComponent<NodeConvexHull>();
            if (ch != null)
            {
                dataHullInModel = ch.hull.Transformed(dataToModel);
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

                    var mdrm = Ray.Transform(dataRay, mat);
                    double modelT, dataT;
                    Vector2 backprojected;
                    double range;
                    if (!RayExtensions.ClosestIntersection(modelRay, mdrm, out modelT, out dataT))
                    {
                        // Rays are parallel or very close to parallel - try backprojecting from ~infinity
                        dataT = 1000;
                        modelT = 0;
                        res.intersection = false;
                    }
                    else
                    {
                        res.intersection = true;
                    }
                    Vector3 dataPt = mdrm.Position + mdrm.Direction * dataT;
                    backprojected = modelCam.Backproject(dataPt, out range);
                    res.error = backprojected - modelFeature.Location;
                    res.modelT = modelT;
                    res.dataT = dataT;
                    return res;
                };

                if (dataToModel.Uncertain)
                {
                    // Compute probability distribution of reprojection error for closest point
                    int badPoints = 0;
                    int totalPoints = 0;
                    var error = dataToModel.Transform((mat) =>
                    {
                        totalPoints++;
                        var res = Reproject(mat);
                        if (!res.intersection || res.dataT < -0.01 || res.modelT < -0.01)
                        {
                            badPoints++;
                        }
                        return res.error.ToMathNet();
                    });
                    // If more than 40% of points failed to meaningfully project, skip match
                    if (badPoints / (double)totalPoints > 0.4)
                    {
                        rejectedInvalid++;
                        continue;
                    }
                    // If zero error is >4 sigma away from mean, skip match
                    double mhDistSqr = error.MahalanobisDistanceSquared(Vector2.Zero.ToMathNet());
                    if (mhDistSqr > 4*4)
                    {
                        rejectedSigma++;
                        continue;
                    }
                }
                else
                {
                    // Transform is exact-ish, just make sure it's close
                    var res = Reproject(dataToModel.Mean);
                    if (res.modelT < -0.01 || res.dataT < -0.01 || res.error.LengthSquared() > 20 * 20)
                    {
                        rejectedError++;
                        continue;
                    }
                }

                // we peachy
                goodMatches.Add(pair);
            }

            logger.Debug(string.Format("Rejected: {0} for hull intersection, {1} for bad projection, {2} for sigma threshold, {3} for error", rejectedHull, rejectedInvalid, rejectedSigma, rejectedError));

            if (goodMatches.Count == 0) return null;
            matches = new ImagePairCorrespondence(matches.ModelImage, matches.DataImage, matches.ModelFeatures, matches.DataFeatures, goodMatches);
            matches.Compact();
            return matches;
        }
    }
}
