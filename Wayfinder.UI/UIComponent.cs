using OpenTK;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wayfinder.UI.Schemas;

namespace WayfinderUI
{
    public class UIComponent
    {
        /// <summary>
        /// The component that this wraps around
        /// </summary>
        public Component BaseComponent { get; set; }

        /// <summary>
        /// Boundaries of this element relative to the global root element
        /// Units range from 0 to 1
        /// </summary>
        public Box2d AbsoluteBounds { get; set; }

        /// <summary>
        /// Indicates whether this component is showing its subcomponents or whether it is closed
        /// </summary>
        public bool IsOpen { get; set; }

        public bool IsFilteredOut { get; set; }

        public UIComponent(Component baseComponent)
        {
            BaseComponent = baseComponent;
            AbsoluteBounds = default(Box2d);
            IsOpen = false;
            IsFilteredOut = false;
        }
        
        public override string ToString()
        {
            return "UI:" + BaseComponent.Name + ":" + BaseComponent.UniqueId.ToString("N");
        }
    }
}
