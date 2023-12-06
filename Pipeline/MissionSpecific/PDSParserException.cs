using System;

namespace JPLOPS.Pipeline
{
    public class PDSParserException : Exception
    {
        public PDSParserException() { }
        public PDSParserException(string message) : base(message) { }
        public PDSParserException(string message, Exception inner) : base(message, inner) { }
    }
}
