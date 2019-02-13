using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OPS.Util
{
    public class ConsoleHelper
    {
        public static string Prompt(string prompt, string fixedValue = null, string defaultValue = null)
        {
            if (fixedValue != null)
            {
                Console.WriteLine(prompt + ": " + fixedValue);
                return fixedValue;
            }
            string ret = null;
            while (string.IsNullOrEmpty(ret))
            {
                Console.Write(prompt + (defaultValue != null ? " [" + defaultValue + "]" : "") + ": ");
                //sometimes a cut and paste will include control chars
                ret = StringHelper.StripNonPrintable(Console.ReadLine().Trim());
                if (string.IsNullOrEmpty(ret) && defaultValue != null)
                {
                    ret = defaultValue;
                }
            }
            return ret;
        }

        public static int Prompt(string prompt, int? fixedValue, int? defaultValue)
        {
            string fv = fixedValue.HasValue ? fixedValue.ToString() : null;
            string dv = defaultValue.HasValue ? defaultValue.ToString() : null;
            int? ret = null;
            while (!ret.HasValue)
            {
                ret = StringHelper.ParseIntSafe(Prompt(prompt, fv, dv));
            }
            return ret.Value;
        }

        public static int Prompt(string prompt, string fixedValue, int? defaultValue)
        {
            return Prompt(prompt, StringHelper.ParseIntSafe(fixedValue), defaultValue);
        }

        public static int Prompt(string prompt, int? fixedValue, string defaultValue)
        {
            return Prompt(prompt, fixedValue, StringHelper.ParseIntSafe(defaultValue));
        }
    }
}
