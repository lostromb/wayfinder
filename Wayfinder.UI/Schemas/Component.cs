using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wayfinder.Common.Schemas;

namespace Wayfinder.UI.Schemas
{
    public class Component
    {
        public Guid UniqueId { get; set; }
        public string Name { get; set; }
        public Guid Parent { get; set; }
        public HashSet<Guid> Children { get; set; }
        public AssemblyData AssemblyInfo { get; set; }
        public AssemblyComponentType ComponentType { get; set; }
        public bool HasDependents { get; set; }
        public List<string> Errors { get; set; }

        /// <summary>
        /// Boundaries, given as relative percentages from the boundaries of the parent container's edges
        /// </summary>
        public ComponentBounds Bounds { get; set; }
        public HashSet<Guid> LinksTo { get; set; }

        public Component()
        {
            UniqueId = Guid.NewGuid();
            Children = new HashSet<Guid>();
            Bounds = new ComponentBounds();
            LinksTo = new HashSet<Guid>();
        }

        public void LinkTo(Component other)
        {
            if (other == this)
            {
                throw new Exception("Can't make a reflexive link");
            }

            LinksTo.Add(other.UniqueId);
        }

        public void UnlinkFrom(Component other)
        {
            if (LinksTo.Contains(other.UniqueId))
            {
                LinksTo.Remove(other.UniqueId);
            }
        }

        public override int GetHashCode()
        {
            return UniqueId.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            Component other = obj as Component;
            if (other != null)
            {
                return false;
            }

            return other.UniqueId == UniqueId;
        }

        public override string ToString()
        {
            return Name + ":" + UniqueId.ToString("N");
        }
    }
}
