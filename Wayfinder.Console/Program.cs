using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wayfinder.DependencyResolver;
using Wayfinder.DependencyResolver.Logger;

namespace Wayfinder.Console
{
    public class Program
    {
        public static void Main(string[] args)
        {
            using (AssemblyInspector inspector = new AssemblyInspector(new ConsoleLogger()))
            {
                //DirectoryInfo inputDir = new DirectoryInfo(@"C:\Code\CortanaCore\runtime\services\CortexService\service\src\bin\x64\Debug\net471");
                //DirectoryInfo inputDir = new DirectoryInfo(@"C:\Code\Durandal\target");
                DirectoryInfo inputDir = new DirectoryInfo(@"C:\Code\WebCrawler\bin");
                ISet<DependencyGraphNode> graph = inspector.BuildDependencyGraph(inputDir, null);
            }
        }
    }
}
