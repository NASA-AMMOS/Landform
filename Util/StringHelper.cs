using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace OPS.Util
{
    public class StringHelper
    {
        /// <summary>
        /// Convert a string containing * and ? wildcard characters to a string that can be used to generate a regex
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string WildCardToRegularExressionString(string value)
        {
            return "^" + Regex.Escape(value).Replace("\\?", ".").Replace("\\*", ".*") + "$";
        }

        public static Regex WildCardToRegularExression(string value)
        {
            return new Regex(WildCardToRegularExressionString(value));
        }

        public static string EnsureTrailingSlash(string str)
        {
            return str.EndsWith("/") ? str : (str + "/");
        }

        public static string NormalizeSlashes(string str, bool preserveTrailingSlash = false)
        {
            str = str.Replace('\\', '/');
            return preserveTrailingSlash ? str : str.TrimEnd(new char[] { '/' });
        }

        public static string EnsureProtocol(string url, string protocol)
        {
            if (!protocol.EndsWith("://"))
            {
                protocol += "://";
            }

            if (url == null)
            {
                url = "";
            }
                
            if (!url.Contains("://"))
            {
                return protocol + url;
            }
            else if (!url.ToLower().StartsWith(protocol.ToLower()))
            {
                throw new Exception(string.Format("expected url \"{0}\" to start with \"{1}\"", url, protocol));
            }

            return url;
        }

        public static string StripProtocol(string url, string protocol)
        {
            if (!protocol.EndsWith("://"))
            {
                protocol += "://";
            }

            if (url == null)
            {
                url = "";
            }
                
            if (!url.Contains("://"))
            {
                return url;
            }
            else if (url.ToLower().StartsWith(protocol))
            {
                return url.Substring(protocol.Length);
            }
            else
            {
                return url;
            }
        }

        public static string NormalizeUrl(string url, string protocol = null, bool preserveTrailingSlash = false)
        {
            url = NormalizeSlashes(url, preserveTrailingSlash);
            return !string.IsNullOrEmpty(protocol) ? EnsureProtocol(url, protocol) : url;
        }

        public static string GetLastUrlPathSegment(string url, bool stripExtension = false)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }
            int lastSlash = url.LastIndexOf('/'); 
            if (lastSlash < 0)
            {
                return url;
            }
            if (lastSlash == url.Length - 1)
            {
                return "";
            }
            string ret = url.Substring(lastSlash + 1);
            if (!stripExtension)
            {
                return ret;
            }
            else
            {
                int lastDot = ret.LastIndexOf('.');
                return lastDot < 0 ? ret : ret.Substring(0, lastDot);
            }
        }

        public static string GetUrlExtension(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }
            //be robust to the case that URL is actually a windows abomination, but without allocating
            int lastSlash = Math.Max(url.LastIndexOf('/'), url.LastIndexOf('\\'));
            int lastDot = url.LastIndexOf('.');
            if (lastDot > lastSlash)
            {
                return url.Substring(lastDot);
            }
            else
            {
                return null;
            }
        }

        public static string StripUrlExtension(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }
            //be robust to the case that URL is actually a windows abomination, but without allocating
            int lastSlash = Math.Max(url.LastIndexOf('/'), url.LastIndexOf('\\'));
            int lastDot = url.LastIndexOf('.');
            if (lastDot > lastSlash)
            {
                return url.Substring(0, lastDot);
            }
            else
            {
                return url;
            }
        }

        public static string StripNonPrintable(string str)
        {
            //https://stackoverflow.com/a/40568888
            return Regex.Replace(str, @"\p{C}+", string.Empty);
        }

        public static int? ParseIntSafe(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return null;
            }
            int ret = 0;
            if (Int32.TryParse(str, out ret))
            {
                return ret;
            }
            return null;
        }

        public static bool? ParseBoolSafe(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return null;
            }
            bool ret = false;
            if (bool.TryParse(str, out ret))
            {
                return ret;
            }
            return null;
        }

        private double ParsePercent(string val, double total)
        {
            if (val.EndsWith("%"))
            {
                return double.Parse(val.Substring(0, val.Length - 1)) * 0.01 * total;
            }
            else
            {
                return double.Parse(val);
            }
        }

        public static string CollapseWhitespace(string str)
        {
            return !string.IsNullOrEmpty(str) ? Regex.Replace(str, @"\s+", " ") : str;
        }

        public static string CommonPrefix(IEnumerable<string> values)
        {
            return new string(values.First().Substring(0, values.Min(s => s.Length))
                              .TakeWhile((c, i) => values.All(s => s[i] == c)).ToArray());
        }
    }
}
