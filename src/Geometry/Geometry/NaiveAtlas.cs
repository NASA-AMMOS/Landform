using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Sharp3DBinPacking;
using JPLOPS.MathExtensions;

namespace JPLOPS.Geometry
{
    public class NaiveAtlas
    {
        /// <summary>
        /// Helper struct used when performing the bin-pack during naive atlasing
        /// </summary>
        private struct NaiveAtlasPackingTag
        {
            public Vector2 uv0, uv1, uv2;
            public int P0, P1, P2;
        }

        private const int CUBOID_MINIMUM_DIMENSION = 1;
        private const int NAIVE_PACKING_BIN_DEPTH = 1;
        private const int NAIVE_PACKING_BIN_WIEGHT = 0;

        /// <summary>
        /// Packs the atlas based on bounding boxes of each individual triangle in the mesh.
        /// The resultant atlas / mesh will essentially be a set of completely separate faces/triangles -
        /// no two triangles will share a single vertex.
        /// That is, a 'single' vertex will be repeated as many times as there are faces that contain it -
        /// each 'repeated' instance will have the same position, but different UV coordinate.
        /// This version of the API has paramaters like UVAtlasNET.UVATlas.Atlas().
        /// </summary>
        /// <param name="mesh">mesh to be atlased</param>
        /// <param name="outU">u-coordinate of the vertices of the atlased mesh</param>
        /// <param name="outV">v-coordinate of the vertices of the atlased mesh</param>
        /// <param name="outIndices">indices whose sets of 3 entries correspond to the faces of the atlased mesh</param>
        /// <param name="outVertexRemap">array mapping vertices of output mesh to vertices of input mesh</param>
        /// <returns>true on success</returns>
        public static bool Compute(Mesh mesh, out float[] outU, out float[] outV, out int[] outIndices,
                                   out int[] outVertexRemap)
        {
            // go through all faces in mesh, make a tag for each
            var tags = new NaiveAtlasPackingTag[mesh.Faces.Count];
            double smallestUnscaledBoundingBoxDimension = Double.MaxValue;
            for (int iFace = 0; iFace < mesh.Faces.Count; iFace++)
            {
                var face = mesh.Faces[iFace];
                var tag = new NaiveAtlasPackingTag();

                // determine side lengths
                var p0Pos = mesh.Vertices[face.P0].Position;
                var p1Pos = mesh.Vertices[face.P1].Position;
                var p2Pos = mesh.Vertices[face.P2].Position;
                var p0p1LengthSq = (p1Pos - p0Pos).LengthSquared();
                var p1p2LengthSq = (p2Pos - p1Pos).LengthSquared();
                var p2p0LengthSq = (p0Pos - p2Pos).LengthSquared();

                // find longest side, align that side along horizontal edge of cuboid
                if (p0p1LengthSq >= p1p2LengthSq)
                {
                    if (p0p1LengthSq >= p2p0LengthSq)
                    {
                        // p0-p1 is longest side, so p0 will become the 'origin'
                        tag.P0 = face.P0;
                        tag.P1 = face.P1;
                        tag.P2 = face.P2;
                    }
                    else
                    {
                        // p2-p0 is longest side, so p1 will become the 'origin'
                        tag.P0 = face.P2;
                        tag.P1 = face.P0;
                        tag.P2 = face.P1;
                    }
                }
                else if (p1p2LengthSq >= p2p0LengthSq)
                {
                    // p1-p2 is longest side, so p1 will become the 'origin'
                    tag.P0 = face.P1;
                    tag.P1 = face.P2;
                    tag.P2 = face.P0;
                }
                else
                {
                    // p2-p0 is longest side, so p2 will become the 'origin'
                    tag.P0 = face.P2;
                    tag.P1 = face.P0;
                    tag.P2 = face.P1;
                }

                // remapping so p0-p1 is long side
                p0Pos = mesh.Vertices[tag.P0].Position;
                p1Pos = mesh.Vertices[tag.P1].Position;
                p2Pos = mesh.Vertices[tag.P2].Position;

                // construct 2d, within-cuboid coordinates for each point
                var p0p1 = p1Pos - p0Pos;
                var p0p2 = p2Pos - p0Pos;
                var p0p1Length = p0p1.Length();
                var p0p2Length = p0p2.Length();
                tag.uv0 = Vector2.Zero;
                tag.uv1 = new Vector2(p0p1Length, 0d);

                // guaranteed positive since angle between p0-p1 and p0-p2 will be acute
                var cosTheta = Vector3.Dot(p0p1, p0p2) / (p0p1Length * p0p2Length);

                // guaranteed positive since that's the range of acos
                // (which, with cosTheta being positive, implies 0 <= theta <= pi/2)
                var theta = Math.Acos(cosTheta);

                tag.uv2 = new Vector2(p0p2Length * cosTheta, p0p2Length * Math.Sin(theta));

                if (tag.uv2.Y == 0)
                {
                    outU = null;
                    outV = null;
                    outIndices = null;
                    outVertexRemap = null;
                    return false; //can happen with long skinny triangles
                }
                // store the tag
                tags[iFace] = tag;

                /*
                =======================================
                |                        uv2          |
                |                       /   \         |
                |                    /       \        |
                |                 /           \       | <-- BOUNDING BOX (CUBOID)
                |              /               \      |
                |           /                   \     |
                |        /                       \    |
                |     /                           \   |
                |  /                               \  |
                |uv0-------------------------------uv1|
                =======================================
                p0 -> uv0
                p1 -> uv1
                p2 -> uv2
                */

                // check if need to update smallest bounding box dim (for scaling the cuboids to an appropriate size)
                if (tag.uv2.Y < smallestUnscaledBoundingBoxDimension)
                {
                    // height of cuboid is always smaller than width since long triangle side is on bottom
                    smallestUnscaledBoundingBoxDimension = tag.uv2.Y;
                }
            }

            // determine upscaling value of triange so smallest fits snugly into integer-dimensioned cuboid
            // prevents the case e.g. where a triangle with base 0.01 and height 0.005 is put into a 1x1 cuboid, leaving
            // bunches of empty/wasted space
            double scaleFactor = 1d;
            if (smallestUnscaledBoundingBoxDimension < CUBOID_MINIMUM_DIMENSION)
            {
                scaleFactor = CUBOID_MINIMUM_DIMENSION / smallestUnscaledBoundingBoxDimension;
            }

            // create a cuboid for each tag/face, scaling so all faces fit well in their cuboids
            Cuboid[] inCubes = new Cuboid[tags.Length];
            ulong totalArea = 0;
            for (int iCube = 0; iCube < inCubes.Length; iCube++)
            {
                // scale up dimensions stored in tag
                var tag = tags[iCube];
                tag.uv0 = tag.uv0 * scaleFactor;
                tag.uv1 = tag.uv1 * scaleFactor;
                tag.uv2 = tag.uv2 * scaleFactor;

                // create a cuboid for this face
                var cubeWidth = (ulong)Math.Ceiling(tag.uv1.X);
                var cubeHeight = (ulong)Math.Ceiling(tag.uv2.Y);
                inCubes[iCube] = new Cuboid(cubeWidth, cubeHeight, NAIVE_PACKING_BIN_DEPTH, NAIVE_PACKING_BIN_WIEGHT,
                                            tag);

                // update area total
                totalArea += cubeWidth * cubeHeight;
            }

            // binDimension should really be two variables - one for width and one for height.
            // But since we're forcing a square atlas we're just using one.
            // When calling 'pack', binDimension refers to the dimensions (width and height) of the bin into which 
            // all of the cuboids are being packed.
            // The bigger the bin, the easier it is to fit all the cuboids, but the less tightly packed they'll be.
            // A tightly packed bin leads to a tightly packed atlas and a better texture, so we start with 
            // the smallest possible bin size (the smallest power of two large enough to fit all of the cuboids without
            // any space to spare, i.e. 'totalArea') and go from there.
            int binDimension = MathE.CeilPowerOf2(Math.Sqrt(totalArea));

            // pack cubiods - adjust bin width and height until all cuboids pack into one bin
            BinPackResult packed = null;
            int numBins = 0;
            while (numBins != 1)
            {
                bool allowRotation = false;
                var parameter = new BinPackParameter(binDimension, binDimension,
                                                     NAIVE_PACKING_BIN_DEPTH, NAIVE_PACKING_BIN_WIEGHT,
                                                     allowRotation, inCubes);
                var binPacker = BinPacker.GetDefault(BinPackerVerifyOption.BestOnly);
                packed = binPacker.Pack(parameter);
                numBins = packed.BestResult.Count;
                if (numBins != 1)
                {
                    binDimension *= 2;
                }
            }

            // should be one cube per face
            var packedCubes = packed.BestResult.First();
            if (packedCubes.Count != mesh.Faces.Count)
            {
                throw new Exception("Number of packed cubes does not match number of mesh faces");
            }

            var newVertexCount = packedCubes.Count * 3;
            outVertexRemap = new int[newVertexCount];
            outIndices = new int[newVertexCount];
            outU = new float[newVertexCount];
            outV = new float[newVertexCount];

            // populate UVs based on cuboid pos and cuboid relative triangle points (uv0, uv1, & uv2)
            for (int iCube = 0; iCube < packedCubes.Count; ++iCube)
            {
                var cube = packedCubes[iCube];
                var tag = (NaiveAtlasPackingTag)cube.Tag;

                // PO
                var vertexIndex = iCube * 3;
                outVertexRemap[vertexIndex] = tag.P0;
                outIndices[vertexIndex] = vertexIndex;
                outU[vertexIndex] = ((float)cube.X + (float)tag.uv0.X) / binDimension;
                outV[vertexIndex] = ((float)cube.Y + (float)tag.uv0.Y) / binDimension;

                // P1
                vertexIndex = iCube * 3 + 1;
                outVertexRemap[vertexIndex] = tag.P1;
                outIndices[vertexIndex] = vertexIndex;
                outU[vertexIndex] = ((float)cube.X + (float)tag.uv1.X) / binDimension;
                outV[vertexIndex] = ((float)cube.Y + (float)tag.uv1.Y) / binDimension;

                // P2
                vertexIndex = iCube * 3 + 2;
                outVertexRemap[vertexIndex] = tag.P2;
                outIndices[vertexIndex] = vertexIndex;
                outU[vertexIndex] = ((float)cube.X + (float)tag.uv2.X) / binDimension;
                outV[vertexIndex] = ((float)cube.Y + (float)tag.uv2.Y) / binDimension;

            }

            return true;
        }
    }
}
