using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wayfinder.Common.Logger;
using Wayfinder.Common.Schemas;

namespace Wayfinder.DependencyResolver
{
    public class BindingEmulator
    {
        public static bool AttemptBind(
            AssemblyData candidateAssembly,
            string targetAssemblyName,
            BinaryType targetBinaryType,
            Version targetVersion,
            string bindingRedirectCodeBasePath,
            ILogger logger)
        {
            // Check assembly name
            if (!string.Equals(targetAssemblyName, candidateAssembly.AssemblyBinaryName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Check binary type
            if (targetBinaryType != candidateAssembly.AssemblyType)
            {
                logger.Log(string.Format("Failed bind to {0}: Wrong binary type (expected {1} got {2})",
                    candidateAssembly.AssemblyFilePath == null ? candidateAssembly.AssemblyBinaryName : candidateAssembly.AssemblyFilePath.FullName,
                    Enum.GetName(typeof(BinaryType), targetBinaryType),
                    Enum.GetName(typeof(BinaryType), candidateAssembly.AssemblyType)),
                    LogLevel.Wrn);
                return false;
            }

            // Check major version match
            if (targetVersion != null &&
                candidateAssembly.AssemblyVersion != null &&
                candidateAssembly.AssemblyVersion.Major != targetVersion.Major)
            {
                logger.Log(string.Format("While binding to {0}: Major version mismatch (expected {1} got {2})",
                    candidateAssembly.AssemblyFilePath == null ? candidateAssembly.AssemblyBinaryName : candidateAssembly.AssemblyFilePath.FullName,
                    targetVersion,
                    candidateAssembly.AssemblyVersion),
                    LogLevel.Wrn);
                // don't actually treat it as an error though
                //return false;
            }

            // Check codeBase restraints
            if (!string.IsNullOrEmpty(bindingRedirectCodeBasePath))
            {
                FileInfo expectedFileName = new FileInfo(Path.Combine(candidateAssembly.AssemblyFilePath.DirectoryName, bindingRedirectCodeBasePath));
                if (expectedFileName != candidateAssembly.AssemblyFilePath)
                {
                    logger.Log(string.Format("Failed bind to {0}: codeBase mismatch (expected {1})",
                        candidateAssembly.AssemblyFilePath == null ? candidateAssembly.AssemblyBinaryName : candidateAssembly.AssemblyFilePath.FullName,
                        expectedFileName),
                        LogLevel.Wrn);
                    return false;
                }
            }

            return true;
        }

        public static bool CanBindReferenceExactly(AssemblyReferenceName reference, AssemblyData candidateAssembly)
        {
            return string.Equals(reference.AssemblyBinaryName, candidateAssembly.AssemblyBinaryName, StringComparison.OrdinalIgnoreCase) &&
                reference.ReferencedAssemblyVersion == candidateAssembly.AssemblyVersion;
        }

        public static bool IsCrossFrameworkReferenceLegal(DotNetFrameworkVersion sourceFramework, DotNetFrameworkVersion targetFramework)
        {
            if (sourceFramework.FrameworkType == DotNetFrameworkType.Unknown ||
                targetFramework.FrameworkType == DotNetFrameworkType.Unknown)
            {
                return true;
            }

            if (sourceFramework.FrameworkType == targetFramework.FrameworkType)
            {
                // Within the same framework just check for higher version
                return sourceFramework.FrameworkVersion >= targetFramework.FrameworkVersion;
            }
            else if (targetFramework.FrameworkType == DotNetFrameworkType.NetStandard)
            {
                // Apply .Net Standard dependency binding rules here
                // https://learn.microsoft.com/en-us/dotnet/standard/net-standard
                if (targetFramework.FrameworkVersion == DotNetFrameworkVersion.VERSION_1_0 ||
                    targetFramework.FrameworkVersion == DotNetFrameworkVersion.VERSION_1_1)
                {
                    return true;
                }
                else if (targetFramework.FrameworkVersion == DotNetFrameworkVersion.VERSION_1_2)
                {
                    return (sourceFramework.FrameworkType == DotNetFrameworkType.NetFramework && sourceFramework.FrameworkVersion >= DotNetFrameworkVersion.VERSION_4_5_1) ||
                        (sourceFramework.FrameworkType == DotNetFrameworkType.NetCore);
                }
                else if (targetFramework.FrameworkVersion == DotNetFrameworkVersion.VERSION_1_3)
                {
                    return (sourceFramework.FrameworkType == DotNetFrameworkType.NetFramework && sourceFramework.FrameworkVersion >= DotNetFrameworkVersion.VERSION_4_6) ||
                        (sourceFramework.FrameworkType == DotNetFrameworkType.NetCore);
                }
                else if (targetFramework.FrameworkVersion == DotNetFrameworkVersion.VERSION_1_4 ||
                    targetFramework.FrameworkVersion == DotNetFrameworkVersion.VERSION_1_5 ||
                    targetFramework.FrameworkVersion == DotNetFrameworkVersion.VERSION_1_6)
                {
                    return (sourceFramework.FrameworkType == DotNetFrameworkType.NetFramework && sourceFramework.FrameworkVersion >= DotNetFrameworkVersion.VERSION_4_6_1) ||
                        (sourceFramework.FrameworkType == DotNetFrameworkType.NetCore);
                }
                else if (targetFramework.FrameworkVersion == DotNetFrameworkVersion.VERSION_2_0)
                {
                    return (sourceFramework.FrameworkType == DotNetFrameworkType.NetFramework && sourceFramework.FrameworkVersion >= DotNetFrameworkVersion.VERSION_4_6_1) ||
                        (sourceFramework.FrameworkType == DotNetFrameworkType.NetCore && sourceFramework.FrameworkVersion >= DotNetFrameworkVersion.VERSION_2_0);
                }
                else if (targetFramework.FrameworkVersion == DotNetFrameworkVersion.VERSION_2_1)
                {
                    return sourceFramework.FrameworkType == DotNetFrameworkType.NetCore && sourceFramework.FrameworkVersion >= DotNetFrameworkVersion.VERSION_3_0;
                }
                else
                {
                    throw new NotImplementedException($"Unknown .Net Standard version {targetFramework.FrameworkVersion}");
                }
            }
            else if (targetFramework.FrameworkType == DotNetFrameworkType.NetFramework)
            {
                // NetCore can depend on a NetFramework assembly (in addition to another NetFramework assembly which case was already handled)
                // TODO: What are the binding rules exactly? Can a NetCore 3.1 assembly depend on NetFx 4.8.0?
                // This also doesn't handle compatibility mode which allows .Net Standard to reference Net Framework https://learn.microsoft.com/en-us/dotnet/core/porting/third-party-deps#net-framework-compatibility-mode
                return sourceFramework.FrameworkType == DotNetFrameworkType.NetCore;
            }
            else if (targetFramework.FrameworkType == DotNetFrameworkType.NetCore)
            {
                // Only a .Net Core assembly can depend on another .Net Core assembly.
                return false;
            }
           
            throw new NotImplementedException($"Hit an edge case when trying to determine legality of dependency {sourceFramework}=>{targetFramework}");
        }
    }
}
