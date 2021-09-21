using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wayfinder.Common.Schemas
{
    public class AssemblyReferenceName : IEquatable<AssemblyReferenceName>
    {
        /// <summary>
        /// The name of this assembly in code, e.g. "mscorlib"
        /// </summary>
        public string AssemblyBinaryName { get; set; }

        /// <summary>
        /// The version number of the assembly being referenced, as it is found in the raw assembly
        /// </summary>
        public Version ReferencedAssemblyVersion { get; set; }

        /// <summary>
        /// The version number of the assembly being referenced, after applying any relevant binding redirects
        /// </summary>
        public Version ReferencedAssemblyVersionAfterBindingOverride { get; set; }

        /// <summary>
        /// For CLR libraries, the fully qualified name (e.g. Akka.Serialization, Version=5.0.10.0, Culture=neutral, PublicKeyToken=null)
        /// </summary>
        public string AssemblyFullName { get; set; }

        /// <summary>
        /// If this reference is affected by a &lt;codeBase&gt; statement in the assembly binding config, this is the
        /// href attribute of the code base redirect. This is a hint to the runtime to look for this reference in a
        /// very specific file path.
        /// </summary>
        public string BindingRedirectCodeBasePath { get; set; }

        /// <summary>
        /// The type of reference being made (CLR dependency, p/invoke, native dependency, etc.)
        /// </summary>
        public AssemblyReferenceType ReferenceType { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(AssemblyBinaryName ?? "");
            writer.Write(ReferencedAssemblyVersion == null ? "" : ReferencedAssemblyVersion.ToString());
            writer.Write(ReferencedAssemblyVersionAfterBindingOverride == null ? "" : ReferencedAssemblyVersionAfterBindingOverride.ToString());
            writer.Write((int)ReferenceType);
            writer.Write(AssemblyFullName ?? "");
            writer.Write(BindingRedirectCodeBasePath ?? "");
        }

        public static AssemblyReferenceName Deserialize(BinaryReader reader)
        {
            AssemblyReferenceName returnVal = new AssemblyReferenceName();
            returnVal.AssemblyBinaryName = reader.ReadString();
            string scratch = reader.ReadString();
            Version assemblyVersion;
            if (!string.IsNullOrEmpty(scratch) && Version.TryParse(scratch, out assemblyVersion))
            {
                returnVal.ReferencedAssemblyVersion = assemblyVersion;
            }

            scratch = reader.ReadString();
            if (!string.IsNullOrEmpty(scratch) && Version.TryParse(scratch, out assemblyVersion))
            {
                returnVal.ReferencedAssemblyVersionAfterBindingOverride = assemblyVersion;
            }

            returnVal.ReferenceType = (AssemblyReferenceType)reader.ReadInt32();
            returnVal.AssemblyFullName = reader.ReadString();
            returnVal.BindingRedirectCodeBasePath = reader.ReadString();
            return returnVal;
        }

        public override string ToString()
        {
            StringBuilder returnVal = new StringBuilder();
            returnVal.Append("Assembly ref: ");

            if (!string.IsNullOrEmpty(AssemblyFullName))
            {
                returnVal.Append(AssemblyFullName);
            }
            else
            {
                returnVal.Append(AssemblyBinaryName);

                if (ReferencedAssemblyVersion != null)
                {
                    returnVal.Append(", Version=");
                    returnVal.Append(ReferencedAssemblyVersion.ToString());
                }

            }
            returnVal.Append(", Type=");
            returnVal.Append(Enum.GetName(typeof(AssemblyReferenceType), ReferenceType));

            return returnVal.ToString();
        }

        public bool Equals(AssemblyReferenceName other)
        {
            if (other == null)
            {
                return false;
            }

            return string.Equals(AssemblyBinaryName, other.AssemblyBinaryName, StringComparison.Ordinal) &&
                ReferencedAssemblyVersion == other.ReferencedAssemblyVersion &&
                ReferenceType == other.ReferenceType;
        }

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            AssemblyReferenceName other = (AssemblyReferenceName)obj;
            return string.Equals(AssemblyBinaryName, other.AssemblyBinaryName, StringComparison.Ordinal) &&
                ReferencedAssemblyVersion == other.ReferencedAssemblyVersion &&
                ReferenceType == other.ReferenceType;
        }

        public override int GetHashCode()
        {
            return (AssemblyBinaryName ?? string.Empty).GetHashCode() ^
                (ReferencedAssemblyVersion ?? new Version()).GetHashCode() ^
                ReferenceType.GetHashCode();
        }
    }
}
