using System.Collections.Generic;

namespace JPLOPS.Util
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
