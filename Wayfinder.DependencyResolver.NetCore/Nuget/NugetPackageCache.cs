using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Wayfinder.DependencyResolver.Schemas;

namespace Wayfinder.DependencyResolver.Nuget
{
    public class NugetPackageCache
    {
        /// <summary>
        /// Maps from nuget package identity -> the list of assembly names found within that package, _minus file extensions_ (e.g. "System.Numerics")
        /// </summary>
        private readonly IDictionary<NugetPackageIdentity, ISet<FileInfo>> _packageFileIndex;

        private readonly IDictionary<FileInfo, string> _md5Cache;

        public NugetPackageCache() : this(new DirectoryInfo[] { new DirectoryInfo(Environment.GetEnvironmentVariable("UserProfile") + "\\.nuget\\packages") })
        {
        }

        public NugetPackageCache(IEnumerable<DirectoryInfo> nugetDirectories)
        {
            _packageFileIndex = new Dictionary<NugetPackageIdentity, ISet<FileInfo>>();
            _md5Cache = new Dictionary<FileInfo, string>();

            // Index all the package directories
            foreach (DirectoryInfo packageRootFolder in nugetDirectories)
            {
                if (!packageRootFolder.Exists)
                {
                    continue;
                }

                foreach (DirectoryInfo packageFolder in packageRootFolder.EnumerateDirectories())
                {
                    foreach (DirectoryInfo packageVersionFolder in packageFolder.EnumerateDirectories())
                    {
                        if (packageVersionFolder.Name.Contains('.') &&
                            char.IsDigit(packageVersionFolder.Name[0]))
                        {
                            foreach (FileInfo packageContentFile in packageVersionFolder.EnumerateFiles("*", SearchOption.AllDirectories))
                            {
                                if (string.Equals(packageContentFile.Extension, ".dll", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(packageContentFile.Extension, ".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    NugetPackageIdentity thisPackage = new NugetPackageIdentity()
                                    {
                                        PackageName = packageFolder.Name,
                                        PackageVersion = packageVersionFolder.Name,
                                    };

                                    if (!_packageFileIndex.ContainsKey(thisPackage))
                                    {
                                        _packageFileIndex[thisPackage] = new HashSet<FileInfo>();
                                    }

                                    _packageFileIndex[thisPackage].Add(packageContentFile);
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Resolves an assembly name (with or without extension) plus an exact file hash into a list of potentially multiple nuget packages which contain that assembly file.
        /// </summary>
        /// <param name="assemblyName">The name of the assembly to look for, with or without extension e.g. "System.Numerics.dll"</param>
        /// <param name="fileMD5Hash">The MD5 hash of the exact file that you are looking for, or null if hash is not known.</param>
        /// <returns></returns>
        public IList<NugetAssemblyResolutionResult> ResolveAssembly(string assemblyName, string fileMD5Hash)
        {
            List<NugetAssemblyResolutionResult> returnVal = new List<NugetAssemblyResolutionResult>();
            assemblyName = TrimAssemblyFileExtension(assemblyName);

            foreach (KeyValuePair<NugetPackageIdentity, ISet<FileInfo>> nugetPackage in _packageFileIndex)
            {
                foreach (FileInfo assemblyFile in nugetPackage.Value)
                {
                    string fileNameWithoutExtension = TrimAssemblyFileExtension(assemblyFile.Name);
                    if (string.Equals(fileNameWithoutExtension, assemblyName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrEmpty(fileMD5Hash))
                        {
                            // No MD5 check
                            returnVal.Add(new NugetAssemblyResolutionResult(nugetPackage.Key, assemblyFile));
                        }
                        else
                        {
                            // Check MD5
                            string candidateMd5 = GetMD5HashOfFile(assemblyFile);
                            if (string.Equals(fileMD5Hash, candidateMd5, StringComparison.OrdinalIgnoreCase))
                            {
                                returnVal.Add(new NugetAssemblyResolutionResult(nugetPackage.Key, assemblyFile));
                            }
                        }
                    }
                }
            }

            return returnVal;
        }

        /// <summary>
        /// Removes .exe and .dll suffix on a file name
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        private static string TrimAssemblyFileExtension(string fileName)
        {
            if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return fileName.Substring(0, fileName.LastIndexOf('.'));
            }
            else
            {
                return fileName;
            }
        }

        private string GetMD5HashOfFile(FileInfo fileName)
        {
            string returnVal;
            if (!_md5Cache.TryGetValue(fileName, out returnVal))
            {
                MD5 hasher = MD5.Create();
                hasher.Initialize();
                StringBuilder builder = new StringBuilder();
                using (FileStream stream = new FileStream(fileName.FullName, FileMode.Open, FileAccess.Read))
                {
                    byte[] hash = hasher.ComputeHash(stream);
                    stream.Close();
                    foreach (byte x in hash)
                    {
                        builder.Append(x.ToString("X"));
                    }
                }

                returnVal = builder.ToString().ToLowerInvariant();
            }

            return returnVal;
        }
    }
}
