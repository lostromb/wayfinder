using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wayfinder.UI.Schemas
{
    public class Project
    {
        public Dictionary<Guid, Component> Components { get; set; }

        [JsonIgnore]
        public Component RootComponent => Components[Guid.Empty];

        public Project()
        {
            Components = new Dictionary<Guid, Component>();

            Component rootComponent = new Component();
            rootComponent.Name = "Project";
            rootComponent.UniqueId = Guid.Empty;
            Components[Guid.Empty] = rootComponent;
        }

        public void AddComponent(Component toAdd, Guid? parentGuid = null)
        {
            if (Components.ContainsKey(toAdd.UniqueId))
            {
                throw new ArgumentException("Component already exists");
            }

            Components[toAdd.UniqueId] = toAdd;

            // Handle parent/child links
            Guid actualParentGuid = parentGuid.GetValueOrDefault(Guid.Empty);
            toAdd.Parent = actualParentGuid;
            
            Component parentComponent = Components[actualParentGuid];
            parentComponent.Children.Add(toAdd.UniqueId);
        }

        public void RemoveComponent(Guid toRemoveId)
        {
            if (toRemoveId == Guid.Empty)
            {
                return;
            }

            if (!Components.ContainsKey(toRemoveId))
            {
                return;
            }

            Component toRemove = Components[toRemoveId];
            // First, remove all subchildren recursively
            foreach (Guid childComponentId in toRemove.Children.ToArray())
            {
                RemoveComponent(childComponentId);
            }

            // Sever all links
            foreach (Guid link in toRemove.LinksTo.ToArray()) // make a clone of the link array so we can safely iterate
            {
                toRemove.UnlinkFrom(Components[link]);
            }

            // Remove child from its parent's list of children
            Components[toRemove.Parent].Children.Remove(toRemoveId);

            // Then delete the component
            Components.Remove(toRemoveId);
        }
    }
}
