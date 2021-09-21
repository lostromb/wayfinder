using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Wayfinder.Common;
using Wayfinder.Common.Schemas;

namespace Wayfinder.DependencyResolver
{
    /// <summary>
    /// Proxy for loading assemblies in a self-contained container such as an assembly load context
    /// </summary>
    public class NetCoreAssemblyLoaderProxy
    {
        public byte[] Process(string fileName)
        {
            Assembly loadFromAssembly = null;
            ExceptionDispatchInfo loadException = null;
            AssemblyData returnVal = new AssemblyData();
            returnVal.AssemblyFilePath = new FileInfo(fileName);

            if (!returnVal.AssemblyFilePath.Exists)
            {
                returnVal.LoaderError = "File not found";
            }
            else
            {
                try
                {
                    loadFromAssembly = Assembly.LoadFrom(fileName);
                }
                catch (Exception e)
                {
                    loadException = ExceptionDispatchInfo.Capture(e);
                }

                UpdateManagedDllInfo(returnVal, loadFromAssembly, loadException);
            }

            return returnVal.Serialize();
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

        private static void UpdateManagedDllInfo(
            AssemblyData returnVal,
            Assembly loadFromAssembly,
            ExceptionDispatchInfo loadException)
        {
            if (loadFromAssembly == null)
            {
                returnVal.LoaderError = "Unable to determine any information from the assembly. " + loadException.SourceException.GetType().Name + ": " + loadException.SourceException.Message;
                return;
            }

            returnVal.AssemblyType = BinaryType.Managed;
            AssemblyName fullName = loadFromAssembly.GetName();
            returnVal.AssemblyBinaryName = fullName.Name;
            returnVal.AssemblyFullName = fullName.FullName;
            returnVal.AssemblyVersion = fullName.Version;
            PortableExecutableKinds peKind;
            ImageFileMachine machineType;
            loadFromAssembly.ManifestModule.GetPEKind(out peKind, out machineType);
            if (peKind == PortableExecutableKinds.ILOnly &&
                machineType == ImageFileMachine.I386)
            {
                returnVal.Platform = BinaryPlatform.AnyCPU;
            }
            else if (peKind.HasFlag(PortableExecutableKinds.ILOnly) &&
                peKind.HasFlag(PortableExecutableKinds.Preferred32Bit) &&
                machineType == ImageFileMachine.I386)
            {
                returnVal.Platform = BinaryPlatform.AnyCPUPrefer32;
            }
            else if (peKind.HasFlag(PortableExecutableKinds.PE32Plus) &&
                machineType == ImageFileMachine.AMD64)
            {
                returnVal.Platform = BinaryPlatform.AMD64;
            }
            else if (peKind == PortableExecutableKinds.Required32Bit &&
                machineType == ImageFileMachine.I386)
            {
                returnVal.Platform = BinaryPlatform.X86;
            }
            else
            {
                returnVal.Platform = BinaryPlatform.Unknown;
            }

            if (loadFromAssembly != null)
            {
                try
                {
                    TargetFrameworkAttribute targetFramework = loadFromAssembly.GetCustomAttribute<TargetFrameworkAttribute>();
                    if (targetFramework != null)
                    {
                        returnVal.AssemblyFramework = targetFramework.FrameworkName;
                        returnVal.StructuredFrameworkVersion = new DotNetFrameworkVersion(targetFramework.FrameworkName);
                    }

                    AssemblyFileVersionAttribute fileVersion = loadFromAssembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
                    if (fileVersion != null)
                    {
                        //report.Add("Assembly file version: " + fileVersion.Version);
                    }
                }
                catch (Exception e)
                {
                    returnVal.LoaderError = e.GetType().ToString() + ": " + e.Message;
                }
            }

            try
            {
                foreach (AssemblyName name in loadFromAssembly.GetReferencedAssemblies().OrderBy((n) => n.Name))
                {
                    if (!string.Equals("mscorlib", name.Name, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals("System", name.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        returnVal.ReferencedAssemblies.Add(new AssemblyReferenceName()
                        {
                            AssemblyBinaryName = name.Name,
                            AssemblyFullName = name.FullName,
                            ReferencedAssemblyVersion = name.Version,
                            ReferencedAssemblyVersionAfterBindingOverride = name.Version,
                            ReferenceType = AssemblyReferenceType.Managed,
                        });
                    }
                }
            }
            catch (Exception e)
            {
                returnVal.LoaderError = e.GetType().ToString() + ": " + e.Message;
            }

            // Resolve binding redirects on managed references
            ApplyBindingRedirects(returnVal);

            // If possible, also dive into the assembly to search for DllImport attributes, which would indicate native dll references
            if (loadFromAssembly != null)
            {
                HashSet<string> pInvokeDlls = new HashSet<string>();
                try
                {
                    foreach (Type exportedType in loadFromAssembly.GetTypes())
                    {
                        foreach (MethodInfo member in exportedType.GetRuntimeMethods())
                        {
                            DllImportAttribute dllImport = member.GetCustomAttribute<DllImportAttribute>();
                            if (dllImport != null && !pInvokeDlls.Contains(dllImport.Value.ToLowerInvariant()))
                            {
                                pInvokeDlls.Add(dllImport.Value.ToLowerInvariant());
                            }
                        }
                    }

                    foreach (string pInvokeDll in pInvokeDlls)
                    {
                        returnVal.ReferencedAssemblies.Add(new AssemblyReferenceName()
                        {
                            AssemblyBinaryName = TrimAssemblyFileExtension(pInvokeDll),
                            ReferencedAssemblyVersion = null,
                            ReferenceType = AssemblyReferenceType.PInvoke,
                            AssemblyFullName = null
                        });
                    }
                }
                catch (Exception) { }
            }
        }

        private static void ApplyBindingRedirects(AssemblyData sourceAssembly)
        {
            // FIXME this doesn't affect the full name of the reference, which might be misleading
            // also we don't care about publicKeyToken or culture
            IList<AssemblyBindingModifier> bindingModifiers = ParseAppConfig(sourceAssembly.AssemblyFilePath);
            bool changeMade;
            int loops = 0;
            do
            {
                changeMade = false;
                foreach (AssemblyReferenceName reference in sourceAssembly.ReferencedAssemblies)
                {
                    if (reference.ReferencedAssemblyVersionAfterBindingOverride == null)
                    {
                        continue;
                    }

                    foreach (AssemblyBindingModifier modifier in bindingModifiers)
                    {
                        if (!string.Equals(reference.AssemblyBinaryName, modifier.AssemblyBinaryName, StringComparison.OrdinalIgnoreCase) ||
                            reference.ReferencedAssemblyVersionAfterBindingOverride < modifier.OldVersionMinimumRange ||
                            reference.ReferencedAssemblyVersionAfterBindingOverride > modifier.OldVersionMaximumRange)
                        {
                            continue;
                        }

                        if (modifier.NewVersion != null && modifier.NewVersion != reference.ReferencedAssemblyVersionAfterBindingOverride)
                        {
                            reference.ReferencedAssemblyVersionAfterBindingOverride = modifier.NewVersion;
                            changeMade = true;
                        }

                        if (!string.IsNullOrEmpty(modifier.TargetCodeBase))
                        {
                            reference.BindingRedirectCodeBasePath = modifier.TargetCodeBase;
                            changeMade = true;
                        }
                    }
                }
            } while (changeMade && loops++ < 5);
        }

        private static IList<AssemblyBindingModifier> ParseAppConfig(FileInfo assemblyFileName)
        {
            List<AssemblyBindingModifier> returnVal = new List<AssemblyBindingModifier>();

            try
            {
                // See if the app config exists for this assembly
                FileInfo appConfigFile = new FileInfo(assemblyFileName.FullName + ".config");
                if (appConfigFile.Exists)
                {
                    XmlDocument parsedDocument = new XmlDocument();
                    parsedDocument.Load(appConfigFile.FullName);

                    XmlNodeList nodeList = parsedDocument.GetElementsByTagName("dependentAssembly");
                    foreach (XmlElement dependentAssembly in nodeList.OfType<XmlElement>())
                    {
                        XmlElement assemblyIdentity = SelectXMLChildren(dependentAssembly, "assemblyIdentity").FirstOrDefault();
                        if (assemblyIdentity == null || !assemblyIdentity.HasAttribute("name"))
                        {
                            Console.WriteLine("Warning: dependentAssembly tag has no child assemblyIdentity tag");
                            continue;
                        }

                        string binaryName = assemblyIdentity.GetAttribute("name");

                        // Parse binding redirects
                        foreach (XmlElement bindingRedirect in SelectXMLChildren(dependentAssembly, "bindingRedirect"))
                        {
                            if (!bindingRedirect.HasAttribute("oldVersion") ||
                                !bindingRedirect.HasAttribute("newVersion"))
                            {
                                Console.WriteLine("Warning: bindingRedirect tag is missing essential attributes");
                                continue;
                            }

                            Version oldVersionRangeStart;
                            Version oldVersionRangeEnd;
                            Version newVersion;

                            string oldVersionString = bindingRedirect.GetAttribute("oldVersion");
                            if (oldVersionString.Contains('-'))
                            {
                                // Old version is a range
                                int dashIdx = oldVersionString.IndexOf('-');
                                if (!Version.TryParse(oldVersionString.Substring(0, dashIdx), out oldVersionRangeStart) ||
                                    !Version.TryParse(oldVersionString.Substring(dashIdx + 1), out oldVersionRangeEnd) ||
                                    !Version.TryParse(bindingRedirect.GetAttribute("newVersion"), out newVersion))
                                {
                                    Console.WriteLine("Warning: Couldn't parse version strings from bindingRedirect tag");
                                    continue;
                                }
                            }
                            else
                            {
                                // Old version is a specific version
                                if (!Version.TryParse(oldVersionString, out oldVersionRangeStart) ||
                                    !Version.TryParse(bindingRedirect.GetAttribute("newVersion"), out newVersion))
                                {
                                    Console.WriteLine("Warning: Couldn't parse version strings from bindingRedirect tag");
                                    continue;
                                }

                                oldVersionRangeEnd = oldVersionRangeStart;
                            }

                            returnVal.Add(new AssemblyBindingModifier()
                            {
                                AssemblyBinaryName = binaryName,
                                OldVersionMinimumRange = oldVersionRangeStart,
                                OldVersionMaximumRange = oldVersionRangeEnd,
                                NewVersion = newVersion,
                                TargetCodeBase = null,
                            });
                        }

                        // Parse codeBase tags
                        foreach (XmlElement codeBase in SelectXMLChildren(dependentAssembly, "codeBase"))
                        {
                            if (!codeBase.HasAttribute("version") ||
                                !codeBase.HasAttribute("href"))
                            {
                                Console.WriteLine("Warning: codeBase tag is missing essential attributes");
                                continue;
                            }

                            Version codeBaseVersion;
                            if (!Version.TryParse(codeBase.GetAttribute("version"), out codeBaseVersion))
                            {
                                Console.WriteLine("Warning: Couldn't parse codeBase tag version");
                                continue;
                            }

                            returnVal.Add(new AssemblyBindingModifier()
                            {
                                AssemblyBinaryName = binaryName,
                                OldVersionMinimumRange = codeBaseVersion,
                                OldVersionMaximumRange = codeBaseVersion,
                                NewVersion = codeBaseVersion,
                                TargetCodeBase = codeBase.GetAttribute("href"),
                            });
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }

            return returnVal;
        }

        /// <summary>
        /// There is no reason for this method to exist. I should be able to just do XmlNode.SelectNodes().
        /// But for some reason I cannot get that to work for the most basic task of selecting children with a specific name.
        /// </summary>
        /// <param name="rootElement"></param>
        /// <param name="childName"></param>
        /// <returns></returns>
        private static IEnumerable<XmlElement> SelectXMLChildren(XmlElement rootElement, string childName)
        {
            return rootElement.ChildNodes.OfType<XmlElement>().Where((c) => string.Equals(c.Name, childName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
