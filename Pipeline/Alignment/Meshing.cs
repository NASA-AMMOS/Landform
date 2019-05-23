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
using OPS.MathExtensions;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class Meshing
    {
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

        public static void CheckCameraFrame(Image img, string what)
        {
            CheckCameraFrame(new PDSParser((PDSMetadata)img.Metadata), what);
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
        /// returns null if no valid points
        ///
        /// NOTE: it is subtly incorrect to call this method with a range map
        /// because stereo correlation often uses 2D disparity which means the recovered surface point for a pixel
        /// may not actually lie on the ray through that pixel
        /// https://github.jpl.nasa.gov/OnSight/Landform/issues/471
        /// </summary>
        public static Image ConvertPoints(Image img)
        {
            if (img == null)
            {
                return null;
            }
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
        /// returns null if no valid points
        /// </summary>
        public static Image ConvertXYZ(Image img, PDSParser parser = null)
        {
            if (img == null)
            {
                return null;
            }
            parser = parser ?? new PDSParser((PDSMetadata)img.Metadata);
            CheckType(parser, RoverProductType.XYZ, "ConvertXYZ");
            Matrix xform = RoverCoordinateSystem.GetTransformToRoverFrame(parser);
            Image ret = new Image(3, img.Width, img.Height);
            AddMaskForMissingConstant(ret, img, parser);
            bool hasMissingConstant = parser.HasMissingConstant;
            bool anyValid = false;
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    if (img.IsInvalid(row, col)) //respect input image mask if it has one
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!hasMissingConstant || ret.IsValid(row, col))
                    {
                        var p = new Vector3(img[0, row, col], img[1, row, col], img[2, row, col]);
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
        /// NOTE: this method is subtly incorrect and should be avoided
        /// because stereo correlation often uses 2D disparity which means the recovered surface point for a pixel
        /// may not actually lie on the ray through that pixel
        /// https://github.jpl.nasa.gov/OnSight/Landform/issues/471
        /// </summary>
        public static Image ConvertRNG(Image img, PDSParser parser)
        {
            if (img == null)
            {
                return null;
            }
            Image ret = new Image(3, img.Width, img.Height);

            bool hasMissingConstant = false;
            if (img.Metadata.GetType() == typeof(PDSMetadata))
            {
                parser = parser ?? new PDSParser((PDSMetadata)img.Metadata);
                hasMissingConstant = parser.HasMissingConstant;
                CheckType(parser, RoverProductType.Range, "ConvertRange");
                CheckCameraCenter(parser, img, "ConvertRNG");
                AddMaskForMissingConstant(ret, img, parser);
            }
            bool anyValid = false;
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    if (img.IsInvalid(row, col)) //respect input image mask if it has one
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!hasMissingConstant || ret.IsValid(row, col))
                    {
                        Vector3 p = img.CameraModel.Unproject(new Vector2(col, row), img[0, row, col]);
                        ret.SetBandValues(row, col, p.ToFloatArray());
                        anyValid = true;
                    }
                    //else AddMaskForMissingConstant() already masked ret[row, col]
                }
            }
            return anyValid ? ret : null;
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
            bool hasMissingConstant = parser.HasMissingConstant;
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    if (img.IsInvalid(row, col) || //respect input image mask if it has one
                        img[0, row, col] <= 0.0f) //non-positive range values are invalid
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!hasMissingConstant || ret.IsValid(row, col))
                    {
                        ret[0, row, col] = 1 / img[0, row, col];
                    }
                    //else AddMaskForMissingConstant() already masked ret[row, col]
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
            Vector3 c = CheckCameraCenter(img, "GenerateConfidenceFromXYZ", false);
            Matrix xform = RoverCoordinateSystem.GetTransformToRoverFrame(parser);
            Image ret = new Image(1, img.Width, img.Height);
            AddMaskForMissingConstant(ret, img, parser);
            bool hasMissingConstant = parser.HasMissingConstant;
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    if (img.IsInvalid(row, col)) //respect input image mask if it has one
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!hasMissingConstant || ret.IsValid(row, col))
                    {
                        var p = new Vector3(img[0, row, col], img[1, row, col], img[2, row, col]);
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
        /// as the poisson reconstruction tool uses the magnitude of the normal to indicate confidence
        ///
        /// also sets mask of return image to be union of input mask, if any
        /// plus invalid values according to image header metadata
        ///   
        /// returns null if there were no valid normals
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
            bool hasMissingConstant = parser.HasMissingConstant;
            bool anyValid = false;
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
                    else if (!hasMissingConstant || ret.IsValid(row, col))
                    {
                        anyValid = true;
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
                    //else AddMaskForMissingConstant() already masked ret[row, col]
                }
            }
            return anyValid ? ret : null;
        }

        /// <summary>
        /// get transform from a specific rover frame to the corresponding, observation, sitedrive or root frame
        /// also works to get a transform from a rover frame to any other rover frame
        /// result is null if the transform could not be resolved
        /// if usePriors = true then only prior transform sources will be used
        /// if onlyAligned = true then the result will be null unless at least one transform in the chain is not a prior
        /// </summary>
        public static UncertainRigidTransform GetTransform(string fromFrame, string toFrame, FrameCache frameCache,
                                                           bool usePriors = false, bool onlyAligned = false)
        {
            if (toFrame == "rover" || toFrame == PDSParser.ReferenceCoordinateFrame.RoverNav.ToString())
            {
                return new UncertainRigidTransform(); //identity, no uncertainty
            }

            Frame obsFrame = frameCache.GetFrame(fromFrame);
            Frame sdFrame = frameCache.GetFrame(obsFrame.ParentName);

            if (toFrame == "sitedrive" || toFrame == PDSParser.ReferenceCoordinateFrame.LocalLevel.ToString())
            {
                var obsToSD = usePriors ? frameCache.GetBestPrior(obsFrame) : frameCache.GetBestTransform(obsFrame);
                return (obsToSD == null || (onlyAligned && obsToSD.IsPrior())) ? null : obsToSD.Transform;
            }

            if (toFrame == "site" || toFrame == PDSParser.ReferenceCoordinateFrame.Site.ToString())
            {
                throw new NotImplementedException("transform to site frame not implemented");
            }

            if (toFrame == "root" || string.IsNullOrEmpty(toFrame))
            {
                var obsToSD = usePriors ? frameCache.GetBestPrior(obsFrame) : frameCache.GetBestTransform(obsFrame);
                var sdToRoot = usePriors ? frameCache.GetBestPrior(sdFrame) : frameCache.GetBestTransform(sdFrame);
                if (obsToSD == null || sdToRoot == null || (onlyAligned && obsToSD.IsPrior() && sdToRoot.IsPrior()))
                {
                    return null;
                }
                else
                {
                    return obsToSD.Transform * sdToRoot.Transform;
                }
            }
            else
            {
                var srcToRoot = GetTransform(fromFrame, "root", frameCache, usePriors, onlyAligned);
                var dstToRoot = GetTransform(toFrame, "root", frameCache, usePriors, onlyAligned);
                return (srcToRoot == null || dstToRoot == null) ? null : srcToRoot.TimesInverse(dstToRoot);
            }
        }
    }
}
