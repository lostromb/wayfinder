using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wayfinder.Common.Schemas
{
    [Flags]
    public enum AssemblyInspectionType
    {
        None = 0x0,
        ShowIdentity = 0x1,
        ListReferences = 0x2,
        ListMembers = 0x4
    }
}
