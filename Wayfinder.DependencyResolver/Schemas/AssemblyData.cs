using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Wayfinder.DependencyResolver.Nuget;

namespace Wayfinder.DependencyResolver.Schemas
{
    public class AssemblyData
    {
        /// <summary>
        /// The fully specified file path (e.g. c:\windows\system32\mscorlib.dll)
        /// </summary>
        public FileInfo AssemblyFilePath { get; set; }

        /// <summary>
        /// The name that the binary calls itself in code (e.g. Newtonsoft.Json)
        /// </summary>
        public string AssemblyBinaryName { get; set; }

        /// <summary>
        /// The fully resolved binary name (e.g. Akka.Serialization, Version=5.0.10.0, Culture=neutral, PublicKeyToken=null)
        /// </summary>
        public string AssemblyFullName { get; set; }

        /// <summary>
        /// The version number of this assembly
        /// </summary>
        public Version AssemblyVersion { get; set; }

        /// <summary>
        /// The framework name of this assembly, if it is managed (e.g. ".NETFramework,Version=v4.5");
        /// </summary>
        public string AssemblyFramework { get; set; }

        /// <summary>
        /// The structured, parsed version of the target framework of this assembly.
        /// </summary>
        public DotNetFrameworkVersion StructuredFrameworkVersion { get; set; }

        /// <summary>
        /// The platform that this assembly was compiled for
        /// </summary>
        public BinaryPlatform Platform { get; set; }

        /// <summary>
        /// The type of assembly (managed / unmanaged)
        /// </summary>
        public BinaryType AssemblyType { get; set; }

        /// <summary>
        /// The MD5 hash of the assembly's contents.
        /// </summary>
        public string AssemblyHashMD5 { get; set; }

        /// <summary>
        /// If an error occured while processing this assembly, put the error here.
        /// </summary>
        public string LoaderError { get; set; }

        /// <summary>
        /// The list of all assembly references that this depends on.
        /// </summary>
        public List<AssemblyReferenceName> ReferencedAssemblies { get; }

        /// <summary>
        /// If this assembly came from a Nuget package, this is the identity of the source package(s) that contain it (e.g. Newtonsoft.Json v9.0.0)
        /// </summary>
        public List<NugetPackageIdentity> NugetSourcePackages { get; }

        public AssemblyData()
        {
            ReferencedAssemblies = new List<AssemblyReferenceName>();
            NugetSourcePackages = new List<NugetPackageIdentity>();
            StructuredFrameworkVersion = new DotNetFrameworkVersion(DotNetFrameworkType.Unknown, new Version(0, 0));
        }

        public AssemblyReferenceName AsReference(BinaryType sourceBinaryType)
        {
            return new AssemblyReferenceName()
            {
                AssemblyBinaryName = this.AssemblyBinaryName,
                AssemblyFullName = this.AssemblyFullName,
                ReferencedAssemblyVersion = this.AssemblyVersion,
                ReferenceType = UbiquitousHelpers.AssemblyTypeToReferenceType(sourceBinaryType, this.AssemblyType)
            };
        }

        public void Serialize(BinaryWriter writer)
        {
            if (AssemblyFilePath == null)
            {
                writer.Write("");
            }
            else
            {
                writer.Write(AssemblyFilePath.FullName);
            }

            writer.Write(AssemblyBinaryName ?? "");
            writer.Write(AssemblyFullName ?? "");
            writer.Write(AssemblyVersion == null ? "" : AssemblyVersion.ToString());
            writer.Write(AssemblyFramework ?? "");
            StructuredFrameworkVersion.Serialize(writer);
            writer.Write((int)Platform);
            writer.Write((int)AssemblyType);
            writer.Write(AssemblyHashMD5 ?? "");
            writer.Write(LoaderError ?? "");
            writer.Write(ReferencedAssemblies.Count);
            foreach (AssemblyReferenceName reference in ReferencedAssemblies)
            {
                reference.Serialize(writer);
            }

            writer.Write(NugetSourcePackages.Count);
            foreach (NugetPackageIdentity nugetPackage in NugetSourcePackages)
            {
                nugetPackage.Serialize(writer);
            }
        }

        public static AssemblyData Deserialize(BinaryReader reader)
        {
            AssemblyData returnVal = new AssemblyData();
            string scratch;
            scratch = reader.ReadString();
            if (!string.IsNullOrEmpty(scratch))
            {
                returnVal.AssemblyFilePath = new FileInfo(scratch);
            }

            returnVal.AssemblyBinaryName = reader.ReadString();
            returnVal.AssemblyFullName = reader.ReadString();
            scratch = reader.ReadString();
            Version assemblyVersion;
            if (!string.IsNullOrEmpty(scratch) && Version.TryParse(scratch, out assemblyVersion))
            {
                returnVal.AssemblyVersion = assemblyVersion;
            }

            returnVal.AssemblyFramework = reader.ReadString();
            returnVal.StructuredFrameworkVersion = DotNetFrameworkVersion.Deserialize(reader);
            returnVal.Platform = (BinaryPlatform)reader.ReadInt32();
            returnVal.AssemblyType = (BinaryType)reader.ReadInt32();
            returnVal.AssemblyHashMD5 = reader.ReadString();
            returnVal.LoaderError = reader.ReadString();
            int referenceCount = reader.ReadInt32();
            for (int c = 0; c < referenceCount; c++)
            {
                returnVal.ReferencedAssemblies.Add(AssemblyReferenceName.Deserialize(reader));
            }

            int nugetPackageCount = reader.ReadInt32();
            for (int c = 0; c < nugetPackageCount; c++)
            {
                returnVal.NugetSourcePackages.Add(NugetPackageIdentity.Deserialize(reader));
            }

            return returnVal;
        }

        public byte[] Serialize()
        {
            using (MemoryStream output = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(output, Encoding.UTF8, true))
                {
                    Serialize(writer);
                }

                return output.ToArray();
            }
        }

        public static AssemblyData Deserialize(byte[] serializedForm)
        {
            using (MemoryStream input = new MemoryStream(serializedForm, false))
            using (BinaryReader reader = new BinaryReader(input, Encoding.UTF8))
            {
                return Deserialize(reader);
            }
        }

        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(AssemblyFullName))
            {
                return AssemblyFullName;
            }
            else if (!string.IsNullOrWhiteSpace(AssemblyBinaryName))
            {
                if (AssemblyVersion != null)
                {
                    return AssemblyBinaryName + " v" + AssemblyVersion.ToString();
                }
                else
                {
                    return AssemblyBinaryName;
                }
            }
            else
            {
                return AssemblyFilePath.Name;
            }
        }
    }
}
