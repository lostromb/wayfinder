using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wayfinder.DependencyResolver.Schemas;

namespace Wayfinder.DependencyResolver
{
    public class DependencyGraphNode
    {
        public AssemblyData ThisAssembly { get; }

        public ISet<DependencyGraphNode> Dependencies { get; }

        /// <summary>
        /// The total number of connections going to this node
        /// </summary>
        public int IncomingConnections { get; set; }

        /// <summary>
        /// The total number of connections going out of this node
        /// </summary>
        public int OutgoingConnections { get; set; }

        /// <summary>
        /// A logarithmic approximation of how heavy this node should be based on its connection count
        /// </summary>
        public double NodeWeight { get; set; }

        public List<string> Errors { get; set; }

        public DependencyGraphNode(AssemblyData assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            ThisAssembly = assembly;
            Dependencies = new HashSet<DependencyGraphNode>();
            Errors = new List<string>();
        }

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            DependencyGraphNode other = (DependencyGraphNode)obj;
            return ThisAssembly == other.ThisAssembly;
        }

        public override int GetHashCode()
        {
            return ThisAssembly.GetHashCode();
        }

        public override string ToString()
        {
            return ThisAssembly.ToString();
        }
    }
}
