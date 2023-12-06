using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using JPLOPS.Imaging;
using JPLOPS.Geometry;

namespace JPLOPS.ImageFeatures
{
    public class KnownGeometryMatcher : IFeatureMatcher
    {
        //maximum ratio between distance of nearest data feature descriptor to model feature descriptor
        //vs 2nd nearest data feature descriptor to the same model feature descriptor
        //set to 1 to disable filtering by this ratio
        public double MaxDistanceRatio = 0.9;

        //for each data feature discard model features that are further than this
        //from the epipolar line of the data feature in the model image
        public double MaxPixelsToEpipolarLine = 10;

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

            var modelCam = modelNode.GetComponent<NodeImage>();
            var dataCam = dataNode.GetComponent<NodeImage>();
            if (modelCam == null || modelCam.CameraModel == null || dataCam == null || dataCam.CameraModel == null)
            {
                throw new ArgumentException("KnownGeometryMatcher requires camera models");
            }

            var dataToModel = dataNode.GetComponent<NodeUncertainTransform>().To(modelNode);
            var modelToData = modelNode.GetComponent<NodeUncertainTransform>().To(dataNode);

            ConvexHull modelHullInData = null;
            var hullComponent = modelNode.GetComponent<NodeConvexHull>();
            if (hullComponent != null && hullComponent.Hull != null)
            {
                modelHullInData = ConvexHull.Transformed(hullComponent.Hull, modelToData);
            }

            var matches = Match(modelFeatures, dataFeatures, modelCam.CameraModel, dataCam.CameraModel,
                                dataToModel.Mean, modelHullInData).ToArray();

            if (swapped)
            {
                var tmp1 = modelUrl;
                modelUrl = dataUrl;
                dataUrl = tmp1;

                var tmp2 = new FeatureMatch[matches.Length];
                for (int i = 0; i < matches.Length; i++)
                {
                    tmp2[i] = new FeatureMatch()
                        {
                            DataIndex = matches[i].ModelIndex,
                            ModelIndex = matches[i].DataIndex,
                            DescriptorDistance = matches[i].DescriptorDistance
                        };
                }
                matches = tmp2;
            }
            
            return new ImagePairCorrespondence(modelUrl, dataUrl, matches);
        }

        public IEnumerable<FeatureMatch> Match(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures)
        {
            throw new NotImplementedException("KnownGeometryMatcher requires camera models");
        }

        public IEnumerable<FeatureMatch> Match(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures,
                                               CameraModel modelCam, CameraModel dataCam,
                                               Matrix dataToModel, ConvexHull modelHullInData = null)
        {
            if (modelFeatures.Length < 1 || dataFeatures.Length < 1) yield break;
            double maxDistanceRatioSq = MaxDistanceRatio * MaxDistanceRatio;
            var epiFinder = new EpipolarLineFinder();
            for (int i = 0; i < dataFeatures.Length; i++)
            {
                var dataFeat = dataFeatures[i];
                var dataRay = dataCam.Unproject(dataFeat.Location);
                if (modelHullInData != null && !modelHullInData.Intersects(dataRay))
                {
                    continue;
                }
                //find epipolar line of dataFeat in model image
                var epiLine = epiFinder.Find(modelCam, dataCam, dataToModel, dataFeat);
                if (!epiLine.Success)
                {
                    continue;
                }
                Func<ImageFeature, bool> filter =
                    modelFeat => Math.Abs(epiLine.SignedDistance(modelFeat.Location)) <= MaxPixelsToEpipolarLine;
                var match = BruteForceMatcher.FindBestModelFeatureForDataFeature(modelFeatures, dataFeatures, i,
                                                                                 maxDistanceRatioSq, filter);
                if (match != null)
                {
                    yield return match;
                }
            }
        }
    }
}

