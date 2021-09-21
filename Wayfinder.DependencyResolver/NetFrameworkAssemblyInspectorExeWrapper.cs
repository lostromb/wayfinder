using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wayfinder.Common;
using Wayfinder.Common.Logger;
using Wayfinder.Common.Schemas;

namespace Wayfinder.DependencyResolver
{
    public class NetFrameworkAssemblyInspectorExeWrapper : IAssemblyInspector
    {
        private readonly ILogger _logger;
        private readonly FileInfo _exePath;

        public NetFrameworkAssemblyInspectorExeWrapper(ILogger logger, FileInfo exePath)
        {
            _logger = logger ?? NullLogger.Singleton;
            _exePath = exePath;
            if (_exePath == null)
            {
                throw new ArgumentNullException(nameof(exePath));
            }
        }

        public string InspectorName => ".Net Framework (Process Isolated) Inspector";

        public AssemblyData InspectAssemblyFile(FileInfo assemblyFile)
        {
            if (File.Exists(_exePath.FullName))
            {
                ProcessStartInfo processInfo = new ProcessStartInfo
                {
                    FileName = _exePath.FullName,
                    Arguments = "\"" + assemblyFile.FullName + "\"",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true
                };

                Process runningProcess = Process.Start(processInfo);

                using (BufferedStream processOutput = new BufferedStream(runningProcess.StandardOutput.BaseStream, 131072))
                using (BufferedStream processInput = new BufferedStream(runningProcess.StandardInput.BaseStream, 131072))
                using (BinaryReader reader = new BinaryReader(processOutput))
                {
                    AssemblyData returnVal = AssemblyData.Deserialize(reader);
                    runningProcess.WaitForExit();
                    return returnVal;
                }
            }
            else
            {
                _logger.Log("Assembly inspector helper " + _exePath.FullName + " not found!", LogLevel.Err);
                return null;
            }
        }
    }
}
