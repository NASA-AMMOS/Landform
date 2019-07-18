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
            int i;
            for (i = 0; i < from.Length; i++)
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

            Random r = new Random();
            int i;
            for (i = 0; i < _maxIterations; i++)
            {
                double temperature = 1 - (i / (double)_maxIterations);
                temperature = Math.Pow(temperature, temperatureExponent) * temperatureScale;

                Copy(x, candidateX);
                int j;
                for (j = 0; j < dimensions; j++)
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

                if (noisy && i % 50 == 0)
                {
                    Console.WriteLine("{0}% - best error: {1}", (int)(((i + 1) / (float)_maxIterations) * 100), bestError);
                }
            }
            return bestX;
        }

        public int maxIterations
        {
            get
            {
                return _maxIterations;
            }
            set
            {
                _maxIterations = value;
            }
        }
        int _maxIterations;
        public double temperatureExponent;
        public double temperatureScale;
        public double probabilityScale;
        public double epsilon;
        public bool noisy;
        public double[] sigma;

        public double[] Minimize(Func<double[], double> errorFunction, double[] x0, double[] sigma)
        {
            this.sigma = sigma;
            return Minimize(errorFunction, x0);
        }
    }
}
