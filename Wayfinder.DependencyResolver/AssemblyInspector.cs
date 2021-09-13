using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting;
using System.Text;
using System.Threading.Tasks;
using Wayfinder.DependencyResolver.Logger;
using Wayfinder.DependencyResolver.Nuget;
using Wayfinder.DependencyResolver.Schemas;

namespace Wayfinder.DependencyResolver
{
    public class AssemblyInspector : IDisposable
    {
        private readonly IDictionary<FileInfo, AssemblyData> _resolvedAssemblyCache = new Dictionary<FileInfo, AssemblyData>();
        private readonly ILogger _logger;

        public AssemblyInspector(ILogger logger)
        {
            _logger = logger ?? NullLogger.Singleton;
            // Unpack native helper assemblies (dumpbin.exe)
            try
            {
                File.WriteAllBytes("dumpbin.exe", NativeAssemblies.dumpbin);
                File.WriteAllBytes("link.exe", NativeAssemblies.link);
                File.WriteAllBytes("mspdbcore.dll", NativeAssemblies.mspdbcore);
            }
            catch (Exception)
            {
                Console.WriteLine("Warning: Failed to load native DLL inspection tools. Reflection on native DLLs will not be supported");
            }
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists("dumpbin.exe")) File.Delete("dumpbin.exe");
                if (File.Exists("link.exe")) File.Delete("link.exe");
                if (File.Exists("mspdbcore.dll")) File.Delete("mspdbcore.dll");
            }
            catch (Exception)
            {
                Console.WriteLine("Warning: Failed to load native DLL inspection tools. Reflection on native DLLs will not be supported");
            }
        }

        public AssemblyData InspectSingleAssembly(FileInfo assemblyFile, NugetPackageCache nugetPackageCache)
        {
            if (!assemblyFile.Exists)
            {
                throw new FileNotFoundException("Input file " + assemblyFile.FullName + " not found!", assemblyFile.FullName);
            }

            // Is the result already cached?
            if (_resolvedAssemblyCache.ContainsKey(assemblyFile))
            {
                return _resolvedAssemblyCache[assemblyFile];
            }

            //Debug.WriteLine("Inspecting " + assemblyFile.Name);
            AssemblyData returnVal;
            if (Debugger.IsAttached)
            {
                returnVal = InspectSingleAssemblyWithoutAppDomain(assemblyFile);
            }
            else
            {
                returnVal = InspectSingleAssemblyWithAppDomain(assemblyFile);
            }

            _resolvedAssemblyCache[assemblyFile] = returnVal;

            // Add nuget resolution data
            if (returnVal != null && nugetPackageCache != null)
            {
                IList<NugetAssemblyResolutionResult> resolutionResults = nugetPackageCache.ResolveAssembly(returnVal.AssemblyBinaryName, returnVal.AssemblyHashMD5);
                returnVal.NugetSourcePackages.AddRange(ConvertNugetResolutionResults(resolutionResults));
            }

            return returnVal;
        }

        public ISet<DependencyGraphNode> BuildDependencyGraph(FileInfo singleAssembly, NugetPackageCache nugetPackageCache)
        {
            AssemblyData sourceNodeData = InspectSingleAssembly(singleAssembly, nugetPackageCache);
            DependencyGraphNode rootNode = new DependencyGraphNode(sourceNodeData);

            HashSet<DependencyGraphNode> nodes = new HashSet<DependencyGraphNode>();
            nodes.Add(rootNode);

            foreach (AssemblyReferenceName reference in sourceNodeData.ReferencedAssemblies)
            {
                BinaryType expectedBinaryType = UbiquitousHelpers.ReferenceTypeToAssemblyType(reference.ReferenceType);

                AssemblyData referenceData = new AssemblyData()
                {
                    AssemblyBinaryName = reference.AssemblyBinaryName,
                    AssemblyVersion = reference.ReferencedAssemblyVersion,
                    AssemblyFullName = reference.AssemblyFullName,
                    AssemblyType = expectedBinaryType,
                };

                // Resolve nuget data for references, if possible
                if (nugetPackageCache != null)
                {
                    IList<NugetAssemblyResolutionResult> resolutionResults = nugetPackageCache.ResolveAssembly(reference.AssemblyBinaryName, fileMD5Hash: null);
                    referenceData.NugetSourcePackages.AddRange(ConvertNugetResolutionResults(resolutionResults));
                }

                DependencyGraphNode spokeNode = new DependencyGraphNode(referenceData);
                rootNode.Dependencies.Add(spokeNode);
                nodes.Add(spokeNode);
            }

            CalculateConnectionCounts(nodes);
            return nodes;
        }

        private static void CalculateConnectionCounts(ISet<DependencyGraphNode> nodes)
        {
            foreach (DependencyGraphNode node in nodes)
            {
                foreach (DependencyGraphNode dependency in node.Dependencies)
                {
                    dependency.IncomingConnections++;
                    node.OutgoingConnections++;
                }
            }

            foreach (DependencyGraphNode node in nodes)
            {
                node.NodeWeight = Math.Log(node.OutgoingConnections + node.IncomingConnections + 1);
            }
        }

        private static bool AttemptBind(
            AssemblyData candidateAssembly,
            string targetAssemblyName,
            BinaryType targetBinaryType,
            Version targetVersion,
            string bindingRedirectCodeBasePath,
            ILogger logger)
        {
            // Check assembly name
            if (!string.Equals(targetAssemblyName, candidateAssembly.AssemblyBinaryName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Check binary type
            if (targetBinaryType != candidateAssembly.AssemblyType)
            {
                logger.Log(string.Format("Failed bind to {0}: Wrong binary type (expected {1} got {2})",
                    candidateAssembly.AssemblyFilePath == null ? candidateAssembly.AssemblyBinaryName : candidateAssembly.AssemblyFilePath.FullName,
                    Enum.GetName(typeof(BinaryType), targetBinaryType),
                    Enum.GetName(typeof(BinaryType), candidateAssembly.AssemblyType)),
                    LogLevel.Wrn);
                return false;
            }

            // Check major version match
            if (targetVersion != null &&
                candidateAssembly.AssemblyVersion != null &&
                candidateAssembly.AssemblyVersion.Major != targetVersion.Major)
            {
                logger.Log(string.Format("While binding to {0}: Major version mismatch (expected {1} got {2})",
                    candidateAssembly.AssemblyFilePath == null ? candidateAssembly.AssemblyBinaryName : candidateAssembly.AssemblyFilePath.FullName,
                    targetVersion,
                    candidateAssembly.AssemblyVersion),
                    LogLevel.Wrn);
                // don't actually treat it as an error though
                //return false;
            }

            // Check codeBase restraints
            if (!string.IsNullOrEmpty(bindingRedirectCodeBasePath))
            {
                FileInfo expectedFileName = new FileInfo(Path.Combine(candidateAssembly.AssemblyFilePath.DirectoryName, bindingRedirectCodeBasePath));
                if (expectedFileName != candidateAssembly.AssemblyFilePath)
                {
                    logger.Log(string.Format("Failed bind to {0}: codeBase mismatch (expected {1})",
                        candidateAssembly.AssemblyFilePath == null ? candidateAssembly.AssemblyBinaryName : candidateAssembly.AssemblyFilePath.FullName,
                        expectedFileName),
                        LogLevel.Wrn);
                    return false;
                }
            }

            return true;
        }

        public ISet<DependencyGraphNode> BuildDependencyGraph(DirectoryInfo projectDirectory, NugetPackageCache nugetPackageCache)
        {
            HashSet<DependencyGraphNode> nodes = new HashSet<DependencyGraphNode>();
            foreach (FileInfo assemblyFile in projectDirectory.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                if (string.Equals(assemblyFile.Extension, ".dll", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(assemblyFile.Extension, ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    AssemblyData nodeData = InspectSingleAssembly(assemblyFile, nugetPackageCache);
                    if (nodeData != null)
                    {
                        nodes.Add(new DependencyGraphNode(nodeData));
                    }
                }
            }

            HashSet<DependencyGraphNode> stubNodes = new HashSet<DependencyGraphNode>();

            // Iterate through all the assemblies we found, resolve bindings, and put them into a graph (or potentially multiple disconnected graphs)
            foreach (DependencyGraphNode sourceNode in nodes)
            {
                _logger.Log("Starting assembly binding from source node " + sourceNode.ThisAssembly);
                foreach (AssemblyReferenceName reference in sourceNode.ThisAssembly.ReferencedAssemblies)
                {
                    // SIMULATE ASSEMBLY BINDING PROCESS HERE
                    string targetAssemblyName = reference.AssemblyBinaryName; // todo: can a code base filename override this?
                    Version targetVersion = reference.ReferencedAssemblyVersionAfterBindingOverride ?? reference.ReferencedAssemblyVersion;
                    BinaryType targetBinaryType = UbiquitousHelpers.ReferenceTypeToAssemblyType(reference.ReferenceType);

                    if (targetVersion != null)
                    {
                        _logger.Log("Attempting to bind to " + targetAssemblyName + " v" + targetVersion.ToString() + " (" + Enum.GetName(typeof(BinaryType), targetBinaryType) + ")", LogLevel.Vrb);
                    }
                    else
                    {
                        _logger.Log("Attempting to bind to " + targetAssemblyName + " (" + Enum.GetName(typeof(BinaryType), targetBinaryType) + ")", LogLevel.Vrb);
                    }

                    DependencyGraphNode targetNode = null;
                    foreach (DependencyGraphNode candidateNode in nodes)
                    {
                        if (sourceNode == candidateNode)
                        {
                            continue;
                        }

                        if (AttemptBind(candidateNode.ThisAssembly, targetAssemblyName, targetBinaryType, targetVersion, reference.BindingRedirectCodeBasePath, _logger))
                        {
                            targetNode = candidateNode;
                            _logger.Log("Successful bind to " + candidateNode.ThisAssembly.AssemblyFilePath.FullName);
                            break;
                        }
                    }

                    if (targetNode == null)
                    {
                        // Can't resolve this reference. Does a stub node already exist?
                        foreach (DependencyGraphNode existingStubNode in stubNodes)
                        {
                            if (AttemptBind(existingStubNode.ThisAssembly, targetAssemblyName, targetBinaryType, targetVersion, string.Empty, _logger))
                            {
                                if (targetBinaryType == BinaryType.Managed)
                                {
                                    _logger.Log("Reusing existing stub node for " + targetAssemblyName + " v" + targetVersion, LogLevel.Vrb);
                                }
                                else
                                {
                                    _logger.Log("Reusing existing stub node for " + targetAssemblyName, LogLevel.Vrb);
                                }

                                targetNode = existingStubNode;
                            }
                        }

                        // Create a stub node if necessary
                        if (targetNode == null)
                        {
                            if (targetBinaryType == BinaryType.Managed)
                            {
                                _logger.Log("Failed bind. Creating stub node for " + targetAssemblyName + " v" + targetVersion, LogLevel.Wrn);
                            }
                            else
                            {
                                _logger.Log("Creating stub node for presumably built-in native binary " + targetAssemblyName, LogLevel.Vrb);
                            }

                            AssemblyData referenceData = new AssemblyData()
                            {
                                AssemblyBinaryName = targetAssemblyName,
                                AssemblyVersion = targetVersion,
                                AssemblyFullName = reference.AssemblyFullName,
                                AssemblyType = targetBinaryType,
                            };

                            // Resolve nuget data for references, if possible
                            if (nugetPackageCache != null)
                            {
                                IList<NugetAssemblyResolutionResult> resolutionResults = nugetPackageCache.ResolveAssembly(reference.AssemblyBinaryName, fileMD5Hash: null);
                                referenceData.NugetSourcePackages.AddRange(ConvertNugetResolutionResults(resolutionResults));
                            }

                            targetNode = new DependencyGraphNode(referenceData);
                            stubNodes.Add(targetNode);
                        }
                    }
                    else
                    {
                        // Add an error if the reference version is incorrect
                        if (targetVersion != null &&
                            targetNode?.ThisAssembly?.AssemblyVersion != null &&
                            targetVersion > targetNode.ThisAssembly.AssemblyVersion)
                        {
                            sourceNode.Errors.Add("This assembly depends on " + targetNode.ThisAssembly.AssemblyBinaryName + " v" + targetVersion + ", but a lower version was actually resolved");
                        }    
                    }

                    sourceNode.Dependencies.Add(targetNode);
                }
            }

            foreach (var stubNode in stubNodes)
            {
                nodes.Add(stubNode);
            }

            CalculateConnectionCounts(nodes);
            return nodes;
        }

        private IEnumerable<NugetPackageIdentity> ConvertNugetResolutionResults(IList<NugetAssemblyResolutionResult> results)
        {
            if (results == null)
            {
                yield break;
            }

            ISet<NugetPackageIdentity> dedupedIdentities = new HashSet<NugetPackageIdentity>();
            foreach (NugetAssemblyResolutionResult result in results)
            {
                if (!dedupedIdentities.Contains(result.SourcePackage))
                {
                    dedupedIdentities.Add(result.SourcePackage);
                    yield return result.SourcePackage;
                }
            }
        }

        //private NugetPackageIdentity DeduplicateResolutionResults(IList<NugetAssemblyResolutionResult> results, AssemblyReferenceName assemblyReference, NugetPackageCache packageCache)
        //{
        //    if (results == null || results.Count == 0)
        //    {
        //        return null;
        //    }

        //    if (results.Count == 1)
        //    {
        //        return results[0].SourcePackage;
        //    }

        //    List<NugetAssemblyResolutionResult> exactBindMatches = new List<NugetAssemblyResolutionResult>();

        //    // Deduplicate by inspecting each actual candidate and seeing if the versions and assembly names match
        //    List<AssemblyData> resolvedCandidates = new List<AssemblyData>();
        //    foreach (NugetAssemblyResolutionResult result in results)
        //    {
        //        AssemblyData candidateAssemblyData = InspectSingleAssembly(result.AssemblyFile, packageCache);
        //        if (UbiquitousHelpers.CanBindReferenceExactly(assemblyReference, candidateAssemblyData))
        //        {
        //            exactBindMatches.Add(result);
        //        }

        //        resolvedCandidates.Add(candidateAssemblyData);
        //    }

        //    // Does a single candidate match exactly? Then just use it
        //    if (exactBindMatches.Count == 1)
        //    {
        //        return exactBindMatches[0].SourcePackage;
        //    }
        //    else if (exactBindMatches.Count > 1)
        //    {
        //        // Deduplicate different versions which bind exactly.
        //        // In this case, favor the one which matches the package name most closely.
        //        // Though we should really be parsing the project file to see what the original code was compiled against
        //        float[] sortedPackageSimilarities = new float[exactBindMatches.Count];
        //        NugetAssemblyResolutionResult[] sortedPackageRefs = new NugetAssemblyResolutionResult[exactBindMatches.Count];
        //        for (int c = 0; c < exactBindMatches.Count; c++)
        //        {
        //            sortedPackageSimilarities[c] = UbiquitousHelpers.NormalizedEditDistance(assemblyReference.AssemblyBinaryName, exactBindMatches[c].SourcePackage.PackageName);
        //            sortedPackageRefs[c] = exactBindMatches[c];
        //        }

        //        Array.Sort(sortedPackageSimilarities, sortedPackageRefs);
        //        // Have a high-confidence match. Use it
        //        if (sortedPackageSimilarities[0] < 0.1f)
        //        {
        //            return sortedPackageRefs[0].SourcePackage;
        //        }
        //        else
        //        {
        //            // No good match, but might as well use it anyways since this is an exact binding
        //            return sortedPackageRefs[0].SourcePackage;
        //        }
        //    }
        //    else
        //    {
        //        // No exact bindings. Find the closest approximate binding based on package name
        //        float[] sortedPackageSimilarities = new float[results.Count];
        //        NugetAssemblyResolutionResult[] sortedPackageRefs = new NugetAssemblyResolutionResult[results.Count];
        //        for (int c = 0; c < results.Count; c++)
        //        {
        //            sortedPackageSimilarities[c] = UbiquitousHelpers.NormalizedEditDistance(assemblyReference.AssemblyBinaryName, results[c].SourcePackage.PackageName);
        //            sortedPackageRefs[c] = results[c];
        //        }

        //        Array.Sort(sortedPackageSimilarities, sortedPackageRefs);
        //        if (sortedPackageSimilarities[0] < 0.1f)
        //        {
        //            return sortedPackageRefs[0].SourcePackage;
        //        }
        //        else
        //        {
        //            // None of these packages look like the right one.
        //            return null;
        //        }
        //    }
        //}

        private static AssemblyData InspectSingleAssemblyWithoutAppDomain(FileInfo assemblyFile)
        {
            AssemblyLoaderProxy proxy = new AssemblyLoaderProxy();
            byte[] serializedAssemblyData = proxy.Process(assemblyFile.FullName);
            AssemblyData returnVal = AssemblyData.Deserialize(serializedAssemblyData);
            return returnVal;
        }

        private AssemblyData InspectSingleAssemblyWithAppDomain(FileInfo assemblyFile)
        {
            WayfinderPluginLoadContext loadContext = new WayfinderPluginLoadContext(_logger, runtimeDirectory: new DirectoryInfo(Environment.CurrentDirectory), containerDirectory: assemblyFile.Directory);
            AssemblyName assemblyName = typeof(AssemblyLoaderProxy).Assembly.GetName();
            Assembly containerHostAssembly = loadContext.LoadFromAssemblyName(assemblyName);
            if (containerHostAssembly == null)
            {
                _logger.Log("Could not find entry point dll " + assemblyName + " to use to create load context guest.", LogLevel.Err);
                return null;
            }

            string containerGuestTypeName = typeof(AssemblyLoaderProxy).FullName;
            Type containerGuestType = containerHostAssembly.ExportedTypes.FirstOrDefault(t => string.Equals(t.FullName, containerGuestTypeName));
            object containerGuest = Activator.CreateInstance(containerGuestType);
            if (containerGuest == null)
            {
                _logger.Log("Error while creating remoting proxy to load context container.", LogLevel.Err);
                return null;
            }

            // For some reason we run into troubles when just trying to cast the returned object as an IContainerGuest (probably because the defining assemblies of the interface are different).
            // So we have to use reflection to find the initialization method and invoke it
            MethodInfo initializeMethodSig = containerGuest.GetType().GetMethod(nameof(AssemblyLoaderProxy.Process), new Type[] { typeof(string) });
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
