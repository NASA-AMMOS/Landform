//#define LEGACY_TUNING
//#define PRE_G65_TUNING
using System;
using System.Collections.Generic;
using JPLOPS.Util;

//This is an implementation of composite image stitching roughly based on 
//
//Kazhdan, Surendran, Hoppe.  Distributed Gradient-Domain Processing of Planar and Spherical Images.  ACM Transactions
//On Graphics, Vol 29, No 2, April 2010. http://www.cs.jhu.edu/~misha/MyPapers/ToG10.pdf
//
//The core idea of composite image stitching is to reduce the visibility of seams in an image that is composed of
//multiple sub-images.
//
//Misha Kazhdan provides a reference implementation at http://www.cs.jhu.edu/~misha/Code/DMG and
//https://github.com/mkazhdan/DMG.  DMG stands for for "Distributed Multigrid".  Both the paper and the reference
//implementation actually can perform a variety of imaging operations in a distributed multigrid framework, not just
//composite image stitching but also smoothing, sharpening, and high-low compositing.  The algorithm and implementation
//can process large (gigapixel) images by splitting the original image into bands which are solved in parallel on
//multiple CPUs.  The problem is not trivially parallelizable, so this involves carefully scheduled communication
//between the CPUs.
//
//The DMG framework solves the screened Poisson equation https://en.wikipedia.org/wiki/Screened_Poisson_equation to
//find an image that best fits given constraining (a) value and (b) gradient images.  The composite image stitching
//problem is mapped to this framework by setting the gradient constraint to 0 at the image seams to be smoothed.
//
//DMG appears to be a successor to "Streaming Multigrid" (SMG) https://www.cs.jhu.edu/~misha/Code/SMG.
//
//In Landform we are really interested specifically in composite image stitching, but because some of our original
//implementations were based on calling Misha's DMG.exe, we came to equate the term "DMG" with composite image
//stitching.
//
//Around November 2014, Charley Goddard re-implemented the screened Poisson formulation of composite image stitching in
//this single C# file as LimberDMG.  "Limber" (presumably with the meaning "lithe or supple") is simply a word that
//Charley picked, and "DMG" is really a misnomer here, because this implementation is not distributed (though it is
//multigrid).  It operates on simple monolithic in-core images, though it is parallelized in a few places.
//
//Charley wrote:
//
//   I took a lot of inspiration from Misha's DMG and SMG but diverged a bit, both for ease of implementation and for
//   additional functionality. The b-spline basis functions (among other things) got thrown out in favor of simpler
//   discretized derivatives. I think I more-or-less took the core idea of solving the screened poisson equation, framed
//   it as an affine transformation to allow pixels to be held constant, and worked out the math on paper.
//
//   Section 3 of this paper
//   https://grail.cs.washington.edu/projects/screenedPoissonEq/screenedPoissonEq_files/screenedPoissonEq.pdf has a
//   similar derivation to how I probably got the particular equation
//
//   \nabla^2f - \lambda f = \lambda u - \nabla \cdot g
//
//   Note that it looks like they used a different sign convention. (or maybe I goofed?)
//
//Around June 2018, Andrew Zhang adapted LimberDMG to use the Landform Image class.
namespace JPLOPS.Imaging
{
    /// <summary>
    /// Poisson solver for image stitching
    /// Contains classes PoissonProblem2D and ImageStitchingProblem
    /// </summary>
    public class LimberDMG
    {
#if LEGACY_TUNING
        public const double DEF_RESIDUAL_EPSILON = 1e-5; //1e-5 in JDBlendImageGradients, 1e-3 in LimberDMG
        public const int DEF_NUM_RELAXATION_STEPS = 15;
        public const int DEF_NUM_MULTIGRID_ITERATIONS = 5;
        public const double DEF_LAMBDA = 0.003;
#elif PRE_G65_TUNING
        public const double DEF_RESIDUAL_EPSILON = 0.01;
        public const int DEF_NUM_RELAXATION_STEPS = 500;
        public const int DEF_NUM_MULTIGRID_ITERATIONS = 5;
        public const double DEF_LAMBDA = 0.0001;
#else
        public const double DEF_RESIDUAL_EPSILON = 0.001;
        public const int DEF_NUM_RELAXATION_STEPS = 1000;
        public const int DEF_NUM_MULTIGRID_ITERATIONS = 10;
        public const double DEF_LAMBDA = 0.00001;
#endif

        public const EdgeBehavior DEF_EDGE_BEHAVIOR = EdgeBehavior.Clamp;
        public const ColorConversion DEF_COLOR_CONVERSION = ColorConversion.RGBToLogLAB;

        public enum Flags { NONE = 0, HOLD_CONSTANT = 1, GRADIENT_ONLY = 2, NO_DATA = 4 }

        public enum EdgeBehavior { Clamp, WrapSphere, WrapCylinder, WrapTorus }

        public enum ColorConversion { None, RGBToLAB, RGBToLogLAB };

        /// <summary>
        /// Acceptable error in solving the linear system.
        /// Lower will give better quality results, at the expense of computation time.
        /// </summary>
        private double residualEpsilon;

        /// <summary>
        /// Number of iterations of relaxation to perform between multigrid iterations.
        /// Higher may produce better quality results, at the expense of computation time.
        /// </summary>
        private int numRelaxationSteps;

        /// <summary>
        /// Max number of multigrid iterations.
        /// Higher may produce better quality results, at the expense of computation time.
        /// </summary>
        private int numMultigridIterations;

        /// <summary>
        /// Weighting applied to original pixel values.
        /// Higher values will cause sharper transitions between images but better conform to the inputs.
        /// </summary>
        private double lambda;

        /// <summary>
        /// Boundary behavior.
        /// </summary>
        private EdgeBehavior edgeMode;

        /// <summary>
        /// Color conversion to apply before blending and then unapply after blending
        /// </summary>
        private ColorConversion colorConversion;

        private Action<string> progress;

        public LimberDMG(double residualEpsilon = DEF_RESIDUAL_EPSILON,
                         int numRelaxationSteps = DEF_NUM_RELAXATION_STEPS,
                         int numMultigridIterations = DEF_NUM_MULTIGRID_ITERATIONS,
                         double lambda = DEF_LAMBDA,
                         EdgeBehavior edgeMode = DEF_EDGE_BEHAVIOR,
                         ColorConversion colorConversion = DEF_COLOR_CONVERSION,
                         Action<string> progress = null)
        {
            this.residualEpsilon = residualEpsilon;
            this.numRelaxationSteps = numRelaxationSteps;
            this.numMultigridIterations = numMultigridIterations;
            this.lambda = lambda;
            this.edgeMode = edgeMode;
            this.colorConversion = colorConversion;
            this.progress = progress;
        }

        /// <summary>
        /// A discrete Poisson problem over a regular 2D grid with missing cells.
        /// 
        /// $\nabla^2f - \lambda f = \lambda u - \nabla \cdot g$
        /// 
        /// </summary>
        private class PoissonProblem2D
        {
            public int Width, Height;
            public byte[] Flags;
            protected double[] U;
            protected double[] divG;
            protected double lambda;
            protected EdgeBehavior edgeBehavior;
            internal PixelNeighborFunction getNeighbors;

            double[] k;

            public PoissonProblem2D(int width, int height, double[] U, double[] divG, byte[] flags, double lambda,
                                    EdgeBehavior edgeBehavior)
            {
                this.Width = width;
                this.Height = height;
                this.U = U;
                this.divG = divG;
                this.Flags = flags;
                this.lambda = lambda;
                this.edgeBehavior = edgeBehavior;

                switch (edgeBehavior)
                {
                    case EdgeBehavior.Clamp: getNeighbors = PixelNeighborsClamp; break;
                    case EdgeBehavior.WrapCylinder: getNeighbors = PixelNeighborsCylinder; break;
                    case EdgeBehavior.WrapSphere: getNeighbors = PixelNeighborsSphere; break;
                    case EdgeBehavior.WrapTorus: getNeighbors = PixelNeighborsTorus; break;
                    default: throw new ArgumentException("Invalid edge behavior!");
                }

                if (divG != null)
                {
                    ComputeK();
                }
            }

            internal delegate IEnumerable<int> PixelNeighborFunction(int pixelIdx);

            private IEnumerable<int> PixelNeighborsClamp(int pixelIdx)
            {
                int u = pixelIdx % Width,
                    v = pixelIdx / Width;

                if (u > 0 && (Flags[pixelIdx - 1] & (byte)LimberDMG.Flags.NO_DATA) == 0)
                {
                    yield return pixelIdx - 1;
                }
                if (u < Width - 1 && (Flags[pixelIdx + 1] & (byte)LimberDMG.Flags.NO_DATA) == 0)
                {
                    yield return pixelIdx + 1;
                }
                if (v > 0 && (Flags[pixelIdx - Width] & (byte)LimberDMG.Flags.NO_DATA) == 0)
                {
                    yield return pixelIdx - Width;
                }
                if (v < Height - 1 && (Flags[pixelIdx + Width] & (byte)LimberDMG.Flags.NO_DATA) == 0)
                {
                    yield return pixelIdx + Width;
                }
            }

            private IEnumerable<int> PixelNeighborsCylinder(int pixelIdx)
            {
                int u = pixelIdx % Width,
                    v = pixelIdx / Width;

                // Horizontal neighbors
                int h1 = v * Width + ((u + 1) % Width);
                if ((Flags[h1] & (byte)LimberDMG.Flags.NO_DATA) == 0) yield return h1;
                int h0 = v * Width + ((u + Width - 1) % Width);
                if ((Flags[h0] & (byte)LimberDMG.Flags.NO_DATA) == 0) yield return h0;

                if (v > 0 && (Flags[pixelIdx - Width] & (byte)LimberDMG.Flags.NO_DATA) == 0)
                {
                    yield return pixelIdx - Width;
                }
                if (v < Height - 1 && (Flags[pixelIdx + Width] & (byte)LimberDMG.Flags.NO_DATA) == 0)
                {
                    yield return pixelIdx + Width;
                }
            }

            private IEnumerable<int> PixelNeighborsSphere(int pixelIdx)
            {
                int u = pixelIdx % Width,
                    v = pixelIdx / Width;

                // Horizontal neighbors
                int h1 = v * Width + ((u + 1) % Width);
                if ((Flags[h1] & (byte)LimberDMG.Flags.NO_DATA) == 0) yield return h1;
                int h0 = v * Width + ((u + Width - 1) % Width);
                if ((Flags[h0] & (byte)LimberDMG.Flags.NO_DATA) == 0) yield return h0;

                // Wrap to mirror side of image on vertical edges
                if ((v == 0 || v == Height - 1) &&
                    (Flags[v * Width + (Width - 1 - u)] & (byte)LimberDMG.Flags.NO_DATA) == 0)
                {
                    yield return v * Width + (Width - 1 - u);
                }

                if (v > 0 && (Flags[pixelIdx - Width] & (byte)LimberDMG.Flags.NO_DATA) == 0)
                {
                    yield return pixelIdx - Width;
                }
                if (v < Height - 1 && (Flags[pixelIdx + Width] & (byte)LimberDMG.Flags.NO_DATA) == 0)
                {
                    yield return pixelIdx + Width;
                }
            }

            private IEnumerable<int> PixelNeighborsTorus(int pixelIdx)
            {
                int u = pixelIdx % Width,
                    v = pixelIdx / Width;

                // Horizontal neighbors
                int h1 = v * Width + ((u + 1) % Width);
                if ((Flags[h1] & (byte)LimberDMG.Flags.NO_DATA) == 0) yield return h1;
                int h0 = v * Width + ((u + Width - 1) % Width);
                if ((Flags[h0] & (byte)LimberDMG.Flags.NO_DATA) == 0) yield return h0;

                // Vertical neighbors
                int v1 = ((v + 1) % Height) * Width + u;
                if ((Flags[v1] & (byte)LimberDMG.Flags.NO_DATA) == 0) yield return v1;
                int v0 = ((v + Height - 1) % Height) * Width + u;
                if ((Flags[v0] & (byte)LimberDMG.Flags.NO_DATA) == 0) yield return v0;
            }

            protected void ComputeK()
            {
                if (k == null || k.Length != Width * Height)
                {
                    k = new double[Width * Height];
                }

                int i;
                for (i = 0; i < Width * Height; i++)
                {
                    if ((Flags[i] & (byte)LimberDMG.Flags.NO_DATA) != 0)
                    {
                        k[i] = 0;
                        continue;
                    }
                    double scale = lambda;
                    if ((Flags[i] & (byte)LimberDMG.Flags.GRADIENT_ONLY) != 0) scale = 0;

                    k[i] = scale * U[i] - divG[i];
                }
            }

            /// <summary>
            /// Calculate the laplacian at index $i$ ignoring missing cells.
            /// </summary>
            /// <param name="x">Input vector</param>
            /// <param name="i">Index in input vector</param>
            /// <returns>$\nabla^2f$</returns>
            protected double Laplacian(double[] x, int i)
            {
                if ((Flags[i] & (byte)LimberDMG.Flags.NO_DATA) != 0) return 0;

                int u = i % Width,
                    v = i / Width;

                double delGrad = 0;

                foreach (int neighborIdx in getNeighbors(i))
                {
                    delGrad += x[neighborIdx] - x[i];
                }

                return delGrad;
            }

            /// <summary>
            /// Given our Poisson matrix $A = \nabla^2 - \lambda$, compute $A x = b$.
            /// </summary>
            /// <param name="x">Input vector</param>
            /// <param name="b">Output vector</param>
            protected void MatrixMultiply(double[] x, double[] b, bool useAffine = false, double affineScale = 1.0)
            {
                CoreLimitedParallel.For(0, Width * Height, (i) =>
                {
                    if ((Flags[i] & (byte)LimberDMG.Flags.NO_DATA) != 0)
                    {
                        b[i] = 0;
                        return;
                    }
                    double lhs = lambda * x[i];
                    if ((Flags[i] & (byte)LimberDMG.Flags.GRADIENT_ONLY) != 0) lhs = 0;
                    else if (useAffine && (Flags[i] & (byte)LimberDMG.Flags.HOLD_CONSTANT) != 0)
                    {
                        lhs = lambda * affineScale * U[i];
                    }

                    b[i] = lhs - Laplacian(x, i);
                });
            }

            /// <summary>
            /// Calculate the residual $r = Ax - b$.
            /// </summary>
            /// <param name="x">Input vector</param>
            /// <param name="r">Output vector</param>
            /// <param name="b">If not null, the value $A x$ will be stored here</param>
            protected void CalculateResidual(double[] x, double[] r, double[] b = null)
            {
                if (b == null)
                {
                    b = new double[Width * Height];
                }
                MatrixMultiply(x, b);

                int i;
                for (i = 0; i < Width * Height; i++)
                {
                    if ((Flags[i] & (byte)LimberDMG.Flags.NO_DATA) != 0)
                    {
                        r[i] = 0;
                        continue;
                    }
                    r[i] = k[i] - b[i];
                }
            }

            /// <summary>
            /// Refine a guess vector x using the conjugate gradient method.
            /// </summary>
            /// <param name="x">Guess $x$ for $A x = k$</param>
            /// <param name="maxIters">Maximum number of iterations</param>
            /// <param name="residualEpsilon">Acceptable error for early bailout</param>
            public double OptimizeConjugateGradient(double[] x, int maxIters, double residualEpsilon)
            {
                double[] R = new double[Width * Height];
                double[] P = new double[Width * Height];
                double[] AP = new double[Width * Height];
                double sqrError = 0.0;

                // Calculate initial R, P, sqrError
                CalculateResidual(x, R);
                for (int i = 0; i < Width * Height; i++)
                {
                    sqrError += R[i] * R[i];
                    P[i] = R[i];
                }
#if !LEGACY_TUNING
                sqrError /= (Width * Height);
#endif

                if (sqrError < residualEpsilon * residualEpsilon)
                {
                    return Math.Sqrt(sqrError);
                }

                for (int iter = 0; iter < maxIters; iter++)
                {
                    // Calculate A*P
                    MatrixMultiply(P, AP, useAffine: true, affineScale: 0);

                    // Calculate alpha
                    double invAlpha = 0.0;
                    for (int i = 0; i < Width * Height; i++)
                    {
                        if ((Flags[i] & (byte)LimberDMG.Flags.HOLD_CONSTANT) != 0)
                        {
                            P[i] = 0;
                            AP[i] = 0;
                        }
                        invAlpha += P[i] * AP[i];
                    }
                    double alpha = sqrError / invAlpha;

                    double newSqrError = 0.0;
                    for (int i = 0; i < Width * Height; i++)
                    {
                        x[i] += alpha * P[i];
                        R[i] -= alpha * AP[i];
                        newSqrError += R[i] * R[i];
                    }
#if !LEGACY_TUNING
                    newSqrError /= (Width * Height);
#endif

                    if (newSqrError < residualEpsilon * residualEpsilon)
                    {
                        return Math.Sqrt(sqrError);
                    }

                    for (int i = 0; i < Width * Height; i++)
                    {
                        P[i] = R[i] + newSqrError / sqrError * P[i];
                    }

                    sqrError = newSqrError;
                }

                return Math.Sqrt(sqrError);
            }

            private double ComputeGaussSeidelValue(double[] x, int i, ref double sqrDelta)
            {
                if ((Flags[i] & (byte)(LimberDMG.Flags.HOLD_CONSTANT | LimberDMG.Flags.NO_DATA)) != 0)
                {
                    return x[i];
                }

                int u = i % Width,
                    v = i / Width;

                double partial = 0;

                int numNeighbors = 0;
                foreach (int neighborIdx in getNeighbors(i))
                {
                    numNeighbors++;
                    partial -= x[neighborIdx];
                }

                double scale = lambda;
                if ((Flags[i] & (byte)LimberDMG.Flags.GRADIENT_ONLY) != 0) scale = 0;

                if (numNeighbors > 0)
                {
                    double newX = (k[i] - partial) / (numNeighbors + scale);
                    sqrDelta += (newX - x[i]) * (newX - x[i]);
                    return newX;
                }
                return x[i];
            }

            /// <summary>
            /// Refine a guess vector x using Gauss-Seidel relaxation.
            /// </summary>
            /// <param name="x">Guess $x$ for $A x = k$</param>
            /// <param name="maxIters">Maximum number of iterations</param>
            /// <param name="residualEpsilon">Acceptable change in x for early bailout</param>
            public double OptimizeGaussSeidel(double[] x, int maxIters, double deltaEpsilon)
            {
                // Gauss-Seidel relaxation
                // Our matrix A = \lambda - \nabla^2 can be defined as follows:
                // For any i \neq j, a_{ij} = -1 if nodes i and j are adjacent and 0 otherwise.
                // For any i a_{ii} = deg(i) + lambda
                double sqrDeltaSum = 0.0;
                object sumLock = new object();
                for (int iter = 0; iter < maxIters; iter++)
                {
                    sqrDeltaSum = 0;

                    // Here we exploit the fact that given a poisson problem over a regular
                    // grid, each cell has no dependency on its diagonal neighbors. The
                    // space can be partitioned into 'red' and 'black' cells, as such:
                    // r b r
                    // b r b
                    // r b r 
                    // Gauss-Seidel updates can be performed in parallel within each set.

                    // Red
                    CoreLimitedParallel.For<double>(0, Height, () => 0, (v, sqrDelta) =>
                    {
                        int u;
                        for (u = v % 2; u < Width; u += 2)
                        {
                            int idx = v * Width + u;
                            x[idx] = ComputeGaussSeidelValue(x, idx, ref sqrDelta);
                        }
                        return sqrDelta;

                    }, sqrDelta => { lock (sumLock) { sqrDeltaSum += sqrDelta; } });

                    // Black
                    CoreLimitedParallel.For<double>(0, Height, () => 0, (v, sqrDelta) =>
                    {
                        int u;
                        for (u = (v + 1) % 2; u < Width; u += 2)
                        {
                            int idx = v * Width + u;
                            x[idx] = ComputeGaussSeidelValue(x, idx, ref sqrDelta);
                        }
                        return sqrDelta;

                    }, sqrDelta => { lock (sumLock) { sqrDeltaSum += sqrDelta; } });

#if LEGACY_TUNING
                    if (sqrDeltaSum < deltaEpsilon)
#else
                    sqrDeltaSum /= (Width * Height);
                    if (sqrDeltaSum < deltaEpsilon * deltaEpsilon)
#endif
                    {
                        return Math.Sqrt(sqrDeltaSum);
                    }
                }
                return Math.Sqrt(sqrDeltaSum);
            }
        }

        private class ImageStitchingProblem : PoissonProblem2D
        {
            int[] indices;
            /// <summary>
            /// 
            /// </summary>
            /// <param name="width">width of image represented by composite, indices, and flags</param>
            /// <param name="height">height of image represented by composite, indices, and flags</param>
            /// <param name="composite">pixel data values of a composite image in a particular band</param>
            /// <param name="indices">pixel data values of a mask in a particular band</param>
            /// <param name="flags">pixel data values of flags in a particular band</param>
            /// <param name="lambda"></param>
            /// <param name="edgeBehavior"></param>
            public ImageStitchingProblem(int width, int height, double[] composite, int[] indices, byte[] flags,
                                         double lambda, EdgeBehavior edgeBehavior)
                : base(width, height, composite, null, flags, lambda, edgeBehavior)
            {
                this.indices = indices;
                this.divG = CalculateTargetLaplacian();
                ComputeK();
            }

            private double[] CalculateTargetLaplacian()
            {
                double[] divGrad = new double[Width * Height];
                int i;
                for (i = 0; i < Width * Height; i++)
                {
                    if ((Flags[i] & (byte)LimberDMG.Flags.NO_DATA) != 0)
                    {
                        divGrad[i] = 0;
                        continue;
                    }

                    int u = i % Width,
                        v = i / Width;

                    foreach (int neighborIdx in getNeighbors(i))
                    {
                        if (indices[neighborIdx] != indices[i]) continue;
                        divGrad[i] += U[neighborIdx] - U[i];
                    }
                }
                return divGrad;
            }

            public ImageStitchingProblem Downsample(int scaleDivisor, double[] x, out double[] mX)
            {
                int mWidth = Width / scaleDivisor,
                    mHeight = Height / scaleDivisor;
                mX = new double[mWidth * mHeight];
                double[] mU = new double[mWidth * mHeight];
                byte[] mFlags = new byte[mWidth * mHeight];
                int[] mIndices = new int[mWidth * mHeight];

                int i, j;
                for (j = 0; j < mHeight; j++)
                {
                    for (i = 0; i < mWidth; i++)
                    {
                        mFlags[j * mWidth + i] = 0;
                        Dictionary<int, int> indexCount = new Dictionary<int, int>();
                        byte flag = (byte)(LimberDMG.Flags.NO_DATA | LimberDMG.Flags.HOLD_CONSTANT);
                        int maxIndex = 0, maxIndexCt = 0;
                        int divisor = 0;
                        int u, v;
                        for (v = 0; v < scaleDivisor; v++)
                        {
                            for (u = 0; u < scaleDivisor; u++)
                            {
                                int idx = (scaleDivisor * j + v) * Width + (scaleDivisor * i + u);
                                if ((Flags[idx] & (byte)LimberDMG.Flags.NO_DATA) != 0) continue;

                                flag &= 255 ^ (byte)LimberDMG.Flags.NO_DATA;

                                if ((Flags[idx] & (byte)LimberDMG.Flags.HOLD_CONSTANT) == 0)
                                {
                                    flag &= 255 ^ (byte)LimberDMG.Flags.HOLD_CONSTANT;
                                }

                                mX[j * mWidth + i] += x[idx];
                                mU[j * mWidth + i] += U[idx];
                                if (!indexCount.ContainsKey(indices[idx]))
                                {
                                    indexCount.Add(indices[idx], 1);
                                }
                                else
                                {
                                    indexCount[indices[idx]]++;
                                }
                                if (indexCount[indices[idx]] > maxIndexCt)
                                {
                                    maxIndex = indices[idx];
                                    maxIndexCt = indexCount[indices[idx]];
                                }
                                divisor++;
                            }
                        }
                        if (divisor != 0)
                        {
                            mX[j * mWidth + i] /= divisor;
                            mU[j * mWidth + i] /= divisor;
                            mIndices[j * mWidth + i] = maxIndex;
                        }
                        else
                        {
                            mIndices[j * mWidth + i] = 0;
                        }
                        mFlags[j * mWidth + i] = flag;
                    }
                }
                return new ImageStitchingProblem(mWidth, mHeight, mU, mIndices, mFlags, lambda, edgeBehavior);
            }
        }

        private void Log(string msg, params Object[] args)
        {
            if (progress != null)
            {
                progress(string.Format(msg, args));
            }
        }

        private double IterateMultigrid(ImageStitchingProblem p, int scale, double[] x)
        {
            // Downsample problem and initial guess to requested scale
            double[] mX;
            ImageStitchingProblem coarser = p.Downsample(scale, x, out mX);
            double[] mX0 = new double[mX.Length];
            int i;
            for (i = 0; i < mX.Length; i++)
            {
                mX0[i] = mX[i];
            }

            double ret = 0;
#if LEGACY_TUNING
            if (scale >= 64)
            {
                ret = coarser.OptimizeConjugateGradient(mX, maxIters: 15, residualEpsilon: residualEpsilon);
            }
            else
            {
                ret = coarser.OptimizeGaussSeidel(mX, maxIters: 15, deltaEpsilon: residualEpsilon / 100);
            }
#else
            ret = coarser.OptimizeGaussSeidel(mX, maxIters: numRelaxationSteps, deltaEpsilon: residualEpsilon);
#endif

            // Propagate difference to original scale
            for (i = 0; i < p.Width * p.Height; i++)
            {
                if ((p.Flags[i] & (byte)(Flags.HOLD_CONSTANT | Flags.NO_DATA)) != 0) continue;
                int u = i % p.Width,
                    v = i / p.Width;

                double up = (u % scale) / (double)scale,
                       vp = (v % scale) / (double)scale;
                int upi = (int)Math.Floor(u / (double)scale),
                    vpi = (int)Math.Floor(v / (double)scale);

                int i00 = vpi * coarser.Width + upi,
                    i10 = vpi * coarser.Width + Math.Min(coarser.Width - 1, upi + 1),
                    i01 = Math.Min(coarser.Height - 1, vpi + 1) * coarser.Width + upi,
                    i11 = Math.Min(coarser.Height - 1, vpi + 1) * coarser.Width + Math.Min(coarser.Width - 1, upi + 1);

                double dx = (mX[i00] - mX0[i00]) * (1 - up) * (1 - vp) +
                            (mX[i10] - mX0[i10]) * up * (1 - vp) +
                            (mX[i01] - mX0[i01]) * (1 - up) * vp +
                            (mX[i11] - mX0[i11]) * up * vp;
                x[i] += dx;
            }
            return ret;
        }

        private double[] StitchBand(double[] composite, int[] indices, byte[] flags, int width, int height, int bandNum)
        {
            Log("stitching band {0}", bandNum);
            ImageStitchingProblem p =
                new ImageStitchingProblem(width, height, composite, indices, flags, lambda, edgeMode);
            // Set initial guess to be the composite image
            double[] x = new double[width * height];
            int i;
            for (i = 0; i < width * height; i++)
            {
                x[i] = composite[i];
            }

            double err = -1;
            for (int N = 0; N < numMultigridIterations; N++)
            {
                // Multigrid for low-frequency correction
                for (i = 8; i > 0; i--)
                {
                    Log("stitching band {0}, iteration {1}, multigrid scale 1/{2}, error {3}", bandNum, N, 1 << i, err);
                    err = IterateMultigrid(p, 1 << i, x);
                }

                Log("stitching band {0}, iteration {1}, multigrid scale 1, error {2}", bandNum, N, err);
#if LEGACY_TUNING
                err = p.OptimizeGaussSeidel(x, maxIters: numRelaxationSteps, deltaEpsilon: 1e-10);
#else
                err = p.OptimizeGaussSeidel(x, maxIters: numRelaxationSteps, deltaEpsilon: residualEpsilon);
#endif
                if (err < residualEpsilon)
                {
                    Log("stitching band {0}, stopping at iteration {1}, error {2} < {3}",
                        bandNum, N, err, residualEpsilon);
                    break;
                }
                Log("stitching band {0}, iteration {1} final error {2}", bandNum, N, err);
            }
            Log("finished stitching band {0}, final error {1}", bandNum, err);
            return x;
        }

        /// <summary>
        /// Given a composite image, corresponding index, and flags, output a stitched image with smoothed seams
        ///
        /// The flags image may be omitted but the other two are required.
        ///
        /// Note on image dimensions: the input images must all be the same size.  If either the width or height is not
        /// a power of two, all the inputs will be copied to temp images with power of two dimensions.  In this
        /// situation the Wrap edge modes may not work as originally intended because the images will be padded with
        /// NO_DATA areas.
        ///
        /// </summary>
        /// <param name="composite">original mosaic of images</param>
        /// <param name="index">source of each pixel, should have either one band or same number as input image
        /// <param name="flags">extra options to apply at each pixel (see LimberDMG.Flags enum), null for none, otherwise should have either one band or same number as input image</param>
        /// <param name="valid">optional predicate to check valid (row, col)</param>
        /// <returns></returns>
        public Image StitchImage(Image composite, Image index, Image flags = null, Func<int, int, bool> valid = null)
        {
            int originalWidth = composite.Width;
            int originalHeight = composite.Height;

            if (index.Width != composite.Width || index.Height != composite.Height)
            {
                throw new ArgumentException("Sizes of composite and index images don't match");
            }

            if (index.Bands != 1 && index.Bands != composite.Bands)
            {
                throw new ArgumentException(string.Format("Expected either 1 or {0} index bands, got {1}",
                                                          composite.Bands, index.Bands));
            }
            
            if (flags != null)
            {
                if (flags.Width != composite.Width || flags.Height != composite.Height)
                {
                    throw new ArgumentException("Sizes of composite and flag images don't match");
                }
                if (flags.Bands != 1 && flags.Bands != composite.Bands)
                {
                    throw new ArgumentException(string.Format("Expected either 1 or {0} flag bands, got {1}",
                                                              composite.Bands, flags.Bands));
                }
            }

            Log("performing colorspace conversion {0}", colorConversion);
            switch (colorConversion)
            {
                case ColorConversion.RGBToLAB: composite = composite.RGBToLAB(); break;
                case ColorConversion.RGBToLogLAB: composite = composite.RGBToLAB(logLuminance: true); break;
                case ColorConversion.None: break;
                default: throw new ArgumentException("unkown color conversion: " + colorConversion);
            }

            //change image dimensions to powers of 2
            if ((composite.Width & (composite.Width - 1)) != 0 || (composite.Height & (composite.Height - 1)) != 0)
            {
                int width = MathExtensions.MathE.CeilPowerOf2(composite.Width);
                int height = MathExtensions.MathE.CeilPowerOf2(composite.Height);
                Log("converting {0}x{1} to {2}x{3}", composite.Width, composite.Height, width, height);
                Image comp = new Image(composite.Bands, width, height);
                Image ind = new Image(index.Bands, width, height);
                Image flag = new Image(flags != null ? flags.Bands : 1, width, height);
                for (int r = 0; r < comp.Height; r++)
                {
                    for (int c = 0; c < comp.Width; c++)
                    {
                        if (r < originalHeight && c < originalWidth)
                        {
                            comp.SetBandValues(r, c, composite.GetBandValues(r, c));
                            ind.SetBandValues(r, c, index.GetBandValues(r, c));
                            if (flags != null)
                            {
                                flag.SetBandValues(r, c, flags.GetBandValues(r, c));
                            }
                            else
                            {
                                flag[0, r, c] = (float)Flags.NONE;
                            }
                            if (valid != null && !valid(r, c))
                            {
                                for (int b = 0; b < flag.Bands; b++)
                                {
                                    flag[b, r, c] = (float)(LimberDMG.Flags.NO_DATA | LimberDMG.Flags.HOLD_CONSTANT);
                                }
                            }
                        }
                        else
                        {
                            for (int b = 0; b < flag.Bands; b++)
                            {
                                flag[b, r, c] = (float)Flags.NO_DATA;
                            }
                        }
                    }
                }
                composite = comp;
                index = ind;
                flags = flag;
            }       
            else if (valid != null || flags == null)
            {
                //don't mutate inputs
                flags = flags != null ? ((Image)flags.Clone()) : new Image(1, index.Width, index.Height);

                for (int r = 0; r < index.Height; r++)
                {
                    for (int c = 0; c < index.Width; c++)
                    {
                        if (valid != null && !valid(r, c))
                        {
                            for (int b = 0; b < flags.Bands; b++)
                            {
                                flags[b, r, c] = (float)(LimberDMG.Flags.NO_DATA | LimberDMG.Flags.HOLD_CONSTANT);
                            }
                        }
                    }
                }
            }

            Image blendedImage = new Image(composite.Bands, composite.Width, composite.Height);

            CoreLimitedParallel.For(0, composite.Bands, (i) =>
            {
                var blendedBand = StitchBand(GetBandDouble(composite, i),
                                             GetBandInt(index, index.Bands == 1 ? 0 : i),
                                             GetBandByte(flags, flags.Bands == 1 ? 0 : i),
                                             composite.Width, composite.Height, i);
                WriteBand(blendedImage, i, blendedBand);
            });

            var ret = blendedImage;
            if (originalWidth < blendedImage.Width || originalHeight < blendedImage.Height)
            {
                Log("cropping {0}x{1} to {2}x{3}",
                    blendedImage.Width, blendedImage.Height, originalWidth, originalHeight);
                ret = blendedImage.Crop(0, 0, originalWidth, originalHeight);
            }

            Log("undoing colorspace conversion {0}", colorConversion);
            switch (colorConversion)
            {
                case ColorConversion.RGBToLAB: return ret.LABToRGB();
                case ColorConversion.RGBToLogLAB: return ret.LABToRGB(logLuminance: true);
                case ColorConversion.None: return ret;
                default: throw new ArgumentException("unkown color conversion: " + colorConversion);
            }
        }

        private static double[] GetBandDouble(Image img, int bandNum)
        {
            double[] band = new double[img.Width*img.Height];
            for(int i = 0; i < img.Height; i++)
            {
                for(int j = 0; j < img.Width; j++)
                {
                    band[i * img.Width + j] = img[bandNum, i, j];
                }
            }
            return band;
        }

        private static int[] GetBandInt(Image img, int bandNum)
        {
            int[] band = new int[img.Width * img.Height];
            for (int i = 0; i < img.Height; i++)
            {
                for (int j = 0; j < img.Width; j++)
                {
                    band[i * img.Width + j] = (int)img[bandNum, i, j];
                }
            }
            return band;
        }

        private static byte[] GetBandByte(Image img, int bandNum)
        {
            byte[] band = new byte[img.Width * img.Height];
            for (int i = 0; i < img.Height; i++)
            {
                for (int j = 0; j < img.Width; j++)
                {
                    band[i * img.Width + j] = (byte)img[bandNum, i, j];
                }
            }
            return band;
        }

        private static void WriteBand(Image img, int bandNum, double[] data)
        {
            for (int i = 0; i < img.Height; i++)
            {
                for (int j = 0; j < img.Width; j++)
                {
                    img[bandNum, i, j] = (float)data[i * img.Width + j];
                }
            }
        }
    }
}
