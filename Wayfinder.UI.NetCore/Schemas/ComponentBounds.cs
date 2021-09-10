using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wayfinder.UI.Schemas
{
    public class ComponentBounds
    {
        public double FromLeft { get; set; }
        public double FromTop { get; set; }
        public double FromRight { get; set; }
        public double FromBottom { get; set; }

        public ComponentBounds Clone()
        {
            return new ComponentBounds()
            {
                FromBottom = this.FromBottom,
                FromTop = this.FromTop,
                FromRight = this.FromRight,
                FromLeft = this.FromLeft,
            };
        }

        [JsonIgnore]
        public double Width => 1 - FromLeft - FromRight;
        [JsonIgnore]
        public double Height => 1 - FromTop - FromBottom;
        [JsonIgnore]
        public double CenterX => FromLeft + (Width / 2);
        [JsonIgnore]
        public double CenterY => FromTop + (Height / 2);
    }
}
