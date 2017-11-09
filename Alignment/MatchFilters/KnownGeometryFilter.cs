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

namespace OPS.Alignment.MatchFilters
{
    public class KnownGeometryFilter : IMatchFilter
    {
        public delegate SceneNode ImageNodeDelegate(ImageRef image);

        public KnownGeometryFilter(ImageNodeDelegate imageToNode)
        {
            ImageToNode = imageToNode;
        }
        ImageNodeDelegate ImageToNode;

        public ImagePairCorrespondence Filter(ImagePairCorrespondence matches)
        {
            SceneNode modelNode = ImageToNode(matches.ModelImage);
            SceneNode dataNode = ImageToNode(matches.DataImage);

            if (modelNode == null || dataNode == null) return matches;

            var dataToWorld = dataNode.GetOrAddComponent<NodeTransformUncertainty>().LocalToWorld;
            var worldToModel = modelNode.GetOrAddComponent<NodeTransformUncertainty>().WorldToLocal;
            UncertainRigidTransform dataToModel = dataToWorld * worldToModel;

            var modelCam = matches.ModelImage.Image.CameraModel;
            var dataCam = matches.DataImage.Image.CameraModel;

            // if 'data' node has a convex hull, compute it (uncertainty-inflated) in model space
            ConvexHull dataHullInModel = null;
            var ch = dataNode.GetComponent<NodeConvexHull>();
            if (ch != null)
            {
                if (dataToModel.Uncertain)
                {
                    List<Vector3> inflatedVerts = new List<Vector3>(ch.hull.Vertices.Count * 6);
                    foreach (var vert in ch.hull.Vertices)
                    {
                        var vertDistrib = dataToModel.TransformPoint(vert.Position);
                        inflatedVerts.AddRange(UnscentedTransform.SigmaPoints(vertDistrib).Select(pt => pt.ToXna()));
                    }
                    dataHullInModel = new ConvexHull(inflatedVerts);
                }
                else
                {
                    dataHullInModel = ch.hull.Transformed(dataToModel.Mean);
                }
            }

            Dictionary<int, bool> modelRayIntersects = new Dictionary<int, bool>();
            List<KeyValuePair<int, int>> goodMatches = new List<KeyValuePair<int, int>>();

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
                    if (!modelRayIntersects[pair.Value]) continue;
                }

                Func<Matrix, Vector2> ReprojectionError = (mat) =>
                {
                    var mdrm = Ray.Transform(dataRay, mat);
                    double modelT, dataT;
                    Vector2 backprojected;
                    double range;
                    if (!RayExtensions.ClosestIntersection(modelRay, mdrm, out modelT, out dataT))
                    {
                        // Rays are parallel or very close to parallel - try backprojecting from ~infinity
                        dataT = 1000;
                    }
                    Vector3 dataPt = mdrm.Position + mdrm.Direction * dataT;
                    backprojected = modelCam.Backproject(dataPt, out range);
                    return backprojected - modelFeature.Location;
                };

                if (dataToModel.Uncertain)
                {
                    // Compute probability distribution of reprojection error for closest point
                    var error = dataToModel.Transform((mat) =>
                    {
                        return ReprojectionError(mat).ToMathNet();
                    });
                    // If zero error is >4 sigma away from mean, skip match
                    double mhDistSqr = error.MahalanobisDistanceSquared(Vector2.Zero.ToMathNet());
                    if (mhDistSqr > 4*4) continue;
                }
                else
                {
                    // Transform is exact-ish, just make sure it's close
                    var error = ReprojectionError(dataToModel.Mean);
                    if (error.LengthSquared() > 10 * 10) continue;
                }

                // we peachy
                goodMatches.Add(pair);
            }
            return new ImagePairCorrespondence(matches.ModelImage, matches.DataImage, matches.ModelFeatures, matches.DataFeatures, goodMatches);
        }
    }
}
