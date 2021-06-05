using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wayfinder.UI.Schemas
{
    public enum AssemblyComponentType
    {
        // Unknown assembly type
        Unknown,

        // A managed assembly in the current project
        Managed_Local,

        // A native assembly in the current project
        Native_Local,

        // A managed assembly from the core runtime (i.e. System.Numerics)
        Managed_Builtin,

        // A native assembly from the core runtime (i.e. kernel32)
        Native_Builtin
    }
}
