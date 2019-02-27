using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Diagnostics;
using log4net;
using Microsoft.Xna.Framework;
using MathNet.Numerics.LinearAlgebra;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    /// <summary>
    /// collects the observations in the same frame that contribute to building a mesh
    /// </summary>
    public class MeshObservations
    {
        public Observation Points;
        public Observation Normals;
        public Observation Mask;
    }

    public class Meshing
    {
        /// <summary>
        /// check if an observation is from a mastcam
        /// </summary>
        public static bool IsMastcam(RoverObservation observation)
        {
            //temporarily suppress mastcam point cloud data until validated
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/261
            return
                observation.Sensor == RoverProductCamera.MastcamLeft.ToString() ||
                observation.Sensor == RoverProductCamera.MastcamRight.ToString();
        }

        /// <summary>
        /// sift through the available observations for a frame
        /// and try to collect those that are required to build a mesh
        /// returns null if the required observation types are not found for the frame
        /// </summary>
        public static MeshObservations CollectMeshObservationsForFrame(string frameName, FrameCache frameCache,
                                                                       ObservationCache observationCache,
                                                                       bool allowMastcam = false,
                                                                       bool requireNormals = true)
        {
            var pointsType = ObservationType.Points.ToString();
            var normalsType = ObservationType.Normals.ToString();
            var maskType = ObservationType.RoverMask.ToString();

            List<RoverObservation> obsForFrame =
                observationCache.GetAllObservationsForFrame(frameCache.GetFrame(frameName))
                .Cast<RoverObservation>()
                .Where(obs => allowMastcam || !IsMastcam(obs))
                .ToList();

            obsForFrame.Sort(MSLProject.RoverObservationComparison);

            if (obsForFrame.Count == 0)
            {
                return null;
            }

            var ret = new MeshObservations();

            ret.Points = obsForFrame.Find(obs => obs.ObservationType == pointsType);
            if (ret.Points == null)
            {
                return null;
            }

            ret.Normals = obsForFrame.Find(obs => obs.ObservationType == normalsType);
            if (ret.Normals != null &&
                (ret.Normals.Width != ret.Points.Width || ret.Normals.Height != ret.Points.Height))
            {
                ret.Normals = null;
            }

            if (requireNormals && ret.Normals == null)
            {
                return null;
            }

            ret.Mask = obsForFrame.Find(obs => obs.ObservationType == maskType);
            if (ret.Mask != null && (ret.Mask.Width != ret.Points.Width || ret.Mask.Height != ret.Points.Height))
            {
                ret.Mask = null;
            }

            return ret;
        }

        /// <summary>
        /// try to collect mesh observations for all frames
        /// corresponding to observations in the passed observation cache
        /// </summary>
        public static List<MeshObservations> CollectMeshObservations(FrameCache frameCache,
                                                                     ObservationCache observationCache,
                                                                     bool allowMastcam = false,
                                                                     bool requireNormals = false)
        {
            List<MeshObservations> ret = new List<MeshObservations>();
            foreach (var frameName in observationCache.GetAllFramesWithObservations())
            {
                var obs = CollectMeshObservationsForFrame(frameName, frameCache, observationCache,
                                                          allowMastcam, requireNormals);
                if (obs != null)
                {
                    ret.Add(obs);
                } 
            }
            return ret;
        }

        /// <summary>
        /// accepts a range or XYZ map in any coordinate frame and returns an XYZ map in rover frame
        /// also sets mask of return image to be union of input mask, if any
        /// plus invalid values according to image header metadata
        /// </summary>
        public static Image ConvertPoints(Image img)
        {
            PDSParser parser = new PDSParser((PDSMetadata)img.Metadata);
            switch (parser.DerivedImageType)
            {
                case RoverProductType.Range: return ConvertRNG(img);
                case RoverProductType.XYZ: return ConvertXYZ(img);
                default: throw new ArgumentException(string.Format("cannot convert {0} image to XYR",
                                                                   parser.DerivedImageType));
            }
        }

        /// <summary>
        /// convert an XYZ map to rover frame
        /// also sets mask of return image to be union of input mask, if any
        /// plus invalid values according to image header metadata
        /// </summary>
        public static Image ConvertXYZ(Image img)
        {
            //validate assumptions about input data
            PDSParser parser = new PDSParser((PDSMetadata)img.Metadata);
            if (parser.DerivedImageType != RoverProductType.XYZ)
            {
                throw new NotImplementedException("XYZ to XYR requires XYZ map"); ;
            }
            Matrix xform = RoverCoordinateSystem.GetTransformToRoverFrame(parser);

            Image xyr = new Image(3, img.Metadata.Width, img.Metadata.Height);

            //don't assume that input image already has a mask
            //but also don't mutate the input image to add a mask
            if (parser.HasMissingConstant)
            {
                xyr.CreateMask(parser.MissingConstant.Select(x => (float)x).ToArray());
            }
            else
            {
                xyr.CreateMask(false);
            }

            for (int idxRow = 0; idxRow < img.Metadata.Height; idxRow++)
            {
                for (int idxCol = 0; idxCol < img.Metadata.Width; idxCol++)
                {
                    if (img.IsInvalid(idxRow, idxCol)) //respect input image mask if it has one
                    {
                        xyr.SetMaskValue(idxRow, idxCol, true);
                    }
                    else if (!parser.HasMissingConstant || !xyr.IsInvalid(idxRow, idxCol))
                    {
                        var p = new Vector3(img[0, idxRow, idxCol], img[1, idxRow, idxCol], img[2, idxRow, idxCol]);
                        xyr.SetBandValues(idxRow, idxCol, Vector3.Transform(p, xform).ToFloatArray());
                    }
                }
            }

            return xyr;
        }

        /// <summary>
        /// convert a range image into an XYZ map in rover frame similar to the XYR products
        /// also sets mask of return image to be union of input mask, if any
        /// plus invalid values according to image header metadata
        /// </summary>
        public static Image ConvertRNG(Image img)
        {
            //validate assumptions about input data
            PDSParser parser = new PDSParser((PDSMetadata)img.Metadata);
            if (parser.DerivedImageType != RoverProductType.Range)
            {
                throw new NotImplementedException("RNG to XYR requires range map"); ;
            }
            if (parser.CameraModelRefFrame != PDSParser.ReferenceCoordinateFrame.RoverNav)
            {
                throw new NotImplementedException("RNG to XYR requires camera model in rover frame");
            }
            CAHV cahv = img.CameraModel as CAHV;
            if (cahv == null)
            {
                throw new NotImplementedException("RNG to XYR requires CAHV camera model");
            }
            Matrix xform = RoverCoordinateSystem.GetTransformToRoverFrame(parser);
            Vector3 rangeOrigin = Vector3.Transform(parser.RangeOrigin, xform);
            if (!Vector3.AlmostEqual(rangeOrigin, cahv.C, 0.0005))
            {
                throw new NotImplementedException("RNG to XYR requires range maps projected from camera location");
            }

            Image xyr = new Image(3, img.Metadata.Width, img.Metadata.Height);

            //don't assume that input image already has a mask
            //but also don't mutate the input image to add a mask
            if (parser.HasMissingConstant)
            {
                xyr.CreateMask(parser.MissingConstant.Select(x => (float)x).ToArray());
            }
            else
            {
                xyr.CreateMask(false);
            }

            for (int idxRow = 0; idxRow < img.Metadata.Height; idxRow++)
            {
                for (int idxCol = 0; idxCol < img.Metadata.Width; idxCol++)
                {
                    if (img.IsInvalid(idxRow, idxCol)) //respect input image mask if it has one
                    {
                        xyr.SetMaskValue(idxRow, idxCol, true);
                    }
                    else if (!parser.HasMissingConstant || !xyr.IsInvalid(idxRow, idxCol))
                    {
                        Vector3 p = img.CameraModel.Unproject(new Vector2(idxCol, idxRow), img[0, idxRow, idxCol]);
                        xyr.SetBandValues(idxRow, idxCol, p.ToFloatArray());
                    }
                }
            }

            return xyr;
        }

        /// <summary>
        /// until mission products giving useful error estimates are available
        /// this code generates a confidence that is inversely proportional to range
        /// </summary>
        public static Image GenerateConfidence(Image img)
        {
            //validate assumptions about input data
            PDSParser parser = new PDSParser((PDSMetadata)img.Metadata);
            if (parser.DerivedImageType != RoverProductType.Range)
            {
                throw new NotImplementedException("synthetic confidence requires range map"); ;
            }

            Image confidence = new Image(1, img.Metadata.Width, img.Metadata.Height);

            //don't assume that input image already has a mask
            //but also don't mutate the input image to add a mask
            if (parser.HasMissingConstant)
            {
                confidence.CreateMask(parser.MissingConstant.Select(x => (float)x).ToArray());
            }
            else
            {
                confidence.CreateMask(false);
            }

            for (int idxRow = 0; idxRow < img.Metadata.Height; idxRow++)
            {
                for (int idxCol = 0; idxCol < img.Metadata.Width; idxCol++)
                {
                    if (img.IsInvalid(idxRow, idxCol) || //respect input image mask if it has one
                        img[0, idxRow, idxCol] <= 0.0f) //negative range values are invalid
                    {
                        confidence.SetMaskValue(idxRow, idxCol, true);
                    }
                    else if (!parser.HasMissingConstant || !confidence.IsInvalid(idxRow, idxCol))
                    {
                        //naive confidence: farther away the point is, the lower the confidence
                        confidence[0, idxRow, idxCol] = 1 / img[0, idxRow, idxCol];
                    }
                }
            }

            return confidence;
        }

        /// <summary>
        /// gnenerate normals in rover frame consistent to the UVW mission product
        /// but normals within a pixel of an invalid area are ignored to avoid an issue
        /// seen where normals close to invalid areas frequently face downwards
        /// 
        /// if a confidence map is also provided the returned normals are scaled by confidence
        /// as the poisson reconstruction tool uses the magnitude of the normal to indicate confidence
        ///
        /// also sets mask of return image to be union of input mask, if any
        /// plus invalid values according to image header metadata
        /// </summary>
        public static Image ConvertNormals(Image img, Image confidence = null)
        {
            //validate assumptions about input data
            PDSParser parser = new PDSParser((PDSMetadata)img.Metadata);
            if (parser.DerivedImageType != RoverProductType.NormalMap)
            {
                throw new NotImplementedException("normals image requires normal map"); ;
            }

            Matrix xform = RoverCoordinateSystem.GetTransformToRoverFrame(parser);
            bool nonIdentityXform = !xform.Equals(Matrix.Identity);

            Image normals = new Image(img);

            //don't assume that input image already has a mask
            //but also don't mutate the input image to add a mask
            if (parser.HasMissingConstant)
            {
                normals.CreateMask(parser.MissingConstant.Select(x => (float)x).ToArray());
            }
            else
            {
                normals.CreateMask(false);
            }

            for (int idxRow = 0; idxRow < img.Metadata.Height; idxRow++)
            {
                for (int idxCol = 0; idxCol < img.Metadata.Width; idxCol++)
                {
                    int up = Math.Max(0, idxRow - 1);
                    int down = Math.Min(idxRow + 1, img.Height - 1);
                    int left = Math.Max(0, idxCol - 1);
                    int right = Math.Min(idxCol + 1, img.Width - 1);
                    if (img.IsInvalid(idxRow, idxCol) || //respect input image mask if it has one
                        (confidence != null && confidence.IsInvalid(idxRow, idxCol)) ||
                        img.IsInvalid(up, left) || img.IsInvalid(up, idxCol) || img.IsInvalid(up, right) ||
                        img.IsInvalid(idxRow, left) || img.IsInvalid(idxRow, right) ||
                        img.IsInvalid(down, left) || img.IsInvalid(down, idxCol) || img.IsInvalid(down, right))
                    {
                        normals.SetMaskValue(idxRow, idxCol, true);
                    }
                    else if (!parser.HasMissingConstant || !normals.IsInvalid(idxRow, idxCol))
                    {
                        if (nonIdentityXform)
                        {
                            var n = new Vector3(img[0, idxRow, idxCol], img[1, idxRow, idxCol], img[2, idxRow, idxCol]);
                            normals.SetBandValues(idxRow, idxCol, Vector3.TransformNormal(n, xform).ToFloatArray());
                        }
                        if (confidence != null)
                        {
                            normals[0, idxRow, idxCol] *= confidence[0, idxRow, idxCol];
                        }
                    }
                }
            }

            return normals;
        }

        /// <summary>
        /// if mask image is provided then any pixels which are 0 there are ignored
        /// </summary>
        public static Mesh BuildPointCloud(Image points, Image normals = null, Image mask = null)
        {
            Mesh ret = new Mesh(hasNormals: normals != null);
            for (int idxRow = 0; idxRow < points.Metadata.Height; idxRow++)
            {
                for (int idxCol = 0; idxCol < points.Metadata.Width; idxCol++)
                {
                    if (points.IsInvalid(idxRow, idxCol) ||
                        (normals != null && normals.IsInvalid(idxRow, idxCol)) ||
                        mask != null && mask[0, idxRow, idxCol] == 0)
                    {
                        continue;
                    }

                    var v = new Vertex(new Vector3(points[0, idxRow, idxCol],
                                                   points[1, idxRow, idxCol],
                                                   points[2, idxRow, idxCol]));
                    if (normals != null)
                    {
                        v.Normal = new Vector3(normals[0, idxRow, idxCol],
                                               normals[1, idxRow, idxCol],
                                               normals[2, idxRow, idxCol]);
                    }
                                               
                    ret.Vertices.Add(v);
                }
            }
            return ret;
        }

        /// <summary>
        /// add texture coordinates to a mesh by projecting vertices onto an image
        /// also optionally removes any vertices of the mesh that aren't visible in the image
        /// the passed mesh is mutated in place
        /// </summary>
        public static Mesh AddUVs(Mesh mesh, Image img, Matrix? meshToImage = null,
                                  bool removeVertsOutsideView = true, bool processVertsInParallel = true)
        {
            Matrix xform = meshToImage ?? Matrix.Identity;
            ConcurrentBag<Vertex> verticesToRemove = new ConcurrentBag<Vertex>();
            Action<Vertex> generateUV = v => {
                double range;
                Vector2 pixel = img.CameraModel.Project(Vector3.Transform(v.Position, xform), out range);
                if (range < 0 || pixel.X < 0 || pixel.X > (img.Width - 1) || pixel.Y < 0 || pixel.Y > (img.Height - 1))
                {
                    verticesToRemove.Add(v);
                }
                else
                {
                    // TODO: review this half pixel offset
                    //v.UV =  new Vector2((pixel.X - 0.5) / (image.Width+1), 1 - ((pixel.Y - 0.5) / (image.Height+1)));
                    v.UV = img.PixelToUV(pixel);
                    v.UV = Vector2.Clamp(v.UV, Vector2.Zero, Vector2.One);
                }
            };
            if (processVertsInParallel)
            {
                CoreLimitedParallel.ForEach(mesh.Vertices, generateUV);
            }
            else
            {
                mesh.Vertices.ForEach(generateUV);
            }
            mesh.HasUVs = true;
            if (removeVertsOutsideView)
            {
                mesh.RemoveVertices(verticesToRemove);
            }
            return mesh;
        }
    }
}
