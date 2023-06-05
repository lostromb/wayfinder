using System;
using System.Collections.Generic;
using System.IO;
using Wayfinder.Common;
using Wayfinder.Common.Logger;
using Wayfinder.Common.Schemas;
using Wayfinder.DependencyResolver.Native;
using Wayfinder.DependencyResolver;

namespace Wayfinder.Console
{
    public class Program
    {
        public static void Main(string[] args)
        {
            ILogger logger = new ConsoleLogger();
            using (NativeAssemblyInspector nativeInspector = new NativeAssemblyInspector(new ConsoleLogger()))
            {
                List<IAssemblyInspector> inspectors = new List<IAssemblyInspector>();
                inspectors.Add(new NetCoreAssemblyInspector(logger, true));
                inspectors.Add(new NetFrameworkAssemblyInspectorExeWrapper(logger, new FileInfo(@".\Wayfinder.DependencyResolver.NetFramework.exe")));
                inspectors.Add(nativeInspector);

                AssemblyAnalyzer analyzer = new AssemblyAnalyzer(logger, inspectors);
                AssemblyData d = analyzer.InspectSingleAssembly(new FileInfo(@"C:\Code\Durandal\Tools\Prototype.NetCore\bin\Debug\net7.0-windows\Durandal.Extensions.NativeAudio.dll"), null);
                if (d != null)
                {
                    d.ToString();
                }

                //DirectoryInfo inputDir = new DirectoryInfo(@"C:\Code\CortanaCore\runtime\services\CortexService\service\src\bin\x64\Debug\net471");
                //DirectoryInfo inputDir = new DirectoryInfo(@"C:\Code\Durandal\target");
                //DirectoryInfo inputDir = new DirectoryInfo(@"C:\Code\cortana-ux-bing-answers\services\BingAnswers\src\Service\bin\x64\Debug\net461");
                //ISet<DependencyGraphNode> graph = analyzer.BuildDependencyGraph(inputDir, null);

                //System.Console.WriteLine("Got full dependency graph of {0} nodes.", graph.Count);
                //bool anyErrors = false;
                //foreach (DependencyGraphNode node in graph)
                //{
                //    if (node.Errors != null && node.Errors.Count > 0)
                //    {
                //        anyErrors = true;
                //        if (!string.IsNullOrEmpty(node.ThisAssembly.AssemblyFullName))
                //        {
                //            System.Console.WriteLine(node.ThisAssembly.AssemblyFullName);
                //        }
                //        else
                //        {
                //            System.Console.WriteLine(node.ThisAssembly.AssemblyBinaryName);
                //        }

                //        foreach (string error in node.Errors)
                //        {
                //            System.Console.Write("        ");
                //            System.Console.WriteLine(error);
                //        }
                //    }
                //}

                //if (!anyErrors)
                //{
                //    System.Console.WriteLine("No errors detected.");
                //}
            }
        }
    }
}
