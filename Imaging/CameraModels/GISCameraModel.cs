using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OSGeo.GDAL;
using OSGeo.OSR;

namespace OPS.Imaging
{
    public class GISCameraModel : CameraModel
    {
        public int Width { get { return GeoTiffImage.Width; } }
        public int Height { get { return GeoTiffImage.Height; } }

        protected OrbitalImage GeoTiffImage;
        private Matrix MeshToOrbitalBody; //#ISSUE 1036: need an entry in the framecache to take us from body to mesh frame
        
        protected GISCameraModel() { }

        public GISCameraModel(OrbitalImage orbitalImage, Matrix meshToOrbitalBody)
        {
            GeoTiffImage = orbitalImage;
            MeshToOrbitalBody = meshToOrbitalBody;
        }

        public GISCameraModel(GISCameraModel that)
        {
            GeoTiffImage = that.GeoTiffImage;
            MeshToOrbitalBody = that.MeshToOrbitalBody;
        }

        /// <summary>
        /// Fast reference based method projecting a ray.  Camera model classes
        /// implement this method
        /// </summary>
        /// <param name="pixelPos"></param>
        /// <param name="ray"></param>
        /// <returns></returns>
        public override void Unproject(ref Vector2 pixelPos, out Ray ray)
        {
            //#ISSUE 1039: our code doesn't know the camera location for a ray origin calculation.
            throw new NotImplementedException();
        }

        /// <summary>
        /// Project a 3D position to a pixel location in an image
        /// </summary>
        /// <param name="pos"></param>
        /// <returns>col, row</returns>
        public override Vector2 Project(Vector3 pos, out double range)
        {
            range = 0; //NOT SUPPORTED. #ISSUE 1039: our code doesn't know the camera location for a range calculation.

            var ptBodyXYZ = Vector3.Transform(pos, MeshToOrbitalBody);
            Vector3 pixel = GeoTiffImage.XYZToImage(ptBodyXYZ);
            return new Vector2 (pixel.X, pixel.Y);
        }

        public override object Clone()
        {
            return new GISCameraModel(this);
        }

        public override bool Linear
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// the direction normal to the image plane and pointing outward.
        /// This is not necessarily the direction through the middle pixel of your image.
        /// </summary>
        public override Vector3 ImagePlaneNormal
        {
            get
            {
                return new Vector3(0, 0, 1); //ISSUE #1039: figure out the best way to handle this approximation
            }
        }
    }
}
