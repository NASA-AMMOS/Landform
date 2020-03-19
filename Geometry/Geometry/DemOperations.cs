using log4net;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Util;
using OPS.Imaging;
using OPS.Util;

namespace OPS.Geometry
{
    public static class DemOperations
    {
        public const int CHUNK_SIZE = 512;
        public const int CHUNK_CACHE_SIZE = 400; //important: cache size > 0 limits memory usage

        //Swap x, y and negate z
        public static Matrix demToSitedriveCoordinateFlip = new Matrix(0, 1, 0, 0,
                                                                       1, 0, 0, 0,
                                                                       0, 0, -1, 0,
                                                                       0, 0, 0, 1);

        /// <summary>
        /// Get Orbital -> SiteDrive transform
        /// </summary>
        public static Matrix GetOrbitalDEMToSiteDriveTransform(Vector2 siteDriveLatLon, GDALDEM orbitalDEM,
                                                               double demMetersPerPixel = 1)
        {
            Vector2 sdPixelInDEM = orbitalDEM.LatLonToImage(siteDriveLatLon);

            Vector3 demSDOriginXYZ = new Vector3((sdPixelInDEM.X - orbitalDEM.Width / 2.0) * demMetersPerPixel,
                                                 -1 * (sdPixelInDEM.Y - orbitalDEM.Height / 2.0) * demMetersPerPixel,
                                                 0);

            return Matrix.CreateTranslation(-1 * demSDOriginXYZ) * demToSitedriveCoordinateFlip;
        }

        /// <summary>
        /// Sparse DEM image backed by an image file.
        /// File format must support partial reads, currently only GDALSerializer does.
        /// The chunks are loaded lazily.  Call Populate() to load them all.
        /// </summary>
        public class SparseDEMImage : SparseImage
        {
            public SparseDEMImage(string path) : base(path, chunkSize: CHUNK_SIZE, cacheSize: CHUNK_CACHE_SIZE) { }

            protected override IImageConverter GetReadConverter()
            {
                return ImageConverters.PassThrough;
            }
            
            protected override IImageConverter GetWriteConverter()
            {
                return ImageConverters.PassThrough;
            }
        }

        /// <summary>
        /// Do bilinear interpolation with potentially null points. x and y should be horizontal and vertical offset from the top left corner respectively, see diagram.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="tl"></param>
        /// <param name="tr"></param>
        /// <param name="bl"></param>
        /// <param name="br"></param>
        /// <returns></returns>
        /// 
        /// tl ---------------------------- tr
        ///  |       |                      |
        ///  |       | y                    |
        ///  |       |                      |
        ///  |-------P                      |
        ///  |   x                          |
        ///  |                              |
        ///  |                              |
        ///  |                              |
        ///  |                              |
        ///  |                              |
        /// bl ---------------------------- br
        /// 
        ///  Returns the interpolated value at point P by summing the values at each corner, weighted by the area to the far corner.
        ///  If a corner is missing, its contribution is ignored in the weighted average.

        public static Vector3? Interpolate(double x, double y, Vector3? tl, Vector3? tr, Vector3? bl, Vector3? br)
        {
            Vector3 ret = new Vector3(0, 0, 0);
            double area = 0;
            if (tl.HasValue)
            {
                ret += tl.Value * (1-x) * (1-y);
                area += (1-x) * (1-y);
            }
            if (tr.HasValue)
            {
                ret += tr.Value * x * (1-y);
                area += x * (1-y);
            }
            if (bl.HasValue)
            {
                ret += bl.Value * (1 - x) * y;
                area += (1 - x) * y;
            }
            if (br.HasValue)
            {
                ret += br.Value * x * y;
                area += x * y;
            }
            if (area > 0)
            {
                return ret / area;
            }
            else
            {
                return null;
            }
        }

        public static Vector3? GetInterpolatedXYZ(Image dem, double r, double c, double minFilter = -1000000, double maxFilter = 1000000)
        {
            Vector3? tl = GetXYZ(dem, (int)r, (int)c);
            Vector3? tr = GetXYZ(dem, (int)r, (int)Math.Ceiling(c));
            Vector3? bl = GetXYZ(dem, (int)Math.Ceiling(r), (int)c);
            Vector3? br = GetXYZ(dem, (int)Math.Ceiling(r), (int)Math.Ceiling(c));
            return Interpolate(c - (int)c, r - (int)r, tl, tr, bl, br);
        }

        /// <summary>
        /// Unprojects passed in row, col in dem to return an xyz. Will return null if index is out of bounds, or point should be masked out.
        /// </summary>
        /// <param name="dem"></param>
        /// <param name="mask"></param>
        /// <param name="row"></param>
        /// <param name="col"></param>
        /// <param name="filterValues"></param>
        /// <returns></returns>
        public static Vector3? GetXYZ(Image dem, Mask mask, int row, int col, double scale = 1, 
            bool filterValues = true, double minFilter = -1000000, double maxFilter = 1000000)
        {
            if (row < 0 || row >= dem.Height || col < 0 || col >= dem.Width || !dem.IsValid(row, col)) //respect input image mask if it has one
            {
                return null;
            }

            double value = dem[0, row, col];

            //HACK
            if (value == 0)
            {
                return null;
            }

            if (!filterValues || value >= minFilter && value <= maxFilter)
            {
                if (mask != null && !mask.isValid(row, col))
                {
                    return null;
                }
                return dem.CameraModel.Unproject(new Vector2(col, row), -1 * value * scale);
            }
            return null;
        }

        public static Vector3? GetXYZ(Image dem, int row, int col, double scale = 1, 
            bool filterValues = true, double minFilter = -1000000, double maxFilter = 1000000)
        {
            return GetXYZ(dem, null, row, col, scale, filterValues, minFilter, maxFilter);
        }

        public static Vector3? GetInterpolatedNormal(Image dem, double r, double c, double minFilter = -1000000, double maxFilter = 1000000)
        {
            Vector3? tl = GetNormal(dem, (int)r, (int)c);
            Vector3? tr = GetNormal(dem, (int)r, (int)Math.Ceiling(c));
            Vector3? bl = GetNormal(dem, (int)Math.Ceiling(r), (int)c);
            Vector3? br = GetNormal(dem, (int)Math.Ceiling(r), (int)Math.Ceiling(c));
            return Interpolate(c - (int)c, r - (int)r, tl, tr, bl, br);
        }

        public static Vector3? GetNormal(Image dem, int row, int col, double minFilter = -1000000, double maxFilter = 1000000)
        {
            Vector3? c = GetXYZ(dem, row, col);
            if(!c.HasValue)
            {
                return null;
            }

            Vector3? t = GetXYZ(dem, row - 1, col);
            Vector3? b = GetXYZ(dem, row + 1, col);
            Vector3? r = GetXYZ(dem, row, col + 1);
            Vector3? l = GetXYZ(dem, row, col - 1);

            Vector3 ret = Vector3.Zero;

            if (t.HasValue)
            {
                if(r.HasValue)
                {
                    ret += new Triangle(t.Value, c.Value, r.Value).Normal;
                }
                if(l.HasValue)
                {
                    ret += new Triangle(t.Value, l.Value, c.Value).Normal;
                }
            }
            if(b.HasValue)
            {
                if (r.HasValue)
                {
                    ret += new Triangle(b.Value, r.Value, c.Value).Normal;
                }
                if (l.HasValue)
                {
                    ret += new Triangle(b.Value, c.Value, l.Value).Normal;
                }
            }

            if(ret == Vector3.Zero)
            {
                return null;
            }

            ret.Normalize();
            return ret;
        }

        public static Vector2? GetRowCol(Image dem, Vector3 xyz)
        {
            return dem.CameraModel.Project(xyz, out double range);
        }

        /// <summary>
        /// Given Image dem, find corners that are not masked out. Optionally enter top left corner and a size parameter to get corners of a subregion.
        /// May not return a full set of vertices (potentially none) if image heavily masked
        /// </summary>
        /// <param name="dem"></param>
        /// <param name="minRow"></param>
        /// <param name="minCol"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        public static List<Vector2> FindCorners(Image dem, int minRow = 0, int minCol = 0, int width = -1, int height = -1)
        {
            int diagonalLength = 0;
            bool foundTopLeft = false;
            bool foundTopRight = false;
            bool foundBotLeft = false;
            bool foundBotRight = false;
            if (width == -1)
            {
                width = dem.Width - minCol - 1;
            }
            if (height == -1)
            {
                height = dem.Height - minRow - 1;
            }

            List<Vector2> ret = new List<Vector2>();
            while (diagonalLength < Math.Min(width, height) && (!foundTopLeft || !foundTopRight || !foundBotLeft || !foundBotRight))
            {
                int col;
                for (int row = 0; row <= diagonalLength; row++)
                {
                    col = diagonalLength - row;
                    if (!foundTopLeft)
                    {
                        Vector3? tl = DemOperations.GetXYZ(dem, null, minRow + row, minCol + col);
                        if (tl.HasValue)
                        {
                            foundTopLeft = true;
                            ret.Add(new Vector2(minCol + col, minRow + row));
                        }
                    }
                    if (!foundTopRight)
                    {
                        Vector3? tr = DemOperations.GetXYZ(dem, null, minRow + row, minCol + width - col);
                        if (tr.HasValue)
                        {
                            foundTopRight = true;
                            ret.Add(new Vector2(minCol + width - col, minRow + row));
                        }
                    }
                    if (!foundBotLeft)
                    {
                        Vector3? bl = DemOperations.GetXYZ(dem, null, minRow + height - row, minCol + col);
                        if (bl.HasValue)
                        {
                            foundBotLeft = true;
                            ret.Add(new Vector2(minCol + col, minRow + height - row));
                        }
                    }
                    if (!foundBotRight)
                    {
                        Vector3? br = DemOperations.GetXYZ(dem, null, minRow + height - row, minCol + width - col);
                        if (br.HasValue)
                        {
                            foundBotRight = true;
                            ret.Add(new Vector2(minCol + width - col, minRow + height - row));
                        }
                    }
                }
                ++diagonalLength;
            }
            return ret;
        }

        public static Matrix AlignSceneToDem(Image scenemap, Matrix sceneToWorld, Image dem, Matrix demToWorld, 
                                             int numAnnealingStages, SimulatedAnnealingOptions saOpts = null,
                                             bool preserveXY = false, double minOverlap = 0,
                                             double minFilter = -1000000, double maxFilter = 1000000,
                                             int sampleLimit = 3000, Action<string> log = null)
        {
            return AlignScenesToDem(new[] { scenemap }, new[] { sceneToWorld }, dem, demToWorld,
                                    numAnnealingStages, saOpts, preserveXY, minOverlap, minFilter, maxFilter,
                                    sampleLimit, log);
        }

        /// <summary>
        /// Returns alignment from dem to first passed in scenemap (using samples from full list)
        /// </summary>
        public static Matrix AlignScenesToDem(Image[] scenemaps, Matrix[] sceneToWorlds, Image dem, Matrix demToWorld, 
                                              int numAnnealingStages, SimulatedAnnealingOptions saOpts = null,
                                              bool preserveXY = false, double minOverlap = 0, 
                                              double minFilter = -1000000, double maxFilter = 1000000,
                                              int sampleLimit = 3000, Action<string> log = null)
        {
            log = log ?? (msg => {});

            if (sceneToWorlds.Count() != scenemaps.Count())
            {
                throw new Exception("Number of scenemaps does not match number of priors.");
            }

            int succeeded = 0;
            int total = 0;

            List<Vector3> samples = new List<Vector3>();

            double[] adjustment = { 0, 0, 0, 0, 0 };
            double zTranslation = 0;
            Func<double[], Matrix> arrayToTransform = new Func<double[], Matrix>((transform) =>
            {
                AxisAngleVector aav = new AxisAngleVector(transform[0], transform[1], transform[2]);
                Quaternion rotation = aav.ToQuaternion();
                Vector3 translation = new Vector3(transform[3], transform[4], 0);
                return Matrix.CreateFromQuaternion(rotation) * Matrix.CreateTranslation(translation);
            });

            for (int i = 0; i < scenemaps.Count(); i++)
            {
                Image scenemap = scenemaps[i];

                double skip = Math.Sqrt(scenemaps.Count() * scenemap.Height * scenemap.Width / sampleLimit);
                skip = Math.Max(skip, 1);               

                for (int r = 0; r < scenemap.Height / skip; r++)
                {
                    for (int c = 0; c < scenemap.Width / skip; c++)
                    {
                        Vector3? scenePoint = GetXYZ(scenemap, (int)Math.Min(r * skip, scenemap.Height - 1), (int)Math.Min(c * skip, scenemap.Width - 1));
                        if (scenePoint.HasValue)
                        {
                            Vector3 worldPoint = Vector3.Transform(scenePoint.Value, sceneToWorlds[i]);
                            Vector3 targetDemPoint = Vector3.Transform(worldPoint, Matrix.Invert(demToWorld));
                            Vector2? demRowCol = GetRowCol(dem, targetDemPoint);

                            //Ensure that samples are taken where meshes overlap in projected space
                            if (demRowCol.HasValue) {
                                Vector3? demPoint = GetInterpolatedXYZ(dem, demRowCol.Value.Y, demRowCol.Value.X, minFilter, maxFilter);
                                if (demPoint.HasValue)
                                {
                                    succeeded++;
                                    samples.Add(worldPoint);
                                }
                            }
                            total++;
                        }
                    }
                }
            }

            if (succeeded / (double)total < minOverlap)
            {
                throw new Exception(string.Format("insufficient overlap {0}/{1} < {2}", succeeded, total, minOverlap));
            }
            else
            {
                //Trim outliers
                int initialSampleCount = samples.Count;
                samples = samples.OrderBy(s => s.Z).ToList();
                double median = samples[samples.Count / 2].Z;
                var deviations = samples.Select(s => Math.Abs(s.Z - median)).ToArray();
                double mad = deviations.OrderBy(x => x).ToArray()[samples.Count / 2];
                samples = samples.Where(s => Math.Abs(s.Z - median) < 20 * mad).ToList();
                log(string.Format("trimmed {0} outliers", initialSampleCount - samples.Count));
                log(string.Format("proceeding with {0}/{1} overlapping samples", succeeded, total));
            }

            if (succeeded == 0)
            {
                throw new Exception("No overlap for heightmap align.");
            }
           
            double[] sigma = new double[] { Math.PI / 2880, Math.PI / 2880, Math.PI / 2880, 0.02, 0.02 };

            if(preserveXY)
            {
                sigma[2] = 0; //Prevent in plane rotation
                sigma[3] = 0; //Prevent in plane translation
                sigma[4] = 0;
            }

            Func<Quaternion, Vector3, double[]> transformToArray = new Func<Quaternion, Vector3, double[]>((r, t) =>
            {
                AxisAngleVector aav = new AxisAngleVector(r);
                return new double[]
                {
                    aav.X, aav.Y, aav.Z,
                    t.X, t.Y
                };
            });

            Func<double[], double> meanZSquaredError = new Func<double[], double>((transformArray) => {
                double error = 0;
                //Aligning scene sample points to dem; final transform will be dem to scene.
                //This could be refactored to avoid invert but should not make much of a computational difference
                Matrix currentTransformAdjustment = Matrix.Invert(arrayToTransform(transformArray)
                                                    * Matrix.CreateTranslation(new Vector3(0, 0, zTranslation)));
                int count = 0;
                int pos = 0;
                int neg = 0;
                for (int i = 0; i < samples.Count; i++)
                {
                    Vector3 adjustedSample = Vector3.Transform(samples[i], currentTransformAdjustment);
                    Vector3 inDem = Vector3.Transform(adjustedSample, Matrix.Invert(demToWorld));
                    //Project the transformed scene point onto dem
                    Vector2? demRowCol = GetRowCol(dem, inDem);
                    if (demRowCol.HasValue)
                    {
                        //Unproject to get dem height
                        Vector3? actualDemPoint = GetInterpolatedXYZ(dem, demRowCol.Value.Y, demRowCol.Value.X, minFilter, maxFilter);
                        //TODO: Issue 644 - better way to handle when the scene samples no longer hit the dem? Should be rare for orbital but could be a problem for more general use case
                        if (actualDemPoint.HasValue) {
                            Vector3 actualSample = Vector3.Transform(actualDemPoint.Value, demToWorld);
                            double zOff = adjustedSample.Z - actualSample.Z;
                            if (zOff > 0) pos++; else neg++; 
                            error += zOff * zOff;
                            ++count;
                        }
                    }
                }
                return count == 0 ? double.MaxValue : error / (double)count;
            });

            //Use a vertical projection to get height offset
            Func<double> meanZOffset = new Func<double>(() =>
            {
                double x = 0;
                Matrix currentTransformAdjustment = Matrix.Invert(arrayToTransform(adjustment) 
                                                    * Matrix.CreateTranslation(new Vector3(0, 0, zTranslation)));
                int count = 0;
                int pos = 0;
                int neg = 0;
                for (int i = 0; i < samples.Count; i++)
                {                  
                    Vector3 adjustedSample = Vector3.Transform(samples[i], currentTransformAdjustment);
                    Vector3 inDem = Vector3.Transform(adjustedSample, Matrix.Invert(demToWorld));
                    Vector2? demRowCol = GetRowCol(dem, inDem);
                    if (demRowCol.HasValue)
                    {
                        Vector3? actualDemPoint = GetInterpolatedXYZ(dem, demRowCol.Value.Y, demRowCol.Value.X, minFilter, maxFilter); //TODO: check half pixel on interpolate
                        if (actualDemPoint.HasValue)
                        {
                            Vector3 actualSample = Vector3.Transform(actualDemPoint.Value, demToWorld); 
                            x += actualSample.Z - adjustedSample.Z;
                            var zOff = actualSample.Z - adjustedSample.Z;
                            if (zOff > 0) pos++; else neg++;
                            ++count;
                        }
                    }
                }
                return count == 0 ? 0 : x / (double)count;
            });

            zTranslation = -1 * meanZOffset();

            SimulatedAnnealing sa = new SimulatedAnnealing();
            if (saOpts == null)
            {
                saOpts = new SimulatedAnnealingOptions();
                saOpts.maxIterations = 800;
                saOpts.verbose = false;
                saOpts.temperatureScale = 4; //1
                saOpts.probabilityScale = 1000; //100
                saOpts.sigma = sigma;
            }
            sa.opts = saOpts;

            for (int i = 0; i < numAnnealingStages; i++)
            {
                log(string.Format("annealing pass {0}/{1}: error {2}", i + 1, numAnnealingStages,
                                  meanZSquaredError(adjustment)));
                sa.temperatureExponent = 1.0 / (Math.Max(4, numAnnealingStages) - i);
                double[] saTransform = sa.Minimize(meanZSquaredError, adjustment);
                adjustment = saTransform;
                zTranslation -= meanZOffset();
            }

            log(string.Format("finished annealing, final error {0}", meanZSquaredError(adjustment)));

            return arrayToTransform(adjustment) * Matrix.CreateTranslation(new Vector3(0, 0, zTranslation));
        }

        public static List<Vector3> CreateAdjustments(Image dem, MeshOperator surfaceMeshOp, Vector2 pixelCenter,
            Matrix demToSurface, int orbitalRadiusPixels)
        {
            //Get subset of dem around sitedrive
            int baseC = (int)Math.Max(pixelCenter.X - orbitalRadiusPixels, 0);
            int baseR = (int)Math.Max(pixelCenter.Y - orbitalRadiusPixels, 0);
            int pixelWidth = (int)Math.Min(pixelCenter.X + orbitalRadiusPixels, dem.Width) - baseC;
            int pixelHeight = (int)Math.Min(pixelCenter.Y + orbitalRadiusPixels, dem.Height) - baseR;

            if (!dem.HasMask)
            {
                dem.CreateMask();
            }

            Matrix baseSiteDriveToDem = Matrix.Invert(demToSurface);
            List<Vector3> adjustments = new List<Vector3>();

            for (int y = 0; y < 2 * orbitalRadiusPixels; y++)
            {
                for (int x = 0; x < 2 * orbitalRadiusPixels; x++)
                {
                    int r = baseR + y;
                    int c = baseC + x;
                    var demPoint = DemOperations.GetXYZ(dem, r, c);
                    if (demPoint.HasValue)
                    {
                        var transformedDemPoint = Vector3.Transform(demPoint.Value, demToSurface);
                        var bp = surfaceMeshOp.UVToBarycentric(new Vector2(transformedDemPoint.X, transformedDemPoint.Y));
                        if (bp != null)
                        {
                            Vector3 meshPoint = bp.Position;
                            adjustments.Add(new Vector3(transformedDemPoint.X,
                                                        transformedDemPoint.Y,
                                                        meshPoint.Z - transformedDemPoint.Z));
                        }
                    }
                }
            }

            return adjustments;
        }

        /// <summary>
        /// Build mesh from dem points surrounding surface mesh, with a padding of filterRadius.
        /// Extent is determined by orbitalRadiusPixels around pixelCenter.
        /// Point density (interpolated if higher than dem resolution) is controlled by subSampleFactor
        /// Note: touches the dem mask (creates if null) //TODO: remove side effect
        /// </summary>
        /// <param name="dem"></param>
        /// <param name="surfaceMesh"></param>
        /// <param name="pixelCenter"></param>
        /// <param name="demToSurface"></param>
        /// <param name="orbitalRadiusPixels"></param>
        /// <param name="filterRadius"></param>
        /// <param name="subSampleFactor"></param>
        /// <returns></returns>
        public static Mesh BuildOrbitalMeshAroundSurface(Image dem, Mesh surfaceMesh, Vector2 pixelCenter, 
            Matrix demToSurface, int orbitalRadiusPixels, double filterRadius, double subSampleFactor, Func<Vector3, Vector3> adjust)
        {
            //Get subset of dem around sitedrive
            int baseC = (int)Math.Max(pixelCenter.X - orbitalRadiusPixels, 0);
            int baseR = (int)Math.Max(pixelCenter.Y - orbitalRadiusPixels, 0);
            int pixelWidth = (int)Math.Min(pixelCenter.X + orbitalRadiusPixels, dem.Width) - baseC;
            int pixelHeight = (int)Math.Min(pixelCenter.Y + orbitalRadiusPixels, dem.Height) - baseR;

            if (!dem.HasMask)
            {
                dem.CreateMask();
            }

            Matrix baseSiteDriveToDem = Matrix.Invert(demToSurface);

            double filterRadiusSq = filterRadius * filterRadius;
            foreach (var p in surfaceMesh.Vertices)
            {
                Vector3 testPoint = Vector3.Transform(p.Position, baseSiteDriveToDem);
                Vector2 rc = dem.CameraModel.Project(testPoint, out double throwAwayRange);
                for (int r = (int)Math.Floor(Math.Max(rc.Y - filterRadius, baseR));
                    r <= (int)Math.Ceiling(Math.Min(rc.Y + filterRadius, baseR + pixelHeight - 1)); ++r)
                {
                    for (int c = (int)Math.Floor(Math.Max(rc.X - filterRadius, baseC));
                        c <= (int)Math.Ceiling(Math.Min(rc.X + filterRadius, baseC + pixelWidth - 1)); ++c)
                    {
                        double distSq = (rc.Y - r) * (rc.Y - r) + (rc.X - c) * (rc.X - c);
                        if (distSq < filterRadiusSq)
                        {
                            dem.SetMaskValue(r, c, true);
                        }
                    }
                }
            }
            MeshOperator mo = new MeshOperator(surfaceMesh, buildFaceTree:false, buildVertexTree:false, buildUVFaceTree:true);
            Mesh demMesh = new Mesh();
            for (int y = 0; y < 2 * orbitalRadiusPixels * subSampleFactor; y++)
            {
                for (int x = 0; x < 2 * orbitalRadiusPixels * subSampleFactor; x++)
                {
                    double r = baseR + y / subSampleFactor;
                    double c = baseC + x / subSampleFactor;
                    var pos = DemOperations.GetInterpolatedXYZ(dem, r, c);
                    if (pos.HasValue)
                    {
                        var transformedPos = Vector3.Transform(pos.Value, demToSurface);
                        if (mo.UVToBarycentric(new Vector2(transformedPos.X, transformedPos.Y)) == null)
                        {
                            Vertex v = new Vertex();
                            v.Position = adjust(transformedPos);
                            v.Normal = DemOperations.GetInterpolatedNormal(dem, r, c) ?? new Vector3(0, 0, -1);
                            v.Normal = Vector3.Normalize(Vector3.TransformNormal(v.Normal, demToSurface));
                            demMesh.Vertices.Add(v);
                        }
                    }
                }
            }
            demMesh.Vertices.AddRange(surfaceMesh.Vertices);

            demMesh = Delaunay.Triangulate(demMesh.Vertices, reverseWinding: true);
            demMesh.Faces = demMesh.Faces.Where(face =>
            {
                Triangle tri = new Triangle(demMesh.Vertices[face.P0], demMesh.Vertices[face.P1], demMesh.Vertices[face.P2]);
                Vector3 c = tri.Barycenter();
                return mo.UVToBarycentric(new Vector2(c.X, c.Y)) == null;
            }).ToList();

            demMesh.RemoveUnreferencedVertices();
            return demMesh;
        }
    }    
}
