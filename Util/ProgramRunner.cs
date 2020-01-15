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

        public ProgramRunner(string cmd, string arguments, bool createNoWindow = true, bool useShellExecute = false,
                             bool captureOutput = false, string workingDir = null)
        {
            this.cmd = cmd;
            this.arguments = arguments;           
            this.createNoWindow = createNoWindow;
            this.useShellExecute = useShellExecute;
            this.captureOutput = captureOutput;
            this.workingDir = workingDir;
        }

        public int Run(Action<Process> callback = null)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = cmd;
            startInfo.CreateNoWindow = createNoWindow;
            startInfo.UseShellExecute = useShellExecute;
            startInfo.Arguments = arguments;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            if (workingDir != null)
            {
                startInfo.WorkingDirectory = workingDir;
            }
            Process process = Process.Start(startInfo);

            //this is deadlock prone
            //https://csharp.today/how-to-avoid-deadlocks-when-reading-redirected-child-console-in-c-part-2
            //OutputText = p.StandardOutput.ReadToEnd();
            //ErrorText = p.StandardError.ReadToEnd();

            var osb = new StringBuilder();
            var esb = new StringBuilder();
            process.OutputDataReceived += (_, evt) => {
                if (string.IsNullOrEmpty(evt.Data))
                {
                    return;
                }
                if (evt.Data.IndexOf("error:", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    esb.AppendLine(evt.Data);
                }
                if (captureOutput)
                {
                    osb.AppendLine(evt.Data);
                }
                else
                {
                    Console.WriteLine(evt.Data);
                }
            };
            process.ErrorDataReceived += (_, evt) => {
                if (string.IsNullOrEmpty(evt.Data))
                {
                    return;
                }
                esb.AppendLine(evt.Data);
                if (!captureOutput)
                {
                    Console.Error.WriteLine(evt.Data);
                }
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (callback != null)
            {
                callback(process);
            }

            process.WaitForExit();

            if (captureOutput)
            {
                OutputText = osb.ToString();
            }
            ErrorText = esb.ToString();

            int code = process.ExitCode;
            process.Close();
            return code;
        }
    }
}
