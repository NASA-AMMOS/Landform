using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using JPLOPS.Geometry;
using System;
using System.Collections.Generic;

namespace PipelineTest
{
    [TestClass]
    public class LazyMatrixTest
    {
        [TestMethod]
        public void ChangeParent()
        {
            SceneNode root = new SceneNode("Root");
            var n1 = new SceneNode("n1", root.Transform);
            n1.Transform.Translation = new Vector3(1, 0, 0);
            var n2 = new SceneNode("n2", n1.Transform);
            n2.Transform.Translation = new Vector3(-1, 0, 0);

            Assert.AreEqual(Matrix.Identity, n2.Transform.LocalToWorld);

            n2.Transform.Parent = root.Transform;
            Assert.AreEqual(Matrix.Identity, n2.Transform.LocalToWorld);
        }

        [TestMethod]
        public void MoveDeepParent()
        {
            SceneNode root = new SceneNode("Root");
            int depth = 10;

            SceneNode chosenOne = root;
            for (int i = 0; i < depth; i++)
            {
                List<SceneNode> children = new List<SceneNode>();
                int numChildren = rand.Next(1, 6);
                for (int j = 0; j < numChildren; j++)
                {
                    SceneNode newChild = new SceneNode();
                    newChild.Transform.SetParent(chosenOne.Transform, false);

                    Vector3 t, s;
                    Quaternion r;
                    RandomTransform(out t, out r, out s);

                    newChild.Transform.Translation = t;
                    newChild.Transform.Rotation = r;
                    newChild.Transform.Scale = new Vector3(1, 1, 1);

                    children.Add(newChild);
                }
                chosenOne = children[rand.Next(0, children.Count)];
            }

            Matrix oldTransform = chosenOne.Transform.LocalToWorld;
            root.Transform.Translation = new Vector3(0, 0, 1);
            Matrix newTransform = chosenOne.Transform.LocalToWorld;
            Matrix expected = oldTransform * Matrix.CreateTranslation(0, 0, 1);
            Assert.IsTrue(newTransform.AlmostEqual(expected));
        }

        [TestMethod]
        public void TRSOrder()
        {
            SceneNode node = new SceneNode();
            Vector3 t, s;
            Quaternion r;

            for (int i = 0; i < 1000; i++)
            {
                RandomTransform(out t, out r, out s);

                node.Transform.Translation = t;
                node.Transform.Rotation = r;
                node.Transform.Scale = s;

                Assert.IsTrue(node.Transform.LocalToWorld.AlmostEqual(
                    Matrix.CreateFromQuaternion(r)
                    * Matrix.CreateScale(s)
                    * Matrix.CreateTranslation(t)));
            }
        }

        [TestMethod]
        public void StochasticTreeShuffle()
        {
            SceneNode root = new SceneNode("Root");

            List<SceneNode> leafNodes = new List<SceneNode> { root };
            List<SceneNode> allNodes = new List<SceneNode>() { root };

            // populate tree
            int targetNodes = 1000;
            while (allNodes.Count < targetNodes)
            {
                // choose a random leaf node
                int idx = rand.Next(leafNodes.Count);
                SceneNode n = leafNodes[idx];
                leafNodes.RemoveAt(idx);

                // add between one and nine children
                int numChildren = Math.Min(rand.Next(1, 10), targetNodes - allNodes.Count);
                for (int i = 0; i < numChildren; i++)
                {
                    SceneNode newChild = new SceneNode();
                    newChild.Transform.SetParent(n.Transform, false);

                    Vector3 t, s;
                    Quaternion r;
                    RandomTransform(out t, out r, out s);

                    newChild.Transform.Translation = t;
                    newChild.Transform.Rotation = r;
                    newChild.Transform.Scale = s;
                    leafNodes.Add(newChild);
                    allNodes.Add(newChild);
                }
            }

            // change a random node's transform and validate the state
            // of the entire tree
            for (int i = 0; i < 2000; i++)
            {
                int idx = rand.Next(allNodes.Count);
                SceneNode n = allNodes[idx];

                Vector3 t, s;
                Quaternion r;
                RandomTransform(out t, out r, out s);

                n.Transform.Translation = t;
                n.Transform.Rotation = r;
                n.Transform.Scale = s;

                Assert.IsTrue(TransformStateValid(root.Transform, recursive: true));
            }
        }

        static Random rand = new Random(7);
        static Vector3 RandWithinUnitSphere()
        {
            Vector3 res;
            do
            {
                res = new Vector3(
                rand.NextDouble() * 2 - 1,
                rand.NextDouble() * 2 - 1,
                rand.NextDouble() * 2 - 1);
            } while (res.LengthSquared() > 1);
            return res;
        }
        static void RandomTransform(out Vector3 translation, out Quaternion rotation, out Vector3 scale)
        {
            translation = RandWithinUnitSphere();

            Vector3 axis = Vector3.Normalize(RandWithinUnitSphere());
            double angle = rand.NextDouble() * Math.PI;

            rotation = Quaternion.CreateFromAxisAngle(axis, angle);

            double scaleFactor = rand.NextDouble() * (10 - 0.1) + 0.1;
            scale = new Vector3(scaleFactor, scaleFactor, scaleFactor);
        }

        static bool TransformStateValid(NodeTransform t, bool recursive=true)
        {
            if (!(t.LocalToWorld * t.WorldToLocal).AlmostEqual(Matrix.Identity)) return false;
            if (t.Parent == null)
            {
                if (t.Matrix != t.LocalToWorld) return false;
            }
            else if (!t.LocalToWorld.AlmostEqual(t.Matrix * t.Parent.LocalToWorld)) return false;
            if (recursive)
            {
                foreach(var child in t.Children)
                {
                    if (!TransformStateValid(child, true)) return false;
                }
            }
            return true;
        }
    }
}
