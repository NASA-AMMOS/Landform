using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Geometry;
using OPS.Imaging;

namespace OPS.Pipeline
{
    /// <summary>
    /// Class for generating an atlas for a baseline mesh using its camera model
    /// Projects each vertex in the mesh to the camera model
    /// Only works when texturing the mesh with the image that was used to generate it
    /// </summary>
    public class BaselineAtlaser : IMeshAtlaser
    {
        private Matrix meshToCameraModelFrame;
        private Image image;

        /// <summary>
        /// </summary>
        /// <param name="meshToCameraModelFrame">A matrix that maps the coordinate frame the mesh is in into the camera model frame.  
        /// Every vertex in the mesh will have this matrix applied before being projected into the camera model.  Typically this will
        /// be a localLevelToRover or siteToRover matrix because camera models are usually in rover frame.</param>
        /// <param name="image">Image to use to atlas the mesh.  Must contain a camera model to project, typically in rover frame.</param>
        public BaselineAtlaser(Matrix meshToCameraModelFrame, Image image)
        {
            this.meshToCameraModelFrame = meshToCameraModelFrame;
            this.image = image;
        }

        public Mesh GenerateAtlas(Mesh mesh)
        {
            return GenerateAtlas(mesh, true, true);
        }

        /// <summary>
        /// Return a copy of the provided mesh but UVed according to our provided image
        /// Any vertices (and referencing faces) outside the view of the camera will be removed
        /// This can occure when atlasing decimated versions of the original mesh which have artifically enlarged it
        /// </summary>
        /// <param name="mesh"></param>
        /// <returns></returns>
        public Mesh GenerateAtlas(Mesh mesh, bool removeVertsOutsideView, bool processVertsInParallel)
        {
            return Meshing.AddUVs(new Mesh(mesh), image, meshToCameraModelFrame, removeVertsOutsideView,
                                  processVertsInParallel);
        }

        /// <summary>
        /// Helper method to generate an atlas for a baseline mesh in site frame given a valid PDS image in rover frame
        /// </summary>
        /// <param name="m">A mesh in site frame</param>
        /// <param name="image">An image with a valid PDSMetadata object and camera model in rover frame</param>
        /// <returns></returns>
        public static Mesh AtlasSiteFrameMesh(Mesh m, Image image, bool removeVertsOutsideView = true,
                                              bool processVertsInParallel = true)
        {
            PDSMetadata metadata = (PDSMetadata)image.Metadata;
            var parser = new PDSParser(metadata);
            Matrix siteFrameMeshToLocalLevelImage = RoverCoordinateSystem.SiteToRover(parser.RoverOriginRotation, parser.OriginOffset);
            var atlaser = new BaselineAtlaser(siteFrameMeshToLocalLevelImage, image);
            return atlaser.GenerateAtlas(m, removeVertsOutsideView, processVertsInParallel);
        }
    }
}
