using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Util
{
    /// <summary>
    /// Helper methods for executing external programs
    /// </summary>
    public class ProgramRunner
    {
        /// <summary>
        /// Helper method to execute a program.
        /// Ignores output
        /// </summary>
        /// <param name="executableFilename"></param>
        /// <param name="arguments"></param>
        public static void Run(string executableFilename, string arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = executableFilename;
            startInfo.CreateNoWindow = true;
            startInfo.UseShellExecute = false;
            startInfo.Arguments = arguments;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            Process p = Process.Start(startInfo);
            p.WaitForExit();
            p.Close();
        }
    }
}
