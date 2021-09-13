using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Wayfinder.DependencyResolver.Schemas
{
    public class DotNetFrameworkVersion
    {
        private static readonly Regex FrameworkVersionParser = new Regex("(\\.NETFramework|\\.NETStandard|\\.NETCoreApp)(?:,Version=v([\\d\\.]+))?", RegexOptions.IgnoreCase);

        /// <summary>
        /// Whether this is .net framework, .net standard, .net core, etc.
        /// </summary>
        public DotNetFrameworkType FrameworkType { get; set; }

        /// <summary>
        /// The target version of this framework
        /// </summary>
        public Version FrameworkVersion { get; set; }

        public DotNetFrameworkVersion(DotNetFrameworkType frameworkType, Version frameworkVersion)
        {
            FrameworkType = frameworkType;
            FrameworkVersion = frameworkVersion;
        }

        public DotNetFrameworkVersion(string assemblyFrameworkString)
        {
            Match parserMatch = FrameworkVersionParser.Match(assemblyFrameworkString);
            if (parserMatch.Success)
            {
                if (string.Equals(parserMatch.Groups[1].Value, ".NETFramework", StringComparison.OrdinalIgnoreCase))
                {
                    FrameworkType = DotNetFrameworkType.NetFramework;
                }
                else if (string.Equals(parserMatch.Groups[1].Value, ".NETStandard", StringComparison.OrdinalIgnoreCase))
                {
                    FrameworkType = DotNetFrameworkType.NetStandard;
                }
                else if (string.Equals(parserMatch.Groups[1].Value, ".NETCoreApp", StringComparison.OrdinalIgnoreCase))
                {
                    FrameworkType = DotNetFrameworkType.NetCore;
                }
                else
                {
                    FrameworkType = DotNetFrameworkType.Unknown;
                }

                if (parserMatch.Groups[2].Success)
                {
                    Version scratch;
                    if (Version.TryParse(parserMatch.Groups[2].Value, out scratch))
                    {
                        FrameworkVersion = scratch;
                    }
                    else
                    {
                        FrameworkVersion = new Version(0, 0);
                    }
                }
                else
                {
                    FrameworkVersion = new Version(0, 0);
                }
            }
            else
            {
                FrameworkType = DotNetFrameworkType.Unknown;
                FrameworkVersion = new Version(0, 0);
            }
        }

        private DotNetFrameworkVersion()
        {
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write((int)FrameworkType);
            writer.Write(FrameworkVersion == null ? "" : FrameworkVersion.ToString());
        }

        public static DotNetFrameworkVersion Deserialize(BinaryReader reader)
        {
            DotNetFrameworkVersion returnVal = new DotNetFrameworkVersion();
            returnVal.FrameworkType = (DotNetFrameworkType)reader.ReadInt32();
            string scratch;
            scratch = reader.ReadString();
            Version frameworkVersion;
            if (!string.IsNullOrEmpty(scratch) && Version.TryParse(scratch, out frameworkVersion))
            {
                returnVal.FrameworkVersion = frameworkVersion;
            }

            return returnVal;
        }

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            DotNetFrameworkVersion other = (DotNetFrameworkVersion)obj;
            return FrameworkType == other.FrameworkType &&
                FrameworkVersion == other.FrameworkVersion;
        }

        public override int GetHashCode()
        {
            return FrameworkType.GetHashCode() ^
                (FrameworkVersion ?? new Version(0, 0)).GetHashCode();
        }

        public override string ToString()
        {
            if (FrameworkVersion == null)
            {
                switch (FrameworkType)
                {
                    case DotNetFrameworkType.NetFramework:
                        return ".NETFramework";
                    case DotNetFrameworkType.NetStandard:
                        return ".NETStandard";
                    case DotNetFrameworkType.NetCore:
                        return ".NETCore";
                    default:
                        return "Unknown";
                }
            }
            else
            {
                switch (FrameworkType)
                {
                    case DotNetFrameworkType.NetFramework:
                        return ".NETFramework,Version=" + FrameworkVersion.ToString();
                    case DotNetFrameworkType.NetStandard:
                        return ".NETStandard,Version=" + FrameworkVersion.ToString();
                    case DotNetFrameworkType.NetCore:
                        return ".NETCore,Version=" + FrameworkVersion.ToString();
                    default:
                        return "Unknown";
                }
            }
        }
    }
}
