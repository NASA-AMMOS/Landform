using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using OPS.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PipelineTest
{
    [TestClass]
    public class LazyMatrixTest
    {
        [TestMethod]
        public void ChangeParent()
        {
            SceneGraph graph = new SceneGraph();
            var n1 = new SceneNode("n1", graph.Root.Transform);
            n1.Transform.Translation = new Vector3(1, 0, 0);
            var n2 = new SceneNode("n2", n1.Transform);
            n2.Transform.Translation = new Vector3(-1, 0, 0);

            Assert.AreEqual(Matrix.Identity, n2.Transform.LocalToWorld);

            n2.Transform.Parent = graph.Root.Transform;
            Assert.AreEqual(Matrix.Identity, n2.Transform.LocalToWorld);
        }

        [TestMethod]
        public void MoveDeepParent()
        {
            SceneGraph graph = new SceneGraph();
            Random r = new Random();
            int depth = 10;

            SceneNode chosenOne = graph.Root;
            for (int i = 0; i < depth; i++)
            {
                List<SceneNode> children = new List<SceneNode>();
                int numChildren = r.Next(1, 6);
                for (int j = 0; j < numChildren; j++)
                {
                    SceneNode newChild = new SceneNode();
                    newChild.Transform.Parent = chosenOne.Transform;
                    newChild.Transform.Translation = new Vector3(
                        r.NextDouble() * 2 - 1,
                        r.NextDouble() * 2 - 1,
                        r.NextDouble() * 2 - 1);

                    Vector3 axis = new Vector3(
                        r.NextDouble(),
                        r.NextDouble(),
                        r.NextDouble());
                    axis.Normalize();
                    double angle = r.NextDouble() * Math.PI;

                    newChild.Transform.Rotation = Quaternion.CreateFromAxisAngle(axis, angle);
                    children.Add(newChild);
                }
                chosenOne = children[r.Next(0, children.Count)];
            }

            Matrix oldTransform = chosenOne.Transform.LocalToWorld;
            graph.Root.Transform.Translation = new Vector3(0, 0, 1);
            Matrix newTransform = chosenOne.Transform.LocalToWorld;
            Matrix expected = oldTransform * Matrix.CreateTranslation(0, 0, 1);
            Assert.IsTrue(newTransform.AlmostEqual(expected));
        }
    }
}
