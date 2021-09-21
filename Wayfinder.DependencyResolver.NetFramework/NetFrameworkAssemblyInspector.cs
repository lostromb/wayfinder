using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting;
using System.Text;
using System.Threading.Tasks;
using Wayfinder.Common.Logger;
using Wayfinder.Common.Nuget;
using Wayfinder.Common.Schemas;

namespace Wayfinder.DependencyResolver.NetFramework
{
    public class NetFrameworkAssemblyInspector
    {
        private readonly ILogger _logger;
        private readonly bool _useAppDomains;

        public NetFrameworkAssemblyInspector(ILogger logger, bool useAppDomains)
        {
            _logger = logger ?? NullLogger.Singleton;
            _useAppDomains = useAppDomains;
        }

        public AssemblyData InspectSingleAssembly(FileInfo assemblyFile)
        {
            if (!assemblyFile.Exists)
            {
                throw new FileNotFoundException("Input file " + assemblyFile.FullName + " not found!", assemblyFile.FullName);
            }

            //Debug.WriteLine("Inspecting " + assemblyFile.Name);
            AssemblyData returnVal;
            if (_useAppDomains)
            {
                returnVal = InspectSingleAssemblyWithAppDomain(assemblyFile);
            }
            else
            {
                returnVal = InspectSingleAssemblyWithoutAppDomain(assemblyFile);
            }

            return returnVal;
        }

        private AssemblyData InspectSingleAssemblyWithoutAppDomain(FileInfo assemblyFile)
        {
            AppDomainAssemblyLoader proxy = new AppDomainAssemblyLoader();
            byte[] serializedAssemblyData = proxy.Process(assemblyFile.FullName);
            AssemblyData returnVal = AssemblyData.Deserialize(serializedAssemblyData);
            return returnVal;
        }

        private AssemblyData InspectSingleAssemblyWithAppDomain(FileInfo assemblyFile)
        {
            AppDomainSetup setup = new AppDomainSetup();
            AppDomain domain = AppDomain.CreateDomain("AssemblyProbe_" + Guid.NewGuid().ToString("N").Substring(0, 8), null, setup);
            try
            {
                ObjectHandle handle = domain.CreateInstanceFrom(Assembly.GetExecutingAssembly().Location, typeof(AppDomainAssemblyLoader).FullName);
                AppDomainAssemblyLoader proxy = (AppDomainAssemblyLoader)handle.Unwrap();
                byte[] serializedAssemblyData = proxy.Process(assemblyFile.FullName);
                AssemblyData returnVal = AssemblyData.Deserialize(serializedAssemblyData);
                return returnVal;
            }
            finally
            {
                AppDomain.Unload(domain);
            }
        }
    }
}
