using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.Imaging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    /// <summary>
    /// Class for generating an atlas for a baseline mesh using its camera model
    /// Backprojects each vertex in the mesh to the camera model
    /// Only works when texturing the mesh with the image that was used to generate it
    /// </summary>
    public class BaselineAtlaser : IMeshAtlaser
    {
        Matrix meshToCameraModelFrame;
        Image image;

        /// <summary>
        /// </summary>
        /// <param name="meshToCameraModelFrame">A matrix that maps the coordinate frame the mesh is in into the camera model frame.  
        /// Every vertex in the mesh will have this matrix applied before being backprojected into the camera model.  Typically this will
        /// be a localLevelToRover or siteToRover matrix because camera models are usually in rover frame.</param>
        /// <param name="image">Image to use to atlas the mesh.  Must contain a camera model to backproject, typically in rover frame.</param>
        public BaselineAtlaser(Matrix meshToCameraModelFrame, Image image)
        {
            this.meshToCameraModelFrame = meshToCameraModelFrame;
            this.image = image;
        }

        /// <summary>
        /// Return a copy of the provided mesh but UVed according to our provided image
        /// Any vertices (and referencing faces) outside the view of the camera will be removed
        /// This can occure when atlasing decimated versions of the original mesh which have artifically enlarged it
        /// </summary>
        /// <param name="mesh"></param>
        /// <returns></returns>
        public Mesh GenerateAtlas(Mesh mesh)
        {
            Mesh result = new Mesh(mesh);
            result.HasUVs = true;
            ConcurrentBag<Vertex> verticesToRemove = new ConcurrentBag<Vertex>();
            Parallel.ForEach(result.Vertices, v => 
            {
                // Transform the mesh frame vertex into camera frame
                Vector3 meshFramePoint = v.Position;
                Vector3 cameraFramePoint = Vector3.Transform(meshFramePoint, meshToCameraModelFrame);
                // Backproject
                double range;
                Vector2 pixel = this.image.CameraModel.Backproject(cameraFramePoint, out range);
                // Remove this vertex if it is outside the field of view of the camera
                if (pixel.X < 0 || pixel.X >= this.image.Width || pixel.Y < 0 || pixel.Y >= this.image.Height)
                {
                    verticesToRemove.Add(v);
                }
                else
                {
                    // Confirm that this point isn't behiend the camera
                    if (range <= 0)
                    {
                        throw new Exception("Unexpected range value");
                    }
                    // Convert the pixel coordinate to UV
                    v.UV = image.PixelToUV(pixel);
                    v.UV = Vector2.Clamp(v.UV, Vector2.Zero, Vector2.One);
                }
            });
            // Remove any verts that were outside our field of view
            result.RemoveVertices(verticesToRemove.ToList());
            return result;
        }
    }
}
