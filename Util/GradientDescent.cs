using System;

namespace Util
{
    //Taken from terrain tools
    public class GradientDescent
    {
        void CalculateGradient(Func<double[], double> errorFunction, double[] x, double[] dx)
        {
            double[] cx = new double[x.Length];
            int i;
            for (i = 0; i < x.Length; i++)
            {
                cx[i] = x[i];
            }

            for (i = 0; i < x.Length; i++)
            {
                cx[i] = x[i] - sigma[i] / 2;
                double leftErr = errorFunction(cx);
                cx[i] = x[i] + sigma[i] / 2;
                double rightErr = errorFunction(cx);
                cx[i] = x[i];
                dx[i] = (rightErr - leftErr) / (sigma[i]);
            }
        }

        public double[] Minimize(Func<double[], double> errorFunction, double[] x0)
        {
            double[] x = x0;
            int dimensions = x0.Length;

            double[] cx = new double[dimensions];
            double currentError = errorFunction(x0);

            double[] dx = new double[dimensions];
            int i, j;
            for (i = 0; i < _maxIterations; i++)
            {
                double newError = double.PositiveInfinity;

                CalculateGradient(errorFunction, x, dx);

                // Perform line search in direction of steepest descent
                double a0 = 0,
                       a1 = stepSize / 2.0;
                double e0 = 0,
                       e1 = errorFunction(x);

                double bestA = a1,
                       bestE = e1;
                int iters = 20;
                do
                {
                    e0 = e1;
                    a0 = a1;
                    for (j = 0; j < dimensions; j++)
                    {
                        cx[j] = x[j] - dx[j] * a0;
                    }
                    e1 = errorFunction(cx);

                    if (e1 < bestE)
                    {
                        bestE = e1;
                        bestA = a1;
                    }

                    double de_da = (e1 - e0) / (a1 - a0);
                    a1 = a1 - e1 / de_da;
                    iters--;
                } while (Math.Abs(e1 - e0) > 1e-5 && iters > 0);

                if (bestE >= currentError)
                {
                    Console.WriteLine("Line search failed to find better solution.");
                    return x;
                }
                else
                {
                    for (j = 0; j < dimensions; j++)
                    {
                        x[j] = x[j] - dx[j] * bestA;
                    }
                    newError = bestE;
                }

                Console.WriteLine("Iteration {0}: {1} -> {2}", i + 1, currentError, newError);
                if (Math.Abs(newError - currentError) < epsilon)
                {
                    break;
                }
                currentError = newError;
            }
            return x;
        }

        public int MaxIterations { get => _maxIterations; set => _maxIterations = value; }
        int _maxIterations;

        public double stepSize;
        public double epsilon;
        public double[] sigma;
    }
}
