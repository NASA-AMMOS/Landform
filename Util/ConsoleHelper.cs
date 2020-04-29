using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OPS.Util
{
    public class ConsoleHelper
    {
        public static string Prompt(string prompt, string fixedValue = null, string defaultValue = null,
                                    bool forceInteractive = false)
        {
            if (fixedValue != null)
            {
                return fixedValue;
            }
            if (defaultValue != null && !forceInteractive)
            {
                return defaultValue;
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

        public static int Prompt(string prompt, int? fixedValue, int? defaultValue, bool forceInteractive = false)
        {
            string fv = fixedValue.HasValue ? fixedValue.ToString() : null;
            string dv = defaultValue.HasValue ? defaultValue.ToString() : null;
            int? ret = null;
            while (!ret.HasValue)
            {
                ret = StringHelper.ParseIntSafe(Prompt(prompt, fv, dv, forceInteractive));
            }
            return ret.Value;
        }

        public static int Prompt(string prompt, string fixedValue, int? defaultValue, bool forceInteractive = false)
        {
            return Prompt(prompt, StringHelper.ParseIntSafe(fixedValue), defaultValue, forceInteractive);
        }

        public static int Prompt(string prompt, int? fixedValue, string defaultValue, bool forceInteractive = false)
        {
            return Prompt(prompt, fixedValue, StringHelper.ParseIntSafe(defaultValue), forceInteractive);
        }

        public static bool Prompt(string prompt, bool? fixedValue, bool? defaultValue, bool forceInteractive = false)
        {
            string fv = fixedValue.HasValue ? fixedValue.ToString() : null;
            string dv = defaultValue.HasValue ? defaultValue.ToString() : null;
            bool? ret = null;
            while (!ret.HasValue)
            {
                ret = StringHelper.ParseBoolSafe(Prompt(prompt, fv, dv, forceInteractive));
            }
            return ret.Value;
        }

        public static bool Prompt(string prompt, string fixedValue, bool? defaultValue, bool forceInteractive = false)
        {
            return Prompt(prompt, StringHelper.ParseBoolSafe(fixedValue), defaultValue, forceInteractive);
        }

        public static bool Prompt(string prompt, bool? fixedValue, string defaultValue, bool forceInteractive = false)
        {
            return Prompt(prompt, fixedValue, StringHelper.ParseBoolSafe(defaultValue), forceInteractive);
        }
    }
}
