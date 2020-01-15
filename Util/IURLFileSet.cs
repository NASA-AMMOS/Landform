using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Util
{
    /// <summary>
    /// An set of URLs with different filename extensions.
    /// </summary>
    public interface IURLFileSet
    {
        /// <summary>
        /// ext argument is case insensitive, with or without leading dot
        /// return is case sensitive and without leading dot
        /// </summary>
        string GetUrlWithExtension(string ext);

        /// <summary>
        /// ext argument is case insensitive, with or without leading dot
        /// </summary>
        bool HasUrlExtension(string ext);

        /// <summary>
        /// case sensitive and without leading dot
        /// </summary>
        IEnumerable<string> GetUrlExtensions();
    }
}
