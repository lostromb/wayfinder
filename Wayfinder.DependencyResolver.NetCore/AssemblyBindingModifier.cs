using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wayfinder.DependencyResolver
{
    /// <summary>
    /// A rule usually derived from a dll.config file which alters the assembly binding logic, such as a version override
    /// </summary>
    public class AssemblyBindingModifier
    {
        /// <summary>
        /// The name of the binary being redirected, e.g. "Microsoft.Extensions.DependencyInjection"
        /// </summary>
        public string AssemblyBinaryName { get; set; }

        /// <summary>
        /// The inclusive start of the old version that this redirect applies to
        /// </summary>
        public Version OldVersionMinimumRange { get; set; }

        /// <summary>
        /// The inclusive end of the old version that this redirect applies to
        /// </summary>
        public Version OldVersionMaximumRange { get; set; }

        /// <summary>
        /// The newVersion attribute of the binding redirect, if an override version is specified.
        /// </summary>
        public Version NewVersion { get; set; }

        /// <summary>
        /// If there is a &lt;codeBase&gt; statement in the assembly binding config, this is the
        /// href attribute of the code base redirect. This is a hint to the runtime to look for this reference in a
        /// very specific file path, e.g. "Bond_531/Bond.Reflection.dll"
        /// </summary>
        public string TargetCodeBase { get; set; }
    }
}
