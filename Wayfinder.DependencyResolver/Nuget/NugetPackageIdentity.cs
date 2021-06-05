using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wayfinder.DependencyResolver.Nuget
{
    public class NugetPackageIdentity : IEquatable<NugetPackageIdentity>
    {
        /// <summary>
        /// This is the name of the package (e.g. Newtonsoft.Json)
        /// </summary>
        public string PackageName { get; set; }

        /// <summary>
        /// This is the (potentially semver) version number of the package (e.g 9.0.1-prerelease)
        /// </summary>
        public string PackageVersion { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(PackageName ?? "");
            writer.Write(PackageVersion ?? "");
        }

        public static NugetPackageIdentity Deserialize(BinaryReader reader)
        {
            NugetPackageIdentity returnVal = new NugetPackageIdentity();
            returnVal.PackageName = reader.ReadString();
            returnVal.PackageVersion = reader.ReadString();
            return returnVal;
        }

        public override string ToString()
        {
            return "Nuget ref: " + PackageName + " v" + PackageVersion;
        }

        public bool Equals(NugetPackageIdentity other)
        {
            if (other == null)
            {
                return false;
            }

            return string.Equals(PackageName, other.PackageName, StringComparison.Ordinal) &&
                string.Equals(PackageVersion, other.PackageVersion, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            NugetPackageIdentity other = (NugetPackageIdentity)obj;
            return string.Equals(PackageName, other.PackageName, StringComparison.Ordinal) &&
                string.Equals(PackageVersion, other.PackageVersion, StringComparison.Ordinal);
        }

        public override int GetHashCode()
        {
            return (PackageName ?? string.Empty).GetHashCode() ^ (PackageVersion ?? string.Empty).GetHashCode();
        }
    }
}
