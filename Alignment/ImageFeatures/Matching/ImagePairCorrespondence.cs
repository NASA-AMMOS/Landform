using OPS.Geometry;
using OPS.Imaging;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace OPS.Alignment
{
    /// <summary>
    /// Represents a computed correspondence between visual features in
    /// a pair of images.
    /// </summary>
    public class ImagePairCorrespondence
    {
        public ImageRef ModelImage, DataImage;
        public KeyValuePair<int,int>[] DataToModel;
        public EpipolarMatrix FundamentalMatrix;
        public Matrix? BestTransformEstimate;
        
        /// <summary>
        /// Output a set of arrays with data features duplicated as necessary
        /// to have exactly one data feature entry per correspondence.
        /// </summary>
        /// <param name="mf">Output array of model features</param>
        /// <param name="df">Output array of data features</param>
        /// <param name="d2m">Output mapping from elements of df to mf</param>
        public void Flatten(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures,
            out ImageFeature[] mf, out ImageFeature[] df, out int[] d2m)
        {
            List<ImageFeature> resModelFeatures = new List<ImageFeature>();
            List<ImageFeature> resDataFeatures = new List<ImageFeature>();
            List<int> resDataToModel = new List<int>();

            Dictionary<int, int> modelOldToNew = new Dictionary<int, int>();

            foreach (var pair in DataToModel)
            {
                var di = pair.Key;
                var mi = pair.Value;
                resDataFeatures.Add(dataFeatures[di]);

                if (!modelOldToNew.ContainsKey(mi))
                {
                    modelOldToNew[mi] = resModelFeatures.Count;
                    resModelFeatures.Add(modelFeatures[mi]);
                }
                resDataToModel.Add(modelOldToNew[mi]);
            }

            mf = resModelFeatures.ToArray();
            df = resDataFeatures.ToArray();
            d2m = resDataToModel.ToArray();
        }

        public ImagePairCorrespondence(ImageRef model, ImageRef data, IEnumerable<int> dataToModel, EpipolarMatrix fundamentalMat = null, Matrix? estimate = null)
        {
            this.ModelImage = model;
            this.DataImage = data;
            int[] d2m = dataToModel.ToArray();
            this.DataToModel = Enumerable.Range(0, d2m.Length).Zip(d2m,
                (di, mi) => new KeyValuePair<int, int>(di, mi)).ToArray();
            this.FundamentalMatrix = fundamentalMat;
            this.BestTransformEstimate = estimate;
        }
        public ImagePairCorrespondence(ImageRef model, ImageRef data,
            IEnumerable<KeyValuePair<int, int>> dataToModel, EpipolarMatrix fundamentalMat = null, Matrix? estimate = null)
        {
            this.ModelImage = model;
            this.DataImage = data;
            this.DataToModel = dataToModel.ToArray();
            this.FundamentalMatrix = fundamentalMat;
            this.BestTransformEstimate = estimate;
        }
        public ImagePairCorrespondence() { }
    }
}
