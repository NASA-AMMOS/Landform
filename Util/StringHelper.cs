using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace JPLOPS.Util
{
    public class StringHelper
    {
        /// <summary>
        /// Convert a string containing * and ? wildcard characters to a string that can be used to generate a regex
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string WildcardToRegularExpressionString(string value, bool fullMatch = true,
                                                               bool matchSlashes = true,
                                                               bool allowAlternation = false)
        {
            string any = matchSlashes ? "." : @"[^/\\]";
            string regex = Regex.Escape(value).Replace("\\?", any).Replace("\\*", any + "*");
            if (allowAlternation) {
                regex = regex.Replace("\\(", "(").Replace("\\)", ")").Replace("\\|", "|");
            }
            return fullMatch ? ("^" + regex + "$") : regex;
        }

        public static Regex WildcardToRegularExpression(string value, bool fullMatch = true, bool matchSlashes = true,
                                                        bool allowAlternation = false,
                                                        RegexOptions opts = RegexOptions.None)
        {
            return new Regex(WildcardToRegularExpressionString(value, fullMatch, matchSlashes, allowAlternation), opts);
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
            if (string.IsNullOrEmpty(str))
            {
                return str;
            }
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

        public static float[] ParseFloatListSafe(string list, char sep = ',')
        {
            string[] fl = ParseList(list, sep);
            try
            {
                float[] ret = new float[fl.Length];
                for (int i = 0; i < fl.Length; i++)
                {
                    ret[i] = float.Parse(fl[i], CultureInfo.InvariantCulture);
                }
                return ret;
            }
            catch (FormatException)
            {
                return null;
            }
            catch (OverflowException)
            {
                return null;
            }
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

        public static string hashHex40Char(string str, bool preserveExtension = false)
        {
            string ext = preserveExtension ? GetUrlExtension(str) : "";
            //HCL AppScan reports Cryptography.InsecureAlgorithm if we use SHA1...
            var hash = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(str)).Take(20).ToArray();
            return string.Concat(hash.Select(b => b.ToString("x2"))) + ext;
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

        public static string SnakeCase(string str)
        {
            str = str.Replace('-','_').Replace('.','_');
            str = Regex.Replace(str, @"\s+", "_");
            str = Regex.Replace(str, @"([^A-Z_])([A-Z])", "$1_$2"); //FooBar -> Foo_Bar, S3Foo -> S3_Foo, AtB -> At_B
            str = Regex.Replace(str, @"([^_])([A-Z][^A-Z_])", "$1_$2"); //RDRFoo -> RDR_Foo
            return str;
        }

        private static double[] NEG_POW_10 = new double[]
        {
            1,
            1e-1, 1e-2, 1e-3, 1e-4, 1e-5, 1e-6, 1e-7, 1e-8, 1e-9, 1e-10,
            1e-11, 1e-12, 1e-13, 1e-14, 1e-15, 1e-16, 1e-17, 1e-18, 1e-19, 1e-20
        };

        /// <summary>
        /// float range is +/-1.5e-45 to +/-3.4e38 with up to 9 digits of precision
        /// parse (first) float in str, ignoring leading and trailing garbage
        /// allows up to 18 digits before and after decimal point and exponents from -45 to 38
        /// does not parse special values like inf or nan
        /// allows numbers with or without leading sign, decimal point, and exponent
        /// allows no digits either before or after (but not both before and after) decimal point
        /// </summary>
        public static float FastParseFloat(string str)
        {
            //int range is -2,147,483,648 to 2,147,483,647
            //long range is -9,223,372,036,854,775,808 to 9,223,372,036,854,775,807
            //using long here gives less than 1% perf penalty but allows parsing the full range of float
            int maxlen = 18;
            long ipart = 0, fpart = 0, epart = 0;
            //int maxlen = 9;
            //int ipart = 0, fpart = 0, epart = 0;
            
            int sign = -1, firstDigit = -1, decimalPoint = -1, exponent = -1, exponentSign = -1;
            int ilen = 0, flen = 0, elen = 0;

            for (int i = 0; i < str.Length; i++)
            {
                if (str[i] == '+' || str[i] == '-')
                {
                    if (sign < 0 && firstDigit < 0)
                    {
                        sign = i;
                    }
                    else if (exponentSign < 0 && exponent >= 0 && i == (exponent + 1))
                    {
                        exponentSign = i;
                    }
                    else
                    {
                        break;
                    }
                }
                else if (str[i] == '.')
                {
                    if (decimalPoint < 0 && exponent < 0)
                    {
                        decimalPoint = i;
                    }
                    else
                    {
                        break;
                    }
                }
                else if (str[i] == 'e' || str[i] == 'E')
                {
                    if (exponent < 0 && firstDigit >= 0)
                    {
                        exponent = i;
                    }
                    else
                    {
                        break;
                    }
                }
                else if (str[i] >= '0' && str[i] <= '9')
                {
                    if (firstDigit < 0)
                    {
                        firstDigit =  i;
                    }

                    if (decimalPoint < 0 && exponent < 0)
                    {
                        if (ilen < maxlen)
                        {
                            ipart = ipart * 10 + (str[i] - '0');
                            ilen++;
                        }
                        else
                        {
                            throw new FormatException($"overflow parsing float from {str}");
                        }
                    }
                    else if (exponent < 0)
                    {
                        if (flen < maxlen)
                        {
                            fpart = fpart * 10 + (str[i] - '0');
                            flen++;
                        }
                    }
                    else
                    {
                        if (elen < maxlen)
                        {
                            epart = epart * 10 + (str[i] - '0');
                            elen++;
                        }
                        else
                        {
                            throw new FormatException($"overflow parsing float from {str}");
                        }
                    }
                }
                else if (sign >= 0 || firstDigit >= 0 || decimalPoint >= 0)
                {
                    break;
                }
            }

            //Console.WriteLine("str={0}, sign={1}, firstDigit={2}, decimalPoint={3}, exponent={4}, exponentSign={5}",
            //                  str, sign, firstDigit, decimalPoint, exponent, exponentSign);
            //Console.WriteLine("ipart={0}, ilen={1}, fpart={2}, flen={3}, epart={4}, elen={5}",
            //                  ipart, ilen, fpart, flen, epart, elen);

            if (firstDigit < 0 || (ilen == 0 && flen == 0) || (exponent >= 0 && elen == 0))
            {
                throw new FormatException($"error parsing float from {str}");
            }

            if (sign >= 0 && str[sign] == '-')
            {
                ipart = -ipart;
                fpart = -fpart;
            }

            double ret = ipart;

            if (flen > 0)
            {
                ret += ((double)fpart) * NEG_POW_10[flen];
            }

            if (epart != 0)
            {
                if (exponentSign >= 0 && str[exponentSign] == '-')
                {
                    epart = -epart;
                }
                if (epart > 38 || epart < -45)
                {
                    throw new FormatException($"exponent out of range parsing float from {str}");
                }
                ret *= Math.Pow(10, epart);
            }

            if (ret < float.MinValue || ret > float.MaxValue)
            {
                throw new FormatException($"overflow parsing float from {str}");
            }

            return (float)ret;
        }

        /// <summary>
        /// int range is -2,147,483,648 to 2,147,483,647
        /// parses (first) int in str
        /// ignores leading and trailing garbage
        /// </summary>
        public static int FastParseInt(string str)
        {
            //using long here gives less than 1% perf penalty but allows parsing the full range of int
            int maxlen = 18;
            long ret = 0;
            //int maxlen = 9;
            //int ret = 0;

            int sign = -1, len = 0;
            for (int i = 0; i < str.Length; i++)
            {
                if (sign < 0 && len == 0 && (str[i] == '-' || str[i] == '+'))
                {
                    sign = i;
                }
                else if (str[i] >= '0' && str[i] <= '9')
                {
                    if (len < maxlen)
                    {
                        ret = ret * 10 + (str[i] - '0');
                        len++;
                    }
                    else
                    {
                        throw new FormatException($"overflow parsing number from {str}");
                    }
                }
                else if (len > 0 || sign >= 0)
                {
                    break;
                }
            }
            if (len == 0)
            {
                throw new FormatException($"error parsing number from {str}");
            }
            if (sign >= 0 && str[sign] == '-')
            {
                ret = -ret;
            }
            if (ret > int.MaxValue || ret < int.MinValue)
            {
                throw new FormatException($"overflow error parsing int from {str}");
            }
            return (int)ret;
        }
    }
}
