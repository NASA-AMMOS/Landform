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
                ret = Console.ReadLine().Trim();
                if (string.IsNullOrEmpty(ret) && defaultValue != null)
                {
                    ret = defaultValue;
                }
            }
            return ret;
        }
    }
}
