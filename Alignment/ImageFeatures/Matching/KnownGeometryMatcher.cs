using MathNet.Numerics.LinearAlgebra;
using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.Imaging;
using OPS.MathExtensions;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    public class KnownGeometryMatcher : PipelineRoutine, IFeatureMatcher
    {
        public KnownGeometryMatcher(PipelineCore pipeline)
            : base(pipeline)
        {
            EpiFinder = new EpipolarLineFinder(pipeline);
        }
        public EpipolarLineFinder EpiFinder;


        /// <summary>
        /// When two camera rays are parallel, try backprojecting from this distance.
        /// </summary>
        public double ParallelBackprojectDistance;
        /// <summary>
        /// Ratio of bad to good projections to reject an uncertain match.
        /// </summary>
        public double BadProjectionRatio;

        public ImagePairCorrespondence Match(AlignmentScene scene, UnorderedImagePair pair)
        {
            bool oneIsModel = (scene.Context.DetectedFeatures[pair.One].Length > scene.Context.DetectedFeatures[pair.Two].Length);
            ImageRef modelRef, dataRef;
            if (oneIsModel)
            {
                modelRef = pair.One;
                dataRef = pair.Two;
            }
            else
            {
                modelRef = pair.Two;
                dataRef = pair.One;
            }

            Image model = GetImage(modelRef);
            Image data = GetImage(dataRef);

            ImageFeature[] modelFeatures = scene.Context.DetectedFeatures[modelRef];
            ImageFeature[] dataFeatures = scene.Context.DetectedFeatures[dataRef];

            var dataNode = scene.ImageToNode[dataRef];
            var modelNode = scene.ImageToNode[modelRef];
            var dataToModel = dataNode.GetOrAddComponent<NodeUncertainTransform>().To(modelNode);
            var modelToData = modelNode.GetOrAddComponent<NodeUncertainTransform>().To(dataNode);


            ConvexHull modelHullInData = null;
            var ch = modelNode.GetComponent<NodeConvexHull>();
            if (ch != null)
            {
                modelHullInData = ConvexHull.Transformed(ch.Hull, modelToData);
            }

            ImagePairCorrespondence res = new ImagePairCorrespondence
            {
                ModelImage = modelRef,
                DataImage = dataRef
            };

            List<KeyValuePair<int, int>> matches = new List<KeyValuePair<int, int>>();
            for (int i = 0; i < dataFeatures.Length; i++)
            {
                var dataFeat = dataFeatures[i];
                var dataRay = data.CameraModel.Unproject(dataFeat.Location);

                if (!modelHullInData.Intersects(dataRay)) continue;
                
                var epiLine = EpiFinder.Find(modelRef, dataRef, dataToModel.Mean, dataFeat);

                List<int> candidates = new List<int>();
                for (int j = 0; j < modelFeatures.Length; j++)
                {
                    var modelFeat = modelFeatures[j];
                    if (Math.Abs(epiLine.SignedDistance(modelFeat.Location)) > 10) continue;
                    candidates.Add(j);
                }

                if (candidates.Count > 0)
                {
                    BruteForceMatcher bfm = new BruteForceMatcher();
                    var submatch = bfm.Match(modelRef, dataRef, candidates.Select(idx => modelFeatures[idx]).ToArray(), new[] { dataFeat });
                    if (submatch == null) continue;
                    foreach (var match in submatch.DataToModel)
                    {
                        matches.Add(new KeyValuePair<int, int>(i, candidates[match.Value]));
                    }
                }
            }
            res.DataToModel = matches.ToArray();
            return res;
        }
    }
}

