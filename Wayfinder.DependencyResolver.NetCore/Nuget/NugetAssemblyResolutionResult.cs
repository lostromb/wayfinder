using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wayfinder.DependencyResolver.Nuget
{
    public class NugetAssemblyResolutionResult
    {
        public NugetPackageIdentity SourcePackage { get; set; }
        public FileInfo AssemblyFile { get; set; }

        public NugetAssemblyResolutionResult(NugetPackageIdentity sourcePackage, FileInfo assemblyFile)
        { 
            SourcePackage = sourcePackage;
            AssemblyFile = assemblyFile;
        }
    }
}
