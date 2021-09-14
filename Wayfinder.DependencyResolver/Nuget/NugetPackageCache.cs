using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wayfinder.DependencyResolver.Schemas;

namespace Wayfinder.DependencyResolver.Nuget
{
    public class NugetPackageCache
    {
        /// <summary>
        /// Maps from nuget package identity -> the list of assembly names found within that package, _minus file extensions_ (e.g. "System.Numerics")
        /// </summary>
        private readonly FastConcurrentDictionary<NugetPackageIdentity, ISet<FileInfo>> _packageFileIndex;
        private readonly FastConcurrentDictionary<FileInfo, string> _md5Cache;
        private readonly IList<DirectoryInfo> _nugetDirectories;
        private readonly FileInfo _tempCacheFile;
        private readonly object _cacheFileLock = new object();

        public NugetPackageCache(bool allowPersistentCache) : this(new DirectoryInfo[] { new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\.nuget\\packages") }, allowPersistentCache)
        {
        }

        public NugetPackageCache(IEnumerable<DirectoryInfo> nugetDirectories, bool allowPersistentCache)
        {
            _packageFileIndex = new FastConcurrentDictionary<NugetPackageIdentity, ISet<FileInfo>>();
            _md5Cache = new FastConcurrentDictionary<FileInfo, string>();
            _nugetDirectories = new List<DirectoryInfo>(nugetDirectories);
            if (allowPersistentCache)
            {
                _tempCacheFile = new FileInfo(Path.Combine(Path.GetTempPath(), "wayfinder_md5_cache.bin"));
            }
            else
            {
                _tempCacheFile = null;
            }
        }

        public void Initialize()
        {
            // Index all the package directories

            // Load a persistent cache from temp directory if available
            ReadTempCache();

            foreach (DirectoryInfo packageRootFolder in _nugetDirectories)
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

                                    ISet<FileInfo> fileSet;
                                    _packageFileIndex.TryGetValueOrSet(thisPackage, out fileSet, () => new HashSet<FileInfo>());

                                    if (!fileSet.Contains(packageContentFile))
                                    {
                                        _packageFileIndex[thisPackage].Add(packageContentFile);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public void CommitCache()
        {
            WriteTempCache();
        }

        private void ReadTempCache()
        {
            if (_tempCacheFile != null && File.Exists(_tempCacheFile.FullName))
            {
                lock (_cacheFileLock)
                using (Stream fileReadStream = new FileStream(_tempCacheFile.FullName, FileMode.Open, FileAccess.Read))
                using (BinaryReader reader = new BinaryReader(fileReadStream, Encoding.UTF8))
                {
                    int numEntries = reader.ReadInt32();
                    for (int c = 0; c < numEntries; c++)
                    {
                        string fileName = reader.ReadString();
                        string md5 = reader.ReadString();
                        _md5Cache[new FileInfo(fileName)] = md5;
                    }
                }
            }
        }

        private void WriteTempCache()
        {
            if (_tempCacheFile != null)
            {
                lock (_cacheFileLock)
                using (Stream fileWriteStream = new FileStream(_tempCacheFile.FullName, FileMode.Create, FileAccess.Write))
                using (BinaryWriter writer = new BinaryWriter(fileWriteStream, Encoding.UTF8))
                {
                    writer.Write(_md5Cache.Count);
                    foreach (var kvp in _md5Cache)
                    {
                        writer.Write(kvp.Key.FullName);
                        writer.Write(kvp.Value);
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
                _md5Cache.TryAdd(fileName, returnVal);
            }

            return returnVal;
        }
    }
}
