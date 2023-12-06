using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Microsoft.Xna.Framework;
using JPLOPS.Geometry;

namespace GeometryTest
{
    [TestClass()]
    public class ProcrustesTests
    {

        [TestMethod()]
        public void ScaleTest()
        {
            Vector3[] fixedPts =
            {
                new Vector3(-1,2,3),
                new Vector3(6,-5,4),
                new Vector3(7,9,-8)
            };

            double appliedScale = 12.0;
            Matrix finalTransform = Matrix.CreateScale(appliedScale);

            Vector3[] movingPts = new Vector3[fixedPts.Length];
            for (int idx = 0; idx < fixedPts.Length; idx++)
            {
                movingPts[idx] = Vector3.Transform(fixedPts[idx], finalTransform);
            }

            double rmsResidual =
                Procrustes.Calculate(movingPts, fixedPts, 
                                     out Vector3 translation, out Quaternion rotation, out double scale,
                                     calcTranslation: false, calcRotation: false, calcScale: true);

            Assert.IsTrue(Math.Abs(appliedScale * scale - 1) < 0.0001);
            Assert.AreEqual(Vector3.Zero, translation);
            Assert.AreEqual(Quaternion.Identity, rotation);
        }

        [TestMethod()]
        public void RotationTest()
        {
            Vector3[] fixedPts =
            {
                new Vector3(-1,2,3),
                new Vector3(6,-5,4),
                new Vector3(7,9,-8)
            };

            double yaw = Math.PI / 2.0;
            Quaternion appliedRotation = Quaternion.CreateFromAxisAngle(new Vector3(0, 0, 1), yaw);
            Matrix finalTransform = Matrix.CreateFromQuaternion(appliedRotation); 

            Vector3[] movingPts = new Vector3[fixedPts.Length];
            for (int idx = 0; idx < fixedPts.Length; idx++)
            {
                movingPts[idx] = Vector3.Transform(fixedPts[idx], finalTransform);
            }

            double rmsResidual =
                Procrustes.Calculate(movingPts, fixedPts,
                                     out Vector3 translation, out Quaternion rotation, out double scale,
                                     calcTranslation: false, calcRotation: true, calcScale: false);

            
            Assert.AreEqual(1.0, scale);
            Assert.AreEqual(Vector3.Zero, translation);
            Assert.IsTrue(Math.Abs((Quaternion.Identity - appliedRotation * rotation).Length()) < 0.001);
        }


        [TestMethod()]
        public void TranslationTest()
        {
            Vector3[] fixedPts =
            {
                new Vector3(-1,2,3),
                new Vector3(6,-5,4),
                new Vector3(7,9,-8)
            };

            Vector3 appliedTranslation = new Vector3(17, -14, 39);
            Matrix finalTransform = Matrix.CreateTranslation(appliedTranslation);

            Vector3[] movingPts = new Vector3[fixedPts.Length];
            for (int idx = 0; idx < fixedPts.Length; idx++)
            {
                movingPts[idx] = Vector3.Transform(fixedPts[idx], finalTransform);
            }

            double rmsResidual =
                Procrustes.Calculate(movingPts, fixedPts,
                                     out Vector3 translation, out Quaternion rotation, out double scale,
                                     calcTranslation: true, calcRotation: false, calcScale: false);

            Assert.AreEqual(1.0, scale);
            Assert.IsTrue(Math.Abs((appliedTranslation + translation).Length()) < 0.001);
            Assert.AreEqual(Quaternion.Identity, rotation);
        }

        [TestMethod()]
        public void CalculateTest()
        {
            Vector3[] fixedPts =
            {
                new Vector3(-1,2,3),
                new Vector3(6,-5,4),
                new Vector3(7,9,-8)
            };

            double appliedScale = 12.0;
            Vector3 appliedTranslation = new Vector3(17, -14, 39);
            
            double pitch = Math.PI / 6.0;
            Quaternion appliedRotation = Quaternion.CreateFromAxisAngle(new Vector3(0, 1, 0), pitch);
           
            Matrix matScale = Matrix.CreateScale(appliedScale);
            Matrix matRotation = Matrix.CreateFromQuaternion(appliedRotation);
            Matrix matTranslation = Matrix.CreateTranslation(appliedTranslation);
            Matrix finalTransform = matScale * matRotation * matTranslation;

            Vector3[] movingPts = new Vector3[fixedPts.Length];
            for (int idx = 0; idx < fixedPts.Length; idx++)
            {
                movingPts[idx] = Vector3.Transform(fixedPts[idx], finalTransform);
            }

            double rmsResidual =
                Procrustes.Calculate(movingPts, fixedPts,
                                     out Vector3 translation, out Quaternion rotation, out double scale,
                                     calcTranslation: true, calcRotation: true, calcScale: true);

            Assert.IsTrue(Math.Abs(appliedScale * scale - 1) < 0.0001);
            Assert.IsTrue(Math.Abs((Quaternion.Identity - appliedRotation * rotation).Length()) < 0.001);
            Assert.IsTrue(Math.Abs((appliedTranslation - Vector3.Transform(-translation, appliedRotation) * appliedScale).Length()) < 0.001);
        }
    }
}
