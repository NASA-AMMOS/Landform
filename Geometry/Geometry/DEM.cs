using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using log4net;
using Microsoft.Xna.Framework;
using OPS.Imaging;
using OPS.Util;

namespace OPS.Geometry
{
    /// <summary>
    /// Wraps a scalar Image as a Digital Elevation Map with an OrthographicCameraModel.
    /// Wraps instead of derives from Image is so that the data can be either a regular Image or a SparseImage.
    /// </summary>
    public class DEM
    {
        //mission surface frames are typically +X north, +Y east, +Z down
        //orbital DEM images typically have latitude increasing with row and longitude increasing with col
        public static Vector3 DEF_UP_DIR { get; } = new Vector3(0, 0, -1);
        public static Vector3 DEF_RIGHT_DIR { get; } = new Vector3(0, 1, 0);
        public static Vector3 DEF_DOWN_DIR { get; } = new Vector3(-1, 0, 0);

        //public const double DEF_MIN_FILTER = -1000000;
        //public const double DEF_MAX_FILTER = 1000000;
        public const double DEF_MIN_FILTER = 0;
        public const double DEF_MAX_FILTER = 0;

        //dem values outside these bounds are considered invalid
        //ignored if min >= max (e.g. min = max = 0 disables filtering)
        public double MinFilter = DEF_MIN_FILTER; 
        public double MaxFilter = DEF_MAX_FILTER;

        public int Width { get { return dem.Width; } }
        public int Height { get { return dem.Height; } }
        public int Area { get { return dem.Area; } }

        public bool IsValid(int r, int c)
        {
            return dem.IsValid(r, c);
        }

        public OrthographicCameraModel CameraModel { get; private set; }

        public Vector2 OriginPixel { get { return CameraModel.Project(Vector3.Zero); } }

        public Vector2 MetersPerPixel { get { return CameraModel.MetersPerPixel; } }

        //ratio of pixel with to height
        public double PixelAspect { get { return MetersPerPixel.X / MetersPerPixel.Y; } }

        public double AvgMetersPerPixel
        {
            get
            {
                var mpp = MetersPerPixel;
                return (mpp.X + mpp.Y) * 0.5;
            }
        }

        public double WidthMeters { get { return Width * MetersPerPixel.X; } }

        public double HeightMeters { get { return Height * MetersPerPixel.Y; } }

        public double ElevationScale = 1; //applied by GetElevation() and all related

        public Mask Mask; //if non-null then this is an additional mask for dem

        private Image dem; //may have mask

        /// <summary>
        /// Sparse DEM texture image backed by an image file.
        /// File format must support partial reads, currently only GDALSerializer does.
        /// The chunks are loaded lazily from disk.  Call Populate() to load them all.
        /// </summary>
        public class SparseDEMImage : SparseImage
        {
            public const int CHUNK_SIZE = 512;
            public const int CHUNK_CACHE_SIZE = 400; //important: cache size > 0 limits memory usage
            public SparseDEMImage(string path) : base(path, chunkSize: CHUNK_SIZE, cacheSize: CHUNK_CACHE_SIZE) { }
        }

        /// <summary>
        /// Sparse DEM elevation map backed by an image file.
        /// </summary>
        public class SparseDEM : SparseDEMImage
        {
            public SparseDEM(string path) : base(path) { }

            protected override IImageConverter GetReadConverter()
            {
                return ImageConverters.PassThrough;
            }
            
            protected override IImageConverter GetWriteConverter()
            {
                return ImageConverters.PassThrough;
            }
        }

        public DEM(Image dem, OrthographicCameraModel cameraModel)
        {
            this.dem = dem;
            this.CameraModel = cameraModel;
        }

        public DEM(Image dem, double metersPerPixel = 1, double pixelAspect = 1, double elevationScale = 1,
                   Vector2? originPixel = null, double? originElevation = null,
                   double minFilter = DEF_MIN_FILTER, double maxFilter = DEF_MAX_FILTER)
            : this(dem, DEF_UP_DIR, DEF_RIGHT_DIR, DEF_DOWN_DIR,
                   metersPerPixel, pixelAspect, elevationScale, originPixel, originElevation, minFilter, maxFilter)
        { }

        /// <summary>
        /// enclosing coordinate frame origin corresponds to originPixel, originElevation in dem
        ///
        /// if originElevation is omitted it is looked up with GetInterpolatedElevation(originPixel)
        /// (an exception is thrown in that case if originPixel is out of bounds or has no valid neighbors in dem)
        ///
        /// if originPixel is omitted it defaults to the center of dem
        ///
        /// the orientation of the orthographic camera in the enclosing frame is defined by elevationDir,
        /// rightDir, downDir which should be unit vectors
        /// 
        /// the location of the orthographic camera in the enclosing frame corresponds to the center pixel of the dem
        /// at zero elevation
        /// </summary>
        public DEM(Image dem, Vector3 elevationDir, Vector3 rightDir, Vector3 downDir,
                   double metersPerPixel = 1, double pixelAspect = 1, double elevationScale = 1,
                   Vector2? originPixel = null, double? originElevation = null,
                   double minFilter = DEF_MIN_FILTER, double maxFilter = DEF_MAX_FILTER)
        {
            this.dem = dem;

            this.MinFilter = minFilter;
            this.MaxFilter = maxFilter;
            this.ElevationScale = elevationScale;

            Vector2 centerPixel = new Vector2(Width, Height) * 0.5;
            
            if (!originPixel.HasValue)
            {
                originPixel = centerPixel;
            }
            
            if (!originElevation.HasValue)
            {
                //GetInterpolatedElevation() doesn't need camera model
                //and we already initialized MinFilter/MaxFilter and ElevationScale
                originElevation = GetInterpolatedElevation(originPixel.Value);
            }

            if (!originElevation.HasValue)
            {
                throw new Exception(string.Format("failed to get interpolated elevation at DEM pixel ({0}, {1})",
                                                  originPixel.Value.X, originPixel.Value.Y));
            }

            Vector2 originToCenter = centerPixel - originPixel.Value;

            Vector3 right = rightDir * metersPerPixel * pixelAspect;
            Vector3 down = downDir * metersPerPixel;

            Vector3 camCtr = originToCenter.X * right + originToCenter.Y * down - originElevation.Value * elevationDir;
            
            this.CameraModel = new OrthographicCameraModel(camCtr, elevationDir, right, down, Width, Height);
        }

        public DEM Decimated(int blocksize)
        {
            return new DEM(dem.Decimated(blocksize), CameraModel.Decimated(blocksize));
        }

        public DEM DecimateTo(int maxSize)
        {
            double sz = Math.Max(Width, Height);
            if (sz < maxSize)
            {
                return this;
            }
            return Decimated((int)Math.Ceiling(sz / maxSize));
        }

        /// <summary>
        /// fetch an elevation value for a given pixel
        /// returns null if pixel is out of bounds, masked out, or filtered
        /// </summary>
        public double? GetElevation(int row, int col)
        {
            if (row < 0 || row >= Height || col < 0 || col >= Width)
            {
                return null;
            }

            if (!IsValid(row, col))
            {
                return null;
            }

            if (Mask != null && !Mask.IsValid(row, col))
            {
                return null;
            }

            double value = dem[0, row, col];

            if (MinFilter < MaxFilter && (value < MinFilter || value > MaxFilter))
            {
                return null;
            }

            return value;
        }

        /// <summary>
        /// project a 3D point to a 2D pixel on DEM
        /// returns null if pixel is outside DEM image bounds
        /// </summary>
        public Vector2? GetPixel(Vector3 xyz)
        {
            var ret = CameraModel.Project(xyz);
            if (ret.X < 0 || ret.X >= Width || ret.Y < 0 || ret.Y >= Height)
            {
                return null;
            }
            return ret;
        }

        /// <summary>
        /// unprojects 2D pixel in DEM to 3D point
        /// returns null if pixel is out of bounds, masked out, or filtered
        /// </summary>
        public Vector3? GetXYZ(int row, int col)
        {
            double? elev = GetElevation(row, col);
            if (!elev.HasValue)
            {
                return null;
            }
            return CameraModel.Unproject(new Vector2(col, row), elev.Value * ElevationScale);
        }

        public Vector3? GetNormal(int row, int col)
        {
            Vector3? c = GetXYZ(row, col);
            if (!c.HasValue)
            {
                return null;
            }

            Vector3? t = GetXYZ(row - 1, col);
            Vector3? b = GetXYZ(row + 1, col);
            Vector3? r = GetXYZ(row, col + 1);
            Vector3? l = GetXYZ(row, col - 1);

            Vector3 ret = Vector3.Zero;

            if (t.HasValue)
            {
                if (r.HasValue)
                {
                    ret += new Triangle(t.Value, c.Value, r.Value).Normal;
                }
                if (l.HasValue)
                {
                    ret += new Triangle(t.Value, l.Value, c.Value).Normal;
                }
            }
            if (b.HasValue)
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

            if (ret == Vector3.Zero)
            {
                return null;
            }

            ret.Normalize();

            return ret;
        }

        /// <summary>
        /// Bilinear interpolation with potentially null points.
        /// 0 <= x <= 1 and 0 <= y <= 1 are normalized offset from the top left corner respectively, see diagram.
        /// If a corner is missing, its contribution is ignored in the weighted average.
        /// 
        /// tl ---------------- tr
        ///  |       |          |
        ///  |       | y        |
        ///  |       |          |
        ///  |-------*          |
        ///  |   x              |
        ///  |                  |
        ///  |                  |
        /// bl ---------------- br
        /// </summary>
        private static T? Interpolate<T>(double x, double y, T? tl, T? tr, T? bl, T? br, T init,
                                         Func<T, double, T> weight, Func<T, T, T> plus, Func<T, double, T> div)
            where T : struct
        {
            T ret = init;
            double area = 0;
            if (tl.HasValue)
            {
                double a = (1 - x) * (1 - y);
                ret = plus(ret, weight(tl.Value, a));
                area += a;
            }
            if (tr.HasValue)
            {
                double a = x * (1 - y);
                ret = plus(ret, weight(tr.Value, a));
                area += a;
            }
            if (bl.HasValue)
            {
                double a = (1 - x) * y;
                ret = plus(ret, weight(bl.Value, a));
                area += a;
            }
            if (br.HasValue)
            {
                double a = x * y;
                ret = plus(ret, weight(br.Value, a));
                area += a;
            }
            if (area == 0)
            {
                return null;
            }
            return div(ret, area);
        }

        private static T? Interpolate<T>(Vector2 pixel, Func<int, int, T?> func, T init,
                                         Func<T, double, T> weight, Func<T, T, T> plus, Func<T, double, T> div)
            where T : struct
        {
            double r = pixel.Y, c = pixel.X;
            double cr = Math.Ceiling(r), cc = Math.Ceiling(c);
            T? tl = func((int)r,  (int)c);
            T? tr = func((int)r,  (int)cc);
            T? bl = func((int)cr, (int)c);
            T? br = func((int)cr, (int)cc);
            return Interpolate(c - (int)c, r - (int)r, tl, tr, bl, br, init, weight, plus, div);
        }

        private static double? InterpolateDouble(Vector2 pixel, Func<int, int, double?> func)
        {
            return Interpolate(pixel, func, 0, (a, b) => a * b, (a, b) => a + b, (a, b) => a / b);
        }

        private static Vector3? InterpolateVector(Vector2 pixel, Func<int, int, Vector3?> func)
        {
            return Interpolate(pixel, func, Vector3.Zero, (a, b) => a * b, (a, b) => a + b, (a, b) => a / b);
        }

        public double? GetInterpolatedElevation(Vector2 pixel)
        {
            return InterpolateDouble(pixel, GetElevation);
        }

        public Vector3? GetInterpolatedXYZ(Vector2 pixel)
        {
            return InterpolateVector(pixel, GetXYZ);
        }

        public Vector3? GetInterpolatedNormal(Vector2 pixel)
        {
            return InterpolateVector(pixel, GetNormal);
        }

        public Image.Subrect GetSubrectMeters(double radiusMeters, Vector3? centerPoint = null)
        {
            if (!centerPoint.HasValue)
            {
                centerPoint = Vector3.Zero;
            }
            Vector2 center = CameraModel.Project(centerPoint.Value);
            Vector2 mpp = CameraModel.MetersPerPixel;
            return dem.GetSubrect(center, new Vector2(radiusMeters / mpp.X, radiusMeters / mpp.Y));
        }

        public Image.Subrect GetSubrectPixels(double radiusPixels, Vector3? centerPoint = null)
        {
            if (!centerPoint.HasValue)
            {
                centerPoint = Vector3.Zero;
            }
            return GetSubrectPixels(radiusPixels, CameraModel.Project(centerPoint.Value));
        }

        public Image.Subrect GetSubrectPixels(double radiusPixels, Vector2 centerPixel)
        {
            return dem.GetSubrect(centerPixel, radiusPixels);
        }

        public Mesh DelaunayMesh(double maxRadiusMeters = -1, Vector3? centerPoint = null, bool withUV = false,
                                 bool reverseWinding = false)
        {
            var bounds = GetSubrectMeters(maxRadiusMeters, centerPoint);
            var pc = new Mesh();
            double w = bounds.Width, h = bounds.Height;
            for (int r = bounds.MinY; r <= bounds.MaxY; r++)
            {
                for (int c = bounds.MinX; c <= bounds.MaxX; c++)
                {
                    var pt = GetXYZ(r, c);
                    if (pt.HasValue)
                    {
                        Vertex v = new Vertex();
                        v.Position = pt.Value;
                        if (withUV)
                        {
                            v.UV = new Vector2((c - bounds.MinX) / w, 1 - ((r - bounds.MinY) / h));
                        }
                        pc.Vertices.Add(v);
                    }
                }
            }
            var mesh = Delaunay.Triangulate(pc.Vertices, reverseWinding: reverseWinding);
            mesh.HasUVs = withUV;
            return mesh;
        }

        public Mesh OrganizedMesh(double maxRadiusMeters = -1, Vector3? centerPoint = null, bool withUV = false,
                                  bool reverseWinding = false)
        {
            var bounds = GetSubrectMeters(maxRadiusMeters, centerPoint);
            var pc = new Image(3, bounds.Width, bounds.Height);
            pc.CreateMask();
            for (int r = bounds.MinY; r <= bounds.MaxY; r++)
            {
                for (int c = bounds.MinX; c <= bounds.MaxX; c++)
                {
                    var pt = GetXYZ(r, c);
                    if (pt.HasValue)
                    {
                        pc[0, r - bounds.MinY, c - bounds.MinX] = (float)pt.Value.X;
                        pc[1, r - bounds.MinY, c - bounds.MinX] = (float)pt.Value.Y;
                        pc[2, r - bounds.MinY, c - bounds.MinX] = (float)pt.Value.Z;
                    }
                    else
                    {
                        pc.SetMaskValue(r - bounds.MinY, c - bounds.MinX, true);
                    }
                }
            }
            return OrganizedPointCloud.BuildOrganizedMesh(pc, generateUV: withUV, generateNormals: false,
                                                          reverseWinding: reverseWinding);
        }

        public Mesh AdaptiveMesh(double maxError, double maxRadiusMeters = -1, Vector3? centerPoint = null,
                                 bool withUV = false, bool reverseWinding = false)
        {
            if (maxError <= 0)
            {
                throw new ArgumentException("maxError <= 0; use OrganizedMesh() or DelaunayMesh()");
            }

            //build decimated mesh by iterative sampling:
            //start with two tris that connect the dem corners
            //test error and sample regions that need subdividing (quad scheme)
            
            //this Mask is only used to avoid resampling the same point.
            //invalid data is masked out by the GetXYZ function
            var mask = new Mask(Width, Height, useHash: true);

            var b = GetSubrectMeters(maxRadiusMeters, centerPoint);
            var rand = NumberHelper.MakeRandomGenerator();

            var pixels = FindCorners(b.MinX, b.MinY, b.Width, b.Height);
            pixels.AddRange(Split(pixels, b.MinX, b.MinY, b.Width, b.Height, maxError * maxError, mask, rand));

            double w = b.Width, h = b.Height;
            var verts = new List<Vertex>();
            foreach (var px in pixels)
            {
                if (px.X >= b.MinX && px.X <= b.MaxX && px.Y >= b.MinY && px.Y <= b.MaxY)
                {
                    var pt = GetXYZ((int)px.Y, (int)px.X).Value;
                    var vert = new Vertex(pt);
                    if (withUV)
                    {
                        vert.UV = new Vector2((px.X - b.MinX) / w, 1 - ((px.Y - b.MinY) / h));
                    }
                    verts.Add(vert);
                }
            }

            var mesh = Delaunay.Triangulate(verts, reverseWinding: reverseWinding);
            mesh.HasUVs = withUV;
            return mesh;
        }

        /// <summary>
        /// Recursively subsample regions where geometric error is too large
        /// </summary>
        private List<Vector2> Split(List<Vector2> rowCols, double r, double c, double width, double height,
                                    double error, Mask mask, Random rand)
        {
            int sampleNum = 30;
            int testNum = 4;
            double sampleScale = 2;

            //Mesh the current set of vertices
            var verts = rowCols.Select(rc => new Vertex(GetXYZ((int)rc.Y, (int)rc.X).Value)).ToArray();
            Mesh mesh = Delaunay.Triangulate(verts);

            //Sample
            List<Vector2> newRowCols = new List<Vector2>();
            double tested = 0;
            bool shouldSplit = false;
            for (int i = 0; i < sampleNum; i++)
            {
                int testR = (int)(r + (sampleScale * rand.NextDouble() - 0.5 * (sampleScale - 1)) * height);
                int testC = (int)(c + (sampleScale * rand.NextDouble() - 0.5 * (sampleScale - 1)) * width);
                Vector3? v = mask.IsValid(testR, testC) ? GetXYZ(testR, testC) : null;
                if (v.HasValue)
                {
                    mask.SetInvalid(testR, testC); //Prevent point from being resampled
                    newRowCols.Add(new Vector2(testC, testR));
                    
                    //Test error between mesh and samples
                    if(!shouldSplit && tested < testNum &&
                       testR > r && testR < r + height && testC > c && testC < c + width)
                    {
                        double dist = double.MaxValue;
                        List<Triangle> tris = mesh.Triangles();
                        foreach (Triangle t in tris)
                        {
                            double tmp = t.SquaredDistance(v.Value);
                            dist = Math.Min(tmp, dist);
                        }
                        if (dist > error)
                        {
                            shouldSplit = true;
                        }
                        tested++;
                    }
                }
            }

            //Subsample if error exceeded threshold
            if (!shouldSplit)
            {
                return newRowCols;
            }

            //Compute new child tile bounds
            Vector3? tl = GetInterpolatedXYZ(new Vector2(c, r));

            double r1 = Math.Min(r + height, dem.Height - 1);
            double c1 = Math.Min(c + width, dem.Width - 1);
            Vector3? br = GetInterpolatedXYZ(new Vector2(c1, r1));

            if (!tl.HasValue || !br.HasValue)
            {
                throw new Exception("Failed to get tile corner");
            }
            double minX = tl.Value.X;
            double maxY = tl.Value.Y;
            double maxX = br.Value.X;
            double minY = br.Value.Y;

            double midX = (minX + maxX) / 2.0;
            double midY = (minY + maxY) / 2.0;
            double umidX = (minX + maxX + (maxX - minX) * 0.1) / 2.0;
            double lmidX = (minX + maxX - (maxX - minX) * 0.1) / 2.0;
            double umidY = (minY + maxY + (maxY - minY) * 0.1) / 2.0;
            double lmidY = (minY + maxY - (maxY - minY) * 0.1) / 2.0;

            //Add boundary conditions to each tile child
            //(try to find approximate tile corners, and include full dem corners in case of failure)
            List<Vector2> vIdxs1 = FindCorners((int)r, (int)c, (int)((width - 1)/2), (int)((height - 1) / 2));
            List<Vector2> vIdxs2 = FindCorners((int)(r + height / 2), (int)c, (int)((width - 1) / 2), (int)((height - 1) / 2));
            List<Vector2> vIdxs3 = FindCorners((int)r, (int)(c + width / 2), (int)((width - 1) / 2), (int)((height - 1) / 2));
            List<Vector2> vIdxs4 = FindCorners((int)(r + height / 2), (int)(c + width / 2), (int)((width - 1)/2), (int)((height - 1) / 2));
            vIdxs1.AddRange(rowCols.GetRange(0, 4));
            vIdxs2.AddRange(rowCols.GetRange(0, 4));
            vIdxs3.AddRange(rowCols.GetRange(0, 4));
            vIdxs4.AddRange(rowCols.GetRange(0, 4));

            //Partition our current set of vertices + new samples into children
            foreach (Vector2 rc in rowCols.Union(newRowCols))
            {
                Vector3 v = GetXYZ((int)rc.Y, (int)rc.X).Value;
                if (v.X < umidX)
                {
                    if(v.Y > lmidY)
                    {
                        vIdxs1.Add(rc);
                    }
                    if(v.Y < umidY)
                    {
                        vIdxs2.Add(rc);
                    }
                }
                if (v.X > lmidX)
                {
                    if(v.Y > lmidY)
                    {
                        vIdxs3.Add(rc);
                    }
                    if(v.Y < umidY)
                    {
                        vIdxs4.Add(rc);
                    }
                }
            }

            //Recurse on children
            newRowCols.AddRange(Split(vIdxs1, r, c, width/2.0, height/2.0, error, mask, rand));
            newRowCols.AddRange(Split(vIdxs2, r + height/2.0, c, width / 2.0, height / 2.0, error, mask, rand));
            newRowCols.AddRange(Split(vIdxs3, r, c + width / 2.0, width / 2.0, height / 2.0, error, mask, rand));
            newRowCols.AddRange(Split(vIdxs4, r + height / 2.0, c + width / 2.0, width / 2.0, height / 2.0, error, mask, rand));

            return newRowCols;
        }       

        /// <summary>
        /// Given Image dem, find corners that are not masked out.
        /// Optionally enter top left corner and a size parameter to get corners of a subregion.
        /// May not return a full set of vertices (potentially none) if image heavily masked
        /// </summary>
        private List<Vector2> FindCorners(int minRow = 0, int minCol = 0, int width = -1, int height = -1)
        {
            int diagonalLength = 0;
            bool foundTopLeft = false;
            bool foundTopRight = false;
            bool foundBotLeft = false;
            bool foundBotRight = false;
            if (width == -1)
            {
                width = Width - minCol - 1;
            }
            if (height == -1)
            {
                height = Height - minRow - 1;
            }

            List<Vector2> ret = new List<Vector2>();
            while (diagonalLength < Math.Min(width, height) &&
                   (!foundTopLeft || !foundTopRight || !foundBotLeft || !foundBotRight))
            {
                int col;
                for (int row = 0; row <= diagonalLength; row++)
                {
                    col = diagonalLength - row;
                    if (!foundTopLeft)
                    {
                        Vector3? tl = GetXYZ(minRow + row, minCol + col);
                        if (tl.HasValue)
                        {
                            foundTopLeft = true;
                            ret.Add(new Vector2(minCol + col, minRow + row));
                        }
                    }
                    if (!foundTopRight)
                    {
                        Vector3? tr = GetXYZ(minRow + row, minCol + width - col);
                        if (tr.HasValue)
                        {
                            foundTopRight = true;
                            ret.Add(new Vector2(minCol + width - col, minRow + row));
                        }
                    }
                    if (!foundBotLeft)
                    {
                        Vector3? bl = GetXYZ(minRow + height - row, minCol + col);
                        if (bl.HasValue)
                        {
                            foundBotLeft = true;
                            ret.Add(new Vector2(minCol + col, minRow + height - row));
                        }
                    }
                    if (!foundBotRight)
                    {
                        Vector3? br = GetXYZ(minRow + height - row, minCol + width - col);
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
    }    
}
