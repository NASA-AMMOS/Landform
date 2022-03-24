using System;
using System.Text;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MathNet.Numerics.LinearAlgebra;
using Microsoft.Xna.Framework;
using JPLOPS.Geometry;
using JPLOPS.Pipeline.AlignmentServer;

namespace FrameCacheTest
{
    [TestClass]
    public class FrameCacheTest
    {
        [TestMethod]
        public void FrameCacheGetRelativeTransformTest()
        {
            var fc = new FrameCache(null, null);

            Frame makeFrame(string name, string parent, UncertainRigidTransform ut)
            {
                var f = new Frame() { Name = name, ParentName = parent };
                fc.Add(f);
                var t = new FrameTransform() {
                    Name = FrameTransform.MakeName(name, TransformSource.Prior),
                    FrameName = name, 
                    Source = TransformSource.Prior,
                    Transform = ut
                };
                f.Transforms.Add(t.Source);
                fc.Add(t);
                return f;
            }

            double degSqr = Math.Pow(Math.PI / 180, 2);
            var cov = CreateMatrix.Diagonal<double>(new double[]
                    { 0.25, 0.25, 0.25, 0.5 * degSqr, 0.5 * degSqr, 1.0 * degSqr });

            var r = makeFrame("root", null, new UncertainRigidTransform());
            var a = makeFrame("a", "root", new UncertainRigidTransform(Matrix.CreateTranslation(1, 1, 1), cov));
            var b = makeFrame("b", "a", new UncertainRigidTransform(Matrix.CreateTranslation(2, 2, 2), cov));
            var c = makeFrame("c", "root", new UncertainRigidTransform(Matrix.CreateTranslation(3, 3, 3), cov));
            var d = makeFrame("d", "c", new UncertainRigidTransform(Matrix.CreateTranslation(4, 4, 4), cov));
            var e = makeFrame("e", "c", new UncertainRigidTransform(Matrix.CreateTranslation(4, 4, 4), cov));

            double translationTol = 0.01, varianceTol = 0.05;

            void check(UncertainRigidTransform ut, Vector3 translation, Vector3 variance)
            {
                ut.Mean.Decompose(out Vector3 s, out Quaternion q, out Vector3 t);
                var cm = ut.Distribution.Covariance;
                var tv = new Vector3(cm[0, 0], cm[1, 1], cm[2, 2]);
                Assert.AreEqual(Vector3.Distance(translation, t), 0, translationTol);
                Assert.AreEqual(Vector3.Distance(variance, tv), 0, varianceTol);
                
            }

            check(fc.GetRelativeTransform(b, r), new Vector3(3, 3, 3), new Vector3(0.5, 0.5, 0.5));
            check(fc.GetRelativeTransform(d, r), new Vector3(7, 7, 7), new Vector3(0.5, 0.5, 0.5));

            check(fc.GetRelativeTransform(d, b), new Vector3(4, 4, 4), new Vector3(1, 1, 1));

            //LCA functionality should find c as common ancestor of e and d
            //so that the uncertainty of the transform from e to d is less than
            //the uncertainty of the transform from e to root and then back to d
            check(fc.GetRelativeTransform(e, d), new Vector3(0, 0, 0), new Vector3(0.5, 0.5, 0.5));
            var e2r = fc.GetRelativeTransform(e, null);
            var r2d = fc.GetRelativeTransform(null, d);
            check(e2r * r2d, new Vector3(0, 0, 0), new Vector3(1, 1, 1));
        }
    }
}
