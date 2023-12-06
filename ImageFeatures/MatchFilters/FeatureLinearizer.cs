using Microsoft.Xna.Framework;
using JPLOPS.Imaging;
using System;

namespace JPLOPS.ImageFeatures
{
    public class FeatureLinearizer
    {
        public FeatureLinearizer()
        {
            AssumedDepth = 100.0;
        }

        public double AssumedDepth;

        public ImageFeature[] Linearize(CameraModel cmod, ImageFeature[] features)
        {
            if (cmod.Linear) return features;

            CameraModel c = cmod;
            CameraModel linear;
            if (c is CAHV)
            {
                var cc = (CAHV)c;
                linear = new CAHV(cc.C, cc.A, cc.H, cc.V);
            }
            else
            {
                throw new ArgumentException("invalid camera model type");
            }

            ImageFeature[] res = new ImageFeature[features.Length];

            // compute bounding region
            Vector2 oldMin = new Vector2(double.PositiveInfinity, double.PositiveInfinity);
            Vector2 oldMax = new Vector2(double.NegativeInfinity, double.NegativeInfinity);
            foreach (var feat in features)
            {
                var pt = feat.Location;
                if (pt.X < oldMin.X) oldMin.X = pt.X;
                if (pt.X > oldMax.X) oldMax.X = pt.X;
                if (pt.Y < oldMin.Y) oldMin.Y = pt.Y;
                if (pt.Y > oldMax.Y) oldMax.Y = pt.Y;
            }

            Vector2 newMin = new Vector2(double.PositiveInfinity, double.PositiveInfinity);
            Vector2 newMax = new Vector2(double.NegativeInfinity, double.NegativeInfinity);
            for (int i = 0; i < features.Length; i++)
            {
                Vector3 pt = cmod.Unproject(features[i].Location, AssumedDepth);
                Vector2 linearized = linear.Project(pt, out double range);
                res[i] = new ImageFeature(linearized, features[i].Descriptor);

                if (linearized.X < newMin.X) newMin.X = linearized.X;
                if (linearized.X > newMax.X) newMax.X = linearized.X;
                if (linearized.Y < newMin.Y) newMin.Y = linearized.Y;
                if (linearized.Y > newMax.Y) newMax.Y = linearized.Y;
            }

            Vector2 scale = new Vector2(
                (oldMax.X - oldMin.X) / (newMax.X - newMin.X),
                (oldMax.Y - oldMin.Y) / (newMax.Y - newMin.Y)
                );
            for (int i = 0; i < features.Length; i++)
            {
                res[i].Location = Vector2.Multiply(res[i].Location - newMin, scale) + oldMin;
            }

            return res;
        }
    }
}
