using Durandal.Common.MathExt;
using OpenTK;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Wayfinder.Common.Schemas;
using Wayfinder.Common;
using Wayfinder.Common.Logger;
using Wayfinder.DependencyResolver;
using Wayfinder.UI.Schemas;

namespace Wayfinder.UI
{
    public static class DependencyGraphConverter
    {
        public static Project ConvertDependencyGraphToProject(ISet<DependencyGraphNode> graphNodes, ILogger logger)
        {
            Project returnVal = new Project();

            IDictionary<DependencyGraphNode, Component> componentMapping = new Dictionary<DependencyGraphNode, Component>();

            foreach (DependencyGraphNode node in graphNodes)
            {
                Component newComponent = new Component();
                newComponent.AssemblyInfo = node.ThisAssembly;
                newComponent.HasDependents = node.IncomingConnections > 0;

                if (node.ThisAssembly.AssemblyType == BinaryType.Managed)
                {
                    if (node.ThisAssembly.AssemblyFilePath == null)
                    {
                        newComponent.ComponentType = AssemblyComponentType.Managed_Builtin;
                    }
                    else
                    {
                        newComponent.ComponentType = AssemblyComponentType.Managed_Local;
                    }
                }
                else
                {
                    if (node.ThisAssembly.AssemblyFilePath == null)
                    {
                        newComponent.ComponentType = AssemblyComponentType.Native_Builtin;
                    }
                    else
                    {
                        newComponent.ComponentType = AssemblyComponentType.Native_Local;
                    }
                }

                if (!string.IsNullOrEmpty(node.ThisAssembly.AssemblyBinaryName))
                {
                    newComponent.Name = node.ThisAssembly.AssemblyBinaryName;
                }
                else if (node.ThisAssembly.AssemblyFilePath != null)
                {
                    newComponent.Name = node.ThisAssembly.AssemblyFilePath.Name;
                }
                else
                {
                    newComponent.Name = "Unknown Assembly";
                }

                newComponent.Errors = node.Errors;
                returnVal.AddComponent(newComponent);
                componentMapping[node] = newComponent;
            }

            // Create links between UI components
            foreach (DependencyGraphNode sourceNode in graphNodes)
            {
                Component sourceComponent = componentMapping[sourceNode];
                foreach (DependencyGraphNode targetNode in sourceNode.Dependencies)
                {
                    Component targetComponent = componentMapping[targetNode];
                    sourceComponent.LinkTo(targetComponent);
                }
            }

            ArrangeComponentsOnCanvas(graphNodes, componentMapping, logger);

            return returnVal;
        }

        private static void ArrangeComponentsOnCanvas(ISet<DependencyGraphNode> graphNodes, IDictionary<DependencyGraphNode, Component> uiComponentMapping, ILogger logger)
        {
            // Now we need to figure out a sane way of arranging things. First, establish a strict ordering based on dependency heirarchy
            Dictionary<DependencyGraphNode, int> nodeColumns = new Dictionary<DependencyGraphNode, int>();
            foreach (DependencyGraphNode node in graphNodes)
            {
                nodeColumns[node] = 0;
            }

            for (int loopCount = 0; loopCount < graphNodes.Count; loopCount++)
            {
                foreach (DependencyGraphNode sourceNode in graphNodes)
                {
                    foreach (DependencyGraphNode targetNode in sourceNode.Dependencies)
                    {
                        int sourceNodeColumn = nodeColumns[sourceNode];
                        int targetNodeColumn = nodeColumns[targetNode];
                        if (sourceNodeColumn >= targetNodeColumn)
                        {
                            nodeColumns[sourceNode] = targetNodeColumn - 1;
                        }
                    }
                }
            }

            // Make sure column 0 is the first one.
            // It's possible there are circular dependencies, in which case this algorithm should just put
            // whatever has the fewest dependencies on column 0
            int minColumn = int.MaxValue;
            int maxColumn = int.MinValue;
            Counter<int> columnCounts = new Counter<int>(); // number of components in each column
            foreach (int column in nodeColumns.Values)
            {
                minColumn = Math.Min(minColumn, column);
                maxColumn = Math.Max(maxColumn, column);
            }

            foreach (DependencyGraphNode node in graphNodes)
            {
                int newVal = nodeColumns[node] - minColumn;
                nodeColumns[node] = newVal;
                columnCounts.Increment(newVal);
            }

            int numColumns = maxColumn - minColumn + 1;

            // Sort nodes into columns
            DependencyGraphNode[][] columns = new DependencyGraphNode[numColumns][];
            for (int c = 0; c < numColumns; c++)
            {
                columns[c] = new DependencyGraphNode[(int)columnCounts.GetCount(c)];
                int iter = 0;
                foreach (DependencyGraphNode node in graphNodes)
                {
                    int thisNodeColumn = nodeColumns[node];
                    if (c == thisNodeColumn)
                    {
                        columns[c][iter] = node;
                        iter++;
                    }
                }
            }

            // Try and optimize the arrangement of each column to minimize the distance between each dependency
            columns = OptimizeColumnLayout(columns, logger);

            double eachComponentWidth = 0.2 / Math.Sqrt(graphNodes.Count);
            double eachComponentHeight = eachComponentWidth / 2;
            FastConcurrentDictionary<DependencyGraphNode, Vector2d> dynamicGraph = OptimizeGraphLayout(columns, logger, eachComponentWidth, eachComponentHeight, new FastRandom());

            // Arrange columns onto the final canvas
            foreach (var kvp in dynamicGraph)
            {
                Component uiComponent = uiComponentMapping[kvp.Key];
                uiComponent.Bounds = new ComponentBounds()
                {
                    FromBottom = 1.0 - (kvp.Value.Y + eachComponentHeight),
                    FromLeft = kvp.Value.X - eachComponentWidth,
                    FromRight = 1.0 - (kvp.Value.X + eachComponentWidth),
                    FromTop = kvp.Value.Y - eachComponentHeight,
                };
            }
        }

        private static FastConcurrentDictionary<DependencyGraphNode, Vector2d> OptimizeGraphLayout(
            DependencyGraphNode[][] columns,
            ILogger logger,
            double componentWidth,
            double componentHeight,
            IRandom rand)
        {
            logger.Log("Converting from columns to cloud and optimizing layout...");
            FastConcurrentDictionary<DependencyGraphNode, Vector2d> bestGraph = ConvertColumnsToGraph(columns);
            const int NUM_CANDIDATES = 200;
            const int MAX_NUM_LOOPS = 100;

            FastConcurrentDictionary<DependencyGraphNode, Vector2d>[] graphs = new FastConcurrentDictionary<DependencyGraphNode, Vector2d>[NUM_CANDIDATES];
            double[] graphScores = new double[NUM_CANDIDATES];
            double lowestScore = double.MaxValue;
            for (int loop = 0; loop < MAX_NUM_LOOPS; loop++)
            {
                logger.Log("Loop " + loop + ": best score " + lowestScore);

                double progress = (double)loop / (MAX_NUM_LOOPS + 1);
                double increment = 0.2 * (1 - progress);

                // Mutate candidates
                for (int c = 0; c < NUM_CANDIDATES; c++)
                {
                    FastConcurrentDictionary<DependencyGraphNode, Vector2d> candidate = CloneGraph(bestGraph);
                    WiggleNodes(candidate, rand, increment);
                    //for (int ascent = 0; ascent < 3; ascent++)
                    {
                        GradientAscent(candidate, componentWidth * 3, increment);
                    }

                    for (int wiggle = 0; wiggle < 6; wiggle++)
                    {
                        SeparateGroupedNodes(candidate, componentWidth * 3, rand, increment * 2, true);
                        EnforceDependencyOrdering(candidate, componentWidth);
                        NormalizeNodeLocations(candidate);
                    }

                    MoveOutliersCloser(candidate);
                    IsolateDisconnectedNodes(candidate, rand);
                    EnforceDependencyOrdering(candidate, componentWidth);
                    NormalizeNodeLocations(candidate);

                    EnforceCanvasBounds(candidate, componentWidth, componentHeight);
                    graphScores[c] = EvaluateGraphArrangment(candidate);
                    graphs[c] = candidate;
                }

                // Find the best one
                double oldBestScore = lowestScore;
                Array.Sort(graphScores, graphs);
                if (graphScores[0] < lowestScore)
                {
                    bestGraph = graphs[0];
                    lowestScore = graphScores[0];
                }

                // Break the loop when the gains start to go lower than 1% per iteration
                if ((oldBestScore * 0.99) < lowestScore)
                {
                    break;
                }
            }

            // Separate the final graph
            for (int wiggle = 0; wiggle < 50; wiggle++)
            {
                double thisIncrement = 0.5 - (wiggle * (0.5 / 51));
                SeparateGroupedNodes(bestGraph, componentWidth * 3, rand, thisIncrement, true);
                //EnforceDependencyOrdering(bestGraph, componentWidth);
            }

            NormalizeNodeLocations(bestGraph);
            EnforceCanvasBounds(bestGraph, componentWidth, componentHeight);

            return bestGraph;
        }

        private static void GradientAscent(FastConcurrentDictionary<DependencyGraphNode, Vector2d> graph, double idealNodeDistance, double ascentIncrement)
        {
            foreach (var kvp in graph)
            {
                foreach (var dependency in kvp.Key.Dependencies)
                {
                    Vector2d sourcePos = kvp.Value;
                    Vector2d destPos = graph[dependency];
                    Vector2d sourceToDestDirection = (destPos - sourcePos);
                    if (sourceToDestDirection.LengthSquared == 0)
                    {
                        // If nodes are in exactly the same location, nudge to the right
                        sourceToDestDirection = new Vector2d(1.0, 0.0);
                    }
                    else
                    {
                        sourceToDestDirection.Normalize();
                    }

                    Vector2d idealSourcePos = destPos - (sourceToDestDirection * idealNodeDistance);
                    Vector2d finalMotionVec = (idealSourcePos - sourcePos) * ascentIncrement;

                    // Weightier nodes are less inclined to move
                    double sourceMovementRatio = dependency.NodeWeight / (kvp.Key.NodeWeight + dependency.NodeWeight);

                    // Move source node
                    graph[kvp.Key] = graph[kvp.Key] + (finalMotionVec * sourceMovementRatio);
                    //if (double.IsNaN(graph[kvp.Key].X)) throw new ArithmeticException();

                    // Move destination node
                    graph[dependency] = graph[dependency] - (finalMotionVec * (1.0 - sourceMovementRatio));
                    //if (double.IsNaN(graph[dependency].X)) throw new ArithmeticException();
                }
            }
        }

        private static FastConcurrentDictionary<DependencyGraphNode, Vector2d> CloneGraph(FastConcurrentDictionary<DependencyGraphNode, Vector2d> graph)
        {
            FastConcurrentDictionary<DependencyGraphNode, Vector2d> clone = new FastConcurrentDictionary<DependencyGraphNode, Vector2d>(graph.Count);
            foreach (var kvp in graph)
            {
                clone.Add(kvp);
            }

            return clone;
        }

        private static double EvaluateGraphArrangment(IDictionary<DependencyGraphNode, Vector2d> graphLayout)
        {
            // Sum up the distance between each connected node
            double sumDistance = 0;
            foreach (var kvp in graphLayout)
            {
                foreach (var dependency in kvp.Key.Dependencies)
                {
                    Vector2d otherLoc = graphLayout[dependency];
                    sumDistance += (otherLoc - kvp.Value).LengthSquared;
                }
            }

            return sumDistance;
        }

        private static void IsolateDisconnectedNodes(FastConcurrentDictionary<DependencyGraphNode, Vector2d> graph, IRandom rand)
        {
            MovingPercentile yPercentile = new MovingPercentile(200, 0.25, 0.5, 0.75, 0.95);
            foreach (var kvp in graph)
            {
                yPercentile.Add(kvp.Value.Y);
            }

            // Create a haven for disconnected nodes in the bottom edge
            double maxY = yPercentile.GetPercentile(0.95);
            foreach (var kvp in graph)
            {
                if (kvp.Key.IncomingConnections == 0 && kvp.Key.OutgoingConnections == 0)
                {
                   graph[kvp.Key] = new Vector2d(
                        rand.NextDouble(),
                        (rand.NextDouble() * 0.1) + maxY);
                }
            }
        }

        private const double _sigGranularity = 50000f;
        private const double _sigRange = 2f; // POSITIVE AND NEGATIVE
        private static double[] _sigCache;

        /// <summary>
        /// Initialize the tables
        /// </summary>
        static DependencyGraphConverter()
        {
            _sigCache = new double[(int)(_sigGranularity * _sigRange * 2)];
            for (int c = 0; c < _sigCache.Length; c++)
            {
                double input = (c / _sigGranularity) - _sigRange;
                _sigCache[c] = (2 / (1 + Math.Exp(-3 * input))) - 1;
            }
        }

        /// <summary>
        /// Calculates a sigmoid curve, where domain is (-inf, inf) and range is (0, 1)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double OutlierCurve(double value)
        {
            int index = (int)((value + _sigRange) * _sigGranularity);
            if (index < 0 || index >= _sigCache.Length)
                return (2 / (1 + Math.Exp(-3 * value))) - 1;
            return _sigCache[index];
        }

        private static void MoveOutliersCloser(FastConcurrentDictionary<DependencyGraphNode, Vector2d> graph)
        {
            StaticAverage avgX = new StaticAverage();
            StaticAverage avgY = new StaticAverage();
            foreach (KeyValuePair<DependencyGraphNode, Vector2d> kvp in graph)
            {
                avgX.Add(kvp.Value.X);
                avgY.Add(kvp.Value.Y);
            }

            Vector2d centroid = new Vector2d(avgX.Average, avgY.Average);
            foreach (KeyValuePair<DependencyGraphNode, Vector2d> kvp in graph)
            {
                Vector2d diff = kvp.Value - centroid;
                double xDelta = OutlierCurve(diff.X);
                double yDelta = OutlierCurve(diff.Y);
                graph[kvp.Key] = new Vector2d(centroid.X + xDelta, centroid.Y + yDelta);
                if (double.IsNaN(graph[kvp.Key].X)) throw new ArithmeticException();
            }
        }

        private static void SeparateGroupedNodes(
            FastConcurrentDictionary<DependencyGraphNode, Vector2d> graph,
            double minNodeDistance,
            IRandom rand,
            double increment,
            bool weighted)
        {
            foreach (var sourceKvp in graph)
            {
                DependencyGraphNode sourceNode = sourceKvp.Key;
                foreach (var destKvp in graph)
                {
                    DependencyGraphNode destNode = destKvp.Key;
                    if (sourceNode == destNode)
                    {
                        continue;
                    }

                    double connectionWeight = weighted ? 1 : Math.Min(3, Math.Max(1.0, (sourceNode.NodeWeight + destNode.NodeWeight) / 2));
                    double recommendedNodeDistance = minNodeDistance * connectionWeight;

                    Vector2d sourcePos = sourceKvp.Value;
                    Vector2d destPos = destKvp.Value;
                    Vector2d sourceToDestDirection = (destPos - sourcePos);
                    double dist = sourceToDestDirection.Length;
                    if (dist < recommendedNodeDistance)
                    {
                        if (dist == 0)
                        {
                            // If nodes are in exactly the same location, nudge to a random direction
                            sourceToDestDirection = new Vector2d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5);
                        }

                        // Also add a bit of random nudge to break up components that are stacked perfectly on top
                        //sourceToDestDirection.X += (rand.NextDouble() - 0.5) * 0.1;
                        //sourceToDestDirection.Y += (rand.NextDouble() - 0.5) * 0.1;

                        sourceToDestDirection.Normalize();
                        Vector2d idealSourcePos = destPos - (sourceToDestDirection * recommendedNodeDistance);
                        Vector2d finalMotionVec = (idealSourcePos - sourcePos) * increment;

                        // Weightier nodes are less inclined to move
                        double sourceMovementRatio = 0.5;
                        if (destNode.NodeWeight != 0 && sourceNode.NodeWeight != 0)
                        {
                            sourceMovementRatio = destNode.NodeWeight / (sourceNode.NodeWeight + destNode.NodeWeight);
                        }

                        // Move source node
                        graph[sourceNode] = graph[sourceNode] + (finalMotionVec * sourceMovementRatio);
                        //if (double.IsNaN(graph[sourceNode].X)) throw new ArithmeticException();

                        // Move destination node
                        graph[destNode] = graph[destNode] - (finalMotionVec * (1.0 - sourceMovementRatio));
                        //if (double.IsNaN(graph[destNode].X)) throw new ArithmeticException();
                    }
                }
            }
        }

        private static void EnforceDependencyOrdering(FastConcurrentDictionary<DependencyGraphNode, Vector2d> graph, double componentWidth)
        {
            foreach (KeyValuePair<DependencyGraphNode, Vector2d> kvp in graph)
            {
                foreach (DependencyGraphNode dependency in kvp.Key.Dependencies)
                {
                    Vector2d dependencyPos = graph[dependency];
                    double maxLeft = kvp.Value.X + componentWidth;
                    if (dependencyPos.X < maxLeft)
                    {
                        graph[dependency] = new Vector2d(kvp.Value.X + (componentWidth / 2), dependencyPos.Y);
                        //if (double.IsNaN(graph[dependency].X)) throw new ArithmeticException();
                        graph[kvp.Key] = new Vector2d(kvp.Value.X - (componentWidth / 2), kvp.Value.Y);
                        //if (double.IsNaN(graph[kvp.Key].X)) throw new ArithmeticException();
                    }
                }
            }
        }

        private static void WiggleNodes(FastConcurrentDictionary<DependencyGraphNode, Vector2d> graph, IRandom rand, double increment)
        {
            foreach (KeyValuePair<DependencyGraphNode, Vector2d> kvp in graph)
            {
                double dX = increment * (rand.NextDouble() - 0.5);
                double dY = increment * (rand.NextDouble() - 0.5);
                graph[kvp.Key] = new Vector2d(kvp.Value.X + dX, kvp.Value.Y + dY);
                //if (double.IsNaN(graph[kvp.Key].X)) throw new ArithmeticException();
            }
        }

        private static void NormalizeNodeLocations(FastConcurrentDictionary<DependencyGraphNode, Vector2d> graph)
        {
            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minY = double.MaxValue;
            double maxY = double.MinValue;
            foreach (KeyValuePair<DependencyGraphNode, Vector2d> kvp in graph)
            {
                maxX = Math.Max(maxX, kvp.Value.X);
                maxY = Math.Max(maxY, kvp.Value.Y);
                minX = Math.Min(minX, kvp.Value.X);
                minY = Math.Min(minY, kvp.Value.Y);
            }

            const double margin = 0.05;
            const double marginalSize = 1 - margin - margin;
            double width = (maxX - minX);
            double height = (maxY - minY);

            foreach (KeyValuePair<DependencyGraphNode, Vector2d> kvp in graph)
            {
                graph[kvp.Key] = new Vector2d(
                    margin + (marginalSize * (kvp.Value.X - minX) / width),
                    margin + (marginalSize * (kvp.Value.Y - minY) / height));
                //if (double.IsNaN(graph[kvp.Key].X)) throw new ArithmeticException();
            }
        }

        private static void EnforceCanvasBounds(
            FastConcurrentDictionary<DependencyGraphNode, Vector2d> graph,
            double componentWidth,
            double componentHeight)
        {
            foreach (KeyValuePair<DependencyGraphNode, Vector2d> kvp in graph)
            {
                Vector2d pos = kvp.Value;
                bool changed = false;
                if (pos.X < componentWidth)
                {
                    pos.X = componentWidth;
                    changed = true;
                }
                if (pos.X > 1 - componentWidth)
                {
                    pos.X = 1 - componentWidth;
                    changed = true;
                }
                if (pos.Y < componentHeight)
                {
                    pos.Y = componentHeight;
                    changed = true;
                }
                if (pos.Y > 1 - componentHeight)
                {
                    pos.Y = 1 - componentHeight;
                    changed = true;
                }
                if (changed)
                {
                    graph[kvp.Key] = pos;
                    //if (double.IsNaN(graph[kvp.Key].X)) throw new ArithmeticException();
                }
            }
        }

        private static FastConcurrentDictionary<DependencyGraphNode, Vector2d> ConvertColumnsToGraph(DependencyGraphNode[][] columns)
        {
            FastConcurrentDictionary<DependencyGraphNode, Vector2d> nodePoints = new FastConcurrentDictionary<DependencyGraphNode, Vector2d>();
            for (int columnX = 0; columnX < columns.Length; columnX++)
            {
                DependencyGraphNode[] column = columns[columnX];
                for (int columnY = 0; columnY < column.Length; columnY++)
                {
                    DependencyGraphNode node = column[columnY];
                    nodePoints[node] = new Vector2d(GetCenterX(columnX, columns.Length), GetCenterY(columnY, column.Length));
                }
            }

            return nodePoints;
        }

        private static DependencyGraphNode[][] OptimizeColumnLayout(DependencyGraphNode[][] columns, ILogger logger)
        {
            IRandom rand = new FastRandom();
            DependencyGraphNode[][] bestArrangement = columns;
            double lowestScore = EvaluateColumnArrangment(bestArrangement);
            List<DependencyGraphNode[][]> mutations = new List<DependencyGraphNode[][]>();
            const int NUM_MUTATIONS = 50;
            const int MAX_NUM_LOOPS = 50;
            logger.Log("Optimizing column layout...");
            for (int loop = 0; loop < MAX_NUM_LOOPS; loop++)
            {
                logger.Log("Loop " + loop + ": best score " + lowestScore);
                // Step 1 - Create random mutations
                mutations.Clear();
                for (int c = 0; c < NUM_MUTATIONS; c++)
                {
                    DependencyGraphNode[][] clone = CloneColumns(bestArrangement);
                    // Step 2 - Run gradient ascent on them
                    mutations.Add(GradientAscent(MutateColumns(clone, rand), rand));
                }

                // Step 3 - Pick the best mutation
                int bestResultIdx = -1;
                double oldBestScore = lowestScore;
                for (int c = 0; c < NUM_MUTATIONS; c++)
                {
                    double score = EvaluateColumnArrangment(mutations[c]);
                    if (score < lowestScore)
                    {
                        lowestScore = score;
                        bestResultIdx = c;
                    }
                }

                if (bestResultIdx >= 0)
                {
                    bestArrangement = mutations[bestResultIdx];
                }

                // Break the loop when the gains start to go lower than 1% per iteration
                if ((oldBestScore * 0.99) < lowestScore)
                {
                    break;
                }
            }

            return bestArrangement;
        }

        private static DependencyGraphNode[][] MutateColumns(DependencyGraphNode[][] columns, IRandom random)
        {
            for (int columnX = 0; columnX < columns.Length; columnX++)
            {
                DependencyGraphNode[] column = columns[columnX];
                int numSwapsThisColumn = random.NextInt(0, column.Length);
                int swapDist = Math.Max(1, (int)column.Length / 3);

                for (int swapCount = 0; swapCount < numSwapsThisColumn; swapCount++)
                {
                    int startColumnY = random.NextInt(0, column.Length - swapDist);
                    int endColumnY = startColumnY + random.NextInt(0, swapDist + 1);
                    DependencyGraphNode swap = column[startColumnY];
                    column[startColumnY] = column[endColumnY];
                    column[endColumnY] = swap;
                }
            }

            return columns;
        }

        private static DependencyGraphNode[][] GradientAscent(DependencyGraphNode[][] columns, IRandom random)
        {
            double ascentIncrement = random.NextDouble() * 0.5;
            // Initialize current positions of all elements
            IDictionary<DependencyGraphNode, Vector2d> positions = ConvertColumnsToGraph(columns);

            // Augment y positions based on the ideal average of all dependencies
            for (int columnX = 0; columnX < columns.Length; columnX++)
            {
                DependencyGraphNode[] inputColumn = columns[columnX];
                for (int columnY = 0; columnY < inputColumn.Length; columnY++)
                {
                    DependencyGraphNode source = inputColumn[columnY];
                    Vector2d sourcePos = positions[source];
                    foreach (DependencyGraphNode target in source.Dependencies)
                    {
                        Vector2d targetPos = positions[target];
                        double yDiff = (targetPos.Y - sourcePos.Y) * ascentIncrement;
                        sourcePos.Y += yDiff;
                        targetPos.Y -= yDiff;
                        positions[target] = targetPos; // need to explicitly write back to dict because vector is a value type
                    }

                    positions[source] = sourcePos;
                }
            }

            // Now do a paired array sort of each column based on the updated positions
            for (int columnX = 0; columnX < columns.Length; columnX++)
            {
                DependencyGraphNode[] nodeColumn = columns[columnX];
                if (nodeColumn.Length < 2)
                {
                    continue;
                }

                double[] yPosColumn = new double[nodeColumn.Length];
                for (int columnY = 0; columnY < nodeColumn.Length; columnY++)
                {
                    yPosColumn[columnY] = positions[nodeColumn[columnY]].Y;
                }

                Array.Sort(yPosColumn, nodeColumn);
            }

            return columns;
        }

        private static DependencyGraphNode[][] CloneColumns(DependencyGraphNode[][] columns)
        {
            DependencyGraphNode[][] returnVal = new DependencyGraphNode[columns.Length][];
            for (int columnX = 0; columnX < columns.Length; columnX++)
            {
                DependencyGraphNode[] inputColumn = columns[columnX];
                returnVal[columnX] = new DependencyGraphNode[inputColumn.Length];
                for (int columnY = 0; columnY < inputColumn.Length; columnY++)
                {
                    returnVal[columnX][columnY] = inputColumn[columnY];
                }
            }

            return returnVal;
        }

        private static double EvaluateColumnArrangment(DependencyGraphNode[][] columns, IDictionary<DependencyGraphNode, Vector2d> graphLayout = null)
        {
            graphLayout = graphLayout ?? ConvertColumnsToGraph(columns);

            // Sum up the distance between each connected node
            double sumDistance = 0;
            for (int columnX = 0; columnX < columns.Length; columnX++)
            {
                DependencyGraphNode[] column = columns[columnX];
                for (int columnY = 0; columnY < column.Length; columnY++)
                {
                    DependencyGraphNode node = column[columnY];
                    Vector2d nodeLoc = graphLayout[node];
                    foreach (var dependency in node.Dependencies)
                    {
                        Vector2d otherLoc = graphLayout[dependency];
                        sumDistance += Math.Abs(nodeLoc.Y - otherLoc.Y);
                    }
                }
            }

            return sumDistance;
        }

        private static double GetCenterX(int columnX, int numColumns)
        {
            return ((double)columnX + 0.5) / (double)numColumns;
        }

        private static double GetCenterY(int columnY, int columnHeight)
        {
            return ((double)columnY + 0.5) / (double)columnHeight;
        }
    }
}
