using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    public class LimberDMG
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(LimberDMG));

        public double residualEpsilon;
        public int numRelaxationSteps;
        public double lambda;
        PoissonProblem2D.EdgeBehavior edgeMode;

        public LimberDMG(double residualEpsilon = 1e-3, int numRelaxationSteps = 15, double lambda = 0.003, PoissonProblem2D.EdgeBehavior edgeMode = PoissonProblem2D.EdgeBehavior.Clamp)
        {
            this.residualEpsilon = residualEpsilon;
            this.numRelaxationSteps = numRelaxationSteps;
            this.lambda = lambda;
            this.edgeMode = edgeMode;
        }

        public enum Flags
        {
            NONE = 0,
            HOLD_CONSTANT = 1,
            GRADIENT_ONLY = 2,
            NO_DATA = 4
        }

        /// <summary>
        /// A discrete Poisson problem over a regular 2D grid with missing cells.
        /// 
        /// $\nabla^2f - \lambda f = \lambda u - \nabla \cdot g$
        /// 
        /// </summary>
        public class PoissonProblem2D
        {
            public int width, height;
            public double[] U;
            public double[] divG;
            public byte[] flags;
            public double lambda;
            public EdgeBehavior edgeBehavior;
            internal PixelNeighborFunction getNeighbors;

            double[] k;

            public enum EdgeBehavior
            {
                Clamp = 0,
                WrapSphere = 1,
                WrapCylinder = 2,
                WrapTorus = 3
            }

            public PoissonProblem2D(int width, int height, double[] U, double[] divG, byte[] flags, double lambda, EdgeBehavior edgeBehavior)
            {
                this.width = width;
                this.height = height;
                this.U = U;
                this.divG = divG;
                this.flags = flags;
                this.lambda = lambda;
                this.edgeBehavior = edgeBehavior;

                switch (edgeBehavior)
                {
                    case EdgeBehavior.Clamp:
                        getNeighbors = PixelNeighborsClamp;
                        break;
                    case EdgeBehavior.WrapCylinder:
                        getNeighbors = PixelNeighborsCylinder;
                        break;
                    case EdgeBehavior.WrapSphere:
                        getNeighbors = PixelNeighborsSphere;
                        break;
                    case EdgeBehavior.WrapTorus:
                        getNeighbors = PixelNeighborsTorus;
                        break;
                    default:
                        throw new ArgumentException("Invalid edge behavior!");
                }

                if (divG != null)
                {
                    ComputeK();
                }
            }

            #region Pixel neighbor functions
            internal delegate IEnumerable<int> PixelNeighborFunction(int pixelIdx);

            IEnumerable<int> PixelNeighborsClamp(int pixelIdx)
            {
                int u = pixelIdx % width,
                    v = pixelIdx / width;

                if (u > 0 && (flags[pixelIdx - 1] & (byte)Flags.NO_DATA) == 0)
                {
                    yield return pixelIdx - 1;
                }
                if (u < width - 1 && (flags[pixelIdx + 1] & (byte)Flags.NO_DATA) == 0)
                {
                    yield return pixelIdx + 1;
                }
                if (v > 0 && (flags[pixelIdx - width] & (byte)Flags.NO_DATA) == 0)
                {
                    yield return pixelIdx - width;
                }
                if (v < height - 1 && (flags[pixelIdx + width] & (byte)Flags.NO_DATA) == 0)
                {
                    yield return pixelIdx + width;
                }
            }

            IEnumerable<int> PixelNeighborsCylinder(int pixelIdx)
            {
                int u = pixelIdx % width,
                    v = pixelIdx / width;

                // Horizontal neighbors
                int h1 = v * width + ((u + 1) % width);
                if ((flags[h1] & (byte)Flags.NO_DATA) == 0) yield return h1;
                int h0 = v * width + ((u + width - 1) % width);
                if ((flags[h0] & (byte)Flags.NO_DATA) == 0) yield return h0;

                if (v > 0 && (flags[pixelIdx - width] & (byte)Flags.NO_DATA) == 0)
                {
                    yield return pixelIdx - width;
                }
                if (v < height - 1 && (flags[pixelIdx + width] & (byte)Flags.NO_DATA) == 0)
                {
                    yield return pixelIdx + width;
                }
            }

            IEnumerable<int> PixelNeighborsSphere(int pixelIdx)
            {
                int u = pixelIdx % width,
                    v = pixelIdx / width;

                // Horizontal neighbors
                int h1 = v * width + ((u + 1) % width);
                if ((flags[h1] & (byte)Flags.NO_DATA) == 0) yield return h1;
                int h0 = v * width + ((u + width - 1) % width);
                if ((flags[h0] & (byte)Flags.NO_DATA) == 0) yield return h0;

                // Wrap to mirror side of image on vertical edges
                if ((v == 0 || v == height - 1) && (flags[v * width + (width - 1 - u)] & (byte)Flags.NO_DATA) == 0)
                {
                    yield return v * width + (width - 1 - u);
                }

                if (v > 0 && (flags[pixelIdx - width] & (byte)Flags.NO_DATA) == 0)
                {
                    yield return pixelIdx - width;
                }
                if (v < height - 1 && (flags[pixelIdx + width] & (byte)Flags.NO_DATA) == 0)
                {
                    yield return pixelIdx + width;
                }
            }

            IEnumerable<int> PixelNeighborsTorus(int pixelIdx)
            {
                int u = pixelIdx % width,
                    v = pixelIdx / width;

                // Horizontal neighbors
                int h1 = v * width + ((u + 1) % width);
                if ((flags[h1] & (byte)Flags.NO_DATA) == 0) yield return h1;
                int h0 = v * width + ((u + width - 1) % width);
                if ((flags[h0] & (byte)Flags.NO_DATA) == 0) yield return h0;

                // Vertical neighbors
                int v1 = ((v + 1) % height) * width + u;
                if ((flags[v1] & (byte)Flags.NO_DATA) == 0) yield return v1;
                int v0 = ((v + height - 1) % height) * width + u;
                if ((flags[v0] & (byte)Flags.NO_DATA) == 0) yield return v0;
            }
            #endregion

            internal void ComputeK()
            {
                if (k == null || k.Length != width * height)
                {
                    k = new double[width * height];
                }

                int i;
                for (i = 0; i < width * height; i++)
                {
                    if ((flags[i] & (byte)Flags.NO_DATA) != 0)
                    {
                        k[i] = 0;
                        continue;
                    }
                    double scale = lambda;
                    if ((flags[i] & (byte)Flags.GRADIENT_ONLY) != 0) scale = 0;

                    k[i] = scale * U[i] - divG[i];
                }
            }

            /// <summary>
            /// Calculate the laplacian at index $i$ ignoring missing cells.
            /// </summary>
            /// <param name="x">Input vector</param>
            /// <param name="i">Index in input vector</param>
            /// <returns>$\nabla^2f$</returns>
            public double Laplacian(double[] x, int i)
            {
                if ((flags[i] & (byte)Flags.NO_DATA) != 0) return 0;

                int u = i % width,
                    v = i / width;

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
            public void MatrixMultiply(double[] x, double[] b, bool useAffine = false, double affineScale = 1.0)
            {
                Parallel.For(0, width * height, (i) =>
                {
                    if ((flags[i] & (byte)Flags.NO_DATA) != 0)
                    {
                        b[i] = 0;
                        return;
                    }
                    double lhs = lambda * x[i];
                    if ((flags[i] & (byte)Flags.GRADIENT_ONLY) != 0) lhs = 0;
                    else if (useAffine && (flags[i] & (byte)Flags.HOLD_CONSTANT) != 0) lhs = lambda * affineScale * U[i];

                    b[i] = lhs - Laplacian(x, i);
                });
            }

            /// <summary>
            /// Calculate the residual $r = Ax - b$.
            /// </summary>
            /// <param name="x">Input vector</param>
            /// <param name="r">Output vector</param>
            /// <param name="b">If not null, the value $A x$ will be stored here</param>
            public void CalculateResidual(double[] x, double[] r, double[] b = null)
            {
                if (b == null)
                {
                    b = new double[width * height];
                }
                MatrixMultiply(x, b);

                int i;
                for (i = 0; i < width * height; i++)
                {
                    if ((flags[i] & (byte)Flags.NO_DATA) != 0)
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
            public void OptimizeConjugateGradient(double[] x, int maxIters = 20, double residualEpsilon = 1e-6)
            {
                double[] R = new double[width * height];
                double[] P = new double[width * height];
                double[] AP = new double[width * height];
                double sqrError = 0.0;

                // Calculate initial R, P, sqrError
                CalculateResidual(x, R);
                int i;
                for (i = 0; i < width * height; i++)
                {
                    sqrError += R[i] * R[i];
                    P[i] = R[i];
                }

                if (sqrError < residualEpsilon * residualEpsilon)
                    return;

                int iter;
                for (iter = 0; iter < maxIters; iter++)
                {
                    // Calculate A*P
                    MatrixMultiply(P, AP, useAffine: true, affineScale: 0);

                    // Calculate alpha
                    double invAlpha = 0.0;
                    for (i = 0; i < width * height; i++)
                    {
                        if ((flags[i] & (byte)Flags.HOLD_CONSTANT) != 0)
                        {
                            P[i] = 0;
                            AP[i] = 0;
                        }
                        invAlpha += P[i] * AP[i];
                    }
                    double alpha = sqrError / invAlpha;

                    double newSqrError = 0.0;
                    for (i = 0; i < width * height; i++)
                    {
                        x[i] += alpha * P[i];
                        R[i] -= alpha * AP[i];
                        newSqrError += R[i] * R[i];
                    }
                    if (newSqrError < residualEpsilon * residualEpsilon)
                    {
                        break;
                    }

                    for (i = 0; i < width * height; i++)
                    {
                        P[i] = R[i] + newSqrError / sqrError * P[i];
                    }
                    sqrError = newSqrError;
                }
            }

            double ComputeGaussSeidelValue(double[] x, int i, ref double sqrDelta)
            {
                if ((flags[i] & (byte)(Flags.HOLD_CONSTANT | Flags.NO_DATA)) != 0)
                {
                    return x[i];
                }

                int u = i % width,
                    v = i / width;

                double partial = 0;

                int numNeighbors = 0;
                foreach (int neighborIdx in getNeighbors(i))
                {
                    numNeighbors++;
                    partial -= x[neighborIdx];
                }

                double scale = lambda;
                if ((flags[i] & (byte)Flags.GRADIENT_ONLY) != 0) scale = 0;

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
            public void OptimizeGaussSeidel(double[] x, int maxIters = 20, double deltaEpsilon = 1e-8)
            {
                // Gauss-Seidel relaxation
                // Our matrix A = \lambda - \nabla^2 can be defined as follows:
                // For any i \neq j, a_{ij} = -1 if nodes i and j are adjacent and 0 otherwise.
                // For any i a_{ii} = deg(i) + lambda
                int iter;
                for (iter = 0; iter < maxIters; iter++)
                {
                    double sqrDelta = 0.0;
                    // Here we exploit the fact that given a poisson problem over a regular
                    // grid, each cell has no dependency on its diagonal neighbors. The
                    // space can be partitioned into 'red' and 'black' cells, as such:
                    // r b r
                    // b r b
                    // r b r 
                    // Gauss-Seidel updates can be performed in parallel within each set.

                    // Red
                    Parallel.For(0, height, (v) =>
                    {
                        int u;
                        for (u = v % 2; u < width; u += 2)
                        {
                            int idx = v * width + u;
                            x[idx] = ComputeGaussSeidelValue(x, idx, ref sqrDelta);
                        }
                    });
                    // Black
                    Parallel.For(0, height, (v) =>
                    {
                        int u;
                        for (u = (v + 1) % 2; u < width; u += 2)
                        {
                            int idx = v * width + u;
                            x[idx] = ComputeGaussSeidelValue(x, idx, ref sqrDelta);
                        }
                    });
                    if (sqrDelta < deltaEpsilon)
                    {
                        break;
                    }
                }
            }

        }

        class ImageStitchingProblem : PoissonProblem2D
        {
            int[] indices;
            public ImageStitchingProblem(int width, int height, double[] composite, int[] indices, byte[] flags, double lambda, EdgeBehavior edgeBehavior)
                : base(width, height, composite, null, flags, lambda, edgeBehavior)
            {
                this.indices = indices;
                this.divG = CalculateTargetLaplacian(indices);
                ComputeK();
            }

            double[] CalculateTargetLaplacian(int[] indices)
            {
                double[] divGrad = new double[width * height];
                int i;
                for (i = 0; i < width * height; i++)
                {
                    if (indices[i] == 0 || indices[i] == 0xffff)
                    {
                        indices[i] = 0;
                        flags[i] = (byte)(Flags.NO_DATA | Flags.HOLD_CONSTANT);
                    }
                    if ((flags[i] & (byte)Flags.NO_DATA) != 0)
                    {
                        divGrad[i] = 0;
                        continue;
                    }

                    int u = i % width,
                        v = i / width;

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
                int mWidth = width / scaleDivisor,
                    mHeight = height / scaleDivisor;
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
                        byte flag = (byte)(Flags.NO_DATA | Flags.HOLD_CONSTANT);
                        int maxIndex = 0, maxIndexCt = 0;
                        int divisor = 0;
                        int u, v;
                        for (v = 0; v < scaleDivisor; v++)
                        {
                            for (u = 0; u < scaleDivisor; u++)
                            {
                                int idx = (scaleDivisor * j + v) * width + (scaleDivisor * i + u);
                                if ((flags[idx] & (byte)Flags.NO_DATA) != 0) continue;

                                flag &= 255 ^ (byte)Flags.NO_DATA;

                                if ((flags[idx] & (byte)Flags.HOLD_CONSTANT) == 0)
                                {
                                    flag &= 255 ^ (byte)Flags.HOLD_CONSTANT;
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

        void IterateMultigrid(ImageStitchingProblem p, int scale, double[] x)
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

            if (scale >= 64)
            {
                coarser.OptimizeConjugateGradient(mX, maxIters: 15, residualEpsilon: residualEpsilon);
            }
            else
            {
                coarser.OptimizeGaussSeidel(mX, maxIters: 15, deltaEpsilon: residualEpsilon / 100);
            }

            // Propagate difference to original scale
            for (i = 0; i < p.width *p.height; i++)
            {
                if ((p.flags[i] & (byte)(Flags.HOLD_CONSTANT | Flags.NO_DATA)) != 0) continue;
                int u = i % p.width,
                    v = i / p.width;

                double up = (u % scale) / (double)scale,
                       vp = (v % scale) / (double)scale;
                int upi = (int)Math.Floor(u / (double)scale),
                    vpi = (int)Math.Floor(v / (double)scale);

                int i00 = vpi * coarser.width + upi,
                    i10 = vpi * coarser.width + Math.Min(coarser.width - 1, upi + 1),
                    i01 = Math.Min(coarser.height - 1, vpi + 1) * coarser.width + upi,
                    i11 = Math.Min(coarser.height - 1, vpi + 1) * coarser.width + Math.Min(coarser.width - 1, upi + 1);

                double dx = (mX[i00] - mX0[i00]) * (1 - up) * (1 - vp) +
                            (mX[i10] - mX0[i10]) * up * (1 - vp) +
                            (mX[i01] - mX0[i01]) * (1 - up) * vp +
                            (mX[i11] - mX0[i11]) * up * vp;
                x[i] += dx;
            }
        }


        public double[] StitchBand(double[] composite, int[] indices, byte[] flags, int width, int height, int bandNum)
        {
            ImageStitchingProblem p = new ImageStitchingProblem(width, height, composite, indices, flags, lambda, edgeMode);
            // Set initial guess to be the composite image
            double[] x = new double[width * height];
            int i;
            for (i = 0; i < width * height; i++)
            {
                x[i] = composite[i];
            }

            for (int N = 0; N < 5; N++)
            {
                logger.InfoFormat("Band {0}: Beginning multigrid iteration {1}", bandNum, N);
                // Multigrid for low-frequency correction
                for (i = 8; i > 0; i--)
                {
                    IterateMultigrid(p, 1 << i, x);
                }

                p.OptimizeGaussSeidel(x, maxIters: numRelaxationSteps, deltaEpsilon: 1e-10);
            }
            return x;
        }

        public Image StitchImage(Image compositePath, Image indexPath, Image flagsPath, string outputPath)
        {
            int originalWidth = compositePath.Width,
                originalHeight = compositePath.Height;

            // Some sanity checks
            {
                if (indexPath.Width != compositePath.Width || indexPath.Height != compositePath.Height)
                {
                    throw new ArgumentException("Sizes of composite and index images don't match");
                }
                if (indexPath.Bands != 1 && indexPath.Bands != compositePath.Bands)
                {
                    throw new ArgumentException(string.Format("Expected either 1 or {0} index bands, got {1}", compositePath.Bands, indexPath.Bands));
                }

                if (flagsPath != null)
                {
                    if (flagsPath.Width != compositePath.Width || flagsPath.Height != compositePath.Height)
                    {
                        throw new ArgumentException("Sizes of composite and flag images don't match");
                    }
                    if (flagsPath.Bands != 1 && flagsPath.Bands != compositePath.Bands)
                    {
                        throw new ArgumentException(string.Format("Expected either 1 or {0} flag bands, got {1}", compositePath.Bands, indexPath.Bands));
                    }
                }
            }

            //change image dimensions to powers of 2
            if ((compositePath.Width & (compositePath.Width - 1)) != 0 || (compositePath.Height & (compositePath.Height - 1)) != 0)
            {
                Image composite = new Image(compositePath.Bands, (int)Math.Pow(2, Math.Ceiling(Math.Log(compositePath.Width, 2))), (int)Math.Pow(2, Math.Ceiling(Math.Log(compositePath.Height, 2))));
                Image index = new Image(indexPath.Bands, (int)Math.Pow(2, Math.Ceiling(Math.Log(compositePath.Width, 2))), (int)Math.Pow(2, Math.Ceiling(Math.Log(compositePath.Height, 2))));
                Image flags = new Image(flagsPath.Bands, (int)Math.Pow(2, Math.Ceiling(Math.Log(compositePath.Width, 2))), (int)Math.Pow(2, Math.Ceiling(Math.Log(compositePath.Height, 2))));
                float[] noDataBandValues = new float[flags.Bands];
                for (int i = 0; i < flags.Bands; i++)
                {
                    noDataBandValues[i] = (float)Flags.NO_DATA;
                }
                for (int i = 0; i < composite.Height; i++)
                {
                    for (int j = 0; j < composite.Width; j++)
                    {
                        if (i < originalHeight && j < originalWidth)
                        {
                            composite.SetBandValues(i, j, compositePath.GetBandValues(i, j));
                            index.SetBandValues(i, j, indexPath.GetBandValues(i, j));
                            flags.SetBandValues(i, j, flagsPath.GetBandValues(i, j));
                        }
                        else
                        {
                            flags.SetBandValues(i, j, noDataBandValues);
                        }
                    }
                }
                compositePath = composite;
                indexPath = index;
                flagsPath = flags;
            }       

            Image blendedImage = new Image(compositePath.Bands, compositePath.Width, compositePath.Height);

            Parallel.For(0, compositePath.Bands, (i) =>
            {
                logger.DebugFormat("Band {0}: Blending", i);
                var blendedBand = StitchBand(GetBandDouble(compositePath, i), GetBandInt(indexPath, indexPath.Bands == 1 ? 0 : i), GetBandByte(flagsPath, flagsPath.Bands == 1 ? 0 : i), compositePath.Width, compositePath.Height, i);
                WriteBand(blendedImage, i, blendedBand);
            });
            
            blendedImage = blendedImage.Crop(0, 0, originalWidth, originalHeight);
            return blendedImage;
        }

        double[] GetBandDouble(Image img, int bandNum)
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

        int[] GetBandInt(Image img, int bandNum)
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

        byte[] GetBandByte(Image img, int bandNum)
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

        void WriteBand(Image img, int bandNum, double[] data)
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
