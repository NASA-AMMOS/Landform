using OPS.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Util
{
    public class SimulatedAnnealing
    {
        static void Copy(double[] from, double[] to)
        {
            for (int i = 0; i < from.Length; i++)
            {
                to[i] = from[i];
            }
        }
        static void Add(double[] to, double[] from)
        {
            int i;
            for (i = 0; i < from.Length; i++)
            {
                to[i] += from[i];
            }
        }
        static bool haveExtraRandom = false;
        static double extraRandom = 0.0;
        static double NormalRandom(Random r)
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

            Random r = NumberHelper.MakeRandomGenerator();
            int i;
            for (i = 0; i < maxIterations; i++)
            {
                double temperature = 1 - (i / (double)maxIterations);
                temperature = Math.Pow(temperature, temperatureExponent) * temperatureScale;

                Copy(x, candidateX);
                for (int j = 0; j < dimensions; j++)
                {
                    candidateX[j] += NormalRandom(r) * sigma[j] * temperature;
                }

                double candidateError = errorFunction(candidateX);
                if (candidateError < currentError || r.NextDouble() < Math.Exp(-(candidateError - currentError) * probabilityScale / temperature))
                {
                    currentError = candidateError;
                    Copy(candidateX, x);
                }
                if (currentError < bestError)
                {
                    bestError = currentError;
                    Copy(x, bestX);
                }

                if (verbose && i % 50 == 0)
                {
                    Console.WriteLine("{0}% - best error: {1}", (int)(((i + 1) / (float)maxIterations) * 100), bestError);
                }
            }
            return bestX;
        }

        public int maxIterations;
	//Temperature controls the chance of the algorithm moving to a worse solution (to avoid local minima), as well as how far the solution can move. Higher temperature = higher chance of larger movement. Temperature decays with each iteration 
	//Controls the shape of temperature decay, higher exponent = sharper decay
        public double temperatureExponent;
	//Used to scale the temperature by a factor at every iteration
        public double temperatureScale;
	//Scales the error perceived by the algorithm between a candidate solution and the current solution. Higher probability scale = more likely to stay in local minima
        public double probabilityScale;
	
        public double epsilon;
        public bool verbose;
	//Allows weighting how much to fluctuate the current solution per dimension. For the case of a transformation, we likely want to perturb the rotation on a different scale than the translation. Higher sigma value for dimension d = more fluctuation of d 
        public double[] sigma;

        public double[] Minimize(Func<double[], double> errorFunction, double[] x0, double[] sigma)
        {
            this.sigma = sigma;
            return Minimize(errorFunction, x0);
        }
    }
}
