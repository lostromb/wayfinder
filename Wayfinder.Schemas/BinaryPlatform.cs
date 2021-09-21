using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wayfinder.DependencyResolver.Schemas
{
    /// <summary>
    /// Enumerates the type of platform that a binary supports (x86, x64, etc.)
    /// </summary>
    public enum BinaryPlatform
    {
        Unknown,
        AnyCPU,
        AnyCPUPrefer32,
        AMD64,
        X86,
    }
}
