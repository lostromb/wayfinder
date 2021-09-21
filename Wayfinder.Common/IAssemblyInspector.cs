using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Wayfinder.Common.Schemas;

namespace Wayfinder.Common
{
    public interface IAssemblyInspector
    {
        AssemblyData InspectAssemblyFile(FileInfo assemblyFile);
        string InspectorName { get; }
    }
}
