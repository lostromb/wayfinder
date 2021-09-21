using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Wayfinder.Common;
using Wayfinder.Common.Logger;
using Wayfinder.Common.Schemas;

namespace Wayfinder.DependencyResolver
{
    public class NetCoreAssemblyInspector : IAssemblyInspector
    {
        private readonly bool _useIsolatedLoadContext;
        private readonly ILogger _logger;

        public NetCoreAssemblyInspector(ILogger logger, bool useIsolatedLoadContext)
        {
            _logger = logger ?? NullLogger.Singleton;
            _useIsolatedLoadContext = useIsolatedLoadContext;
        }

        public string InspectorName => ".Net Core Inspector";

        public AssemblyData InspectAssemblyFile(FileInfo assemblyFile)
        {
            if (_useIsolatedLoadContext)
            {
                return InspectSingleAssemblyWithLoadContext(assemblyFile);
            }
            else
            {
                return InspectSingleAssemblyWithoutLoadContext(assemblyFile);
            }
        }

        private AssemblyData InspectSingleAssemblyWithoutLoadContext(FileInfo assemblyFile)
        {
            NetCoreAssemblyLoaderProxy proxy = new NetCoreAssemblyLoaderProxy();
            byte[] serializedAssemblyData = proxy.Process(assemblyFile.FullName);
            AssemblyData returnVal = AssemblyData.Deserialize(serializedAssemblyData);
            return returnVal;
        }

        private AssemblyData InspectSingleAssemblyWithLoadContext(FileInfo assemblyFile)
        {
            WayfinderPluginLoadContext loadContext = new WayfinderPluginLoadContext(_logger, runtimeDirectory: new DirectoryInfo(Environment.CurrentDirectory), containerDirectory: assemblyFile.Directory);
            AssemblyName assemblyName = typeof(NetCoreAssemblyLoaderProxy).Assembly.GetName();
            Assembly containerHostAssembly = loadContext.LoadFromAssemblyName(assemblyName);
            if (containerHostAssembly == null)
            {
                _logger.Log("Could not find entry point dll " + assemblyName + " to use to create load context guest.", LogLevel.Err);
                return null;
            }

            string containerGuestTypeName = typeof(NetCoreAssemblyLoaderProxy).FullName;
            Type containerGuestType = containerHostAssembly.ExportedTypes.FirstOrDefault(t => string.Equals(t.FullName, containerGuestTypeName));
            object containerGuest = Activator.CreateInstance(containerGuestType);
            if (containerGuest == null)
            {
                _logger.Log("Error while creating remoting proxy to load context container.", LogLevel.Err);
                return null;
            }

            // For some reason we run into troubles when just trying to cast the returned object as an IContainerGuest (probably because the defining assemblies of the interface are different).
            // So we have to use reflection to find the initialization method and invoke it
            MethodInfo initializeMethodSig = containerGuest.GetType().GetMethod(nameof(NetCoreAssemblyLoaderProxy.Process), new Type[] { typeof(string) });
            if (initializeMethodSig == null)
            {
                _logger.Log("Error while looking for Process method on load context container.", LogLevel.Err);
                return null;
            }

            _logger.Log("Initializing load context guest for " + assemblyFile.FullName, LogLevel.Vrb);
            // Make sure we set the AssemblyLoadContext.CurrentContextualReflectionContext at initialization
            using (var scope = loadContext.EnterContextualReflection())
            {
                object uncastReturnVal = initializeMethodSig.Invoke(containerGuest, new object[] { assemblyFile.FullName });
                byte[] serializedAssemblyData = uncastReturnVal as byte[];
                if (serializedAssemblyData == null)
                {
                    _logger.Log("Error while parsing response AssemblyData from remote container host.", LogLevel.Err);
                    return null;
                }

                AssemblyData returnVal = AssemblyData.Deserialize(serializedAssemblyData);
                return returnVal;
            }
        }
    }
}
