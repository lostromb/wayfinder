using System;
using System.Collections.Generic;
using System.IO;
using Wayfinder.DependencyResolver;
using Wayfinder.DependencyResolver.Logger;
using Wayfinder.DependencyResolver.Schemas;

namespace Wayfinder.Console.NetCore
{
    public class Program
    {
        public static void Main(string[] args)
        {
            using (AssemblyInspector inspector = new AssemblyInspector(new ConsoleLogger()))
            {
                AssemblyData response = inspector.InspectSingleAssembly(new FileInfo(@"C:\Code\Durandal\packages\ManagedBass.3.0.0\lib\netstandard1.4\ManagedBass.dll"), null);
                response.GetHashCode();
                //DirectoryInfo inputDir = new DirectoryInfo(@"C:\Code\CortanaCore\runtime\services\CortexService\service\src\bin\x64\Debug\net471");
                //DirectoryInfo inputDir = new DirectoryInfo(@"C:\Code\Durandal\target");
                //DirectoryInfo inputDir = new DirectoryInfo(@"C:\Code\WebCrawler\bin");
                //ISet<DependencyGraphNode> graph = inspector.BuildDependencyGraph(inputDir, null);
            }
        }
    }
}
