using System;
using System.Diagnostics;
using System.Text;

namespace JPLOPS.Util
{
    /// <summary>
    /// Helper methods for executing external programs
    /// </summary>
    public class ProgramRunner
    {
        private string cmd;
        private string arguments;
        private bool createNoWindow;
        private bool useShellExecute;
        private bool captureOutput;
        private string workingDir;
        private bool waitForExit;

        public string OutputText { get; private set; }
        public string ErrorText { get; private set; }

        public ProgramRunner(string cmdAndArgs, bool createNoWindow = true, bool useShellExecute = false,
                             bool captureOutput = false, string workingDir = null, bool waitForExit = true)
            : this(GetCommand(cmdAndArgs), GetArgs(cmdAndArgs),
                   createNoWindow, useShellExecute, captureOutput, workingDir, waitForExit)
        {
        }

        public static string GetCommand(string cmdAndArgs)
        {
            char[] chars = cmdAndArgs.ToCharArray();
            int firstChar = Array.FindIndex(chars, c => !char.IsWhiteSpace(c));
            if (firstChar >= 0)
            {
                int firstSpace = Array.FindIndex(chars, firstChar, c => char.IsWhiteSpace(c));
                if (firstSpace > firstChar)
                {
                    return cmdAndArgs.Substring(firstChar, firstSpace - firstChar);
                }
                else
                {
                    return cmdAndArgs;
                }
            }
            return "";
        }

        public static string GetArgs(string cmdAndArgs)
        {
            char[] chars = cmdAndArgs.ToCharArray();
            int firstChar = Array.FindIndex(chars, c => !char.IsWhiteSpace(c));
            if (firstChar >= 0)
            {
                int firstSpace = Array.FindIndex(chars, firstChar, c => char.IsWhiteSpace(c));
                if (firstSpace > firstChar)
                {
                    return cmdAndArgs.Substring(firstSpace);
                }
                else
                {
                    return "";
                }
            }
            return "";
        }

        public ProgramRunner(string cmd, string arguments, bool createNoWindow = true, bool useShellExecute = false,
                             bool captureOutput = false, string workingDir = null, bool waitForExit = true)
        {
            this.cmd = cmd;
            this.arguments = arguments;           
            this.createNoWindow = createNoWindow;
            this.useShellExecute = useShellExecute;
            this.captureOutput = captureOutput;
            this.workingDir = workingDir;
            this.waitForExit = waitForExit;
        }

        public int Run(Action<Process> callback = null)
        {
            var process = new Process();

            process.StartInfo.FileName = cmd;
            process.StartInfo.CreateNoWindow = createNoWindow;
            process.StartInfo.UseShellExecute = useShellExecute;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            if (workingDir != null)
            {
                process.StartInfo.WorkingDirectory = workingDir;
            }
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

            process.Start();

            //this is deadlock prone
            //https://csharp.today/how-to-avoid-deadlocks-when-reading-redirected-child-console-in-c-part-2
            //OutputText = process.StandardOutput.ReadToEnd();
            //ErrorText = process.StandardError.ReadToEnd();

            //docs say to (1) assign the *DataReceived handlers; (2) start the process; (3) Begin*ReadLine()
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (callback != null)
            {
                callback(process);
            }

            if (!waitForExit)
            {
                return 0;
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
