using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;
using Microsoft.Xna.Framework;
using log4net;
using OPS.Plumbing;
using OPS.Geometry;

namespace OPS.Alignment
{
    public class MoisanStivalFilter : PipelineRoutine, IMatchFilter
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(MoisanStivalFilter));

        //minimum number of matches this filter can process without crashing 
        private const int MIN_MATCHES = 8; 

        public int MaxIterations;
        public bool RefineStep;
        public bool VirtualLinearCoordinates;
        public MoisanStivalFilter(PipelineCore pipeline, int maxIterations = 5000, bool refineStep = true, bool virtualLinearCoordinates = true)
            : base(pipeline)
        {
            this.MaxIterations = maxIterations;
            this.RefineStep = refineStep;
            this.VirtualLinearCoordinates = virtualLinearCoordinates;
        }

        public EpipolarTransform LastEpipolarTransform;
        public ImagePairCorrespondence Filter(AlignmentScene scene, ImagePairCorrespondence matches)
        {
            if (matches.DataToModel.Length < MIN_MATCHES)
            {
                return matches;
            }
            ImageFeature[] modelFeatures = scene.Context.DetectedFeatures[matches.ModelImage];
            ImageFeature[] dataFeatures = scene.Context.DetectedFeatures[matches.DataImage];

            ImageFeature[] modelFeat, dataFeat;
            int[] dataToModel;
            matches.Flatten(modelFeatures, dataFeatures, out modelFeat, out dataFeat, out dataToModel);

            Vector2[] dataPoints = matches.DataToModel.Select(pair => dataFeatures[pair.Key].Location).ToArray();
            Vector2[] modelPoints = matches.DataToModel.Select(pair => modelFeatures[pair.Value].Location).ToArray();


            var modelImg = GetImage(matches.ModelImage);
            var dataImg = GetImage(matches.DataImage);

            // transform points into virtual linear model
            Action<Image, Vector2[]> linearize = (img, points) =>
            {
                if (img.CameraModel.Linear) return;

                CAHV c = (CAHV)modelImg.CameraModel;
                CAHV linear = new CAHV(c.C, c.A, c.H, c.V);

                // compute bounding region
                Vector2 oldMin = new Vector2(double.PositiveInfinity, double.PositiveInfinity);
                Vector2 oldMax = new Vector2(double.NegativeInfinity, double.NegativeInfinity);
                foreach (var pt in points)
                {
                    if (pt.X < oldMin.X) oldMin.X = pt.X;
                    if (pt.X > oldMax.X) oldMax.X = pt.X;
                    if (pt.Y < oldMin.Y) oldMin.Y = pt.Y;
                    if (pt.Y > oldMax.Y) oldMax.Y = pt.Y;
                }

                Vector2 newMin = new Vector2(double.PositiveInfinity, double.PositiveInfinity);
                Vector2 newMax = new Vector2(double.NegativeInfinity, double.NegativeInfinity);
                for (int i = 0; i < points.Length; i++)
                {
                    Vector3 pt = modelImg.CameraModel.ProjectPoint(points[i], 100.0);
                    Vector2 linearized = linear.Backproject(pt, out double range);
                    points[i] = linearized;

                    if (linearized.X < newMin.X) newMin.X = linearized.X;
                    if (linearized.X > newMax.X) newMax.X = linearized.X;
                    if (linearized.Y < newMin.Y) newMin.Y = linearized.Y;
                    if (linearized.Y > newMax.Y) newMax.Y = linearized.Y;
                }

                Vector2 scale = new Vector2(
                    (oldMax.X - oldMin.X) / (newMax.X - newMin.X),
                    (oldMax.Y - oldMin.Y) / (newMax.Y - newMin.Y)
                    );
                for (int i = 0; i < points.Length; i++)
                {
                    points[i] = Vector2.Multiply(points[i] - newMin, scale) + oldMin;
                }
            };
            if (VirtualLinearCoordinates)
            {
                linearize(modelImg, modelPoints);
                linearize(dataImg, dataPoints);
            }

            MoisanStivalEpipolar mso = new MoisanStivalEpipolar(
                modelPoints, dataPoints,
                new Vector2(modelImg.Width, modelImg.Height),
                new Vector2(dataImg.Width, dataImg.Height)
                );

            mso.Run(MaxIterations, RefineStep);
            if (!mso.Meaningful) return null;
            LastEpipolarTransform = mso.EpipolarTransform;

            List<KeyValuePair<int, int>> goodMatches = new List<KeyValuePair<int, int>>();
            foreach (int idx in mso.ComputeInliers())
            {
                goodMatches.Add(matches.DataToModel[idx]);
            }
            logger.Info("Number of residual matches: " + goodMatches.Count);
            return new ImagePairCorrespondence(
                matches.ModelImage, matches.DataImage,
                goodMatches);
        }
    }
}
