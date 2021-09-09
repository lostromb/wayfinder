using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wayfinder.DependencyResolver.Schemas;

namespace Wayfinder.DependencyResolver
{
    public static class UbiquitousHelpers
    {
        public static BinaryType ReferenceTypeToAssemblyType(AssemblyReferenceType referenceType)
        {
            switch (referenceType)
            {
                case AssemblyReferenceType.Managed:
                    return BinaryType.Managed;
                case AssemblyReferenceType.Native:
                case AssemblyReferenceType.PInvoke:
                    return BinaryType.Native;
                default:
                    return BinaryType.Unknown;
            }
        }

        public static AssemblyReferenceType AssemblyTypeToReferenceType(BinaryType source, BinaryType target)
        {
            if (source == BinaryType.Managed && target == BinaryType.Managed)
            {
                return AssemblyReferenceType.Managed;
            }
            else if (source == BinaryType.Managed && target == BinaryType.Native)
            {
                return AssemblyReferenceType.PInvoke;
            }
            else if (source == BinaryType.Native && target == BinaryType.Native)
            {
                return AssemblyReferenceType.Native;
            }
            else
            {
                return AssemblyReferenceType.Unknown;
            }
        }

        /// <summary>
        /// Normalized edit distance (Levenshtein) algorithm for computing divergence of two strings
        /// </summary>
        /// <param name="one"></param>
        /// <param name="two"></param>
        /// <returns>A value between 0 (no divergence) and 1 (maximum divergence)</returns>
        public static float NormalizedEditDistance(string one, string two)
        {
            string compareOne = one.ToLowerInvariant();
            string compareTwo = two.ToLowerInvariant();
            const int insertWeight = 4;
            const int offsetWeight = 3;
            const int editWeight = 2;

            // The old magic box
            int[] gridA = new int[one.Length + 1];
            int[] gridB = new int[one.Length + 1];
            int[] distA = new int[one.Length + 1];
            int[] distB = new int[one.Length + 1];
            int[] temp;

            // Initialize the horizontal grid values
            for (int x = 0; x <= one.Length; x++)
            {
                gridA[x] = x * insertWeight;
                distA[x] = x;
            }

            for (int y = 1; y <= two.Length; y++)
            {
                // Initialize the vertical grid value
                gridB[0] = y * insertWeight;
                distB[0] = y;

                // Iterate through the DP table
                for (int x = 1; x <= one.Length; x++)
                {
                    int diagWeight = gridA[x - 1];
                    if (compareOne[x - 1] != compareTwo[y - 1])
                    {
                        diagWeight += editWeight;
                    }
                    int leftWeight = gridB[x - 1];
                    if (compareOne[x - 1] != compareTwo[y - 1])
                    {
                        leftWeight += offsetWeight;
                    }
                    else
                    {
                        leftWeight += insertWeight;
                    }
                    int upWeight = gridA[x];
                    if (compareOne[x - 1] != compareTwo[y - 1])
                    {
                        upWeight += offsetWeight;
                    }
                    else
                    {
                        upWeight += insertWeight;
                    }

                    if (diagWeight < leftWeight && diagWeight < upWeight)
                    {
                        gridB[x] = diagWeight;
                        distB[x] = distA[x - 1] + 1;
                    }
                    else if (leftWeight < upWeight)
                    {
                        gridB[x] = leftWeight;
                        distB[x] = distB[x - 1] + 1;
                    }
                    else
                    {
                        gridB[x] = upWeight;
                        distB[x] = distA[x] + 1;
                    }
                }

                // Swap the buffers
                temp = gridA;
                gridA = gridB;
                gridB = temp;

                temp = distA;
                distA = distB;
                distB = temp;
            }

            // Extract the return value from the corner of the DP table
            float minWeight = gridA[one.Length];
            // Normalize it based on the length of the path that was taken
            float pathLength = distA[one.Length];
            if (pathLength == 0)
                return 0;

            return minWeight / pathLength / insertWeight;
        }

        public static bool CanBindReferenceExactly(AssemblyReferenceName reference, AssemblyData candidateAssembly)
        {
            return string.Equals(reference.AssemblyBinaryName, candidateAssembly.AssemblyBinaryName, StringComparison.OrdinalIgnoreCase) &&
                reference.ReferencedAssemblyVersion == candidateAssembly.AssemblyVersion;
        }
    }
}
