using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wayfinder.DependencyResolver
{
    public static class ProcessInvoker
    {
        public static List<string> RunProcessAndReturnOutput(string processName, string args)
        {
            List<string> returnVal = new List<string>();
            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                FileName = processName,
                Arguments = args,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true
            };

            Process runningProcess = Process.Start(processInfo);

            using (BufferedStream processOutput = new BufferedStream(runningProcess.StandardOutput.BaseStream, 131072))
            using (BufferedStream processInput = new BufferedStream(runningProcess.StandardInput.BaseStream, 131072))
            using (StreamReader outputReader = new StreamReader(processOutput))
            {
                while (!outputReader.EndOfStream)
                {
                    returnVal.Add(outputReader.ReadLine());
                }
            }

            runningProcess.WaitForExit();
            return returnVal;
        }
    }
}
