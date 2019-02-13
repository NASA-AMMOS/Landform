using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MathNet.Numerics.LinearAlgebra;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.MathExtensions;

namespace OPS.Alignment
{
    public class KnownGeometryMatcher : IFeatureMatcher
    {
        /// <summary>
        /// When two camera rays are parallel, try backprojecting from this distance.
        /// </summary>
        public double ParallelBackprojectDistance;
        /// <summary>
        /// Ratio of bad to good projections to reject an uncertain match.
        /// </summary>
        public double BadProjectionRatio;

        private IImageLoader loader;

        public KnownGeometryMatcher(IImageLoader loader)
        {
            this.loader = loader;
        }

        public ImagePairCorrespondence Match(AlignmentScene scene, URLPair pair)
        {
            string modelUrl = pair.One;
            string dataUrl = pair.Two; 

            bool oneIsModel = (scene.DetectedFeatures[modelUrl].Length > scene.DetectedFeatures[dataUrl].Length);
            if (!oneIsModel)
            {
                modelUrl = pair.Two;
                dataUrl = pair.One;
            }

            ImageFeature[] modelFeatures = scene.DetectedFeatures[modelUrl];
            ImageFeature[] dataFeatures = scene.DetectedFeatures[dataUrl];

            var dataNode = scene.ImageToNode[dataUrl];
            var modelNode = scene.ImageToNode[modelUrl];
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
                ModelImageUrl = modelUrl,
                DataImageUrl = dataUrl
            };

            var epiFinder = new EpipolarLineFinder();

            List<KeyValuePair<int, int>> matches = new List<KeyValuePair<int, int>>();
            for (int i = 0; i < dataFeatures.Length; i++)
            {
                var modelCam = loader.LoadImage(modelUrl).CameraModel;
                var dataCam = loader.LoadImage(dataUrl).CameraModel;

                var dataFeat = dataFeatures[i];
                var dataRay = dataCam.Unproject(dataFeat.Location);

                if (!modelHullInData.Intersects(dataRay)) continue;
                
                var epiLine = epiFinder.Find(modelCam, dataCam, dataToModel.Mean, dataFeat);

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
                    var submatch = bfm.Match(modelUrl, dataUrl,
                                             candidates.Select(idx => modelFeatures[idx]).ToArray(),
                                             new[] { dataFeat });
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

