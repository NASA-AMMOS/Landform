using OPS.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    /// <summary>
    /// Represents a computed correspondence between visual features in
    /// a pair of images.
    /// </summary>
    public class ImagePairCorrespondence
    {
        public ImageRef ModelImage, DataImage;
        public ImageFeature[] ModelFeatures;
        public ImageFeature[] DataFeatures;
        public KeyValuePair<int,int>[] DataToModel;


        /// <summary>
        /// Remove any unreferenced features.
        /// </summary>
        public void Compact()
        {
            Dictionary<int, int> dataOldToNew = new Dictionary<int, int>();
            List<ImageFeature> dataWinners = new List<ImageFeature>();
            Dictionary<int, int> modelOldToNew = new Dictionary<int, int>();
            List<ImageFeature> modelWinners = new List<ImageFeature>();

            foreach (var pair in DataToModel)
            {
                int di = pair.Key;
                if (dataOldToNew.ContainsKey(di)) continue;
                dataOldToNew[di] = dataWinners.Count;
                dataWinners.Add(DataFeatures[di]);

                int mi = pair.Value;
                if (modelOldToNew.ContainsKey(mi)) continue;
                modelOldToNew[di] = modelWinners.Count;
                modelWinners.Add(ModelFeatures[mi]);
            }

            List<KeyValuePair<int, int>> newDataToModel = new List<KeyValuePair<int, int>>();
            foreach (var pair in DataToModel)
            {
                int di = pair.Key;
                int mi = pair.Value;
                newDataToModel.Add(new KeyValuePair<int, int>(dataOldToNew[di], modelOldToNew[mi]));
            }

            ModelFeatures = modelWinners.ToArray();
            DataFeatures = dataWinners.ToArray();
            DataToModel = newDataToModel.ToArray();
        }

        /// <summary>
        /// Output a set of arrays with data features duplicated as necessary
        /// to have exactly one data feature entry per correspondence.
        /// </summary>
        /// <param name="mf">Output array of model features</param>
        /// <param name="df">Output array of data features</param>
        /// <param name="d2m">Output mapping from elements of df to mf</param>
        public void Flatten(out ImageFeature[] mf, out ImageFeature[] df, out int[] d2m)
        {
            List<ImageFeature> mfl = new List<ImageFeature>();
            List<ImageFeature> dfl = new List<ImageFeature>();
            List<int> d2ml = new List<int>();

            Dictionary<int, int> modelOldToNew = new Dictionary<int, int>();
            List<ImageFeature> modelWinners = new List<ImageFeature>();

            foreach (var pair in DataToModel)
            {
                var di = pair.Key;
                var mi = pair.Value;
                dfl.Add(DataFeatures[di]);

                if (!modelOldToNew.ContainsKey(mi))
                {
                    modelOldToNew[mi] = modelWinners.Count;
                    modelWinners.Add(ModelFeatures[mi]);
                }
                d2ml.Add(modelOldToNew[mi]);
            }

            mf = mfl.ToArray();
            df = dfl.ToArray();
            d2m = d2ml.ToArray();
        }

        public ImagePairCorrespondence(ImageRef model, ImageRef data,
            IEnumerable<ImageFeature> modelFeatures,
            IEnumerable<ImageFeature> dataFeatures,
            IEnumerable<int> dataToModel)
        {
            this.ModelImage = model;
            this.DataImage = data;
            this.ModelFeatures = modelFeatures.ToArray();
            this.DataFeatures = dataFeatures.ToArray();
            this.DataToModel = Enumerable.Range(0, this.DataFeatures.Length).Zip(dataToModel,
                (di, mi) => new KeyValuePair<int, int>(di, mi)).ToArray();
        }
        public ImagePairCorrespondence(ImageRef model, ImageRef data,
            IEnumerable<ImageFeature> modelFeatures,
            IEnumerable<ImageFeature> dataFeatures,
            IEnumerable<KeyValuePair<int, int>> dataToModel)
        {
            this.ModelImage = model;
            this.DataImage = data;
            this.ModelFeatures = modelFeatures.ToArray();
            this.DataFeatures = dataFeatures.ToArray();
            this.DataToModel = dataToModel.ToArray();
        }
    }
}
