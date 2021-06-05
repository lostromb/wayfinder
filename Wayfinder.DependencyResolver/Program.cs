//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.IO;
//using System.Linq;
//using System.Reflection;
//using System.Runtime.Remoting;
//using System.Text;
//using System.Threading.Tasks;

//namespace Wayfinder.DependencyResolver
//{
//    public class Program
//    {
//        public static void Main(string[] args)
//        {
//            bool recursive = false;
//            AssemblyInspectionType inspectionAction = AssemblyInspectionType.ShowIdentity | AssemblyInspectionType.ListReferences;
//            string scanTarget = Environment.CurrentDirectory;
//            FileStream fileOut = null;
//            TextWriter outputStream = Console.Out;

//            try
//            {
//                // Unpack native helper assemblies (dumpbin.exe)
//                try
//                {
//                    File.WriteAllBytes("dumpbin.exe", NativeAssemblies.dumpbin);
//                    File.WriteAllBytes("link.exe", NativeAssemblies.link);
//                    File.WriteAllBytes("mspdbcore.dll", NativeAssemblies.mspdbcore);
//                }
//                catch (Exception)
//                {
//                    Console.WriteLine("Warning: Failed to load native DLL inspection tools. Reflection on native DLLs will not be supported");
//                }

//                // Is the scanning target a file?
//                FileInfo targetFile = new FileInfo(scanTarget);
//                if (targetFile.Exists)
//                {
//                    // Just inspect the one file
//                    InspectSingleAssemblyWithAppDomain(targetFile, inspectionAction, outputStream);
//                }
//                else
//                {
//                    // Otherwise enumerate all assemblies in the directory (recursively, if configured)
//                    DirectoryInfo currentDir = new DirectoryInfo(scanTarget);

//                    if (currentDir.Exists)
//                    {
//                        foreach (FileInfo dllFile in currentDir.EnumerateFiles("*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).OrderBy((t) => t.Name))
//                        {
//                            if (string.Equals("dumpbin.exe", dllFile.Name, StringComparison.OrdinalIgnoreCase) ||
//                                string.Equals("link.exe", dllFile.Name, StringComparison.OrdinalIgnoreCase) ||
//                                string.Equals("mspdbcore.dll", dllFile.Name, StringComparison.OrdinalIgnoreCase) ||
//                                string.Equals(Process.GetCurrentProcess().ProcessName + ".exe", dllFile.Name, StringComparison.OrdinalIgnoreCase))
//                            {
//                                continue;
//                            }

//                            if (string.Equals(dllFile.Extension, ".dll", StringComparison.OrdinalIgnoreCase) ||
//                                string.Equals(dllFile.Extension, ".exe", StringComparison.OrdinalIgnoreCase))
//                            {
//                                InspectSingleAssemblyWithAppDomain(dllFile, inspectionAction, outputStream);
//                            }
//                        }
//                    }
//                    else
//                    {
//                        Console.WriteLine("Error: Input file or directory \"" + scanTarget + "\" does not exist!");
//                    }
//                }
//            }
//            finally
//            {
//                outputStream.Close();
//                outputStream.Dispose();
//                fileOut?.Dispose();

//                // Delete native helper assemblies
//                if (File.Exists("dumpbin.exe")) File.Delete("dumpbin.exe");
//                if (File.Exists("link.exe")) File.Delete("link.exe");
//                if (File.Exists("mspdbcore.dll")) File.Delete("mspdbcore.dll");
//            }
//        }

//        private static void InspectSingleAssembly(FileInfo assemblyFile, AssemblyInspectionType inspectionType, TextWriter reportOut)
//        {
//            if (Debugger.IsAttached)
//            {
//                InspectSingleAssemblyWithoutAppDomain(assemblyFile, inspectionType, reportOut);
//            }
//            else
//            {
//                InspectSingleAssemblyWithAppDomain(assemblyFile, inspectionType, reportOut);
//            }
//        }

//        private static void InspectSingleAssemblyWithoutAppDomain(FileInfo assemblyFile, AssemblyInspectionType inspectionType, TextWriter reportOut)
//        {
//            reportOut.WriteLine("Inspecting assembly " + assemblyFile.FullName);

//            AssemblyLoaderProxy proxy = new AssemblyLoaderProxy();
//            proxy.TryLoad(assemblyFile.FullName);
//            foreach (string reportingLine in proxy.Process())
//            {
//                reportOut.WriteLine(reportingLine);
//            }

//            reportOut.WriteLine();
//            reportOut.WriteLine();
//        }

//        private static void InspectSingleAssemblyWithAppDomain(FileInfo assemblyFile, AssemblyInspectionType inspectionType, TextWriter reportOut)
//        {
//            reportOut.WriteLine("Inspecting assembly " + assemblyFile.FullName);

//            AppDomainSetup setup = new AppDomainSetup();
//            AppDomain domain = AppDomain.CreateDomain("AssemblyProbe_" + Guid.NewGuid().ToString("N").Substring(0, 8), null, setup);
//            try
//            {
//                ObjectHandle handle = domain.CreateInstanceFrom(Assembly.GetExecutingAssembly().Location, typeof(AssemblyLoaderProxy).FullName);
//                AssemblyLoaderProxy proxy = (AssemblyLoaderProxy)handle.Unwrap();
//                proxy.TryLoad(assemblyFile.FullName);

//                foreach (string reportingLine in proxy.Process())
//                {
//                    reportOut.WriteLine(reportingLine);
//                }
//            }
//            finally
//            {
//                AppDomain.Unload(domain);
//            }

//            reportOut.WriteLine();
//            reportOut.WriteLine();
//        }
//    }
//}
