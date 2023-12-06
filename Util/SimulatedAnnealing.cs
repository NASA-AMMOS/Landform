using System;

namespace JPLOPS.Util
{
    public class SimulatedAnnealingOptions
    {
        //Number of iterations per annealing
        public int maxIterations;

        //Temperature controls the chance of the algorithm moving to a worse solution (to avoid local minima),
        //as well as how far the solution can move. Higher temperature = higher chance of larger movement.
        //Temperature decays with each iteration 

        //Used to scale the temperature by a constant factor
        public double temperatureScale;

        //Controls the shape of temperature decay, higher exponent = sharper decay
        public double temperatureExponent;

        //Scales the error perceived by the algorithm between a candidate solution and the current solution.
        //Higher probability scale = more likely to stay in local minima
        public double probabilityScale;

        //Allows weighting how much to fluctuate the current solution per dimension.
        //For a transformation we likely want to perturb the rotation on a different scale than the translation.
        //Higher sigma value for dimension d = more fluctuation of d.
        //Zero sigma value for dimension d = no fluctuation of d.
        public double[] sigma;

        public Action<string> verbose;
    }

    public class SimulatedAnnealing
    {
        public SimulatedAnnealingOptions opts;

        public double[] Minimize(Func<double[], double> errorFunction, double[] x0)
        {
            int dimensions = x0.Length;
            double[] x = new double[dimensions];
            double[] bestX = new double[dimensions];
            double[] candidateX = new double[dimensions];
            Copy(x0, x);
            Copy(x0, bestX);

            double currentError = errorFunction(x);
            double bestError = currentError;

            int numJumps = 0, numImprovements = 0;
            Random r = NumberHelper.MakeRandomGenerator();
            for (int i = 0; i < opts.maxIterations; i++)
            {
                double temperature = 1 - (i / (double)opts.maxIterations);
                temperature = Math.Pow(temperature, opts.temperatureExponent) * opts.temperatureScale;

                Copy(x, candidateX);
                for (int j = 0; j < dimensions; j++)
                {
                    candidateX[j] += NormalRandom(r) * opts.sigma[j] * temperature;
                }

                double candidateError = errorFunction(candidateX);

                if (candidateError < 0)
                {
                    if (opts.verbose != null)
                    {
                        opts.verbose(string.Format("simulated annealing {0}%: error {1}, aborting",
                                                   (int)(((i + 1) / (float)opts.maxIterations) * 100),
                                                   candidateError));
                    }
                    break;
                }

                double jumpThreshold = Math.Exp(-(candidateError - currentError) * opts.probabilityScale / temperature);
                bool advance = candidateError < currentError;
                bool jump = !advance && r.NextDouble() < jumpThreshold;
                if (advance || jump)
                {
                    currentError = candidateError;
                    Copy(candidateX, x);
                }

                if (jump)
                {
                    numJumps++;
                }

                if (currentError < bestError)
                {
                    bestError = currentError;
                    Copy(x, bestX);
                    numImprovements++;
                }

                if (opts.verbose != null && i % 50 == 0)
                {
                    opts.verbose(string.Format("simulated annealing {0}%: best error {1}, " +
                                               "temperature {2}, jump threshold {3}, {4} jumps, {5} improvements",
                                               (int)(((i + 1) / (float)opts.maxIterations) * 100),
                                               bestError, temperature, jumpThreshold, numJumps, numImprovements));
                }

                if (bestError == 0)
                {
                    break;
                }
            }
            return bestX;
        }

        private static void Copy(double[] from, double[] to)
        {
            for (int i = 0; i < from.Length; i++)
            {
                to[i] = from[i];
            }
        }

        private static void Add(double[] to, double[] from)
        {
            int i;
            for (i = 0; i < from.Length; i++)
            {
                to[i] += from[i];
            }
        }

        private static bool haveExtraRandom = false;
        private static double extraRandom = 0.0;
        private static double NormalRandom(Random r)
        {
            if (haveExtraRandom)
            {
                haveExtraRandom = false;
                return extraRandom;
            }
     	    double u = r.NextDouble() * 2 - 1,
                   v = r.NextDouble() * 2 - 1;
            while (u * u + v * v >= 1)
            {
                u = r.NextDouble() * 2 - 1;
                v = r.NextDouble() * 2 - 1;
            }

            double s = u * u + v * v;
            double scale = Math.Sqrt((-2 * Math.Log(s)) / s);

            haveExtraRandom = true;
            extraRandom = v * scale;
            return u * scale;
        }
    }
}
