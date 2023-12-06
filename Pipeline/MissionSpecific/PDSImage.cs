using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using Microsoft.Xna.Framework;
using JPLOPS.Imaging;
using JPLOPS.Geometry;

namespace JPLOPS.Pipeline
{
    public class PDSImage
    {
        public const int CROSS_NORMAL_RADIUS = 2;

        public static double nearLimit = 1;
        public static double farLimit = 100;

        public readonly Image Image;
        public readonly PDSMetadata Metadata;
        public readonly PDSParser Parser;

        /// <summary>
        /// local_level to site
        /// </summary>
        public static UncertainRigidTransform GetSiteDriveToSiteTransformFromPDS(PDSParser parser)
        {            
            if (!parser.RoverCoordinateSystemRelativeToSite)
            {
                throw new Exception("rover frame not relative to site frame");
            }

            // TODO: examine values here
            double degSqr = Math.Pow(Math.PI / 180, 2);
            var covariance = CreateMatrix
                .Diagonal<double>(new double[] { 0.25, 0.25, 0.25, 0.5 * degSqr, 0.5 * degSqr, 1.0 * degSqr });

            return new UncertainRigidTransform(Matrix.CreateTranslation(parser.OriginOffset), covariance);
        }

        public static UncertainRigidTransform GetSiteDriveToSiteTransformFromPDS(Image img)
        {
            return GetSiteDriveToSiteTransformFromPDS(new PDSParser((PDSMetadata)img.Metadata));
        }

        /// <summary>
        /// img must have PDSMetadata  
        /// parser will be created if not supplied
        /// </summary>
        public PDSImage(Image img, PDSParser parser = null)
        {
            if (img == null || !(img.Metadata is PDSMetadata))
            {
                throw new ArgumentException("PDSImage requires an Image with PDS metadata");
            }

            this.Image = img;
            this.Metadata = (PDSMetadata)img.Metadata;
            this.Parser = parser ?? new PDSParser(Metadata);
        }

        public static float[] GetMissingBandValues(Image img, PDSParser parser)
        {
            parser = parser ?? new PDSParser((PDSMetadata)img.Metadata);

            //nominal missing constant value is 0, MSL SIS
            float[] missing = new float[] { 0.0f }; //will be extented to img.Bands below
            if (parser.HasMissingConstant)
            {
                missing = parser.MissingConstant;
            }

            //ROASTT18 wart: single float missing constant for 3 channel navcam
            if (missing.Length == 1 && img.Bands > 1)
            {
                missing = Enumerable.Repeat<float>(missing[0], img.Bands).ToArray();
            }

            return missing;
        }

        public float[] GetMissingBandValues()
        {
            return GetMissingBandValues(this.Image, Parser);
        }

        /// <summary>
        /// mask all pixels in dst corresponding to pixels in src which match the PDS MissingConstant, if any
        /// if the parser is not supplied it will be created from src
        /// </summary>
        public static void AddMaskForMissingConstant(Image dst, Image src, PDSParser parser = null)
        {
            dst.UnionMask(src, GetMissingBandValues(src, parser));
        }

        /// <summary>
        /// uses this PDSImage as src  
        /// </summary>
        public void AddMaskForMissingConstant(Image dst)
        {
            AddMaskForMissingConstant(dst, this.Image, Parser);
        }

        /// <summary>
        /// uses this PDSImage as src and dst
        /// </summary>
        public void AddMaskForMissingConstant()
        {
            AddMaskForMissingConstant(this.Image, this.Image, Parser);
        }

        /// <summary>
        /// format and throw exception if DerivedImageType doesn't match the requested type
        /// </summary>
        public static void CheckType(PDSParser parser, RoverProductType type, string what)
        {
            if (parser.DerivedImageType != type)
            {
                throw new ArgumentException(what + " requires " + type + " product, got " +
                                            parser.DerivedImageType + " for " + parser.ProductIdString);
            }
        }

        /// <summary>
        /// creates parser from metadata  
        /// </summary>
        public static void CheckType(Image img, RoverProductType type, string what)
        {
            CheckType(new PDSParser((PDSMetadata)img.Metadata), type, what);
        }

        /// <summary>
        /// operates on this PDSImage  
        /// </summary>
        public void CheckType(RoverProductType type, string what)
        {
            CheckType(Parser, type, what);
        }

        /// <summary>
        /// format and throw exception if CameraModelRefFrame doesn't match the requested frame
        /// </summary>
        public static void CheckCameraFrame(PDSParser parser, string what,
                                            PDSParser.ReferenceCoordinateFrame frame =
                                            PDSParser.ReferenceCoordinateFrame.RoverNav)
        {
            if (parser.CameraModelRefFrame != frame)
            {
                throw new NotImplementedException(what + " requires camera model in " + frame + " frame, got " +
                                                  parser.CameraModelRefFrame + " for " + parser.ProductIdString);
            }
        }

        /// <summary>
        /// creates parser from metadata  
        /// </summary>
        public static void CheckCameraFrame(Image img, string what,
                                            PDSParser.ReferenceCoordinateFrame frame =
                                            PDSParser.ReferenceCoordinateFrame.RoverNav)
        {
            CheckCameraFrame(new PDSParser((PDSMetadata)img.Metadata), what, frame);
        }

        /// <summary>
        /// operates on this PDSImage  
        /// </summary>
        public void CheckCameraFrame(string what,
                                     PDSParser.ReferenceCoordinateFrame frame =
                                     PDSParser.ReferenceCoordinateFrame.RoverNav)
        {
            CheckCameraFrame(Parser, what, frame);
        }

        /// <summary>
        /// Get transform from data frame to rover frame.
        /// </summary>
        public static Matrix GetDataToRoverFrameTransform(PDSParser parser)
        {
            var roverOriginRotation = parser.RoverOriginRotation;
            var originOffset = parser.OriginOffset;
            var frame = parser.DerivedImageRefFrame;
            switch (frame)
            {
                case PDSParser.ReferenceCoordinateFrame.LocalLevel:
                {
                    return RoverCoordinateSystem.LocalLevelToRover(roverOriginRotation);
                }
                case PDSParser.ReferenceCoordinateFrame.Site:
                {
                    return RoverCoordinateSystem.SiteToRover(roverOriginRotation, originOffset);
                }
                case PDSParser.ReferenceCoordinateFrame.RoverNav: return Matrix.Identity;
                default: throw new NotImplementedException("unknown reference frame: " + frame);
            }
        }

        /// <summary>
        /// check that the CameraModel is CAHV or a derivative thereof
        /// if not format and throw exception
        /// if so return the C vector
        /// </summary>
        public static Vector3 GetCameraCenter(Image img, string what)
        {
            CAHV cahv = img.CameraModel as CAHV;
            if (cahv == null)
            {
                throw new NotImplementedException(what + " requires CAHV camera model");
            }
            return cahv.C;
        }

        /// <summary>
        /// operates on this PDSImage  
        /// </summary>
        public Vector3 GetCameraCenter(string what)
        {
            return GetCameraCenter(this.Image, what);
        }

        /// <summary>
        /// check that CameraModelRefFrame is rover and that CameraModel is CAHV or a derivative thereof  
        /// if not format and throw exception
        /// if so then return the C vector
        /// if checkRangeOrigin is true then also check that RangeOrigin, transformed to rover frame
        /// is approximately equal to the C vector
        /// </summary>
        public static Vector3 CheckCameraCenter(PDSParser parser, Image img, string what, bool checkRangeOrigin = true)
        {
            CheckCameraFrame(parser, what, PDSParser.ReferenceCoordinateFrame.RoverNav);
            Vector3 cameraCenter = GetCameraCenter(img, what);
            if (checkRangeOrigin && parser.DerivedImageType == RoverProductType.Range)
            {
                Matrix xform = GetDataToRoverFrameTransform(parser);
                Vector3 rangeOrigin = Vector3.Transform(parser.RangeOrigin, xform);
                if (!Vector3.AlmostEqual(rangeOrigin, cameraCenter, 0.1))
                {
                    throw new NotImplementedException(what + " requires range maps projected from camera location");
                }
            }
            return cameraCenter;
        }

        /// <summary>
        /// creates parser from img metadata  
        /// </summary>
        public static Vector3 CheckCameraCenter(Image img, string what, bool checkRangeOrigin = true)
        {
            return CheckCameraCenter(new PDSParser((PDSMetadata)img.Metadata), img, what, checkRangeOrigin);
        }

        /// <summary>
        /// operates on this PDS image  
        /// </summary>
        public Vector3 CheckCameraCenter(string what, bool checkRangeOrigin = true)
        {
            return CheckCameraCenter(Parser, this.Image, what, checkRangeOrigin);
        }

        /// <summary>
        /// accepts a range or XYZ map in any coordinate frame and returns an XYZ map in rover frame
        /// also sets mask of return image to be union of input mask, if any
        /// plus invalid values according to image header metadata
        /// returns null if no valid points
        ///
        /// NOTE: it is subtly incorrect to call this method with a range map
        /// because stereo correlation often uses 2D disparity which means the recovered surface point for a pixel
        /// may not actually lie on the ray through that pixel
        /// </summary>
        public Image ConvertPoints()
        {
            switch (Parser.DerivedImageType)
            {
                case RoverProductType.Range: return ConvertRNG();
                case RoverProductType.Points: return ConvertXYZ();
                default: throw new ArgumentException("cannot convert " + Parser.DerivedImageType + " image to XYR");
            }
        }

        /// <summary>
        /// convert an XYZ map to rover frame
        /// also sets mask of return image to be union of input mask, if any
        /// plus invalid values according to image header metadata
        /// returns null if no valid points
        /// </summary>
        public Image ConvertXYZ()
        {
            CheckType(RoverProductType.Points, "ConvertXYZ");
            Matrix xform = GetDataToRoverFrameTransform(Parser);
            Image src = this.Image;
            Image ret = new Image(3, src.Width, src.Height);
            AddMaskForMissingConstant(ret);
            bool hasMissingConstant = Parser.HasMissingConstant;
            bool anyValid = false;
            for (int row = 0; row < src.Height; row++)
            {
                for (int col = 0; col < src.Width; col++)
                {
                    if (!src.IsValid(row, col)) //respect input image mask if it has one
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!hasMissingConstant || ret.IsValid(row, col))
                    {
                        var p = new Vector3(src[0, row, col], src[1, row, col], src[2, row, col]);
                        ret.SetBandValues(row, col, Vector3.Transform(p, xform).ToFloatArray());
                        anyValid = true;
                    }
                    //else AddMaskForMissingConstant() already masked ret[row, col]
                }
            }
            return anyValid ? ret : null;
        }

        /// <summary>
        /// convert a range image into an XYZ map in rover frame similar to the XYR products
        /// also sets mask of return image to be union of input mask, if any
        /// plus invalid values according to image header metadata
        /// returns null if no valid points
        ///
        /// this API is not like the others in that it can get by even if src does not actually have PDS metadata
        ///
        /// NOTE: this method is subtly incorrect and should be avoided
        /// because stereo correlation often uses 2D disparity which means the recovered surface point for a pixel
        /// may not actually lie on the ray through that pixel
        /// </summary>
        public static Image ConvertRNG(Image src, PDSParser parser = null)
        {
            Image ret = new Image(3, src.Width, src.Height);
            bool hasMissingConstant = false;
            if (src.Metadata.GetType() == typeof(PDSMetadata))
            {
                parser = parser ?? new PDSParser((PDSMetadata)src.Metadata);
                hasMissingConstant = parser.HasMissingConstant;
                CheckType(parser, RoverProductType.Range, "ConvertRNG");
                CheckCameraCenter(parser, src, "ConvertRNG");
                AddMaskForMissingConstant(ret, src, parser);
            }
            bool anyValid = false;
            for (int row = 0; row < src.Height; row++)
            {
                for (int col = 0; col < src.Width; col++)
                {
                    if (!src.IsValid(row, col)) //respect input image mask if it has one
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!hasMissingConstant || ret.IsValid(row, col))
                    {
                        Vector3 p = src.CameraModel.Unproject(new Vector2(col, row), src[0, row, col]);
                        ret.SetBandValues(row, col, p.ToFloatArray());
                        anyValid = true;
                    }
                    //else AddMaskForMissingConstant() already masked ret[row, col]
                }
            }
            return anyValid ? ret : null;
        }

        /// <summary>
        /// operates on this PDSImage  
        /// </summary>
        public Image ConvertRNG()
        {
            return ConvertRNG(this.Image, Parser);
        }

        /// <summary>
        /// until mission products giving useful error estimates are available
        /// this code generates a confidence that is inversely proportional to range
        /// </summary>
        public Image GenerateConfidence()
        {
            switch (Parser.DerivedImageType)
            {
                case RoverProductType.Range: return GenerateConfidenceFromRNG();
                case RoverProductType.Points: return GenerateConfidenceFromXYZ();
                default: throw new NotImplementedException("synthetic confidence requires range or XYZ map"); ;
            }
        }

        /// <summary>
        /// naive confidence: the farther away the point is from the camera the lower the confidence
        /// </summary>
        public Image GenerateConfidenceFromRNG()
        {
            CheckType(RoverProductType.Range, "GenerateConfidenceFromRNG");
            Image src = this.Image;
            Image ret = new Image(1, src.Width, src.Height);
            AddMaskForMissingConstant(ret);
            bool hasMissingConstant = Parser.HasMissingConstant;
            for (int row = 0; row < src.Height; row++)
            {
                for (int col = 0; col < src.Width; col++)
                {
                    if (!src.IsValid(row, col) || //respect input image mask if it has one
                        src[0, row, col] <= 0.0f) //non-positive range values are invalid
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!hasMissingConstant || ret.IsValid(row, col))
                    {
                        ret[0, row, col] = (float)DistanceToConfidence(src[0, row, col]);
                    }
                    //else AddMaskForMissingConstant() already masked ret[row, col]
                }
            }

            return ret;
        }

        /// <summary>
        /// naive confidence: the farther away the point is from the camera the lower the confidence
        /// </summary>
        public Image GenerateConfidenceFromXYZ()
        {
            CheckType(RoverProductType.Points, "GenerateConfidenceFromXYZ");
            Vector3 c = CheckCameraCenter("GenerateConfidenceFromXYZ", false);
            Matrix xform = GetDataToRoverFrameTransform(Parser);
            Image src = this.Image;
            Image ret = new Image(1, src.Width, src.Height);
            AddMaskForMissingConstant(ret);
            bool hasMissingConstant = Parser.HasMissingConstant;
            for (int row = 0; row < src.Height; row++)
            {
                for (int col = 0; col < src.Width; col++)
                {
                    if (!src.IsValid(row, col)) //respect input image mask if it has one
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!hasMissingConstant || ret.IsValid(row, col))
                    {
                        var p = new Vector3(src[0, row, col], src[1, row, col], src[2, row, col]);
                        double d = Vector3.Distance(Vector3.Transform(p, xform), c);
                        ret[0, row, col] = (float)DistanceToConfidence(d);
                    }
                    //else AddMaskForMissingConstant() already masked ret[row, col]
                }
            }
            return ret;
        }

        public double DistanceToConfidence(double distance)
        {
            distance = Math.Max(Math.Min(distance, farLimit), nearLimit);
            return 1.0 / distance;
        }

        /// <summary>
        /// generate per-point scale values for FSSR
        /// </summary>
        public Image GenerateScale()
        {
            switch (Parser.DerivedImageType)
            {
                case RoverProductType.Range: return GenerateScaleFromRNG();
                case RoverProductType.Points: return GenerateScaleFromXYZ();
                default: throw new NotImplementedException("synthetic confidence requires range or XYZ map"); ;
            }
        }

        public Image GenerateScaleFromRNG()
        {
            CheckType(RoverProductType.Range, "GenerateScaleFromRNG");
            Image src = this.Image;
            Image ret = new Image(1, src.Width, src.Height);
            AddMaskForMissingConstant(ret);
            bool hasMissingConstant = Parser.HasMissingConstant;
            for (int row = 0; row < src.Height; row++)
            {
                for (int col = 0; col < src.Width; col++)
                {
                    if (!src.IsValid(row, col) || //respect input image mask if it has one
                        src[0, row, col] <= 0.0f) //non-positive range values are invalid
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!hasMissingConstant || ret.IsValid(row, col))
                    {
                        ret[0, row, col] = (float)DistanceToScale(row, col, src[0, row, col]);
                    }
                    //else AddMaskForMissingConstant() already masked ret[row, col]
                }
            }

            return ret;
        }

        public Image GenerateScaleFromXYZ()
        {
            CheckType(RoverProductType.Points, "GenerateScaleFromXYZ");
            Vector3 c = CheckCameraCenter("GenerateScaleFromXYZ", false);
            Matrix xform = GetDataToRoverFrameTransform(Parser);
            Image src = this.Image;
            Image ret = new Image(1, src.Width, src.Height);
            AddMaskForMissingConstant(ret);
            bool hasMissingConstant = Parser.HasMissingConstant;
            for (int row = 0; row < src.Height; row++)
            {
                for (int col = 0; col < src.Width; col++)
                {
                    if (!src.IsValid(row, col)) //respect input image mask if it has one
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!hasMissingConstant || ret.IsValid(row, col))
                    {
                        var p = new Vector3(src[0, row, col], src[1, row, col], src[2, row, col]);
                        double d = Vector3.Distance(Vector3.Transform(p, xform), c);
                        ret[0, row, col] = (float)DistanceToScale(row, col, d);
                    }
                    //else AddMaskForMissingConstant() already masked ret[row, col]
                }
            }
            return ret;
        }

        public double DistanceToScale(int row, int col, double distance)
        {
            double def = 0.1;
            if (row < 0 || col < 0 || row >= Image.Height || col >= Image.Width ||
                Image.Height == 0 || Image.Width == 0 || Image.CameraModel == null)
            {
                return FSSR.DEF_ENLARGE_PIXEL_SCALE * def;
            }
            distance = Math.Max(Math.Min(distance, farLimit), nearLimit);
            int oRow = row > 0 ? row - 1 : row + 1;
            int oCol = col > 0 ? col - 1 : col + 1;
            try
            {
                return FSSR.DEF_ENLARGE_PIXEL_SCALE *
                    Vector3.Distance(Image.CameraModel.Unproject(new Vector2(col, row), distance),
                                     Image.CameraModel.Unproject(new Vector2(oCol, oRow), distance));
            }
            catch (CameraModelException)
            {
                return FSSR.DEF_ENLARGE_PIXEL_SCALE * def;
            }
        }

        /// <summary>
        /// gnenerate normals in rover frame consistent to the UVW mission product
        ///
        /// filters out normals pointing away from camera or fewer than minValid8Neighbors valid neighbors
        ///
        /// if a scale map is also provided the returned normals are scaled
        /// Poisson reconstruction can use the magnitude of the normal to indicate confidence
        /// FSSR reconstruction can use the magnitude of the normal to indicate scale
        ///
        /// also sets mask of return image to be union of input mask, if any
        /// plus invalid values according to image header metadata
        ///
        /// if the points image is supplied then it is used to compute cross product normals using local 4-neighbors
        /// and those are used to further filter the normals
        /// if a source normal points opposite to a cross product normal then the latter is used instead
        ///
        /// returns null if there were no valid normals
        /// </summary>
        public Image ConvertNormals(Image scale = null, Image points = null, int minValid8Neighbors = 8)
        {
            CheckType(RoverProductType.Normals, "ConvertNormals");
            CheckCameraFrame("ConvertNormals");
            Matrix xform = GetDataToRoverFrameTransform(Parser);
            bool nonIdentityXform = !xform.Equals(Matrix.Identity);
            Image src = this.Image;
            Image ret = new Image(src);
            ret.CreateMask();
            bool hasMissingConstant = Parser.HasMissingConstant;
            float[] missing = GetMissingBandValues();
            bool anyValid = false;
            bool isValid(int r, int c)
            {
                return src.IsValid(r, c) &&
                    (!hasMissingConstant || !src.BandValuesEqual(r, c, missing)) &&
                    (scale == null || scale.IsValid(r, c));
            }
            var cns = new List<Vector3>(4);
            Vector3? crossNormal(int r, int c)
            {
                int radius = CROSS_NORMAL_RADIUS;
                if (points == null || !points.IsValid(r, c))
                {
                    return null;
                }
                Vector3 p = new Vector3(points[0, r, c], points[1, r, c], points[2, r, c]);
                Vector3? tu = null;
                int up = r - radius;
                if (up >= 0 && points.IsValid(up, c))
                {
                    tu = new Vector3(points[0, up, c], points[1, up, c], points[2, up, c]) - p;
                }
                Vector3? td = null;
                int down = r + radius;
                if (down < points.Height && points.IsValid(down, c))
                {
                    td = new Vector3(points[0, down, c], points[1, down, c], points[2, down, c]) - p;
                }
                Vector3? tl = null;
                int left = c - radius;
                if (left >= 0 && points.IsValid(r, left))
                {
                    tl = new Vector3(points[0, r, left], points[1, r, left], points[2, r, left]) - p;
                }
                Vector3? tr = null;
                int right = c + radius;
                if (right < points.Width && points.IsValid(r, right))
                {
                    tr = new Vector3(points[0, r, right], points[1, r, right], points[2, r, right]) - p;
                }
                cns.Clear();
                if (tu.HasValue && tr.HasValue)
                {
                    cns.Add(Vector3.Cross(tr.Value, tu.Value));
                }
                if (tr.HasValue && td.HasValue)
                {
                    cns.Add(Vector3.Cross(td.Value, tr.Value));
                }
                if (td.HasValue && tl.HasValue)
                {
                    cns.Add(Vector3.Cross(tl.Value, td.Value));
                }
                if (tl.HasValue && tu.HasValue)
                {
                    cns.Add(Vector3.Cross(tu.Value, tl.Value));
                }
                if (cns.Count < 2)
                {
                    return null;
                }
                return Vector3.Normalize(cns.Aggregate(new Vector3(0, 0, 0), (sum, n) => (sum + n)));
            }
            for (int row = 0; row < src.Height; row++)
            {
                for (int col = 0; col < src.Width; col++)
                {
                    if (!isValid(row, col))
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else
                    {
                        int up = Math.Max(0, row - 1);
                        int down = Math.Min(row + 1, src.Height - 1);
                        int left = Math.Max(0, col - 1);
                        int right = Math.Min(col + 1, src.Width - 1);
                        int valid8Neighbors =
                            (isValid(up, left) ? 1 : 0) +
                            (isValid(up, col) ? 1 : 0) +
                            (isValid(up, right) ? 1 : 0) +
                            (isValid(row, left) ? 1 : 0) +
                            (isValid(row, right) ? 1 : 0) +
                            (isValid(down, left) ? 1 : 0) +
                            (isValid(down, col) ? 1 : 0) +
                            (isValid(down, right) ? 1 : 0);
                        if (valid8Neighbors < minValid8Neighbors)
                        {
                            ret.SetMaskValue(row, col, true);
                        }
                        else
                        {
                            var n = new Vector3(src[0, row, col], src[1, row, col], src[2, row, col]);
                            n.Normalize();
                            var fromCam = src.CameraModel.Unproject(new Vector2(col, row)).Direction;
                            var cn = crossNormal(row, col);
                            if (cn.HasValue && Vector3.Dot(cn.Value, fromCam) < 0 && Vector3.Dot(cn.Value, n) < 0)
                            {
                                n = cn.Value;
                            }
                            if (nonIdentityXform)
                            {
                                n = Vector3.TransformNormal(n, xform);
                            }
                            if (scale != null)
                            {
                                n *= scale[0, row, col];
                            }
                            bool valid = false;
                            try
                            {
                                valid = Vector3.Dot(n, fromCam) < 0;
                            }
                            catch (CameraModelException) { }
                            if (valid)
                            {
                                ret.SetBandValues(row, col, n.ToFloatArray());
                                anyValid = true;
                            }
                            else
                            {
                                ret.SetMaskValue(row, col, true);
                            }
                        }
                    }
                }
            }
            return anyValid ? ret : null;
        }
    }
}
