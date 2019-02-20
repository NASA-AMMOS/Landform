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
        string cmd;
        string arguments;
        bool createNoWindow;
        bool useShellExecute;
        bool captureOutput;
        string workingDir;

        public string OutputText { get; private set; }
        public string ErrorText { get; private set; }

        public ProgramRunner(string cmd, string arguments, bool createNoWindow = true, bool useShellExecute = false, bool captureOutput = false, string workingDir = null)
        {
            this.cmd = cmd;
            this.arguments = arguments;           
            this.createNoWindow = createNoWindow;
            this.useShellExecute = useShellExecute;
            this.captureOutput = captureOutput;
            this.workingDir = workingDir;
        }

        public int Run()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = this.cmd;
            startInfo.CreateNoWindow = createNoWindow;
            startInfo.UseShellExecute = useShellExecute;
            startInfo.Arguments = this.arguments;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.RedirectStandardOutput = this.captureOutput;
            startInfo.RedirectStandardError = this.captureOutput;
            if (workingDir != null)
            {
                startInfo.WorkingDirectory = workingDir;
            }
            Process p = Process.Start(startInfo);
            if (this.captureOutput)
            {
                OutputText = p.StandardOutput.ReadToEnd();
                ErrorText = p.StandardError.ReadToEnd();
            }
            p.WaitForExit();
            int code = p.ExitCode;
            p.Close();
            return code;
        }
    }
}
