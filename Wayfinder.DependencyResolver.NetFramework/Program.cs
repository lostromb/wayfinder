using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wayfinder.Common.Logger;
using Wayfinder.Common.Schemas;

namespace Wayfinder.DependencyResolver.NetFramework
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("Error: This exe is meant to be used programmatically, and expects a single argument of one file path for the file to analyze");
                Environment.Exit(-1);
                return;
            }

            FileInfo fileToInspect = new FileInfo(args[0]);
            NetFrameworkAssemblyInspector inspector = new NetFrameworkAssemblyInspector(NullLogger.Singleton, false);
            AssemblyData returnVal = inspector.InspectSingleAssembly(fileToInspect);
            using (Stream stdOut = Console.OpenStandardOutput())
            using (BinaryWriter writer = new BinaryWriter(stdOut))
            {
                returnVal.Serialize(writer);
                Environment.Exit(0);
            }
        }
    }
}
