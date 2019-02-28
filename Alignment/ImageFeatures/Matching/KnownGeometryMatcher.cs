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
        public double MaxAbsDistToEpipolarLine = 10;

        public delegate SceneNode ImageNodeDelegate(string imageUrl);

        private readonly ImageNodeDelegate imageToNode;

        public KnownGeometryMatcher(ImageNodeDelegate imageToNode = null)
        {
            this.imageToNode = imageToNode;
        }

        public ImagePairCorrespondence Match(AlignmentScene scene, string modelUrl, string dataUrl)
        {
            var modelFeatures = scene.DetectedFeatures[modelUrl];
            var dataFeatures = scene.DetectedFeatures[dataUrl];
            var modelNode = scene.ObservationUrlToNode[modelUrl];
            var dataNode = scene.ObservationUrlToNode[dataUrl];
            return Match(modelFeatures, dataFeatures, modelUrl, dataUrl, modelNode, dataNode);
        }

        public ImagePairCorrespondence Match(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures,
                                             string modelUrl, string dataUrl)
        {
            var modelNode = imageToNode(modelUrl);
            var dataNode = imageToNode(dataUrl);
            return Match(modelFeatures, dataFeatures, modelUrl, dataUrl, modelNode, dataNode);
        }

        public ImagePairCorrespondence Match(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures,
                                             string modelUrl, string dataUrl, SceneNode modelNode, SceneNode dataNode)
        {
            bool swapped = dataFeatures.Length > modelFeatures.Length;
            if (swapped)
            {
                var tmp1 = modelFeatures;
                modelFeatures = dataFeatures;
                dataFeatures = tmp1;

                var tmp2 = modelUrl;
                modelUrl = dataUrl;
                dataUrl = tmp2;

                var tmp3 = modelNode;
                modelNode = dataNode;
                dataNode = tmp3;
            }

            var modelCam = modelNode.GetOrAddComponent<NodeImage>().CameraModel;
            var dataCam = dataNode.GetOrAddComponent<NodeImage>().CameraModel;

            if (modelCam == null || dataCam == null)
            {
                throw new ArgumentException("KnownGeometryMatcher requires camera models");
            }

            var dataToModel = dataNode.GetOrAddComponent<NodeUncertainTransform>().To(modelNode);
            var modelToData = modelNode.GetOrAddComponent<NodeUncertainTransform>().To(dataNode);

            var modelHullInData = modelNode.GetOrAddComponent<NodeConvexHull>().Hull;
            if (modelHullInData != null)
            {
                modelHullInData = ConvexHull.Transformed(modelHullInData, modelToData);
            }

            var matches =
                Match(modelFeatures, dataFeatures, modelCam, dataCam, dataToModel.Mean, modelHullInData).ToArray();

            if (swapped)
            {
                var tmp1 = modelUrl;
                modelUrl = dataUrl;
                dataUrl = tmp1;

                var tmp2 = new KeyValuePair<int, int>[matches.Length];
                for (int i = 0; i < matches.Length; i++)
                {
                    tmp2[i] = new KeyValuePair<int, int>(matches[i].Value, matches[i].Key);
                }
                matches = tmp2;
            }
            
            return new ImagePairCorrespondence(modelUrl, dataUrl, matches);
        }

        public IEnumerable<KeyValuePair<int, int>> Match(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures,
                                                         CameraModel modelCam, CameraModel dataCam,
                                                         Matrix dataToModel, ConvexHull modelHullInData = null)
        {
            if (modelFeatures.Length < 1 || dataFeatures.Length < 1) yield break;
            var epiFinder = new EpipolarLineFinder();
            var bfm = new BruteForceMatcher();
            for (int i = 0; i < dataFeatures.Length; i++)
            {
                var dataFeat = dataFeatures[i];
                var dataRay = dataCam.Unproject(dataFeat.Location);

                if (modelHullInData != null && !modelHullInData.Intersects(dataRay))
                {
                    continue;
                }
                
                var epiLine = epiFinder.Find(modelCam, dataCam, dataToModel, dataFeat);

                List<ImageFeature> candidates = new List<ImageFeature>();
                foreach (var modelFeat in modelFeatures)
                {
                    if (Math.Abs(epiLine.SignedDistance(modelFeat.Location)) <= MaxAbsDistToEpipolarLine)
                    {
                        candidates.Add(modelFeat);
                    }
                }

                foreach (var match in bfm.Match(candidates.ToArray(), new[] { dataFeat }))
                {
                    yield return match;
                }
            }
        }
    }
}

