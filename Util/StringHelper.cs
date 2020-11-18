using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace OPS.Util
{
    public class StringHelper
    {
        /// <summary>
        /// Convert a string containing * and ? wildcard characters to a string that can be used to generate a regex
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string WildcardToRegularExpressionString(string value)
        {
            return "^" + Regex.Escape(value).Replace("\\?", ".").Replace("\\*", ".*") + "$";
        }

        public static Regex WildcardToRegularExpression(string value, RegexOptions opts = RegexOptions.None)
        {
            return new Regex(WildcardToRegularExpressionString(value), opts);
        }

        public static string ReplaceIntWildcards(string str, int value, char wildcardChar = '#')
        {
            var r = new Regex(wildcardChar + "+");
            foreach (Match m in r.Matches(str)) {
                int start = m.Index;
                int len = m.Length;
                str = str.Substring(0, start) + string.Format("{0:D" + len + "}", value) + str.Substring(start + len);
            }
            return str;
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

        public static string StripProtocol(string url, string protocol = null)
        {
            if (protocol != null && !protocol.EndsWith("://"))
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

            if (protocol == null)
            {
                return url.Substring(url.IndexOf("://") + 3);
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

        /// <summary>
        /// Normalizes slashes and optionally preserves or removes trailing slash.
        /// Lowercases protocol and host.
        /// Optionally ensures given protocol.
        /// </summary>
        public static string NormalizeUrl(string url, string protocol = null, bool preserveTrailingSlash = false)
        {
            url = NormalizeSlashes(url, preserveTrailingSlash);
            if (!string.IsNullOrEmpty(protocol))
            {
                return EnsureProtocol(url, protocol);
            }
            else
            {
                int sep = url.IndexOf("://");
                if (sep >= 0)
                {
                    int nextSlash = url.IndexOf("/", sep + 3);
                    if (nextSlash > sep + 3)
                    {
                        sep = nextSlash;
                    }
                    return url.Substring(0, sep).ToLower() + url.Substring(sep);
                }
                else
                {
                    return url;
                }
            }
        }

        public static string GetLastUrlPathSegment(string url, bool stripExtension = false)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }
            //be robust to the case that URL is actually a windows abomination, but without allocating
            int lastSlash = Math.Max(url.LastIndexOf('/'), url.LastIndexOf('\\'));
            if (stripExtension)
            {
                int lastDot = url.LastIndexOf('.');
                if (lastDot >= 0 && lastDot > lastSlash) //ok: lastSlash < 0
                {
                    url = url.Substring(0, lastDot);
                }
            }
            if (lastSlash < 0)
            {
                return url;
            }
            if (lastSlash == url.Length - 1)
            {
                return "";
            }
            return url.Substring(lastSlash + 1);
        }

        /// <summary>
        /// strips last path segment including slash
        /// </summary>
        public static string StripLastUrlPathSegment(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }
            //be robust to the case that URL is actually a windows abomination, but without allocating
            int lastSlash = Math.Max(url.LastIndexOf('/'), url.LastIndexOf('\\'));
            if (lastSlash < 0)
            {
                return url;
            }
            return url.Substring(0, lastSlash);
        }

        /// <summary>
        /// returns extension including the leading dot
        /// unless there is no extension, in which case the return is the empty string (not null)
        /// </summary>
        public static string GetUrlExtension(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }
            //be robust to the case that URL is actually a windows abomination, but without allocating
            int lastSlash = Math.Max(url.LastIndexOf('/'), url.LastIndexOf('\\'));
            int lastDot = url.LastIndexOf('.');
            if (lastDot >= 0 && lastDot > lastSlash) //ok: lastSlash < 0
            {
                return url.Substring(lastDot);
            }
            else
            {
                return "";
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
            if (lastDot >= 0 && lastDot > lastSlash) //ok: lastSlash < 0
            {
                return url.Substring(0, lastDot);
            }
            else
            {
                return url;
            }
        }

        public static string ChangeUrlExtension(string url, string ext)
        {
            return StripUrlExtension(url) + "." + ext.TrimStart('.');
        }

        public static string StripNonPrintable(string str)
        {
            //https://stackoverflow.com/a/40568888
            return Regex.Replace(str, @"\p{C}+", string.Empty);
        }

        public static string StripSuffix(string str, string sfx)
        {
            if (!string.IsNullOrEmpty(str) && !string.IsNullOrEmpty(sfx) && str.EndsWith(sfx))
            {
                return str.Substring(0, str.Length - sfx.Length);
            }
            else
            {
                return str;
            }
        }

        /// <summary>
        /// Parse a list of strings (posibly null).
        /// Returns array of zero or more whitespace trimmed non-empty substrings.
        /// </summary>
        public static string[] ParseList(string list, char sep = ',')
        {
            return (list ?? "").Split(sep).Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
        }

        public static List<string> ParseExts(string extsStr, bool bothCases = false)
        { 
            var exts = ParseList(extsStr)
                .Select(p => p.StartsWith(".") ? p : "." + p)
                .ToList();
            if (bothCases)
            {
                //this will find *.img and *.IMG but not *.iMg - balance between performance and completeness
                exts = exts.SelectMany(ext => new string[] { ext.ToLower(), ext.ToUpper() }).ToList();
            }
            return exts;
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

        public static string RemoveMultiple(string str, IEnumerable<int[]> spans)
        {
            int offset = 0;
            foreach (var span in spans.OrderBy(span => span[0]))
            {
                int start = span[0];
                int length = span[1];
                str = str.Remove(start + offset, length);
                offset -= length;
            }
            return str;
        }

        public static string RemoveMultiple(string str, params int[] spans)
        {
            if (spans.Length % 2 != 0)
            {
                throw new ArgumentException("must pass list of (start, length) pairs");
            }
            var pairs = new List<int[]>();
            for (int i = 0; i < spans.Length / 2; i++)
            {
                pairs.Add(new int[] { spans[2 * i], spans[2 * i + 1] });
            }
            return RemoveMultiple(str, pairs);
        }

        public static string SHA1(string str, bool preserveExtension = false)
        {
            string ext = preserveExtension ? GetUrlExtension(str) : "";
            var sha1 = (new SHA1Managed()).ComputeHash(Encoding.UTF8.GetBytes(str));
            return string.Concat(sha1.Select(b => b.ToString("x2"))) + ext;
        }

        public static string UppercaseFirst(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return str;
            }
            return char.ToUpper(str[0]) + str.Substring(1);
        }

        public static string Abbreviate(string str, int maxLen = 100)
        {
            return str.Length > maxLen ? (str.Substring(0, maxLen) + "...") : str;
        }

        private static double[] NEG_POW_10 = new double[]
            { 1, 1e-1, 1e-2, 1e-3, 1e-4, 1e-5, 1e-6, 1e-7, 1e-8, 1e-9 };

        /// <summary>
        /// float range is +/-1.5e-45 to +/-3.4e38 with up to 9 digits of precision
        /// parse (last) float in str, ignoring leading and trailing garbage
        /// integers to the left and right of the decimal point have the same range as in FastParseInt()
        /// fractional parts with more than 9 digits will be truncated
        /// does not parse special values like inf or nan
        /// does parse scientific notation
        /// allows numbers with or without decimal point and with or without leading sign
        /// allows no digits either before or after (but not both before and after) decimal point
        /// </summary>
        public static float FastParseFloat(string str)
        {
            int firstDigit = -1, lastDigit = -1, decimalPoint = -1, exponent = -1, sign = -1, exponentSign = -1;
            for (int i = str.Length - 1; i >= 0; i--)
            {
                bool isDigit = str[i] >= '0' && str[i] <= '9';
                if (isDigit)
                {
                    firstDigit = i;
                    if (lastDigit < 0)
                    {
                        lastDigit = i;
                    }
                }
                else if (i < lastDigit)
                {
                    if (str[i] == 'e' || str[i] == 'E')
                    {
                        if (exponent < 0 && decimalPoint < 0)
                        {
                            exponent = i;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else if (str[i] == '+' || str[i] == '-')
                    {
                        if (i > 0 && decimalPoint < 0 && exponent < 0 && exponentSign < 0 &&
                            (str[i - 1] == 'e' || str[i - 1] == 'E'))
                        {
                            exponentSign = i;
                        }
                        else
                        {
                            sign = i;
                            break;
                        }
                    }
                    else if (str[i] == '.')
                    {
                        if (decimalPoint < 0)
                        {
                            decimalPoint = i;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }

            //Console.WriteLine("str=\"{0}\", firstDigit={1}, lastDigit={2}, " +
            //                  "decimalPoint={3}, exponent={4}, sign={5}, exponentSign={6}",
            //                  str, firstDigit, lastDigit, decimalPoint, exponent, sign, exponentSign);

            if (lastDigit < 0 ||
                (exponent > 0 && !(firstDigit < exponent && exponent < lastDigit)) ||
                (sign > 0 && !(sign == firstDigit - 1)))
            {
                throw new FormatException($"error parsing float from {str}");
            }

            int exp = 0;
            if (exponent > 0)
            {
                exp = FastParseInt(str, exponent + 1, lastDigit);
                lastDigit = exponent - 1;
                if (lastDigit == decimalPoint)
                {
                    lastDigit = decimalPoint - 1;
                    decimalPoint = -1;
                }
                if (exp > 38 || exp < -45)
                {
                    throw new FormatException($"exponent out of range parsing float from {str}");
                }
            }

            int fpart = 0, flen = 0;
            if (decimalPoint >= 0 && lastDigit > decimalPoint)
            {
                int fstart = decimalPoint + 1;
                int fend = lastDigit;
                flen = fend - fstart + 1;
                if (flen > 9)
                {
                    flen = 9;
                    fend = fstart + flen - 1;
                }
                fpart = FastParseInt(str, fstart, fend);
                lastDigit = decimalPoint - 1;
            }

            int ipart = lastDigit >= firstDigit ? FastParseInt(str, firstDigit, lastDigit) : 0;

            int sgn = (sign >= 0 && str[sign] == '-') ? -1 : 1;

            //Console.WriteLine("exp={0}, fpart={1}, flen={2}, ipart={3}, sgn={4}", exp, fpart, flen, ipart, sgn);

            double ret = sgn * ipart;
            if (flen > 0)
            {
                ret += sgn * (double)fpart * NEG_POW_10[flen];
            }
            if (exp != 0)
            {
                ret *= Math.Pow(10, exp);
            }
            if (ret < float.MinValue || ret > float.MaxValue)
            {
                throw new FormatException($"overflow parsing float from {str}");
            }
            return (float)ret;
        }

        /// <summary>
        /// parses (last) int between s and e inclusive
        /// ignores leading and trailing garbage in str[s-e]
        /// int range is -2,147,483,648 to 2,147,483,647
        /// this impl can parse -1,999,999,999 to 1,999,999,999
        /// return len = number of digits in parsed int, 0 if none
        /// </summary>
        public static int FastParseInt(string str, int s, int e)
        {
            int len = 0, ret = 0, place = 0;
            for (int i = e; i >= s; i--)
            {
                if (str[i] >= '0' && str[i] <= '9')
                {
                    int digit = str[i] - '0';
                    if ((len > 10 && digit > 0) || (len > 9 && digit > 1))
                    {
                        throw new FormatException($"overflow parsing number from {str}[{s}-{e}]");
                    }
                    place = place == 0 ? 1 : 10 * place;
                    ret += digit * place;
                    len++;
                }
                else if (len > 0)
                {
                    if (str[i] == '-')
                    {
                        ret = -ret;
                    }
                    break;
                }
            }
            if (len == 0)
            {
                throw new FormatException($"error parsing number from {str}");
            }
            return ret;
        }

        public static int FastParseInt(string str)
        {
            return FastParseInt(str, 0, str.Length - 1);
        }
    }
}
