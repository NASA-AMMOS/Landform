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
        public static String WildCardToRegularExressionString(String value)
        {
            return "^" + Regex.Escape(value).Replace("\\?", ".").Replace("\\*", ".*") + "$";
        }

        public static Regex WildCardToRegularExression(String value)
        {
            return new Regex(WildCardToRegularExressionString(value));
        }
    }

}
