using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wayfinder.Common.Logger
{
    [Flags]
    public enum LogLevel
    {
        Std = 0x01 << 0,
        Wrn = 0x01 << 1,
        Err = 0x01 << 2,
        Crt = 0x01 << 3,
        Vrb = 0x01 << 4
    }
}
