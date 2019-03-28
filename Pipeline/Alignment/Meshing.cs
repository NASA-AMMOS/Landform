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
        public Observation Texture;
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
                                                                       bool requireNormals = true,
                                                                       bool requireTextures = false)
        {
            var pointsType = ObservationType.Points.ToString();
            var normalsType = ObservationType.Normals.ToString();
            var maskType = ObservationType.RoverMask.ToString();
            var imageType = ObservationType.Image.ToString();

            var observations =
                observationCache.GetAllObservationsForFrame(frameCache.GetFrame(frameName))
                .Cast<RoverObservation>()
                .Where(obs => allowMastcam || !IsMastcam(obs))
                .ToList();
            observations.Sort(MSLProject.RoverObservationComparison);

            var ret = new MeshObservations();

            ret.Points = observations.Find(obs => obs.ObservationType == pointsType);
            if (ret.Points == null)
            {
                return null;
            }

            ret.Normals = observations.Find(obs => obs.ObservationType == normalsType &&
                                            obs.Width == ret.Points.Width && obs.Height == ret.Points.Height);
            if (requireNormals && ret.Normals == null)
            {
                return null;
            }

            ret.Mask = observations.Find(obs => obs.ObservationType == maskType &&
                                         obs.Width == ret.Points.Width && obs.Height == ret.Points.Height);

            ret.Texture = observations.Find(obs => obs.ObservationType == imageType);
            if (requireTextures && ret.Texture == null)
            {
                return null;
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
                                                                     bool requireNormals = true,
                                                                     bool requireTextures = false)
        {
            List<MeshObservations> ret = new List<MeshObservations>();
            foreach (var frameName in observationCache.GetAllFramesWithObservations())
            {
                var obs = CollectMeshObservationsForFrame(frameName, frameCache, observationCache,
                                                          allowMastcam, requireNormals, requireTextures);
                if (obs != null)
                {
                    ret.Add(obs);
                } 
            }
            return ret;
        }

        public static void AddMaskForMissingConstant(Image dst, Image src, PDSParser parser = null)
        {
            parser = parser ?? new PDSParser((PDSMetadata)src.Metadata);
            if (parser.HasMissingConstant)
            {
                dst.UnionMask(src, parser.MissingConstant.Select(x => (float)x).ToArray());
            }
            else
            {
                dst.CreateMask(false);
            }
        }

        public static void CheckType(PDSParser parser, RoverProductType type, string what)
        {
            if (parser.DerivedImageType != type)
            {
                throw new ArgumentException(what + " requires " + type + " product");
            }
        }

        public static void CheckCameraFrame(PDSParser parser, string what)
        {
            if (parser.CameraModelRefFrame != PDSParser.ReferenceCoordinateFrame.RoverNav)
            {
                throw new NotImplementedException(what + " requires camera model in rover frame");
            }
        }

        public static Vector3 GetCameraCenter(Image img, string what)
        {
            CAHV cahv = img.CameraModel as CAHV;
            if (cahv == null)
            {
                throw new NotImplementedException(what + " requires CAHV camera model");
            }
            return cahv.C;
        }

        public static Vector3 CheckCameraCenter(Image img, string what, bool checkRangeOrigin = true)
        {
            return CheckCameraCenter(new PDSParser((PDSMetadata)img.Metadata), img, what, checkRangeOrigin);
        }

        public static Vector3 CheckCameraCenter(PDSParser parser, Image img, string what, bool checkRangeOrigin = true)
        {
            CheckCameraFrame(parser, what);
            Vector3 cameraCenter = GetCameraCenter(img, what);
            if (checkRangeOrigin)
            {
                Matrix xform = RoverCoordinateSystem.GetTransformToRoverFrame(parser);
                Vector3 rangeOrigin = Vector3.Transform(parser.RangeOrigin, xform);
                if (!Vector3.AlmostEqual(rangeOrigin, cameraCenter, 0.1))
                {
                    throw new NotImplementedException(what + " requires range maps projected from camera location");
                }
            }
            return cameraCenter;
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
                case RoverProductType.Range: return ConvertRNG(img, parser);
                case RoverProductType.XYZ: return ConvertXYZ(img, parser);
                default: throw new ArgumentException("cannot convert " + parser.DerivedImageType + " image to XYR");
            }
        }

        /// <summary>
        /// convert an XYZ map to rover frame
        /// also sets mask of return image to be union of input mask, if any
        /// plus invalid values according to image header metadata
        /// </summary>
        public static Image ConvertXYZ(Image img, PDSParser parser = null)
        {
            parser = parser ?? new PDSParser((PDSMetadata)img.Metadata);
            CheckType(parser, RoverProductType.XYZ, "ConvertXYZ");
            Matrix xform = RoverCoordinateSystem.GetTransformToRoverFrame(parser);
            Image ret = new Image(3, img.Width, img.Height);
            AddMaskForMissingConstant(ret, img, parser);
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    if (img.IsInvalid(row, col)) //respect input image mask if it has one
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!parser.HasMissingConstant || !ret.IsInvalid(row, col))
                    {
                        var p = new Vector3(img[0, row, col], img[1, row, col], img[2, row, col]);
                        ret.SetBandValues(row, col, Vector3.Transform(p, xform).ToFloatArray());
                    }
                }
            }
            return ret;
        }

        /// <summary>
        /// convert a range image into an XYZ map in rover frame similar to the XYR products
        /// also sets mask of return image to be union of input mask, if any
        /// plus invalid values according to image header metadata
        /// </summary>
        public static Image ConvertRNG(Image img, PDSParser parser)
        {
            parser = parser ?? new PDSParser((PDSMetadata)img.Metadata);
            CheckType(parser, RoverProductType.Range, "ConvertRange");
            CheckCameraCenter(parser, img, "ConvertRNG");
            Image ret = new Image(3, img.Width, img.Height);
            AddMaskForMissingConstant(ret, img, parser);
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    if (img.IsInvalid(row, col)) //respect input image mask if it has one
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!parser.HasMissingConstant || !ret.IsInvalid(row, col))
                    {
                        Vector3 p = img.CameraModel.Unproject(new Vector2(col, row), img[0, row, col]);
                        ret.SetBandValues(row, col, p.ToFloatArray());
                    }
                }
            }
            return ret;
        }

        /// <summary>
        /// until mission products giving useful error estimates are available
        /// this code generates a confidence that is inversely proportional to range
        /// </summary>
        public static Image GenerateConfidence(Image img)
        {
            PDSParser parser = new PDSParser((PDSMetadata)img.Metadata);
            switch (parser.DerivedImageType)
            {
                case RoverProductType.Range: return GenerateConfidenceFromRNG(img, parser);
                case RoverProductType.XYZ: return GenerateConfidenceFromXYZ(img, parser);
                default: throw new NotImplementedException("synthetic confidence requires range or XYZ map"); ;
            }
        }

        /// <summary>
        /// naive confidence: farther away the point is from the camera the lower the confidence
        /// </summary>
        public static Image GenerateConfidenceFromRNG(Image img, PDSParser parser = null)
        {
            parser = parser ?? new PDSParser((PDSMetadata)img.Metadata);
            CheckType(parser, RoverProductType.Range, "GenerateConfidenceFromRNG");
            Image ret = new Image(1, img.Width, img.Height);
            AddMaskForMissingConstant(ret, img, parser);
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    if (img.IsInvalid(row, col) || //respect input image mask if it has one
                        img[0, row, col] <= 0.0f) //non-positive range values are invalid
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!parser.HasMissingConstant || !ret.IsInvalid(row, col))
                    {
                        ret[0, row, col] = 1 / img[0, row, col];
                    }
                }
            }

            return ret;
        }

        /// <summary>
        /// naive confidence: farther away the point is from the camera the lower the confidence
        /// </summary>
        public static Image GenerateConfidenceFromXYZ(Image img, PDSParser parser = null)
        {
            parser = parser ?? new PDSParser((PDSMetadata)img.Metadata);
            CheckType(parser, RoverProductType.XYZ, "GenerateConfidenceFromXYZ");
            Vector3 c = CheckCameraCenter(img, "GenerateConfidenceFromXYZ");
            Matrix xform = RoverCoordinateSystem.GetTransformToRoverFrame(parser);
            Image ret = new Image(1, img.Width, img.Height);
            AddMaskForMissingConstant(ret, img, parser);
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    if (img.IsInvalid(row, col)) //respect input image mask if it has one
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!parser.HasMissingConstant || !ret.IsInvalid(row, col))
                    {
                        var p = new Vector3(img[0, row, col], img[1, row, col], img[2, row, col]);
                        ret[0, row, col] = 1 / (float)Vector3.Distance(Vector3.Transform(p, xform), c);
                    }
                }
            }
            return ret;
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
            CheckType(parser, RoverProductType.NormalMap, "ConvertNormals");
            Matrix xform = RoverCoordinateSystem.GetTransformToRoverFrame(parser);
            bool nonIdentityXform = !xform.Equals(Matrix.Identity);
            Image ret = new Image(img);
            AddMaskForMissingConstant(ret, img, parser);
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    int up = Math.Max(0, row - 1);
                    int down = Math.Min(row + 1, img.Height - 1);
                    int left = Math.Max(0, col - 1);
                    int right = Math.Min(col + 1, img.Width - 1);
                    if (img.IsInvalid(row, col) || //respect input image mask if it has one
                        (confidence != null && confidence.IsInvalid(row, col)) ||
                        img.IsInvalid(up, left) || img.IsInvalid(up, col) || img.IsInvalid(up, right) ||
                        img.IsInvalid(row, left) || img.IsInvalid(row, right) ||
                        img.IsInvalid(down, left) || img.IsInvalid(down, col) || img.IsInvalid(down, right))
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!parser.HasMissingConstant || !ret.IsInvalid(row, col))
                    {
                        if (nonIdentityXform)
                        {
                            var n = new Vector3(img[0, row, col], img[1, row, col], img[2, row, col]);
                            ret.SetBandValues(row, col, Vector3.TransformNormal(n, xform).ToFloatArray());
                        }
                        if (confidence != null)
                        {
                            ret[0, row, col] *= confidence[0, row, col];
                        }
                    }
                }
            }
            return ret;
        }

        /// <summary>
        /// get transform from a specific rover frame to the corresponding, observation, sitedrive or root frame
        /// </summary>transform a mesh 
        public static UncertainRigidTransform GetTransform(string fromFrame, string toFrame, FrameCache frameCache,
                                                           bool usePriors = false)
        {
            if (toFrame == "rover" || toFrame == PDSParser.ReferenceCoordinateFrame.RoverNav.ToString())
            {
                return new UncertainRigidTransform(); //identity, no uncertainty
            }

            Frame obsFrame = frameCache.GetFrame(fromFrame);
            var obsToSD = usePriors ? frameCache.GetBestPrior(obsFrame) : frameCache.GetBestTransform(obsFrame);

            if (toFrame == "sitedrive" || toFrame == PDSParser.ReferenceCoordinateFrame.LocalLevel.ToString())
            {
                return obsToSD.Transform;
            }

            if (toFrame == "site" || toFrame == PDSParser.ReferenceCoordinateFrame.Site.ToString())
            {
                throw new NotImplementedException("transform to site frame not implemented");
            }

            Frame sdFrame = frameCache.GetFrame(obsFrame.ParentName);
            var sdToRoot = usePriors ? frameCache.GetBestPrior(sdFrame) : frameCache.GetBestTransform(sdFrame);

            if (toFrame == "root" || string.IsNullOrEmpty(toFrame))
            {
                return obsToSD.Transform * sdToRoot.Transform;
            }
            else
            {
                var fromFrameToRoot = GetTransform(fromFrame, "root", frameCache, usePriors);
                var toFrameToRoot = GetTransform(toFrame, "root", frameCache, usePriors);
                return fromFrameToRoot.TimesInverse(toFrameToRoot);
            }
        }

        /// <summary>
        /// decimate a normals image   
        /// </summary>
        public static Image DecimateNormals(Image img, int blocksize, Image mask = null)
        {
            if (mask != null)
            {
                img.UnionMask(mask, new float[] { 0 } );
            }
            if (blocksize > 1)
            {
                img = img.Decimated(blocksize);
                for (int row = 0; row < img.Height; row++)
                {
                    for (int col = 0; col < img.Width; col++)
                    {
                        if (!img.IsInvalid(row, col))
                        {
                            var n = new Vector3(img[0, row, col], img[1, row, col], img[2, row, col]);
                            if (n.LengthSquared() < 0.0001)
                            {
                                img.SetMaskValue(row, col, true);
                            }
                            else
                            {
                                n.Normalize();
                                img.SetBandValues(row, col, n.ToFloatArray());
                            }
                        }
                    }
                }
            }
            return img;
        }

        /// <summary>
        /// decimate a points image, baking mask into it
        /// </summary>
        public static Image DecimatePoints(Image img, int blocksize, Image mask = null)
        {
            if (mask != null)
            {
                img.UnionMask(mask, new float[] { 0 } );
            }
            return blocksize > 1 ? img.Decimated(blocksize) : img;
        }

        public static void LoadOrGenerateMeshImages(PipelineCore pipeline, MeshObservations obs, int decimateBlocksize,
                                                    bool scaleNormalsByConfidence,
                                                    out Image points, out Image normals, out Image mask)
        {
            //TODO generate confidence and mask until real products are available
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/259
            var pointsRaw = pipeline.LoadImage(obs.Points.Url);
            points = ConvertPoints(pointsRaw);

            normals = null;
            if (obs.Normals != null)
            {
                var confidence = scaleNormalsByConfidence ? GenerateConfidence(pointsRaw) : null;
                normals = ConvertNormals(pipeline.LoadImage(obs.Normals.Url), confidence);
            }

            mask = RoverMask.LoadOrBuild(pipeline, obs.Mask, pointsRaw, obs.Points.Name);

            if (decimateBlocksize > 1)
            {
                points = DecimatePoints(points, decimateBlocksize, mask);
                if (normals != null)
                {
                    normals = DecimateNormals(normals, decimateBlocksize, mask);
                }
                mask = null;
            }
        }

        /// add texture coordinates to a mesh by projecting vertices onto an image
        /// also optionally removes any vertices of the mesh that aren't visible in the image
        /// the passed mesh is mutated in place
        /// </summary>
        public static void AddUVs(Mesh mesh, Image img, Matrix? meshToImage = null, bool removeVertsOutsideView = true,
                                  bool processVertsInParallel = false)
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
        }

        /// <summary>
        /// if mask image is provided then any pixels which are 0 there are ignored
        /// </summary>
        public static Mesh BuildPointCloud(Image points, Image normals = null, Image mask = null)
        {
            Mesh ret = new Mesh(hasNormals: normals != null);
            for (int row = 0; row < points.Height; row++)
            {
                for (int col = 0; col < points.Width; col++)
                {
                    if (points.IsInvalid(row, col) || (normals != null && normals.IsInvalid(row, col)) ||
                        mask != null && mask[0, row, col] == 0)
                    {
                        continue;
                    }
                    var v = new Vertex(new Vector3(points[0, row, col], points[1, row, col], points[2, row, col]));
                    if (normals != null)
                    {
                        v.Normal = new Vector3(normals[0, row, col], normals[1, row, col], normals[2, row, col]);
                    }
                    ret.Vertices.Add(v);
                }
            }
            return ret;
        }

        public static Mesh BuildPointCloud(PipelineCore pipeline, MeshObservations obs, FrameCache frameCache,
                                           string frame = "root", bool usePriors = false, int decimateBlocksize = 1,
                                           bool scaleNormalsByConfidence = false)
        {
            Image points, normals, mask;
            LoadOrGenerateMeshImages(pipeline, obs, decimateBlocksize, scaleNormalsByConfidence,
                                     out points, out normals, out mask);
            var ret = BuildPointCloud(points, normals, mask);
            ret.Transform(GetTransform(obs.Points.FrameName, frame, frameCache, usePriors).Mean);
            return ret;
        }

        /// <summary>
        /// build a mesh from the given points and optional normals and mask images
        /// </summary>
        public static Mesh BuildOrganizedMesh(Image points, Image normals = null, Image mask = null,
                                              double maxTriangleAspect = 10)
        {
            if (maxTriangleAspect < 1)
            {
                throw new ArgumentException("max triangle aspect must be >= 1");
            }

            Mesh ret = new Mesh(hasNormals: normals != null);

            Dictionary<Tuple<int, int>, int> pixelToVert = new Dictionary<Tuple<int, int>, int>();

            int getOrAddVert(int r, int c)
            {
                var key = new Tuple<int, int>(r, c);
                if (!pixelToVert.ContainsKey(key))
                {
                    pixelToVert[key] = ret.Vertices.Count;
                    Vertex v = new Vertex();
                    v.Position = new Vector3(points[0, r, c], points[1, r, c], points[2, r, c]);
                    if (normals != null)
                    {
                        v.Normal = new Vector3(normals[0, r, c], normals[1, r, c], normals[2, r, c]);
                    }
                    ret.Vertices.Add(v);
                }
                return pixelToVert[key];
            }

            void addFaceMaybe(int r0, int c0, int r1, int c1, int r2, int c2)
            {
                if (points.IsInvalid(r0, c0) || points.IsInvalid(r1, c1) || points.IsInvalid(r2, c2))
                {
                    return;
                } 
                if (normals != null &&
                    (normals.IsInvalid(r0, c0) || normals.IsInvalid(r1, c1) || normals.IsInvalid(r2, c2)))
                {
                    return;
                }
                if (mask != null && (mask[0, r0, c0] == 0 || mask[0, r1, c1] == 0 || mask[0, r2, c2] == 0))
                {
                    return;
                }

                Vector3 v0 = new Vector3(points[0, r0, c0], points[1, r0, c0], points[2, r0, c0]);
                Vector3 v1 = new Vector3(points[0, r1, c1], points[1, r1, c1], points[2, r1, c1]);
                Vector3 v2 = new Vector3(points[0, r2, c2], points[1, r2, c2], points[2, r2, c2]);

                double s0 = Vector3.Distance(v0, v1);
                double s1 = Vector3.Distance(v1, v2);
                double s2 = Vector3.Distance(v2, v0);

                double l = Math.Min(s0, Math.Min(s1, s2));
                double u = Math.Max(s0, Math.Max(s1, s2));
                if (l > 0 && u / l <= maxTriangleAspect)
                {
                    ret.Faces.Add(new Face(getOrAddVert(r0, c0), getOrAddVert(r1, c1), getOrAddVert(r2, c2)));
                }
            };

            List<int> tris = new List<int>();
            for (int row = 0; row < points.Height - 1 ; row++)
            {
                for (int col = 0; col < points.Width - 1 ; col++)
                {
                    //  (row, col)-----(row, col+1)
                    //           |\    |       
                    //           | \   |        
                    //           |  \  |         
                    //           |   \ |          
                    //           |    \|           
                    //(row+1, col)-----(row+1, col+1)

                    addFaceMaybe(row, col, row+1, col+1, row, col+1); //upper triangle

                    addFaceMaybe(row, col, row+1, col, row+1, col+1); //lower triangle
                }
            }

            return ret;
        }

        public static Mesh BuildOrganizedMesh(PipelineCore pipeline, MeshObservations obs, FrameCache frameCache,
                                              string frame = "root", bool usePriors = false, int decimateBlocksize = 1,
                                              bool scaleNormalsByConfidence = false, double maxTriangleAspect = 10,
                                              bool withUVs = false)
        {
            Image points, normals, mask;
            LoadOrGenerateMeshImages(pipeline, obs, decimateBlocksize, scaleNormalsByConfidence,
                                     out points, out normals, out mask);
            var ret = BuildOrganizedMesh(points, normals, mask, maxTriangleAspect);
            if (withUVs && obs.Texture != null)
            {
                AddUVs(ret, pipeline.LoadImage(obs.Texture.Url));
            }
            ret.Transform(GetTransform(obs.Points.FrameName, frame, frameCache, usePriors).Mean);
            return ret;
        }

        public static ConvexHull BuildFrustumHull(PipelineCore pipeline, MeshObservations obs, FrameCache frameCache,
                                                  string frame = "root", bool usePriors = false,
                                                  bool uncertaintyInflated = false)
        {
            Image img = pipeline.LoadImage(obs.Texture != null ? obs.Texture.Url : obs.Points.Url);
            var parser = new PDSParser((PDSMetadata)img.Metadata);
            CheckCameraFrame(parser, "BuildFrustumHull");
            ConvexHull ret = ConvexHull.FromImage(img);
            var xform = GetTransform(obs.Points.FrameName, frame, frameCache, usePriors);
            return uncertaintyInflated ? ConvexHull.Transformed(ret, xform) : ConvexHull.Transformed(ret, xform.Mean);
        }
    }
}
