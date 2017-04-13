using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public class PDSParserException : Exception
    {
        public PDSParserException() { }
        public PDSParserException(string message) : base(message) { }
        public PDSParserException(string message, Exception inner) : base(message, inner) { }
    }
}
