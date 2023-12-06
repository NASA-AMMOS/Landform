using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Emgu.CV;
using Emgu.CV.Util;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Features2D;
using JPLOPS.Util;
using JPLOPS.Imaging;
using JPLOPS.Imaging.Emgu;
using JPLOPS.Geometry;

namespace JPLOPS.ImageFeatures
{
    public class FeatureMatch
    {
        public int DataIndex;
        public int ModelIndex;

        public double DescriptorDistance;

        public override int GetHashCode()
        {
            return HashCombiner.Combine(DataIndex, ModelIndex);
        }

        public override bool Equals(object obj)
        {
            if (obj == null || !(obj is FeatureMatch))
            {
                return false;
            }
            return this.DataIndex == ((FeatureMatch)obj).DataIndex && this.ModelIndex == ((FeatureMatch)obj).ModelIndex;
        }

        public static Image DrawMatches(Image modelImg, Image dataImg, ImageFeature[] modelFeatures,
                                        ImageFeature[] dataFeatures, KeyValuePair<int, int>[] dataToModel,
                                        string modelName = null, string dataName = null, bool stretch = true)
        {
            var modelFeaturesForDataFeature = new Dictionary<int, HashSet<int>>();
            foreach (var pair in dataToModel)
            {
                int dataFeatureIndex = pair.Key;
                int modelFeatureIndex = pair.Value;
                if (!modelFeaturesForDataFeature.ContainsKey(dataFeatureIndex))
                {
                    modelFeaturesForDataFeature[dataFeatureIndex] = new HashSet<int>();
                }
                modelFeaturesForDataFeature[dataFeatureIndex].Add(modelFeatureIndex);
            }
            var matches = new VectorOfVectorOfDMatch();
            foreach (var pair in modelFeaturesForDataFeature)
            {
                int dataFeatureIndex = pair.Key;
                var matchesForDataFeature = new List<MDMatch>();
                foreach (int modelFeatureIndex in pair.Value)
                {
                    matchesForDataFeature.Add(new MDMatch() {
                            TrainIdx = modelFeatureIndex,
                            QueryIdx = dataFeatureIndex
                        });
                }
                matches.Push(new VectorOfDMatch(matchesForDataFeature.ToArray()));
            }
            var modelKeypoints = new VectorOfKeyPoint(modelFeatures.Cast<SIFTFeature>().CastToMKeyPoint().ToArray());
            var dataKeypoints = new VectorOfKeyPoint(dataFeatures.Cast<SIFTFeature>().CastToMKeyPoint().ToArray());
            var lineColor = new MCvScalar(0, 0, 255); //RGB
            var pointColor = new MCvScalar(255, 255, 0); //RGB
            var ret = new Image<Bgr, byte>(modelImg.Width + dataImg.Width, Math.Max(modelImg.Height, dataImg.Height));
            var modelImgEmgu =
                stretch ? (new Image(modelImg)).ApplyStdDevStretch().ToEmguGrayscale() : modelImg.ToEmguGrayscale();
            var dataImgEmgu =
                stretch ? (new Image(dataImg)).ApplyStdDevStretch().ToEmguGrayscale() : dataImg.ToEmguGrayscale();
            //opencv sometimes throws exceptions here, so roll our own replacement
            //Features2DToolbox.DrawMatches(modelImgEmgu, modelKeypoints, dataImgEmgu, dataKeypoints, matches, ret,
            //                              lineColor, pointColor, null,
            //                              Features2DToolbox.KeypointDrawType.DrawRichKeypoints);
            DrawMatches(modelImgEmgu, modelKeypoints, dataImgEmgu, dataKeypoints, matches, ret, lineColor, pointColor,
                        Features2DToolbox.KeypointDrawType.DrawRichKeypoints);
            if (dataName != null)
            {
                ret.Draw("data: " + dataName, new System.Drawing.Point(5, 30),
                         FontFace.HersheySimplex, 1, new Bgr(255, 0, 255), 2);
            }
            if (modelName != null)
            {
                ret.Draw("model: " + modelName, new System.Drawing.Point(dataImg.Width + 5, 30),
                         FontFace.HersheySimplex, 1, new Bgr(255, 0, 255), 2);
            }
            return ret.ToOPSImage();
        }

        //basic replacement for Features2DToolbox.DrawMatches() which sometimes barfs
        //NOTE these functions draw the model image on the right and the data image on the left
        public static void DrawMatches(Image<Gray, byte> modelImage, VectorOfKeyPoint modelKeypoints,
                                       Image<Gray, byte> dataImage, VectorOfKeyPoint dataKeypoints,
                                       VectorOfVectorOfDMatch matches, Image<Bgr, byte> ret,
                                       MCvScalar lineColor, MCvScalar pointColor,
                                       Features2DToolbox.KeypointDrawType flags)
        {
            var pc = new Bgr(pointColor.V0, pointColor.V1, pointColor.V2);
            var lc = new Bgr(lineColor.V0, lineColor.V1, lineColor.V2);

            //don't import System.Drawing because it creates a conflict with "Image"
            ret.ROI = new System.Drawing.Rectangle(0, 0, dataImage.Width, dataImage.Height);
            dataImage.Convert<Bgr, byte>().CopyTo(ret);
            Features2DToolbox.DrawKeypoints(ret, dataKeypoints, ret, pc, flags);

            ret.ROI = new System.Drawing.Rectangle(dataImage.Width, 0, modelImage.Width, modelImage.Height);
            modelImage.Convert<Bgr, byte>().CopyTo(ret);
            Features2DToolbox.DrawKeypoints(ret, modelKeypoints, ret, pc, flags);

            //ret.ROI = new System.Drawing.Rectangle(0, 0, ret.Width, ret.Height); //tried this, doesn't work
            ret.ROI = new System.Drawing.Rectangle(0, 0, modelImage.Width + dataImage.Width,
                                                   Math.Max(modelImage.Height, dataImage.Height));
            var offset = new System.Drawing.SizeF(dataImage.Width, 0);

            for (int i = 0; i < matches.Size; i++)
            {
                for (int j = 0; j < matches[i].Size; j++)
                {
                    var mp = modelKeypoints[matches[i][j].TrainIdx];
                    var dp = dataKeypoints[matches[i][j].QueryIdx];
                    ret.Draw(new CircleF(mp.Point + offset, mp.Size), lc, 2);
                    ret.Draw(new CircleF(dp.Point, dp.Size), lc, 2);
                    ret.Draw(new LineSegment2DF(mp.Point + offset, dp.Point), lc, 1);
                }
            }
        }

        public static Mesh MakeMatchMesh(CameraModel modelCam, CameraModel dataCam,
                                         ImageFeature[] modelFeat, ImageFeature[] dataFeat,
                                         Matrix modelToRoot, Matrix dataToRoot,
                                         KeyValuePair<int, int>[] dataToModel)
        {
            var modelPts = new List<Vector3>();
            var dataPts = new List<Vector3>();
            foreach (var match in dataToModel)
            {
                var df = dataFeat[match.Key];
                var mf = modelFeat[match.Value];
                if (df.Range > 0 && mf.Range > 0)
                {
                    modelPts.Add(modelCam.Unproject(mf.Location, mf.Range));
                    dataPts.Add(dataCam.Unproject(df.Location, df.Range));
                }
            }
            return MakeMatchMesh(modelPts.ToArray(), dataPts.ToArray(), modelToRoot, dataToRoot);
        }

        public static Mesh MakeMatchMesh(Vector3[] modelPts, Vector3[] dataPts, Matrix modelToRoot, Matrix dataToRoot)
        {
            var lineColor = new Vector4(0, 0, 1, 0);
            var pointColor = new Vector4(0, 1, 0, 0);
            double pointSize = 0.05; //meters
            double lineSize = 0.02; //meters
            var pointMesh = BoundingBoxExtensions.MakeCube(pointSize).ToMesh(pointColor);
            var lineMesh = BoundingBoxExtensions.MakeCube(lineSize).ToMesh(lineColor);
            var meshes = new List<Mesh>();
            for (int i = 0; i < modelPts.Length; i++)
            {
                var mp = Vector3.Transform(modelPts[i], modelToRoot);
                var dp = Vector3.Transform(dataPts[i], dataToRoot);
                meshes.Add(pointMesh.Transformed(Matrix.CreateTranslation(mp)));
                meshes.Add(pointMesh.Transformed(Matrix.CreateTranslation(dp)));
                var lineMat = BoundingBoxExtensions.StretchCubeAlongLineSegment(mp, dp, lineSize);
                meshes.Add(lineMesh.Transformed(lineMat));
            }
            var ret = new Mesh(hasNormals: true, hasColors: true);
            ret.Vertices = new List<Vertex>(meshes.Sum(mesh => mesh.Vertices.Count));
            ret.Faces = new List<Face>(meshes.Sum(mesh => mesh.Faces.Count));
            foreach (var mesh in meshes)
            {
                int nv = ret.Vertices.Count;
                foreach (var face in mesh.Faces)
                {
                    var tmp = face;
                    tmp.P0 += nv;
                    tmp.P1 += nv;
                    tmp.P2 += nv;
                    ret.Faces.Add(tmp);
                }
                foreach (var vertex in mesh.Vertices)
                {
                    ret.Vertices.Add(vertex);
                }
            }
            return ret;
        }

        public static Mesh MakeMatchMesh(Vector3[] modelPts, Vector3[] dataPts)
        {
            return MakeMatchMesh(modelPts, dataPts, Matrix.Identity, Matrix.Identity);
        }
    }
}
