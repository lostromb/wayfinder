using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Wayfinder.Common.Schemas
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
            if (frameworkVersion == null)
            {
                throw new ArgumentNullException(nameof(frameworkVersion));
            }

            FrameworkType = frameworkType;
            FrameworkVersion = frameworkVersion;
        }

        public DotNetFrameworkVersion(string assemblyFrameworkString)
        {
            if (string.IsNullOrEmpty(assemblyFrameworkString))
            {
                FrameworkType = DotNetFrameworkType.Unknown;
                FrameworkVersion = VERSION_0_0;
                return;
            }

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
                        FrameworkVersion = VERSION_0_0;
                    }
                }
                else
                {
                    FrameworkVersion = VERSION_0_0;
                }
            }
            else
            {
                FrameworkType = DotNetFrameworkType.Unknown;
                FrameworkVersion = VERSION_0_0;
            }
        }

        private DotNetFrameworkVersion()
        {
            FrameworkType = DotNetFrameworkType.Unknown;
            FrameworkVersion = VERSION_0_0;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write((int)FrameworkType);
            writer.Write(FrameworkVersion == null ? string.Empty : FrameworkVersion.ToString());
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
                (FrameworkVersion ?? VERSION_0_0).GetHashCode();
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

        public static readonly Version VERSION_0_0 = new Version(0, 0);
        public static readonly Version VERSION_1_0 = new Version("1.0");
        public static readonly Version VERSION_1_1 = new Version("1.1");
        public static readonly Version VERSION_1_2 = new Version("1.2");
        public static readonly Version VERSION_1_3 = new Version("1.3");
        public static readonly Version VERSION_1_4 = new Version("1.4");
        public static readonly Version VERSION_1_5 = new Version("1.5");
        public static readonly Version VERSION_1_6 = new Version("1.6");

        public static readonly Version VERSION_2_0 = new Version("2.0");
        public static readonly Version VERSION_2_1 = new Version("2.1");

        public static readonly Version VERSION_3_0 = new Version("3.0");
        public static readonly Version VERSION_3_1 = new Version("3.1");

        public static readonly Version VERSION_4_0 = new Version("4.0");
        public static readonly Version VERSION_4_5 = new Version("4.5");
        public static readonly Version VERSION_4_5_1 = new Version("4.5.1");
        public static readonly Version VERSION_4_5_2 = new Version("4.5.2");
        public static readonly Version VERSION_4_6 = new Version("4.6");
        public static readonly Version VERSION_4_6_1 = new Version("4.6.1");
        public static readonly Version VERSION_4_6_2 = new Version("4.6.2");
        public static readonly Version VERSION_4_7 = new Version("4.7");
        public static readonly Version VERSION_4_7_1 = new Version("4.7.1");
        public static readonly Version VERSION_4_7_2 = new Version("4.7.2");
        public static readonly Version VERSION_4_8 = new Version("4.8");

        public static readonly Version VERSION_5_0 = new Version("5.0");

        public static readonly Version VERSION_6_0 = new Version("6.0");
    }
}
