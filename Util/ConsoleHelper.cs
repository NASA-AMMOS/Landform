using System;
using System.Runtime;
using System.Diagnostics;
using System.Management;
using System.Threading.Tasks;

namespace JPLOPS.Util
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

        public static void Shutdown()
        {
            //https://stackoverflow.com/a/44614907
            //https://stackoverflow.com/a/65347291
            Process.Start(new ProcessStartInfo("shutdown", "/s /f /t 0")
            { CreateNoWindow = true, UseShellExecute = false });
        }

        public static void Reboot()
        {
            //https://stackoverflow.com/a/44614907
            //https://stackoverflow.com/a/102580
            //https://stackoverflow.com/a/65347291
            Process.Start(new ProcessStartInfo("shutdown", "/r /f /t 0")
            { CreateNoWindow = true, UseShellExecute = false });
        }


        public static void GC(bool includeLOH = true)
        {
            if (includeLOH)
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            }
            System.GC.Collect();
        }

        public static int GetPID()
        {
            try
            {
                using (var proc = Process.GetCurrentProcess())
                {
                    return proc.Id;
                }
            }
            catch (Exception)
            {
                return -1;
            }
        }

        public static string GetMemoryUsage()
        {
            long heap = -1;
            try
            {
                heap = System.GC.GetTotalMemory(false);
            }
            catch (Exception)
            {
                //ignore
            }

            long priv = -1, phys = -1, physPeak = -1;
            try
            {
                using (var proc = Process.GetCurrentProcess())
                {
                    priv = proc.PrivateMemorySize64;
                    phys = proc.WorkingSet64;
                    physPeak = proc.PeakWorkingSet64;
                    //long virtualBytes = proc.VirtualMemorySize64;
                    //long virtualBytesPeak = proc.PeakVirtualMemorySize64;
                    //long pagedBytes = proc.PagedMemorySize64;
                    //long pagedBytesPeak = proc.PeakPagedMemorySize64;
                }
            }
            catch (Exception)
            {
                //ignore
            }

            long totPhys = -1, freePhys = -1, totVirt = -1, freeVirt = -1;
            try
            {
                var searcher = new ManagementObjectSearcher(new ObjectQuery("SELECT * FROM Win32_OperatingSystem"));
                foreach (var obj in searcher.Get())
                {
                    totPhys = Math.Max(totPhys, GetBytes(obj, "TotalVisibleMemorySize"));
                    freePhys = Math.Max(freePhys, GetBytes(obj, "FreePhysicalMemory"));
                    totVirt = Math.Max(totVirt, GetBytes(obj, "TotalVirtualMemorySize"));
                    freeVirt = Math.Max(freeVirt, GetBytes(obj, "FreeVirtualMemory"));
                    //totSwap = Math.Max(totSwap, GetBytes(obj, "TotalSwapSpaceSize"));
                    //freeSwap = Math.Max(freeSwap, GetBytes(obj, "FreeSpaceInPagingFiles"));
                }
            }
            catch (Exception)
            {
                //ignore
            }

            return string.Format("proc used: {0}, {1} heap, {2} phys ({3} pk), " +
                                 "sys free: {4}/{5} phys, {6}/{7} virt", Fmt.Bytes(priv), Fmt.Bytes(heap),
                                 Fmt.Bytes(phys), Fmt.Bytes(physPeak), Fmt.Bytes(freePhys), Fmt.Bytes(totPhys),
                                 Fmt.Bytes(freeVirt), Fmt.Bytes(totVirt));
        }

        private static long GetBytes(ManagementBaseObject obj, string key)
        {
            try
            {
                return long.Parse(obj[key].ToString()) * 1024L;
            }
            catch (Exception)
            {
                return -1;
            }
        }
        
        public static long GetTotalSystemVirtualMemory()
        {
            long totVirt = -1;
            try
            {
                var searcher = new ManagementObjectSearcher(new ObjectQuery("SELECT * FROM Win32_OperatingSystem"));
                foreach (var obj in searcher.Get())
                {
                    totVirt = Math.Max(totVirt, GetBytes(obj, "TotalVirtualMemorySize"));
                }
            }
            catch (Exception)
            {
                //ignore
            }
            return totVirt;
        }

        public static long GetFreeSystemVirtualMemory()
        {
            long freeVirt = -1;
            try
            {
                var searcher = new ManagementObjectSearcher(new ObjectQuery("SELECT * FROM Win32_OperatingSystem"));
                foreach (var obj in searcher.Get())
                {
                    freeVirt = Math.Max(freeVirt, GetBytes(obj, "FreeVirtualMemory"));
                }
            }
            catch (Exception)
            {
                //ignore
            }
            return freeVirt;
        }

        public static void Exit(int exitCode)
        {
            //Environment.Exit() may not actually exit if
            // a) there are foreground threads still running
            //    (and apparently it's nontrivial to force all threads in the Thread.Run() pool to be background??)
            // b) we were not called from the main thread
            //https://stackoverflow.com/a/52861663/4970315
            var p = Process.GetCurrentProcess();
            if (p != null)
            {
                Task.Delay(new TimeSpan(0, 0, 10)).ContinueWith(_ => { p.Kill(); });
            }

            Environment.Exit(exitCode);
        }

        public static bool[] CheckProcesses(ILogger logger, params string[] names)
        {
            if (logger != null)
            {
                logger.LogInfo("checking {0} processes: {1}", names.Length, String.Join(", ", names));
            }
            bool[] ret = new bool[names.Length];
            if (names.Length == 0)
            {
                return ret;
            }
            var processes = Process.GetProcesses();
            try
            {
                foreach (var p in processes)
                {
                    bool requested = false;
                    bool hasExited = false;
                    String name = null;
                    int id = -1;
                    try
                    {
                        id = p.Id;
                        name = p.ProcessName;
                        hasExited = p.HasExited; //can throw access denied if p is elevated and we are not
                    }
                    catch (Exception ex)
                    {
                        if (logger != null)
                        {
                            logger.LogWarn("error getting info for process {0} {1}: {2}", id, name, ex.Message);
                        }
                    }
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (name == names[i])
                        {
                            requested = true;
                            ret[i] |= !hasExited;
                        }
                    }
                    if (logger != null)
                    {
                        logger.LogInfo("process {0} {1}: {2}{3}", id, name, hasExited ? "exited" : "running",
                                       requested ? " (requested)" : "");
                    }
                }
            }
            finally
            {
                foreach (var p in processes)
                {
                    p.Dispose();
                }
            }
            return ret;
        }

        public static bool[] CheckProcesses(params string[] names)
        {
            return CheckProcesses(null, names);
        }

        public static void AtExit(Action handler)
        {
            Console.CancelKeyPress += delegate { handler(); };
        }
    }
}
