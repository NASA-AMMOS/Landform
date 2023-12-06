using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Linq;

namespace JPLOPS.MathExtensions
{
    public class VectorThunk
    {
        public HashSet<Guid> Arguments;
        public Func<ThunkContext, Vector<double>> Evaluate;

        public VectorThunk(IEnumerable<Guid> arguments, Func<ThunkContext, Vector<double>> evaluate)
        {
            Arguments = new HashSet<Guid>(arguments);
            Evaluate = evaluate;
        }

        public static VectorThunk Reference(Guid guid)
        {
            return new VectorThunk(new Guid[] { guid }, (ctx) => { return ctx.Get(guid); });
        }
        public static VectorThunk Mean(Guid guid)
        {
            return new VectorThunk(new Guid[] { guid }, (ctx) => { return ctx.RandomVariables[guid].Mean; });
        }

        public static VectorThunk Constant(Vector<double> value)
        {
            return new VectorThunk(new Guid[] { }, (ctx) => { return value; });
        }

        public static VectorThunk operator+(VectorThunk lhs, VectorThunk rhs)
        {
            return new VectorThunk(
                new HashSet<Guid>(lhs.Arguments.Concat(rhs.Arguments)),
                (ctx) => { return lhs.Evaluate(ctx) + rhs.Evaluate(ctx); }
                );
        }
        public static VectorThunk operator -(VectorThunk lhs, VectorThunk rhs)
        {
            return new VectorThunk(
                new HashSet<Guid>(lhs.Arguments.Concat(rhs.Arguments)),
                (ctx) => { return lhs.Evaluate(ctx) - rhs.Evaluate(ctx); }
                );
        }

        public VectorThunk Then(IEnumerable<Guid> newArguments, Func<ThunkContext, Vector<double>, Vector<double>> func)
        {
            return new VectorThunk(
                new HashSet<Guid>(Arguments.Concat(newArguments)),
                (ctx) =>
                {
                    var intermediate = Evaluate(ctx);
                    return func(ctx, intermediate);
                });
        }
    }
}
