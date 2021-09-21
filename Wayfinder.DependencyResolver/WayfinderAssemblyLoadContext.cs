
namespace Wayfinder.DependencyResolver
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using Wayfinder.Common.Logger;

    public class WayfinderPluginLoadContext : AssemblyLoadContext
    {
        private readonly ILogger _logger;
        private readonly DirectoryInfo _runtimeDirectory;
        private readonly DirectoryInfo _containerDirectory;
        private readonly string _binaryExtension;

        public WayfinderPluginLoadContext(ILogger logger, DirectoryInfo runtimeDirectory, DirectoryInfo containerDirectory)
        {
            _logger = logger;
            _containerDirectory = containerDirectory;
            _runtimeDirectory = runtimeDirectory;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _binaryExtension = ".dll";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                _binaryExtension = ".so";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                _binaryExtension = ".dylib";
            }
            else
            {
                _binaryExtension = ".dll";
            }
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            FileInfo assemblyPath = LookForAssembly(assemblyName);
            if (assemblyPath != null)
            {
                _logger.Log("Resolved container assembly binding " + assemblyName.ToString() + " to " + assemblyPath.FullName, LogLevel.Vrb);
                return LoadFromAssemblyPath(assemblyPath.FullName);
            }
            else
            {
                //_logger.Log("Failed to resolve container assembly binding " + assemblyName.ToString(), LogLevel.Err);
            }

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string libraryPath = _runtimeDirectory.FullName + Path.DirectorySeparatorChar + unmanagedDllName + _binaryExtension;

            if (System.IO.File.Exists(libraryPath))
            {
                _logger.Log("Resolved runtime assembly binding " + unmanagedDllName + " to " + libraryPath, LogLevel.Vrb);
                return LoadUnmanagedDllFromPath(libraryPath);
            }
            else
            {
                libraryPath = _containerDirectory.FullName + Path.DirectorySeparatorChar + unmanagedDllName + _binaryExtension;
                if (System.IO.File.Exists(libraryPath))
                {
                    _logger.Log("Resolved container assembly binding " + unmanagedDllName + " to " + libraryPath, LogLevel.Vrb);
                    return LoadUnmanagedDllFromPath(libraryPath);
                }
                else
                {
                    _logger.Log("Failed to resolve container native assembly binding " + unmanagedDllName, LogLevel.Err);
                }
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Performs exhaustive search in the current directory (top-level only) for the specific assembly
        /// </summary>
        /// <param name="searchDirectory"></param>
        /// <param name="assemblyName"></param>
        /// <returns></returns>
        private FileInfo LookForAssembly(AssemblyName assemblyName)
        {
            if (!_containerDirectory.Exists)
            {
                return null;
            }

            // Check first in the runtime directory, then in the container directory
            FileInfo exactNameMatch = new FileInfo(_runtimeDirectory.FullName + Path.DirectorySeparatorChar + assemblyName.Name + _binaryExtension);
            if (exactNameMatch.Exists)
            {
                return exactNameMatch;
            }

            exactNameMatch = new FileInfo(_containerDirectory.FullName + Path.DirectorySeparatorChar + assemblyName.Name + _binaryExtension);
            if (exactNameMatch.Exists)
            {
                return exactNameMatch;
            }

            //foreach (FileInfo assemblyFile in _containerDirectory.EnumerateFiles("*" + _binaryExtension, SearchOption.TopDirectoryOnly))
            //{
            //    Assembly reflectionAssembly = Assembly.ReflectionOnlyLoadFrom(assemblyFile.FullName);
            //    if (reflectionAssembly.GetName() == assemblyName)
            //    {
            //        return assemblyFile;
            //    }
            //}

            return null;
        }
    }
}
