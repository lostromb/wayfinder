using System;
using System.Collections.Generic;
using System.IO;
using Wayfinder.DependencyResolver;
using Wayfinder.DependencyResolver.Logger;
using Wayfinder.DependencyResolver.Schemas;

namespace Wayfinder.Console
{
    public class Program
    {
        public static void Main(string[] args)
        {
            using (AssemblyInspector inspector = new AssemblyInspector(new ConsoleLogger()))
            {
                AssemblyData d = inspector.InspectSingleAssembly(new FileInfo(@"C:\Code\Durandal\target\netcoreapp3.1\Durandal.dll"), null);

                //DirectoryInfo inputDir = new DirectoryInfo(@"C:\Code\CortanaCore\runtime\services\CortexService\service\src\bin\x64\Debug\net471");
                //DirectoryInfo inputDir = new DirectoryInfo(@"C:\Code\Durandal\target");
                DirectoryInfo inputDir = new DirectoryInfo(@"C:\Code\Durandal\target\netcoreapp3.1\");
                ISet<DependencyGraphNode> graph = inspector.BuildDependencyGraph(inputDir, null);

                System.Console.WriteLine("Got full dependency graph of {0} nodes.", graph.Count);
                bool anyErrors = false;
                foreach (DependencyGraphNode node in graph)
                {
                    if (node.Errors != null && node.Errors.Count > 0)
                    {
                        anyErrors = true;
                        if (!string.IsNullOrEmpty(node.ThisAssembly.AssemblyFullName))
                        {
                            System.Console.WriteLine(node.ThisAssembly.AssemblyFullName);
                        }
                        else
                        {
                            System.Console.WriteLine(node.ThisAssembly.AssemblyBinaryName);
                        }

                        foreach (string error in node.Errors)
                        {
                            System.Console.Write("        ");
                            System.Console.WriteLine(error);
                        }
                    }
                }

                if (!anyErrors)
                {
                    System.Console.WriteLine("No errors detected.");
                }
            }
        }
    }
}
