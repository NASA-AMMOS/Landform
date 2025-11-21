using System;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using OSGeo.GDAL;

namespace JPLOPS.Imaging
{
    /// <summary>
    /// Also see OPS.Geometry.DEM which is a more general purpose DEM implementation that can be backed by any Image.
    /// </summary>
    public class GISElevationMap : IDisposable
    {
        private GISCameraModel cameraModel;

        private Dataset gdalDataset;

        private ConcurrentDictionary<Tuple<Vector2, int>, double> interpCache =
            new ConcurrentDictionary<Tuple<Vector2, int>, double>();

        public GISElevationMap(string geoTiff, string bodyName)
        {
            cameraModel = new GISCameraModel(geoTiff, bodyName);

            gdalDataset = Gdal.Open(geoTiff, Access.GA_ReadOnly);

            if (cameraModel.Bands != 1)
            {
                throw new Exception("expected single band elevation image");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool isDisposing)
        {
            if (isDisposing && gdalDataset != null)
            {
                gdalDataset.Dispose();
                gdalDataset = null;
            }
        }

        /// <summary>
        /// input: X = longitude, Y = latitude
        /// radius is interpolation window size
        /// output: average of non-masked values in window about pixel corresponding to given lon/lat
        /// </summary>
        public double InterpolateElevationAtLonLat(Vector2 lonLat, int radius = 2)
        {
            double lon = lonLat.X, lat = lonLat.Y;
            return interpCache.GetOrAdd(new Tuple<Vector2, int>(new Vector2(lon, lat), radius), _ =>
            {
                
                Vector3 px = cameraModel.LonLatToImage(new Vector3(lon, lat, 0.0));
                
                if (px.X < 0 || px.X >= cameraModel.Width || px.Y < 0 || px.Y >= cameraModel.Height)
                {
                    throw new ArgumentException(string.Format("lat={0} lon={1} out of DEM bounds", lat, lon));
                }
                
                int xl = (int)Math.Max(Math.Round(px.X - radius), 0);
                int yl = (int)Math.Max(Math.Round(px.Y - radius), 0);
                int xu = (int)Math.Min(Math.Round(px.X + radius), cameraModel.Width - 1);
                int yu = (int)Math.Min(Math.Round(px.Y + radius), cameraModel.Height - 1);
                int w = xu - xl + 1;
                int h = yu - yl + 1;
                
                float[] window = new float[w * h];
                double maskValue = 0;
                int hasMaskValue = 0;
                
                //though GDAL seems to claim to be MT safe, this does seem necessary
                //another strategy may be to read the whole entire raster into a big managed array at construction
                lock (this)
                {
                    var band = gdalDataset.GetRasterBand(1);
                    band.ReadRaster(xl, yl, w, h, window, w, h, 0, 0);
                    band.GetNoDataValue(out maskValue, out hasMaskValue);
                }
                
                double sum = 0;
                int n = 0;
                for (int i = 0; i < window.Length; i++)
                {
                    if (hasMaskValue == 0 || window[i] != maskValue)
                    {
                        sum += window[i];
                        n++;
                    }
                }
                
                return sum / n;
            });
        }
    }

    /// <summary>
    /// Sparse GIS elevation map backed by an image file.
    /// Disables the standard read/write converters that normalize the band values to [0, 1].
    /// </summary>
    public class SparseGISElevationMap : SparseGISImage
    {
        public SparseGISElevationMap(string path, CameraModel cameraModel = null) : base(path, cameraModel) { }
        
        protected SparseGISElevationMap(int bands, int width, int height) : base(bands, width, height)
        { }

        public SparseGISElevationMap(SparseGISElevationMap that) : base(that)
        { }

        public override Image Instantiate(int bands, int width, int height)
        {
            return new SparseGISElevationMap(bands, width, height);
        }

        public override object Clone()
        {
            return new SparseGISElevationMap(this);
        }

        protected override IImageConverter GetReadConverter()
        {
            return ImageConverters.PassThrough;
        }
        
        protected override IImageConverter GetWriteConverter()
        {
            return ImageConverters.PassThrough;
        }
    }
}
