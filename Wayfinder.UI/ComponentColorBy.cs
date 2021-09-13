using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wayfinder.UI
{
    public enum ComponentColorBy
    {
        LibraryType, // Color by whether it's a managed dll, native dll, exe, etc.
        FrameworkVersion, // Color by whether it's .net framework 4.5, 4.7.1, .net standard, .net core, etc.
    }
}
