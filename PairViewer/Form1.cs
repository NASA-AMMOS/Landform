using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline;
using OPS.Alignment;
using Microsoft.Xna.Framework;
using MathNet.Numerics.LinearAlgebra;
using System.IO;
using OPS.Plumbing;
using Newtonsoft.Json;
using log4net;

namespace PairViewer
{
    public partial class PairViewerForm : Form
    {
        public ImageView ModelView;
        public ImageView DataView;
        public MSLLocations Locations;
        public PipelineCore Pipeline;

        public AlignmentScene Scene;
        Dictionary<string, SceneNode> siteDriveToNode;

        UncertainRigidTransform dataToModel;
        public UncertainRigidTransform DataToModel
        {
            get
            {
                if (dataToModel == null)
                {
                    if (ModelView.Image == null || DataView.Image == null)
                    {
                        return null;
                    }

                    var dataNode = Scene.ImageToNode[DataView.ImageRef];
                    var modelNode = Scene.ImageToNode[ModelView.ImageRef];
                    dataToModel = dataNode.GetOrAddComponent<NodeUncertainTransform>().To(modelNode);
                }

                return dataToModel;
            }
        }

        UncertainRigidTransform modelToData;
        public UncertainRigidTransform ModelToData
        {
            get
            {
                if (modelToData == null)
                {
                    if (ModelView.Image == null || DataView.Image == null)
                    {
                        return null;
                    }

                    var dataNode = Scene.ImageToNode[DataView.ImageRef];
                    var modelNode = Scene.ImageToNode[ModelView.ImageRef];
                    modelToData = modelNode.GetOrAddComponent<NodeUncertainTransform>().To(dataNode);
                }

                return modelToData;
            }
        }

        public PairViewerForm()
        {
            InitializeComponent();
            Pipeline = new CloudPipeline(new PipelineCoreOptions(), enableS3: false, enableDynamo: false);

            Locations = MSLLocations.LoadFromUrl();
            Scene = new AlignmentScene();
            ModelView = new ImageView(ModelPictureBox);
            DataView = new ImageView(DataPictureBox);
            siteDriveToNode = new Dictionary<string, SceneNode>();
        }

        private void exitMenu_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }
        
        private void LoadInto(ImageView view)
        {
            var dialog = new OpenFileDialog();
            dialog.Filter = "All supported (*.img, *.fits)|*.img;*.fit|)pds images (*.img)|*.img|FITS images (*.fit)|*.fit|All files (*.*)|*.*";
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                var imgRef = new DiskImageRef(dialog.FileName);
                var res = Pipeline.LoadImage(imgRef);
                string stem = Path.GetFileNameWithoutExtension(dialog.FileName);
                if (res.Metadata is PDSMetadata)
                {
                    var md = res.Metadata as PDSMetadata;
                    PDSParser parsed = new PDSParser(md);

                    // Create nodes
                    {
                        if (!siteDriveToNode.ContainsKey(parsed.SiteDrive))
                        {
                            var sdNode = siteDriveToNode[parsed.SiteDrive] = new SceneNode("SD " + parsed.SiteDrive, Scene.Root.Transform);
                            var locData = Locations.Location(new SiteDrive(parsed.Site, parsed.Drive));
                            sdNode.Transform.LocalToWorld = Matrix.CreateTranslation(locData.Position);

                            // Made up covariance values, more or less signifying SD of 0.5m translation and 0.5deg rotation (1.0 for Z)
                            var uncertainty = sdNode.AddComponent<NodeUncertainTransform>();
                            double fifthDegSqr = Math.Pow(0.2 * Math.PI / 180, 2);
                            double tenCM = 0.1;
                            double posCov = tenCM * tenCM;
                            uncertainty.Covariance = CreateMatrix.Diagonal(new double[] { posCov, posCov, posCov, fifthDegSqr, fifthDegSqr, fifthDegSqr });
                        }
                    }
                    // Add image node
                    var imgNode = new SceneNode(stem, siteDriveToNode[parsed.SiteDrive].Transform);
                    imgNode.Transform.Rotation = parsed.RoverOriginRotation;
                    imgNode.Transform.Translation = Vector3.Zero;
                    {
                        var uncertainty = imgNode.AddComponent<NodeUncertainTransform>();
                        double fifthDegSqr = Math.Pow(0.2 * Math.PI / 180, 2);
                        double fiveMM = 0.005;
                        double posCov = fiveMM * fiveMM;
                        uncertainty.Covariance = CreateMatrix.Diagonal(new double[] { posCov, posCov, posCov, fifthDegSqr, fifthDegSqr, fifthDegSqr });
                    }

                    imgNode.AddComponent<NodeImageReference>().Reference = imgRef;
                    Scene.ImageToNode[imgRef] = imgNode;
                }

                view.Image = res;
                view.ImageRef = imgRef;
                dataToModel = modelToData = null;
            }
        }

        Vector2 modelPt, dataPt;


        void DrawEpiLine(ImageView view, Graphics g, EpipolarLineFinder.Result epi)
        {
            bool pureY = Math.Abs(epi.Direction.X) < 1e-6;
            if (pureY)
            {

            }
            else
            {
                double slope = epi.Direction.Y / epi.Direction.X;

                // p = (0, y)
                // p . epi.Perp = epi.PerpD
                // y * epi.Perp.y = epi.PerpD
                // y = epi.PerpD / epi.Perp.Y
                double yIntercept = epi.PerpendicularDistance / epi.PerpendicularDirection.Y;

                int x1 = view.Overlay.Width - 1;
                var pt0 = new PointF(0, (float)yIntercept);
                var pt1 = new PointF(x1, (float)(x1 * slope + yIntercept));
                g.DrawLine(new Pen(Color.FromArgb(128, 255, 255, 255), 2.0f), pt0, pt1);
            }
        }

        private void DrawEpiLines(ImageView model, ImageView data, UncertainRigidTransform dataToModel, Vector2 dataPoint, Vector2 modelPoint, bool clear)
        {
            EpipolarLineFinder finder = new EpipolarLineFinder(Pipeline);

            if (clear)
            {
                using (var g = Graphics.FromImage(data.Overlay))
                {
                    g.Clear(Color.Transparent);
                }
                using (var g = Graphics.FromImage(model.Overlay))
                {
                    g.Clear(Color.Transparent);
                }
            }

            var modelFeat = (modelPoint != Vector2.Zero) ? new ImageFeature(modelPoint, null) : null;
            var dataFeat = (dataPoint != Vector2.Zero) ? new ImageFeature(dataPoint, null) : null;

            var epi = finder.Find(model.ImageRef, data.ImageRef, dataToModel.Mean, dataFeat, modelFeat);
            using (var g = Graphics.FromImage(model.Overlay))
            {
                DrawEpiLine(model, g, epi);

                if (modelFeat != null)
                {
                    // calculate dist of epipolar error
                    var errDist = dataToModel.UnscentedTransform(d2m =>
                    {
                        var epiPrime = finder.Find(model.ImageRef, data.ImageRef, d2m, dataFeat, modelFeat);
                        if (!epiPrime.Success)
                        {
                            return CreateVector.DenseOfArray(new[] { 100.0 });
                        }
                        return CreateVector.DenseOfArray(new[] { epiPrime.SignedDistance(modelPoint) });
                    });
                    var mean = errDist.Mean[0];
                    var std = Math.Sqrt(errDist.Covariance[0, 0]);


                    toolStripStatusLabel1.Text = "MEAN: " + mean.ToString() + " STD: " + std.ToString();
                    g.DrawLine(new Pen(Color.Red, 2.0f), (float)modelPoint.X, (float)modelPoint.Y, (float)modelPoint.X, (float)modelPoint.Y + (float)mean);
                    g.DrawLine(new Pen(Color.Green, 2.0f), (float)modelPoint.X, (float)modelPoint.Y, (float)modelPoint.X + (float)std, (float)modelPoint.Y);
                }
            }
            statusStrip1.Refresh();

            using (var g = Graphics.FromImage(data.Overlay))
            {
                g.DrawEllipse(new Pen(Color.Blue, 2.0f), (float)dataPoint.X - 1.0f, (float)dataPoint.Y - 1.0f, 2.0f, 2.0f);
            }

            ModelView.Box.Refresh();
            DataView.Box.Refresh();
        }

        private void openModelMenu_Click(object sender, EventArgs e)
        {
            LoadInto(ModelView);
        }

        private void openDataMenu_Click(object sender, EventArgs e)
        {
            LoadInto(DataView);
        }

        private void ModelPictureBox_Click(object sender, EventArgs e)
        {
            MouseEventArgs me = (MouseEventArgs)e;
            var imgPos = ModelView.ControlToPixel(me.Location);
            modelPt = imgPos;
            DrawEpiLines(DataView, ModelView, ModelToData, modelPt, dataPt, true);
        }

        private void DataPictureBox_Click(object sender, EventArgs e)
        {
            MouseEventArgs me = (MouseEventArgs)e;
            var imgPos = DataView.ControlToPixel(me.Location);
            dataPt = imgPos;
            DrawEpiLines(ModelView, DataView, DataToModel, dataPt, modelPt, true);
        }
    }
}
