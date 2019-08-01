using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Diagnostics;
using log4net;
using Microsoft.Xna.Framework;
using OPS.MathExtensions;
using OPS.Util;
using OPS.Imaging;

namespace OPS.Pipeline
{
    public class PDSImage
    {
        public readonly Image Image;
        public readonly PDSMetadata Metadata;
        public readonly PDSParser Parser;

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

        /// <summary>
        /// mask all pixels in dst corresponding to pixels in src which match the PDS MissingConstant, if any
        /// if the parser is not supplied it will be created from src
        /// </summary>
        public static void AddMaskForMissingConstant(Image dst, Image src, PDSParser parser = null)
        {
            parser = parser ?? new PDSParser((PDSMetadata)src.Metadata);
            if (parser.HasMissingConstant)
            {
                float[] missing = parser.MissingConstant.Select(x => (float)x).ToArray();
                
                //ROASTT: single float missing constant for 3 channel navcam
                if(missing.Count() == 1 && src.Bands > 1)
                {
                    missing = Enumerable.Repeat<float>(missing.First(), src.Bands).ToArray();
                }

                dst.UnionMask(src, missing);
            }
            else
            {
                dst.CreateMask(false);
            }
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
                throw new ArgumentException(what + " requires " + type + " product");
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
                throw new NotImplementedException(what + " requires camera model in " + frame + " frame");
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
        /// https://github.jpl.nasa.gov/OnSight/Landform/issues/471
        /// </summary>
        public Image ConvertPoints()
        {
            switch (Parser.DerivedImageType)
            {
                case RoverProductType.Range: return ConvertRNG();
                case RoverProductType.XYZ: return ConvertXYZ();
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
            CheckType(RoverProductType.XYZ, "ConvertXYZ");
            Matrix xform = RoverCoordinateSystem.GetTransformToRoverFrame(Parser);
            Image src = this.Image;
            Image ret = new Image(3, src.Width, src.Height);
            AddMaskForMissingConstant(ret);
            bool hasMissingConstant = Parser.HasMissingConstant;
            bool anyValid = false;
            for (int row = 0; row < src.Height; row++)
            {
                for (int col = 0; col < src.Width; col++)
                {
                    if (src.IsInvalid(row, col)) //respect input image mask if it has one
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
        /// https://github.jpl.nasa.gov/OnSight/Landform/issues/471
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
                    if (src.IsInvalid(row, col)) //respect input image mask if it has one
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
                case RoverProductType.XYZ: return GenerateConfidenceFromXYZ();
                default: throw new NotImplementedException("synthetic confidence requires range or XYZ map"); ;
            }
        }

        /// <summary>
        /// naive confidence: farther away the point is from the camera the lower the confidence
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
                    if (src.IsInvalid(row, col) || //respect input image mask if it has one
                        src[0, row, col] <= 0.0f) //non-positive range values are invalid
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!hasMissingConstant || ret.IsValid(row, col))
                    {
                        ret[0, row, col] = 1 / src[0, row, col];
                    }
                    //else AddMaskForMissingConstant() already masked ret[row, col]
                }
            }

            return ret;
        }

        /// <summary>
        /// naive confidence: farther away the point is from the camera the lower the confidence
        /// </summary>
        public Image GenerateConfidenceFromXYZ()
        {
            CheckType(RoverProductType.XYZ, "GenerateConfidenceFromXYZ");
            Vector3 c = CheckCameraCenter("GenerateConfidenceFromXYZ", false);
            Matrix xform = RoverCoordinateSystem.GetTransformToRoverFrame(Parser);
            Image src = this.Image;
            Image ret = new Image(1, src.Width, src.Height);
            AddMaskForMissingConstant(ret);
            bool hasMissingConstant = Parser.HasMissingConstant;
            for (int row = 0; row < src.Height; row++)
            {
                for (int col = 0; col < src.Width; col++)
                {
                    if (src.IsInvalid(row, col)) //respect input image mask if it has one
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!hasMissingConstant || ret.IsValid(row, col))
                    {
                        var p = new Vector3(src[0, row, col], src[1, row, col], src[2, row, col]);
                        ret[0, row, col] = 1 / (float)Vector3.Distance(Vector3.Transform(p, xform), c);
                    }
                    //else AddMaskForMissingConstant() already masked ret[row, col]
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
        /// as Poisson reconstruction uses the magnitude of the normal to indicate confidence
        ///
        /// also sets mask of return image to be union of input mask, if any
        /// plus invalid values according to image header metadata
        ///   
        /// returns null if there were no valid normals
        /// </summary>
        public Image ConvertNormals(Image confidence = null)
        {
            CheckType(RoverProductType.NormalMap, "ConvertNormals");
            Matrix xform = RoverCoordinateSystem.GetTransformToRoverFrame(Parser);
            bool nonIdentityXform = !xform.Equals(Matrix.Identity);
            Image src = this.Image;
            Image ret = new Image(src);
            AddMaskForMissingConstant(ret);
            bool hasMissingConstant = Parser.HasMissingConstant;
            bool anyValid = false;
            for (int row = 0; row < src.Height; row++)
            {
                for (int col = 0; col < src.Width; col++)
                {
                    int up = Math.Max(0, row - 1);
                    int down = Math.Min(row + 1, src.Height - 1);
                    int left = Math.Max(0, col - 1);
                    int right = Math.Min(col + 1, src.Width - 1);
                    if (src.IsInvalid(row, col) || //respect input image mask if it has one
                        (confidence != null && confidence.IsInvalid(row, col)) ||
                        src.IsInvalid(up, left) || src.IsInvalid(up, col) || src.IsInvalid(up, right) ||
                        src.IsInvalid(row, left) || src.IsInvalid(row, right) ||
                        src.IsInvalid(down, left) || src.IsInvalid(down, col) || src.IsInvalid(down, right))
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!hasMissingConstant || ret.IsValid(row, col))
                    {
                        anyValid = true;
                        if (nonIdentityXform)
                        {
                            var n = new Vector3(src[0, row, col], src[1, row, col], src[2, row, col]);
                            ret.SetBandValues(row, col, Vector3.TransformNormal(n, xform).ToFloatArray());
                        }
                        if (confidence != null)
                        {
                            ret[0, row, col] *= confidence[0, row, col];
                        }
                    }
                    //else AddMaskForMissingConstant() already masked ret[row, col]
                }
            }
            return anyValid ? ret : null;
        }
    }
}
