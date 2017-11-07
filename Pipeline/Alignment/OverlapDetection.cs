using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2;

using OPS.Imaging;
using OPS.Cloud;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline
{
    /// <summary>
    /// Utility for detecting images that might overlap. 
    /// TODO: replace with Charley's code (also b/c this might not work at all tbh)
    /// </summary>
    class OverlapDetection
    {
        private StorageHelper storage;
        private DynamoDBContext context;

        public OverlapDetection(DynamoDBContext context)
        {
            storage = new StorageHelper();
            this.context = context;
        }

        public bool ProjectiveFrustumOverlap(RoverObservation obs0, RoverObservation obs1)
        {
            //snagging these from the header but that's not a good way to do it obv
            //TODO hacky
            CameraModel model1 = null; 
            CameraModel model0 = null;
            int width0 =0; int width1=0;
            int height0=0; int height1=0;
            Quaternion roverToLocalLevel0 = Quaternion.Identity ;
            Quaternion roverToLocalLevel1 = Quaternion.Identity;


            //get the camera models the dumb way, by going and getting the file headers again......
            storage.GetStorageStream(obs0.Url, stream =>
            {
                PDSMetadata metadata = new PDSMetadata(stream);
                PDSParser parser = new PDSParser(metadata);
                model0 = metadata.CameraModel;
                width0 = metadata.Width;
                height0 = metadata.Height;
                roverToLocalLevel0 = parser.RoverOriginRotation;
            });
            storage.GetStorageStream(obs1.Url, stream =>
            {
                PDSMetadata metadata = new PDSMetadata(stream);
                PDSParser parser = new PDSParser(metadata);
                model1 = metadata.CameraModel;
                width1 = metadata.Width;
                height1 = metadata.Height;
                roverToLocalLevel1 = parser.RoverOriginRotation;
            });

            //snagged from PDSParser TODO this is a hack to get the other frame transforms and it should probably be a real search through FrameTransformLookup (?)
            //Only works when there's exactly two transforms between observation frame and root, and the intermediate frame is the site drive frame, and it's named as below. YIKE
            string siteDriveFrameName0 = obs0.Site.ToString().PadLeft(5, '0') + obs0.Drive.ToString().PadLeft(5, '0');
            string siteDriveFrameName1 = obs1.Site.ToString().PadLeft(5, '0') + obs1.Drive.ToString().PadLeft(5, '0');

            //getting frames 
            FrameTransform roverToLocal0 = FrameTransform.Find(context, obs0.FrameName, siteDriveFrameName0, obs0.ProjectName).FirstOrDefault();
            FrameTransform roverToLocal1 = FrameTransform.Find(context, obs1.FrameName, siteDriveFrameName1, obs0.ProjectName).FirstOrDefault();

            //there's just no way this is right (modeled off SitedriveTransforms in old pipeline code)
            FrameTransform siteDriveToRoot = FrameTransform.Find(context, siteDriveFrameName0, MSLProject.ROOT_FRAME_NAME, obs0.ProjectName).FirstOrDefault();//currently all the same site drive
            double[,] trans = new double[4, 4];
            trans[0, 0] = 1;
            trans[1, 1] = 1;
            trans[2, 2] = 1;
            trans[3, 3] = 1;
            trans[0, 3] = siteDriveToRoot.Translation.X;
            trans[1, 3] = siteDriveToRoot.Translation.Y;
            trans[2, 3] = siteDriveToRoot.Translation.Z;
            Matrix siteDriveToRootMatrix = new Matrix(trans[0,0],trans[0,1],trans[0,2],trans[0,3],trans[1,0],trans[1,1],trans[1,2],trans[1,3], trans[2, 0], trans[2, 1], trans[2, 2], trans[2, 3], trans[3, 0], trans[3, 1], trans[3, 2], trans[3, 3]);

            Matrix transform0 = Matrix.CreateFromQuaternion(roverToLocal0.Rotation) * siteDriveToRootMatrix;
            Matrix transform1 = Matrix.CreateFromQuaternion(roverToLocal1.Rotation) * siteDriveToRootMatrix;
            Matrix oneToZero = Matrix.Invert(transform1) * transform0;

            

            Vector3[] cornerPoints = new Vector3[] {
                Vector3.Transform(model1.ProjectPoint(new Vector2(0, 0), 0.01), oneToZero),
                Vector3.Transform(model1.ProjectPoint(new Vector2(width1, 0), 0.01), oneToZero),
                Vector3.Transform(model1.ProjectPoint(new Vector2(width1, height1), 0.01), oneToZero),
                Vector3.Transform(model1.ProjectPoint(new Vector2(0, height1), 0.01), oneToZero),
                Vector3.Transform(model1.ProjectPoint(new Vector2(0, 0), 40.0), oneToZero),
                Vector3.Transform(model1.ProjectPoint(new Vector2(width1, 0), 40.0), oneToZero),
                Vector3.Transform(model1.ProjectPoint(new Vector2(width1, height1), 40.0), oneToZero),
                Vector3.Transform(model1.ProjectPoint(new Vector2(0, height1), 40.0), oneToZero)
            };

            double maxZ = double.NegativeInfinity;
            Vector2 minPt = new Vector2(double.PositiveInfinity, double.PositiveInfinity),
                    maxPt = new Vector2(double.NegativeInfinity, double.NegativeInfinity);
            foreach (Vector3 pt in cornerPoints)
            {
                double z;
                Vector2 projected = model0.Backproject(pt, out z);
                if (z > maxZ) maxZ = z;
                if (projected.X < minPt.X) minPt.X = projected.X;
                if (projected.Y < minPt.Y) minPt.Y = projected.Y;
                if (projected.X > maxPt.X) maxPt.X = projected.X;
                if (projected.Y > maxPt.Y) maxPt.Y = projected.Y;
            }
            if (maxZ < 0) return false;
            if (maxPt.X <= 0 || maxPt.Y <= 0 ||
                minPt.X >= width0 || minPt.Y >= height0)
            {
                return false;
            }
            return true;
        }
    }
}
