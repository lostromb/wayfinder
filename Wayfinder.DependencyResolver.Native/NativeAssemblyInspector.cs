using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Wayfinder.Common;
using Wayfinder.Common.Logger;
using Wayfinder.Common.Schemas;

namespace Wayfinder.DependencyResolver.Native
{
    public class NativeAssemblyInspector : IAssemblyInspector, IDisposable
    {
        private readonly ILogger _logger;

        public NativeAssemblyInspector(ILogger logger)
        {
            _logger = logger ?? NullLogger.Singleton;

            // Unpack native helper assemblies (dumpbin.exe)
            try
            {
                File.WriteAllBytes("dumpbin.exe", NativeAssemblies.dumpbin);
                File.WriteAllBytes("link.exe", NativeAssemblies.link);
                File.WriteAllBytes("mspdbcore.dll", NativeAssemblies.mspdbcore);
            }
            catch (Exception)
            {
                _logger.Log("Failed to load native DLL inspection tools. Reflection on native DLLs will not be supported", LogLevel.Wrn);
            }
        }

        public string InspectorName => "Native Assembly Inspector";

        public void Dispose()
        {
            try
            {
                if (File.Exists("dumpbin.exe")) File.Delete("dumpbin.exe");
                if (File.Exists("link.exe")) File.Delete("link.exe");
                if (File.Exists("mspdbcore.dll")) File.Delete("mspdbcore.dll");
            }
            catch (Exception)
            {
                _logger.Log("Failed to dispose of native DLL inspection tools", LogLevel.Wrn);
            }
        }

        public AssemblyData InspectAssemblyFile(FileInfo assemblyFile)
        {
            if (!assemblyFile.Exists)
            {
                throw new FileNotFoundException("Input file " + assemblyFile.FullName + " not found!", assemblyFile.FullName);
            }

            List<string> nativeInspectionReport = ProcessInvoker.RunProcessAndReturnOutputAsLines("dumpbin.exe", "/HEADERS /DEPENDENTS \"" + assemblyFile.FullName + "\"");
            if (nativeInspectionReport == null ||
                nativeInspectionReport.Count == 0 ||
                !nativeInspectionReport.Contains("FILE HEADER VALUES"))
            {
                // Not a native library
                return new AssemblyData()
                {
                    AssemblyFilePath = assemblyFile,
                    AssemblyType = BinaryType.Unknown,
                    LoaderError = "File is not a native executable",
                    AssemblyBinaryName = Path.GetFileNameWithoutExtension(assemblyFile.FullName)
                };
            }

            AssemblyData returnVal = new AssemblyData();
            returnVal.AssemblyFilePath = assemblyFile;
            returnVal.AssemblyType = BinaryType.Native;
            returnVal.LoaderError = string.Empty;
            returnVal.AssemblyBinaryName = Path.GetFileNameWithoutExtension(returnVal.AssemblyFilePath.FullName);

            // Find the part of the native report that shows identity
            foreach (string header in nativeInspectionReport
                .SkipWhile((s) => !s.Contains("FILE HEADER VALUES"))
                .Skip(1)
                .TakeWhile((s) => !string.IsNullOrWhiteSpace(s)))
            {
                if (header.Contains("machine (x64)"))
                {
                    returnVal.Platform = BinaryPlatform.AMD64;
                }
                else if (header.Contains("machine (x86)"))
                {
                    returnVal.Platform = BinaryPlatform.X86;
                }
            }

            // Find the part of the native report that shows dependencies
            foreach (string dependency in nativeInspectionReport
                .SkipWhile((s) => !s.Contains("Image has the following dependencies:"))
                .Skip(2)
                .TakeWhile((s) => !string.IsNullOrWhiteSpace(s))
                .Select((s) => s.Trim()))
            {
                returnVal.ReferencedAssemblies.Add(new AssemblyReferenceName()
                {
                    AssemblyBinaryName = Path.GetFileNameWithoutExtension(dependency).ToLowerInvariant(),
                    ReferencedAssemblyVersion = null,
                    ReferenceType = AssemblyReferenceType.Native,
                    AssemblyFullName = null
                });
            }

            return returnVal;
        }

        //if (inspectionType.HasFlag(AssemblyInspectionType.ListMembers))
        //{
        //    report.Add("===ASSEMBLY EXPORTS===");
        //    if (loadFromAssembly == null)
        //    {
        //        report.Add("Failed to load the assembly (or one of its referenced dependencies) to determine exported types");
        //    }
        //    else
        //    {
        //        try
        //        {
        //            foreach (Type exportedType in loadFromAssembly.GetExportedTypes().OrderBy((t) => t.FullName))
        //            {
        //                if (exportedType.IsInterface)
        //                {
        //                    report.Add("INTERFACE " + exportedType.FullName);
        //                }
        //                else if (exportedType.IsValueType)
        //                {
        //                    report.Add("STRUCT " + exportedType.FullName);
        //                }
        //                else if (exportedType.IsClass)
        //                {
        //                    if (exportedType.IsAbstract)
        //                    {
        //                        report.Add("ABSTRACT CLASS " + exportedType.FullName);
        //                    }
        //                    else
        //                    {
        //                        report.Add("CLASS " + exportedType.FullName);
        //                    }
        //                }
        //                else
        //                {
        //                    report.Add("TYPE " + exportedType.FullName);
        //                }

        //                foreach (PropertyInfo prop in exportedType.GetProperties().OrderBy((t) => t.Name))
        //                {
        //                    bool publicGetter = prop.GetMethod != null && prop.GetMethod.IsPublic;
        //                    bool publicSetter = prop.SetMethod != null && prop.SetMethod.IsPublic;

        //                    if (publicGetter && publicSetter)
        //                    {
        //                        report.Add("    PROPERTY " + prop.ToString() + " { get; set; }");
        //                    }
        //                    else if (publicGetter)
        //                    {
        //                        report.Add("    PROPERTY " + prop.ToString() + " { get; }");
        //                    }
        //                    else if (publicSetter)
        //                    {
        //                        report.Add("    PROPERTY " + prop.ToString() + " { set; }");
        //                    }
        //                    else
        //                    {
        //                        report.Add("    PROPERTY " + prop.ToString() + " { }");
        //                    }
        //                }

        //                foreach (FieldInfo field in exportedType.GetFields().OrderBy((t) => t.Name))
        //                {
        //                    report.Add("    FIELD " + field.ToString());
        //                }

        //                foreach (EventInfo eventInfo in exportedType.GetEvents().OrderBy((t) => t.Name))
        //                {
        //                    report.Add("    EVENT " + eventInfo.ToString());
        //                }

        //                foreach (MethodInfo method in exportedType.GetMethods().OrderBy((t) => t.Name))
        //                {
        //                    report.Add("    METHOD " + method.ToString());
        //                }
        //            }
        //        }
        //        catch (Exception e)
        //        {
        //            report.Add(e.GetType().ToString() + ": " + e.Message);
        //        }
        //    }

        //    report.Add(string.Empty);
        //}
    }
}
