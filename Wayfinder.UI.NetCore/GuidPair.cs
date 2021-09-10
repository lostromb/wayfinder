using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WayfinderUI
{
    public struct GuidPair
    {
        public Guid A;
        public Guid B;

        public GuidPair(Guid a, Guid b)
        {
            A = a;
            B = b;
        }

        public override bool Equals(object obj)
        {
            if (obj.GetType() != typeof(GuidPair))
            {
                return false;
            }

            GuidPair other = (GuidPair)obj;
            return (A == other.A && B == other.B) ||
                (B == other.A && A == other.B);
        }

        public override int GetHashCode()
        {
            return A.GetHashCode() ^ B.GetHashCode();
        }
    }
}
