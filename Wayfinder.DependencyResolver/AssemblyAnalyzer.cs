using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Wayfinder.Common;
using Wayfinder.Common.Logger;
using Wayfinder.Common.Nuget;
using Wayfinder.Common.Schemas;

namespace Wayfinder.DependencyResolver
{
    public class AssemblyAnalyzer
    {
        private readonly IList<IAssemblyInspector> _inspectors;
        private readonly FastConcurrentDictionary<FileInfo, AssemblyData> _resolvedAssemblyCache = new FastConcurrentDictionary<FileInfo, AssemblyData>();
        private readonly ILogger _logger;

        public AssemblyAnalyzer(ILogger logger, IList<IAssemblyInspector> inspectors)
        {
            _logger = logger ?? NullLogger.Singleton;
            _inspectors = inspectors;
            if (_inspectors == null)
            {
                throw new ArgumentNullException(nameof(inspectors));
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

            AssemblyData returnVal = null;
            AssemblyData bestNonNullResult = null;
            foreach (var inspector in _inspectors)
            {
                try
                {
                    _logger.Log("Inspecting " + assemblyFile.FullName + " using " + inspector.InspectorName, LogLevel.Std);
                    returnVal = inspector.InspectAssemblyFile(assemblyFile);
                    if (returnVal != null)
                    {
                        bestNonNullResult = returnVal;
                        if (string.IsNullOrEmpty(returnVal.LoaderError))
                        {
                            _logger.Log("Got valid analysis data from " + inspector.InspectorName, LogLevel.Std);
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.Log(e.ToString(), LogLevel.Wrn);
                }
            }

            // Fall back to the best non-null results we can find
            returnVal = returnVal ?? bestNonNullResult ?? new AssemblyData();

            if (returnVal.AssemblyFilePath == null)
            {
                returnVal.AssemblyFilePath = assemblyFile;
            }
            if (string.IsNullOrEmpty(returnVal.AssemblyBinaryName))
            {
                returnVal.AssemblyBinaryName = UbiquitousHelpers.TrimAssemblyFileExtension(assemblyFile.Name);
            }
            if (string.IsNullOrEmpty(returnVal.AssemblyHashMD5))
            {
                returnVal.AssemblyHashMD5 = GetMD5HashOfFile(assemblyFile.FullName);
            }
            if (returnVal.LoaderError == null)
            {
                returnVal.LoaderError = string.Empty;
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

        private static string GetMD5HashOfFile(string fileName)
        {
            if (!File.Exists(fileName))
            {
                return string.Empty;
            }

            MD5 hasher = MD5.Create();
            hasher.Initialize();
            StringBuilder returnVal = new StringBuilder();
            using (FileStream stream = new FileStream(fileName, FileMode.Open, FileAccess.Read))
            {
                byte[] hash = hasher.ComputeHash(stream);
                stream.Close();
                foreach (byte x in hash)
                {
                    returnVal.Append(x.ToString("X"));
                }
            }

            return returnVal.ToString().ToLowerInvariant();
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

        

        private class AssemblyInspectorWorkItem : ThreadedWorkItem<AssemblyData>
        {
            private readonly Func<FileInfo, NugetPackageCache, AssemblyData> _delegate;
            private readonly FileInfo _arg1;
            private readonly NugetPackageCache _arg2;

            public AssemblyInspectorWorkItem(Func<FileInfo, NugetPackageCache, AssemblyData> workDelegate, FileInfo arg1, NugetPackageCache arg2)
            {
                _delegate = workDelegate;
                _arg1 = arg1;
                _arg2 = arg2;
            }

            protected override AssemblyData DoWork()
            {
                return _delegate(_arg1, _arg2);
            }
        }

        public ISet<DependencyGraphNode> BuildDependencyGraph(DirectoryInfo projectDirectory, NugetPackageCache nugetPackageCache)
        {
            HashSet<DependencyGraphNode> nodes = new HashSet<DependencyGraphNode>();
            List<AssemblyInspectorWorkItem> inspectorWorkItems = new List<AssemblyInspectorWorkItem>();
            foreach (FileInfo assemblyFile in projectDirectory.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                if (string.Equals(assemblyFile.Extension, ".dll", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(assemblyFile.Extension, ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    AssemblyInspectorWorkItem newWorkItem = new AssemblyInspectorWorkItem(InspectSingleAssembly, assemblyFile, nugetPackageCache);
                    Task.Run(newWorkItem.Run);
                    inspectorWorkItems.Add(newWorkItem);
                }
            }

            foreach (AssemblyInspectorWorkItem workItem in inspectorWorkItems)
            {
                try
                {
                    AssemblyData nodeData = workItem.Join();
                    if (nodeData != null)
                    {
                        nodes.Add(new DependencyGraphNode(nodeData));
                    }
                }
                catch (Exception e)
                {
                    _logger.Log(e.ToString(), LogLevel.Err);
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

                        if (BindingEmulator.AttemptBind(candidateNode.ThisAssembly, targetAssemblyName, targetBinaryType, targetVersion, reference.BindingRedirectCodeBasePath, _logger))
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
                            if (BindingEmulator.AttemptBind(existingStubNode.ThisAssembly, targetAssemblyName, targetBinaryType, targetVersion, string.Empty, _logger))
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

                        // And add an error if the framework types between the two references are illegal
                        if (sourceNode?.ThisAssembly?.StructuredFrameworkVersion != null &&
                            targetNode?.ThisAssembly?.StructuredFrameworkVersion != null &&
                            !BindingEmulator.IsCrossFrameworkReferenceLegal(sourceNode.ThisAssembly.StructuredFrameworkVersion, targetNode.ThisAssembly.StructuredFrameworkVersion))
                        {
                            sourceNode.Errors.Add($"This assembly depends on {targetNode.ThisAssembly.AssemblyBinaryName} {targetNode.ThisAssembly.StructuredFrameworkVersion} which is a higher-level framework");
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
    }
}
