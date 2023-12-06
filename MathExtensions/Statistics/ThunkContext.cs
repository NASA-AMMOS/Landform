using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;

namespace JPLOPS.MathExtensions
{
    public class ThunkContext
    {
        public Dictionary<Guid, Vector<double>> Constants;
        public Dictionary<Guid, GaussianND> RandomVariables;

        public ThunkContext()
        {
            Constants = new Dictionary<Guid, Vector<double>>();
            RandomVariables = new Dictionary<Guid, GaussianND>();
        }

        public void AddConstant(Guid guid, Vector<double> value)
        {
            Constants[guid] = value;
        }

        public void AddVariable(Guid guid, GaussianND value)
        {
            RandomVariables[guid] = value;
        }

        public void AddVariable(NamedGaussian variable)
        {
            RandomVariables[variable.Guid] = variable;
        }

        public Vector<double> Get(Guid guid)
        {
            return Constants[guid];
        }
        public Vector<double> Mean(Guid guid)
        {
            return RandomVariables[guid].Mean;
        }
    }
}
