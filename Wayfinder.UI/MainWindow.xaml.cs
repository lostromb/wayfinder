using Durandal.Common.MathExt;
using Durandal.Common.Time;
using Newtonsoft.Json;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Wpf;
using QuickFont;
using QuickFont.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Wayfinder.DependencyResolver;
using Wayfinder.DependencyResolver.Nuget;
using Wayfinder.DependencyResolver.Schemas;
using Wayfinder.UI;
using Wayfinder.UI.Schemas;
using WayfinderUI;

namespace Wayfinder.UI.NetCore
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly object _mutex;
        private readonly IDictionary<Guid, UIComponent> _uiComponents = new Dictionary<Guid, UIComponent>();
        private readonly NugetPackageCache _packageCache;
        private readonly Wayfinder.DependencyResolver.Logger.ILogger _logger;

        private Project _project;
        private Guid _selectedComponentId = Guid.Empty;

        private QFont _font;
        private QFontDrawing _drawing;
        private QFontRenderOptions _renderOptions;

        private bool _initializedResources = false;

        // view center, given in canvas units where the root element is a square (0,0) -> (1,1)
        private Point _viewCenter = new Point(0.5, 0.5);

        // zoom factor, linearly scaled
        private double _zoom = 1;

        private bool _isHoldingRightMouse = false;
        private bool _isHoldingLeftMouse = false;
        private bool _isMovingComponent = false;
        private bool _isResizingComponent = false;
        private Point _mouseDragStartCanvasCenterPoint = new Point();
        private Point _mouseDragStartVirtualCenterPoint = new Point();
        private UIComponent _uiComponentBeingManipulated = null;
        private ComponentBounds _uiComponentBeingManipulatedOriginalBounds;

        private GLTexture _textureResizeHandle;
        private GLTexture _textureExpandButton;

        private readonly Counter<GuidPair> _cachedLinePairs = new Counter<GuidPair>();


        public MainWindow()
        {
            _mutex = new object();
            _project = new Project();
            _logger = new Wayfinder.DependencyResolver.Logger.DebugLogger();

            InitializeProject();
            InitializeComponent();
            _packageCache = new NugetPackageCache();

            var settings = new GLWpfControlSettings();
            settings.MajorVersion = 2;
            settings.MinorVersion = 0;
            Canvas.Start(settings);
            UpdateUiElements();
        }

        ~MainWindow()
        {
        }

        private Task SaveDocumentInBackground(IRealTimeProvider realTime)
        {
            Monitor.Enter(_mutex);
            try
            {
                string json = JsonConvert.SerializeObject(_project);
                File.WriteAllText("project.json.bak", json);
                if (File.Exists("project.json"))
                {
                    File.Delete("project.json");
                }

                File.Move("project.json.bak", "project.json");
                return Task.CompletedTask;
            }
            finally
            {
                Monitor.Exit(_mutex);
            }
        }

        private void InitializeProject()
        {
            Monitor.Enter(_mutex);
            try
            {
                if (File.Exists("project.json"))
                {
                    string json = File.ReadAllText("project.json");
                    _project = JsonConvert.DeserializeObject<Project>(json);
                }
                else if (File.Exists("project.json.bak"))
                {
                    string json = File.ReadAllText("project.json.bak");
                    _project = JsonConvert.DeserializeObject<Project>(json);
                }

                RebuildUiComponents();
            }
            finally
            {
                Monitor.Exit(_mutex);
            }
        }

        private void RebuildUiComponents()
        {
            // Rebuild the root component on every reload - otherwise it doesn't get detected as being modified
            _uiComponents.Remove(Guid.Empty);
            _uiComponents[Guid.Empty] = new UIComponent(_project.RootComponent);

            foreach (var kvp in _project.Components)
            {
                if (!_uiComponents.ContainsKey(kvp.Key))
                {
                    _uiComponents[kvp.Value.UniqueId] = new UIComponent(kvp.Value);
                }
            }

            List<Guid> toRemoveFromUi = new List<Guid>();
            foreach (var key in _uiComponents.Keys)
            {
                if (!_project.Components.ContainsKey(key))
                {
                    toRemoveFromUi.Add(key);
                }
            }

            foreach (Guid toRemove in toRemoveFromUi)
            {
                _uiComponents.Remove(toRemove);
            }

            _uiComponents[Guid.Empty].IsOpen = true;

            _cachedLinePairs.Clear();
            CalculateConnectingLinePairsRecursive(_uiComponents[_project.RootComponent.UniqueId]);
        }

        private void UpdateUIComponentVisibility(string filterSet)
        {
            foreach (UIComponent component in _uiComponents.Values)
            {
                component.IsFilteredOut = false;
            }

            if (string.IsNullOrWhiteSpace(filterSet))
            {
                return;
            }

            string[] rawFilters = filterSet.Split(',');
            List<string> filters = new List<string>();
            foreach (string rawFilter in rawFilters)
            {
                string filter = rawFilter.Trim();
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    filters.Add(filter);
                }
            }

            if (filters.Count > 0)
            {
                // Apply filters to components. Assume everything is hidden first, and then update visibility on components that match criteria
                foreach (UIComponent component in _uiComponents.Values)
                {
                    component.IsFilteredOut = true;
                }

                foreach (UIComponent component in _uiComponents.Values)
                {
                    if (component.BaseComponent.AssemblyInfo == null)
                    {
                        component.IsFilteredOut = false;
                        continue;
                    }

                    foreach (string filter in filters)
                    {
                        string filterToLower = filter.ToLowerInvariant();
                        if (component.BaseComponent.AssemblyInfo.AssemblyBinaryName != null &&
                            component.BaseComponent.AssemblyInfo.AssemblyBinaryName.ToLowerInvariant().Contains(filterToLower))
                        {
                            component.IsFilteredOut = false;
                            break;
                        }

                        foreach (NugetPackageIdentity nugetPackage in component.BaseComponent.AssemblyInfo.NugetSourcePackages)
                        {
                            if (!string.IsNullOrEmpty(nugetPackage.PackageName) &&
                                nugetPackage.PackageName.ToLowerInvariant().Contains(filterToLower))
                            {
                                component.IsFilteredOut = false;
                                break;
                            }
                        }

                        if (!component.IsFilteredOut)
                        {
                            break;
                        }
                    }
                }
            }

            _uiComponents[Guid.Empty].IsFilteredOut = false;

            // Propagate filters to parents
            foreach (UIComponent component in _uiComponents.Values)
            {
                if (!component.IsFilteredOut)
                {
                    UIComponent parent = component;
                    while (parent.BaseComponent.UniqueId != Guid.Empty &&
                        !parent.IsOpen)
                    {
                        parent.IsFilteredOut = false;
                        parent = _uiComponents[parent.BaseComponent.Parent];
                    }
                }
            }
        }

        private void UpdateUiComponentBounds(UIComponent startComponent)
        {
            double aspectRatio = Canvas.ActualWidth / Canvas.ActualHeight;
            double rootComponentLeft = (aspectRatio / 2) - (_viewCenter.X * _zoom);
            double rootComponentRight = (aspectRatio / 2) + (_zoom - (_viewCenter.X * _zoom));
            double rootComponentTop = 0.5 - (_viewCenter.Y * _zoom);
            double rootComponentBottom = 0.5 + (_zoom - (_viewCenter.Y * _zoom));


            lock (_mutex)
            {
                UpdateUiComponentBounds(
                    _uiComponents[_project.RootComponent.UniqueId],
                    rootComponentLeft,
                    rootComponentRight,
                    rootComponentTop,
                    rootComponentBottom);
            }
        }

        private void UpdateUiComponentBounds(UIComponent currentComponent, double left, double right, double top, double bottom)
        {
            currentComponent.AbsoluteBounds = new Box2d(left, top, right, bottom);

            Point canvasTopLeft = VirtualCoordToCanvasCoord(new Point(left, top));
            Point canvasBottomRight = VirtualCoordToCanvasCoord(new Point(right, bottom));
            Point visibleTopLeft = new Point(
                Math.Max(0, Math.Min(Canvas.ActualWidth, canvasTopLeft.X)),
                Math.Max(0, Math.Min(Canvas.ActualHeight, canvasTopLeft.Y)));
            Point visibleBottomRight = new Point(
                Math.Max(0, Math.Min(Canvas.ActualWidth, canvasBottomRight.X)),
                Math.Max(0, Math.Min(Canvas.ActualHeight, canvasBottomRight.Y)));

            // If enough of this component is taking up the screen, auto-expand it
            double visibleArea =
                (visibleBottomRight.X - visibleTopLeft.X) *
                (visibleBottomRight.Y - visibleTopLeft.Y);
            double actualArea =
                (canvasBottomRight.X - canvasTopLeft.X) *
                (canvasBottomRight.Y - canvasTopLeft.Y);
            double totalCanvasSize = 1 + (Canvas.ActualHeight * Canvas.ActualWidth);
            if (visibleArea / totalCanvasSize > 0.3)
            {
                currentComponent.IsOpen = true;
            }

            // If this component is microscopic, auto-close it
            if (actualArea / totalCanvasSize < 0.005)
            {
                currentComponent.IsOpen = false;
            }

            double w = right - left;
            double h = bottom - top;
            foreach (Guid childId in currentComponent.BaseComponent.Children)
            {
                UIComponent child = _uiComponents[childId];
                UpdateUiComponentBounds(child,
                    left + (w * child.BaseComponent.Bounds.FromLeft),
                    right - (w * child.BaseComponent.Bounds.FromRight),
                    top + (h * child.BaseComponent.Bounds.FromTop),
                    bottom - (h * child.BaseComponent.Bounds.FromBottom));
            }
        }

        private void InitializeGlResources()
        {
            GL.Hint(HintTarget.LineSmoothHint, HintMode.Fastest);

            QFontBuilderConfiguration fontBuilderConfig = new QFontBuilderConfiguration(true)
            {
                TextGenerationRenderHint = TextGenerationRenderHint.SizeDependent,
                Characters = CharacterSet.General,
                //ShadowConfig = new QFontShadowConfiguration()
                //{
                //    Type = ShadowType.Expanded,
                //    BlurRadius = 4,
                //    BlurPasses = 8,
                //    Scale = 1.4f
                //},
                //KerningConfig = new QFontKerningConfiguration()
                //{
                //    AlphaEmptyPixelTolerance = 20
                //},
            };

            _font = new QFont(@".\\Resources\\consola.ttf", 10, fontBuilderConfig);
            _drawing = new QFontDrawing();

            _renderOptions = new QFontRenderOptions()
            {
                UseDefaultBlendFunction = true,
                CharacterSpacing = 0.2f,
                Colour = System.Drawing.Color.Black,
                LockToPixel = true,
                //TransformToViewport = new Viewport(0, 0, (float)Canvas.Width, (float)Canvas.Height)
            };

            _textureResizeHandle = GLTexture.Load(new FileInfo(".\\Resources\\resize_handle.png"));
            _textureExpandButton = GLTexture.Load(new FileInfo(".\\Resources\\bring_to_front.png"));
        }

        #region Rendering components and lines

        private void Canvas_OnRender(TimeSpan delta)
        {
            // FIXME need to load this one time after the GL canvas has started
            if (!_initializedResources)
            {
                _initializedResources = true;
                InitializeGlResources();
            }

            if (Canvas.ActualHeight <= 0 ||
                Canvas.ActualWidth <= 0)
            {
                // Nothing to render
                return;
            }

            Monitor.Enter(_mutex);
            try
            {
                GL.ClearColor(0.9f, 0.9f, 0.9f, 1.0f);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                GL.MatrixMode(MatrixMode.Projection);
                GL.LoadIdentity();
                double aspectRatio = Canvas.ActualWidth / Canvas.ActualHeight;
                GL.Ortho(0, aspectRatio, 1, 0, -1, 1);
                GL.MatrixMode(MatrixMode.Modelview);
                GL.LoadIdentity();
                GL.Disable(EnableCap.DepthTest);
                GL.ShadeModel(ShadingModel.Smooth);
                GL.Hint(HintTarget.LineSmoothHint, HintMode.Nicest);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

                // Clear font pixel buffer
                _drawing.ProjectionMatrix = Matrix4.CreateOrthographicOffCenter(0, (float)Canvas.ActualWidth, 0, (float)Canvas.ActualHeight, -1, 1);
                _drawing.DrawingPrimitives.Clear();

                UpdateUiComponentBounds(_uiComponents[_project.RootComponent.UniqueId]);

                // Draw all connecting lines
                foreach (var pair in _cachedLinePairs)
                {
                    UIComponent originComponent = _uiComponents[pair.Key.A];
                    UIComponent destinationComponent = _uiComponents[pair.Key.B];

                    DrawConnectingLine(
                        originComponent,
                        destinationComponent,
                        pair.Value,
                        pair.Key.A == _selectedComponentId || pair.Key.B == _selectedComponentId);
                }

                // Recurse through all containers and draw them
                ComponentColorBy colorBy;
                if (RadioButton_ColorByLibraryType.IsChecked.GetValueOrDefault(true))
                {
                    colorBy = ComponentColorBy.LibraryType;
                }
                else
                {
                    colorBy = ComponentColorBy.FrameworkVersion;
                }

                DrawComponentRecursive(_uiComponents[_project.RootComponent.UniqueId], colorBy);

                // Draw font pixel buffer over the top of everything
                GL.Enable(EnableCap.Texture2D);
                GL.Enable(EnableCap.Blend);
                _drawing.RefreshBuffers();
                _drawing.Draw();
                GL.UseProgram(0);
                GL.Disable(EnableCap.Texture2D);
                GL.Disable(EnableCap.Blend);

                // Draw people
                GL.Enable(EnableCap.Texture2D);
                GL.Enable(EnableCap.Blend);
                GL.UseProgram(0);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.Disable(EnableCap.Texture2D);
                GL.Disable(EnableCap.Blend);

                // Draw debugging data
                //GL.Enable(EnableCap.Texture2D);
                //GL.Enable(EnableCap.Blend);
                //GL.UseProgram(0);
                //GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                //GL.BindTexture(TextureTarget.Texture2D, _textureTest.Handle);
                //GL.Begin(PrimitiveType.Quads);
                //{
                //    GL.Color4(1.0f, 1.0f, 1.0f, 1.0f);
                //    GL.TexCoord2(0, 0); GL.Vertex2(0, 0);
                //    GL.TexCoord2(1, 0); GL.Vertex2(0.5, 0);
                //    GL.TexCoord2(1, 1); GL.Vertex2(0.5, 0.5);
                //    GL.TexCoord2(0, 1); GL.Vertex2(0, 0.5);
                //}
                //GL.End();
                //GL.BindTexture(TextureTarget.Texture2D, 0);

                //GL.Disable(EnableCap.Texture2D);
                //GL.Disable(EnableCap.Blend);

                GL.Finish();
            }
            finally
            {
                Monitor.Exit(_mutex);
            }
        }

        private Point CalculateLineGeometry(Point origin, Point destination, Box2d originBounds, out Orientation orientation)
        {
            double centerDX = destination.X - origin.X;
            double centerDY = destination.Y - origin.Y;
            if (centerDX > 0)
            {
                if (centerDY > 0)
                {
                    // Check right and bottom edges
                    double rightY = origin.Y +
                        ((originBounds.Max.X - origin.X) *
                        (destination.Y - origin.Y) /
                        (destination.X - origin.X));
                    double bottomX = origin.X +
                        ((originBounds.Max.Y - origin.Y) *
                        (destination.X - origin.X) /
                        (destination.Y - origin.Y));

                    if (rightY < originBounds.Max.Y)
                    {
                        orientation = Orientation.Right;
                        return new Point(originBounds.Max.X, rightY);
                    }
                    else
                    {
                        orientation = Orientation.Down;
                        return new Point(bottomX, originBounds.Max.Y);
                    }
                }
                else
                {
                    // Check right and top edges
                    double rightY = origin.Y +
                        ((originBounds.Max.X - origin.X) *
                        (destination.Y - origin.Y) /
                        (destination.X - origin.X));
                    double topX = origin.X +
                        ((origin.Y - originBounds.Min.Y) *
                        (destination.X - origin.X) /
                        (origin.Y - destination.Y));

                    if (rightY > originBounds.Min.Y)
                    {
                        orientation = Orientation.Right;
                        return new Point(originBounds.Max.X, rightY);
                    }
                    else
                    {
                        orientation = Orientation.Up;
                        return new Point(topX, originBounds.Min.Y);
                    }
                }
            }
            else
            {
                if (centerDY > 0)
                {
                    // Check left and bottom edges
                    double leftY = origin.Y +
                        ((origin.X - originBounds.Min.X) *
                        (destination.Y - origin.Y) /
                        (origin.X - destination.X));
                    double bottomX = origin.X +
                        ((originBounds.Max.Y - origin.Y) *
                        (destination.X - origin.X) /
                        (destination.Y - origin.Y));

                    if (leftY < originBounds.Max.Y)
                    {
                        orientation = Orientation.Left;
                        return new Point(originBounds.Min.X, leftY);
                    }
                    else
                    {
                        orientation = Orientation.Down;
                        return new Point(bottomX, originBounds.Max.Y);
                    }
                }
                else
                {
                    // Check left and top edges
                    double leftY = origin.Y +
                        ((origin.X - originBounds.Min.X) *
                        (destination.Y - origin.Y) /
                        (origin.X - destination.X));
                    double topX = origin.X +
                        ((origin.Y - originBounds.Min.Y) *
                        (destination.X - origin.X) /
                        (origin.Y - destination.Y));

                    if (leftY > originBounds.Min.Y)
                    {
                        orientation = Orientation.Left;
                        return new Point(originBounds.Min.X, leftY);
                    }
                    else
                    {
                        orientation = Orientation.Down;
                        return new Point(topX, originBounds.Min.Y);
                    }
                }
            }
        }

        private void CalculateConnectingLinePairsRecursive(UIComponent currentComponent, Stack<UIComponent> inheritanceChain = null)
        {
            inheritanceChain = inheritanceChain ?? new Stack<UIComponent>();
            foreach (Guid otherComponentId in currentComponent.BaseComponent.LinksTo)
            {
                // Find the highest opened parent for both endpoints
                UIComponent componentIter = currentComponent;
                while (true)
                {
                    inheritanceChain.Push(componentIter);
                    if (componentIter.BaseComponent.UniqueId == Guid.Empty)
                    {
                        break;
                    }

                    componentIter = _uiComponents[componentIter.BaseComponent.Parent];
                }

                UIComponent trueOriginComponent = null;
                while (true)
                {
                    UIComponent nextCandidate = inheritanceChain.Pop();
                    trueOriginComponent = nextCandidate;
                    if (nextCandidate.BaseComponent.UniqueId == currentComponent.BaseComponent.UniqueId ||
                        !nextCandidate.IsOpen)
                    {
                        break;
                    }
                }
                inheritanceChain.Clear();

                componentIter = _uiComponents[otherComponentId];
                while (true)
                {
                    inheritanceChain.Push(componentIter);
                    if (componentIter.BaseComponent.UniqueId == Guid.Empty)
                    {
                        break;
                    }

                    componentIter = _uiComponents[componentIter.BaseComponent.Parent];
                }

                UIComponent trueDestinationComponent = null;
                while (true)
                {
                    UIComponent nextCandidate = inheritanceChain.Pop();
                    trueDestinationComponent = nextCandidate;
                    if (nextCandidate.BaseComponent.UniqueId == otherComponentId ||
                        !nextCandidate.IsOpen)
                    {
                        break;
                    }
                }
                inheritanceChain.Clear();

                if (trueOriginComponent.BaseComponent.UniqueId != trueDestinationComponent.BaseComponent.UniqueId &&
                    trueOriginComponent.BaseComponent.UniqueId != Guid.Empty &&
                    trueDestinationComponent.BaseComponent.UniqueId != Guid.Empty)
                {
                    GuidPair pair = new GuidPair(trueOriginComponent.BaseComponent.UniqueId, trueDestinationComponent.BaseComponent.UniqueId);
                    _cachedLinePairs.Increment(pair);
                }
            }

            foreach (Guid childId in currentComponent.BaseComponent.Children)
            {
                CalculateConnectingLinePairsRecursive(_uiComponents[childId], inheritanceChain);
            }
        }

        private void DrawConnectingLine(UIComponent currentComponent, UIComponent otherComponent, float numLinks, bool isSelected)
        {
            // Calculate the begin and endpoints of the line
            Point originCenter = new Point(
                (currentComponent.AbsoluteBounds.Min.X + currentComponent.AbsoluteBounds.Max.X) / 2,
                (currentComponent.AbsoluteBounds.Min.Y + currentComponent.AbsoluteBounds.Max.Y) / 2);
            Point destinationCenter = new Point(
                (otherComponent.AbsoluteBounds.Min.X + otherComponent.AbsoluteBounds.Max.X) / 2,
                (otherComponent.AbsoluteBounds.Min.Y + otherComponent.AbsoluteBounds.Max.Y) / 2);

            Orientation originOrientation;
            Orientation destinationOrientation;
            Point lineOrigin = CalculateLineGeometry(originCenter, destinationCenter, currentComponent.AbsoluteBounds, out originOrientation);
            Point lineDestination = CalculateLineGeometry(destinationCenter, originCenter, otherComponent.AbsoluteBounds, out destinationOrientation);
            float lineAlpha = currentComponent.IsFilteredOut || otherComponent.IsFilteredOut ? 0.15f : 0.5f;

            // Calculate how many "hits" the same link is shared with, and use that to augment the line width
            GL.LineWidth((float)Math.Log10(numLinks) * 3);

            // Create a bezier curve to plot for the connection
            // Determine the orientation of the curve; left, right, up, down
            BezierCurveCubic curve;
            double dX = lineDestination.X - lineOrigin.X;
            double dY = lineDestination.Y - lineOrigin.Y;
            double anchorScale = 0.5;

            Vector2 originAnchor;
            if (originOrientation == Orientation.Up || originOrientation == Orientation.Down)
                originAnchor = new Vector2((float)lineOrigin.X, (float)lineOrigin.Y + (float)(dY * anchorScale));
            else
                originAnchor = new Vector2((float)lineOrigin.X + (float)(dX * anchorScale), (float)lineOrigin.Y);

            Vector2 destinationAnchor;
            if (destinationOrientation == Orientation.Up || destinationOrientation == Orientation.Down)
                destinationAnchor = new Vector2((float)lineDestination.X, (float)lineDestination.Y - (float)(dY * anchorScale));
            else
                destinationAnchor = new Vector2((float)lineDestination.X - (float)(dX * anchorScale), (float)lineDestination.Y);

            curve = new BezierCurveCubic(
                new Vector2((float)lineOrigin.X, (float)lineOrigin.Y),
                new Vector2((float)lineDestination.X, (float)lineDestination.Y),
                originAnchor,
                destinationAnchor);

            if (isSelected)
            {
                GL.Enable(EnableCap.Blend);
                GL.Begin(PrimitiveType.LineStrip);
                {
                    for (int c = 0; c < 100; c++)
                    {
                        // Shift from blue to red as it goes from origin to destination component
                        float grad = (float)c / 100f;
                        GL.Color4(grad, 0.0f, 1.0f - grad, 1.0f);
                        GL.Vertex2(curve.CalculatePoint(grad));
                    }

                    GL.Vertex2(curve.CalculatePoint(1.0f));
                }
                GL.End();
                GL.Disable(EnableCap.Blend);
            }
            else
            {
                GL.Color4(0.0f, 0.0f, 0.0f, lineAlpha);
                GL.Enable(EnableCap.Blend);
                GL.Begin(PrimitiveType.LineStrip);
                {
                    for (int c = 0; c < 100; c++)
                    {
                        GL.Vertex2(curve.CalculatePoint((float)c / 100f));
                    }

                    GL.Vertex2(curve.CalculatePoint(1.0f));
                }
                GL.End();
                GL.Disable(EnableCap.Blend);
            }
        }

        private static void ColorComponentByLibraryType(UIComponent currentComponent, float alpha, out Color4 componentBottomColor, out Color4 componentTopColor)
        {
            // determine color based on assembly type
            switch (currentComponent.BaseComponent.ComponentType)
            {
                case AssemblyComponentType.Managed_Local:
                    if (!currentComponent.BaseComponent.HasDependents)
                    {
                        // Root node - really dark green
                        componentTopColor = new Color4(96 / 255f, 213 / 255f, 68 / 255f, alpha);
                        componentBottomColor = new Color4(139 / 255f, 237 / 255f, 131 / 255f, alpha);
                    }
                    else
                    {
                        // green
                        componentTopColor = new Color4(178 / 255f, 245 / 255f, 182 / 255f, alpha);
                        componentBottomColor = new Color4(205 / 255f, 236 / 255f, 207 / 255f, alpha);
                    }
                    break;
                case AssemblyComponentType.Managed_Builtin:
                    // green and transparent
                    componentTopColor = new Color4(178 / 255f, 245 / 255f, 182 / 255f, alpha * 0.5f);
                    componentBottomColor = new Color4(205 / 255f, 236 / 255f, 207 / 255f, alpha * 0.5f);
                    break;
                case AssemblyComponentType.Native_Local:
                    if (!currentComponent.BaseComponent.HasDependents)
                    {
                        // Root node - really dark blue
                        componentTopColor = new Color4(104 / 255f, 121 / 255f, 226 / 255f, alpha);
                        componentBottomColor = new Color4(153 / 255f, 156 / 255f, 246 / 255f, alpha);
                    }
                    else
                    {
                        // blue
                        componentTopColor = new Color4(178 / 255f, 181 / 255f, 235 / 255f, alpha);
                        componentBottomColor = new Color4(207 / 255f, 205 / 255f, 236 / 255f, alpha);
                    }
                    break;
                case AssemblyComponentType.Native_Builtin:
                    // blue and transparent
                    componentTopColor = new Color4(178 / 255f, 181 / 255f, 235 / 255f, alpha * 0.5f);
                    componentBottomColor = new Color4(207 / 255f, 205 / 255f, 236 / 255f, alpha * 0.5f);
                    break;
                default:
                    // light red
                    componentTopColor = new Color4(233 / 255f, 213 / 255f, 205 / 255f, alpha);
                    componentBottomColor = new Color4(238 / 255f, 226 / 255f, 221 / 255f, alpha);
                    break;
            }
        }

        private static Color4 BlendColors(Color4 a, Color4 b, float ratio)
        {
            if (ratio < 0 || ratio > 1.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(ratio));
            }

            float iratio = 1.0f - ratio;
            return new Color4(
                (a.R * iratio) + (b.R * ratio),
                (a.G * iratio) + (b.G * ratio),
                (a.B * iratio) + (b.B * ratio),
                (a.A * iratio) + (b.A * ratio));
        }

        private static void ColorComponentByFrameworkVersion(UIComponent currentComponent, float alpha, out Color4 componentBottomColor, out Color4 componentTopColor)
        {
            DotNetFrameworkType dotNetType = (currentComponent?.BaseComponent?.AssemblyInfo?.StructuredFrameworkVersion?.FrameworkType).GetValueOrDefault(DotNetFrameworkType.Unknown);
            Version frameworkVersion = (currentComponent?.BaseComponent?.AssemblyInfo?.StructuredFrameworkVersion?.FrameworkVersion) ?? new Version(0, 0);
            Color4 baseColor;

            float lightness = 0.0f;
            if (dotNetType == DotNetFrameworkType.NetFramework)
            {
                // blue
                baseColor = new Color4(99 / 255f, 104 / 255f, 235 / 255f, 1.0f);
                if (frameworkVersion <= DotNetFrameworkVersion.VERSION_4_0)
                {
                    lightness = 0.6f;
                }
                else if (frameworkVersion <= DotNetFrameworkVersion.VERSION_4_5)
                {
                    lightness = 0.55f;
                }
                else if (frameworkVersion <= DotNetFrameworkVersion.VERSION_4_5_1)
                {
                    lightness = 0.5f;
                }
                else if (frameworkVersion <= DotNetFrameworkVersion.VERSION_4_5_2)
                {
                    lightness = 0.45f;
                }
                else if (frameworkVersion <= DotNetFrameworkVersion.VERSION_4_6)
                {
                    lightness = 0.35f;
                }
                else if (frameworkVersion <= DotNetFrameworkVersion.VERSION_4_6_1)
                {
                    lightness = 0.3f;
                }
                else if (frameworkVersion <= DotNetFrameworkVersion.VERSION_4_6_2)
                {
                    lightness = 0.25f;
                }
                else if (frameworkVersion <= DotNetFrameworkVersion.VERSION_4_7)
                {
                    lightness = 0.15f;
                }
                else if (frameworkVersion <= DotNetFrameworkVersion.VERSION_4_7_1)
                {
                    lightness = 0.1f;
                }
                else if (frameworkVersion <= DotNetFrameworkVersion.VERSION_4_7_2)
                {
                    lightness = 0.05f;
                }
                else // 4.8
                {
                    lightness = 0.0f;
                }
            }
            else if (dotNetType == DotNetFrameworkType.NetStandard)
            {
                // cyan
                baseColor = new Color4(99 / 255f, 227 / 255f, 204 / 255f, 1.0f);

                if (frameworkVersion <= DotNetFrameworkVersion.VERSION_1_0)
                {
                    lightness = 0.6f;
                }
                else if (frameworkVersion <= DotNetFrameworkVersion.VERSION_1_1)
                {
                    lightness = 0.55f;
                }
                else if (frameworkVersion <= DotNetFrameworkVersion.VERSION_1_2)
                {
                    lightness = 0.5f;
                }
                else if (frameworkVersion <= DotNetFrameworkVersion.VERSION_1_3)
                {
                    lightness = 0.45f;
                }
                else if (frameworkVersion <= DotNetFrameworkVersion.VERSION_1_4)
                {
                    lightness = 0.4f;
                }
                else if (frameworkVersion <= DotNetFrameworkVersion.VERSION_1_5)
                {
                    lightness = 0.35f;
                }
                else if (frameworkVersion <= DotNetFrameworkVersion.VERSION_1_6)
                {
                    lightness = 0.3f;
                }
                else if (frameworkVersion <= DotNetFrameworkVersion.VERSION_2_0)
                {
                    lightness = 0.15f;
                }
                else // 2.1
                {
                    lightness = 0.0f;
                }
            }
            else if (dotNetType == DotNetFrameworkType.NetCore)
            {
                // green
                baseColor = new Color4(114 / 255f, 227 / 255f, 99 / 255f, 1.0f);
                if (frameworkVersion <= DotNetFrameworkVersion.VERSION_3_0)
                {
                    lightness = 0.6f;
                }
                else if (frameworkVersion <= DotNetFrameworkVersion.VERSION_3_1)
                {
                    lightness = 0.5f;
                }
                else if (frameworkVersion <= DotNetFrameworkVersion.VERSION_5_0)
                {
                    lightness = 0.4f;
                }
                else if (frameworkVersion <= DotNetFrameworkVersion.VERSION_6_0)
                {
                    lightness = 0.2f;
                }
                else
                {
                    lightness = 0.0f;
                }
            }
            else
            {
                // Grey I guess
                baseColor = new Color4(230 / 255f, 230 / 255f, 230 / 255f, 1.0f);
            }

            // Calculate final bottom color based on lightness
            componentBottomColor = BlendColors(baseColor, Color4.White, lightness);

            // And make top color a little lighter than that
            componentTopColor = BlendColors(componentBottomColor, Color4.White, 0.2f);

            // Finally, apply overall alpha
            componentBottomColor.A = alpha;
            componentTopColor.A = alpha;
        }

        private void DrawComponentRecursive(UIComponent currentComponent, ComponentColorBy colorBy)
        {
            double left = currentComponent.AbsoluteBounds.Min.X;
            double right = currentComponent.AbsoluteBounds.Max.X;
            double top = currentComponent.AbsoluteBounds.Min.Y;
            double bottom = currentComponent.AbsoluteBounds.Max.Y;

            // How big is it on screen?
            Point topLeftCornerOfComponentOnScreen = VirtualCoordToCanvasCoord(new Point(currentComponent.AbsoluteBounds.Min.X, currentComponent.AbsoluteBounds.Min.Y));
            Point bottomRightCornerOfComponentOnScreen = VirtualCoordToCanvasCoord(new Point(currentComponent.AbsoluteBounds.Max.X, currentComponent.AbsoluteBounds.Max.Y));
            bool isTiny = ((bottomRightCornerOfComponentOnScreen.X - topLeftCornerOfComponentOnScreen.X) < 30 ||
                (bottomRightCornerOfComponentOnScreen.Y - topLeftCornerOfComponentOnScreen.Y) < 20);
            float alpha = currentComponent.IsFilteredOut ? 0.15f : 1.0f;

            // Don't fill in the default quad. This allows connecting lines to be drawn behind actual components
            if (currentComponent.BaseComponent.UniqueId != Guid.Empty)
            {
                // Draw parent quad
                // First, determine the color
                Color4 componentBottomColor;
                Color4 componentTopColor;

                // Is it selected?
                if (_selectedComponentId != Guid.Empty &&
                    currentComponent.BaseComponent.UniqueId == _selectedComponentId)
                {
                    // red
                    componentTopColor = new Color4(226 / 255f, 104 / 255f, 104 / 255f, alpha);
                    componentBottomColor = new Color4(246 / 255f, 153 / 255f, 153 / 255f, alpha);
                }
                else if (currentComponent.IsOpen)
                {
                    if (currentComponent.BaseComponent.Children.Count == 0)
                    {
                        // Opened with no children
                        // light red
                        componentTopColor = new Color4(233 / 255f, 213 / 255f, 205 / 255f, alpha);
                        componentBottomColor = new Color4(238 / 255f, 226 / 255f, 221 / 255f, alpha);
                    }
                    else
                    {
                        // Opened with children
                        // off-white
                        componentTopColor = new Color4(248f / 255f, 252f / 255f, 255f / 255f, alpha);
                        componentBottomColor = new Color4(248f / 255f, 252f / 255f, 255f / 255f, alpha);
                    }
                }
                else
                {
                    if (currentComponent.BaseComponent.Children.Count == 0)
                    {
                        // Closed with no children
                        // Are there errors?
                        if (currentComponent.BaseComponent.Errors != null &&
                            currentComponent.BaseComponent.Errors.Count > 0)
                        {
                            componentTopColor = new Color4(255 / 255f, 66 / 255f, 0 / 255f, alpha);
                            componentBottomColor = new Color4(255 / 255f, 120 / 255f, 0 / 255f, alpha);
                        }
                        else
                        {
                            if (colorBy == ComponentColorBy.LibraryType)
                            {
                                ColorComponentByLibraryType(currentComponent, alpha, out componentBottomColor, out componentTopColor);
                            }
                            else
                            {
                                ColorComponentByFrameworkVersion(currentComponent, alpha, out componentBottomColor, out componentTopColor);
                            }
                        }
                    }
                    else
                    {
                        // Closed with children
                        // medium grey
                        componentTopColor = new Color4(230f / 255f, 230f / 255f, 230f / 255f, alpha);
                        componentBottomColor = new Color4(230f / 255f, 230f / 255f, 230f / 255f, alpha);
                    }
                }

                GL.Begin(PrimitiveType.Quads);
                {
                    GL.Color4(componentBottomColor);
                    GL.Vertex2(left, top);
                    GL.Vertex2(right, top);
                    GL.Color4(componentTopColor);
                    GL.Vertex2(right, bottom);
                    GL.Vertex2(left, bottom);
                }
                GL.End();

                //if (!isTiny && currentComponent.BaseComponent.UniqueId != Guid.Empty && !currentComponent.IsFiltered)
                //{
                //    // Draw buttons
                //    GL.Enable(EnableCap.Texture2D);
                //    GL.Enable(EnableCap.Blend);
                //    GL.UseProgram(0);
                //    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                //    GL.ActiveTexture(TextureUnit.Texture0);

                //    // Resize handle
                //    GL.BindTexture(TextureTarget.Texture2D, _textureResizeHandle.Handle);
                //    double handleLeft = right - 0.02;
                //    double handleTop = bottom - 0.02;
                //    GL.Begin(PrimitiveType.Quads);
                //    {
                //        GL.Color4(1.0f, 1.0f, 1.0f, 1.0f);
                //        GL.TexCoord2(0, 0); GL.Vertex2(handleLeft, handleTop);
                //        GL.TexCoord2(1, 0); GL.Vertex2(right, handleTop);
                //        GL.TexCoord2(1, 1); GL.Vertex2(right, bottom);
                //        GL.TexCoord2(0, 1); GL.Vertex2(handleLeft, bottom);
                //    }
                //    GL.End();

                //    // Expand/collapse button
                //    GL.BindTexture(TextureTarget.Texture2D, _textureExpandButton.Handle);
                //    double buttonLeft = right - 0.02;
                //    double buttonBottom = top + 0.02;
                //    GL.Begin(PrimitiveType.Quads);
                //    {
                //        GL.Color4(1.0f, 1.0f, 1.0f, 1.0f);
                //        GL.TexCoord2(0, 0); GL.Vertex2(buttonLeft, top);
                //        GL.TexCoord2(1, 0); GL.Vertex2(right, top);
                //        GL.TexCoord2(1, 1); GL.Vertex2(right, buttonBottom);
                //        GL.TexCoord2(0, 1); GL.Vertex2(buttonLeft, buttonBottom);
                //    }
                //    GL.End();
                //    GL.BindTexture(TextureTarget.Texture2D, 0);

                //    GL.Disable(EnableCap.Texture2D);
                //    GL.Disable(EnableCap.Blend);
                //}
            }

            // Draw outline

            GL.LineWidth(1.0f);
            GL.Begin(PrimitiveType.LineLoop);
            {
                GL.Color4(0.2f, 0.2f, 0.2f, alpha);
                GL.Vertex2(left, top);
                GL.Vertex2(right, top);
                GL.Vertex2(right, bottom);
                GL.Vertex2(left, bottom);
            }
            GL.End();

            // Draw the title if the box is wide enough
            // TODO use measurements to determine exact font bounds
            if (!isTiny && !currentComponent.IsFilteredOut)
            {
                GL.Enable(EnableCap.Blend);
                _drawing.Print(_font,
                    currentComponent.BaseComponent.Name,
                    new Vector3((float)Math.Round(left * Canvas.ActualHeight) + 3, (float)Math.Round((1 - top) * Canvas.ActualHeight), 0),
                    QFontAlignment.Left, _renderOptions);
                GL.UseProgram(0);
                GL.Disable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            }

            // Draw children
            if (currentComponent.IsOpen)
            {
                foreach (Guid childId in currentComponent.BaseComponent.Children)
                {
                    DrawComponentRecursive(_uiComponents[childId], colorBy);
                }
            }
        }

        #endregion

        #region Canvas helper methods

        private UIComponent RunComponentHitDetection(UIComponent root, Point clickPos)
        {
            // Don't bother checking filtered components
            if (root.IsFilteredOut)
            {
                return null;
            }

            // Is the click within this pane?
            if (clickPos.X >= root.AbsoluteBounds.Min.X &&
                clickPos.X <= root.AbsoluteBounds.Max.X &&
                clickPos.Y >= root.AbsoluteBounds.Min.Y &&
                clickPos.Y <= root.AbsoluteBounds.Max.Y)
            {
                if (root.IsOpen)
                {
                    // Check all children
                    // Iterate in reverse so that we honor the last-on-top layer ordering of the renderer
                    Stack<Guid> reverser = new Stack<Guid>(root.BaseComponent.Children);
                    while (reverser.Count > 0)
                    {
                        Guid childId = reverser.Pop();
                        UIComponent hit = RunComponentHitDetection(_uiComponents[childId], clickPos);
                        if (hit != null)
                        {
                            return hit;
                        }
                    }
                }

                // Didn't hit a child element, but it did hit this element, so return this
                return root;
            }
            else
            {
                return null;
            }
        }

        private Point CanvasCoordToVirtualCoord(Point canvasPoint)
        {
            return new Point(
                canvasPoint.X / Canvas.ActualHeight,
                canvasPoint.Y / Canvas.ActualHeight);
        }

        private Point VirtualCoordToCanvasCoord(Point virtualPoint)
        {
            return new Point(
                virtualPoint.X * Canvas.ActualHeight,
                virtualPoint.Y * Canvas.ActualHeight);
        }

        #endregion

        #region Mouse events and canvas navigation

        private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isHoldingLeftMouse)
            {
                return;
            }

            // Start click and dragging around the canvas
            Point clickPosition = e.GetPosition(Canvas);
            Point virtualPosition = CanvasCoordToVirtualCoord(clickPosition);
            _mouseDragStartCanvasCenterPoint = clickPosition;
            _mouseDragStartVirtualCenterPoint = _viewCenter;
            _isHoldingRightMouse = true;
            Mouse.PrimaryDevice.Capture(Canvas, CaptureMode.Element);
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isHoldingRightMouse)
            {
                return;
            }

            Point clickPosition = e.GetPosition(Canvas);
            Point virtualPosition = CanvasCoordToVirtualCoord(clickPosition);

            // Run hit detection.

            // Did we hit any UI elements?
            UIComponent clickedComponent = RunComponentHitDetection(_uiComponents[_project.RootComponent.UniqueId], virtualPosition);
            if (clickedComponent != null &&
                clickedComponent.BaseComponent.UniqueId != Guid.Empty)
            {
                // We've hit a component.

                // Is the user shift-clicking to create a link?
                if (Keyboard.IsKeyDown(Key.LeftShift) &&
                    _selectedComponentId != Guid.Empty)
                {
                    UIComponent linkOrigin = _uiComponents[_selectedComponentId];
                    TryToggleLink(linkOrigin, clickedComponent);
                }
                else
                {
                    // Is it super tiny on our screen (50px or smaller)?
                    Point topLeftCornerOfComponent = VirtualCoordToCanvasCoord(new Point(clickedComponent.AbsoluteBounds.Min.X, clickedComponent.AbsoluteBounds.Min.Y));
                    Point bottomRightCornerOfComponent = VirtualCoordToCanvasCoord(new Point(clickedComponent.AbsoluteBounds.Max.X, clickedComponent.AbsoluteBounds.Max.Y));
                    Point topRightCornerOfComponent = VirtualCoordToCanvasCoord(new Point(clickedComponent.AbsoluteBounds.Max.X, clickedComponent.AbsoluteBounds.Min.Y));
                    if ((bottomRightCornerOfComponent.X - topLeftCornerOfComponent.X) < 50 ||
                        (bottomRightCornerOfComponent.Y - topLeftCornerOfComponent.Y) < 50)
                    {
                        // Just treat is as selection
                        _selectedComponentId = clickedComponent.BaseComponent.UniqueId;
                        _uiComponentBeingManipulated = clickedComponent;
                        _uiComponentBeingManipulatedOriginalBounds = clickedComponent.BaseComponent.Bounds.Clone();
                        _isMovingComponent = true;
                    }
                    else if (clickPosition.X > (bottomRightCornerOfComponent.X - 10) &&
                            clickPosition.Y > (bottomRightCornerOfComponent.Y - 10))
                    {
                        // We are hitting its resize handle (bottom-right 10px)
                        _selectedComponentId = clickedComponent.BaseComponent.UniqueId;
                        _uiComponentBeingManipulated = clickedComponent;
                        _uiComponentBeingManipulatedOriginalBounds = clickedComponent.BaseComponent.Bounds.Clone();
                        _isResizingComponent = true;
                    }
                    else if (clickPosition.X > (topRightCornerOfComponent.X - 20) &&
                            clickPosition.Y < (topRightCornerOfComponent.Y + 20))
                    {
                        // We are hitting its open/close buttom (top-right 20px)
                        _selectedComponentId = clickedComponent.BaseComponent.UniqueId;
                        if (clickedComponent.IsOpen)
                        {
                            RecursiveCloseComponents(clickedComponent);
                        }
                        else
                        {
                            clickedComponent.IsOpen = true;
                        }

                        // Update line connections
                        _cachedLinePairs.Clear();
                        CalculateConnectingLinePairsRecursive(_uiComponents[_project.RootComponent.UniqueId]);
                    }
                    else
                    {
                        // We clicked somewhere else on the component boundary; move it around (or create a link if we are in linking mode)
                        _selectedComponentId = clickedComponent.BaseComponent.UniqueId;
                        _uiComponentBeingManipulated = clickedComponent;
                        _uiComponentBeingManipulatedOriginalBounds = clickedComponent.BaseComponent.Bounds.Clone();
                        _isMovingComponent = true;
                    }
                }
            }
            else
            {
                // Didn't hit anything. Deselect
                _selectedComponentId = Guid.Empty;
            }

            UpdateUiElements();
            _mouseDragStartCanvasCenterPoint = clickPosition;
            _isHoldingLeftMouse = true;
            Mouse.PrimaryDevice.Capture(Canvas, CaptureMode.Element);
        }

        private void RecursiveCloseComponents(UIComponent component)
        {
            component.IsOpen = false;
            foreach (Guid child in component.BaseComponent.Children)
            {
                RecursiveCloseComponents(_uiComponents[child]);
            }
        }

        private void TryToggleLink(UIComponent originComponent, UIComponent destinationComponent)
        {
            // Check that they're not reflexive
            if (originComponent.BaseComponent.UniqueId !=
                destinationComponent.BaseComponent.UniqueId)
            {
                // Check if they are linked already
                if (originComponent.BaseComponent.LinksTo.Contains(destinationComponent.BaseComponent.UniqueId))
                {
                    // Break the link
                    destinationComponent.BaseComponent.UnlinkFrom(originComponent.BaseComponent);
                }
                else
                {
                    // Create the link
                    // First, ensure that we are not linking a parent to a child
                    bool parentChildLink = false;
                    Component check = destinationComponent.BaseComponent;
                    while (check.UniqueId != Guid.Empty)
                    {
                        if (check.Parent == originComponent.BaseComponent.UniqueId)
                        {
                            parentChildLink = true;
                            break;
                        }

                        check = _project.Components[check.Parent];
                    }

                    check = originComponent.BaseComponent;
                    while (check.UniqueId != Guid.Empty)
                    {
                        if (check.Parent == destinationComponent.BaseComponent.UniqueId)
                        {
                            parentChildLink = true;
                            break;
                        }

                        check = _project.Components[check.Parent];
                    }

                    if (!parentChildLink)
                    {
                        destinationComponent.BaseComponent.LinkTo(originComponent.BaseComponent);
                    }
                }
            }
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isHoldingLeftMouse = false;
            _isMovingComponent = false;
            _isResizingComponent = false;
            Mouse.PrimaryDevice.Capture(Canvas, CaptureMode.None);
        }

        private void Canvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isHoldingRightMouse = false;
            Mouse.PrimaryDevice.Capture(Canvas, CaptureMode.None);
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            Point clickPosition = e.GetPosition(Canvas);
            Point virtualPosition = CanvasCoordToVirtualCoord(clickPosition);

            if (_isHoldingRightMouse)
            {
                // User is dragging around with a right-click
                double deltaX = (clickPosition.X - _mouseDragStartCanvasCenterPoint.X) / Canvas.ActualHeight;
                double deltaY = (clickPosition.Y - _mouseDragStartCanvasCenterPoint.Y) / Canvas.ActualHeight;
                _viewCenter = new Point(
                    _mouseDragStartVirtualCenterPoint.X - (deltaX / _zoom),
                    _mouseDragStartVirtualCenterPoint.Y - (deltaY / _zoom));
            }
            else if (_isHoldingLeftMouse)
            {
                if (_isMovingComponent)
                {
                    // User is dragging a single component around
                    UIComponent parentComponent = _uiComponents[_uiComponentBeingManipulated.BaseComponent.Parent];
                    double deltaX = (clickPosition.X - _mouseDragStartCanvasCenterPoint.X) / Canvas.ActualHeight / parentComponent.AbsoluteBounds.Size.X;
                    double deltaY = (clickPosition.Y - _mouseDragStartCanvasCenterPoint.Y) / Canvas.ActualHeight / parentComponent.AbsoluteBounds.Size.Y;
                    _uiComponentBeingManipulated.BaseComponent.Bounds.FromLeft = Math.Max(0, Math.Min(1 - _uiComponentBeingManipulatedOriginalBounds.Width, _uiComponentBeingManipulatedOriginalBounds.FromLeft + deltaX));
                    _uiComponentBeingManipulated.BaseComponent.Bounds.FromRight = Math.Max(0, Math.Min(1 - _uiComponentBeingManipulatedOriginalBounds.Width, _uiComponentBeingManipulatedOriginalBounds.FromRight - deltaX));
                    _uiComponentBeingManipulated.BaseComponent.Bounds.FromTop = Math.Max(0, Math.Min(1 - _uiComponentBeingManipulatedOriginalBounds.Height, _uiComponentBeingManipulatedOriginalBounds.FromTop + deltaY));
                    _uiComponentBeingManipulated.BaseComponent.Bounds.FromBottom = Math.Max(0, Math.Min(1 - _uiComponentBeingManipulatedOriginalBounds.Height, _uiComponentBeingManipulatedOriginalBounds.FromBottom - deltaY));
                    UpdateUiComponentBounds(_uiComponentBeingManipulated);
                }
                else if (_isResizingComponent)
                {
                    // User is resizing a single component
                    UIComponent parentComponent = _uiComponents[_uiComponentBeingManipulated.BaseComponent.Parent];
                    double deltaX = (clickPosition.X - _mouseDragStartCanvasCenterPoint.X) / Canvas.ActualHeight / parentComponent.AbsoluteBounds.Size.X;
                    double deltaY = (clickPosition.Y - _mouseDragStartCanvasCenterPoint.Y) / Canvas.ActualHeight / parentComponent.AbsoluteBounds.Size.Y;
                    _uiComponentBeingManipulated.BaseComponent.Bounds.FromRight = Math.Max(0, Math.Min(0.99 - _uiComponentBeingManipulatedOriginalBounds.FromLeft, _uiComponentBeingManipulatedOriginalBounds.FromRight - deltaX));
                    _uiComponentBeingManipulated.BaseComponent.Bounds.FromBottom = Math.Max(0, Math.Min(0.99 - _uiComponentBeingManipulatedOriginalBounds.FromTop, _uiComponentBeingManipulatedOriginalBounds.FromBottom - deltaY));
                    UpdateUiComponentBounds(_uiComponentBeingManipulated);
                }
                //else if (_isCreatingLink)
                //{
                //    // User is dragging a link from one component to another
                //    _currentLinkDestination = virtualPosition;

                //    // Is the cursor outside of the bounds? Then scroll the view
                //    double scrollFactor = 0.3;
                //    if (clickPosition.X < 0)
                //    {
                //        double deltaX = (clickPosition.X / Canvas.ActualHeight) * scrollFactor;
                //        _currentLinkOrigin = new Point(
                //            _currentLinkOrigin.X - deltaX,
                //            _currentLinkOrigin.Y);
                //        _viewCenter = new Point(
                //            _viewCenter.X + (deltaX / _zoom),
                //            _viewCenter.Y);
                //    }
                //    if (clickPosition.X > Canvas.ActualWidth)
                //    {
                //        double deltaX = ((clickPosition.X - Canvas.ActualWidth) / Canvas.ActualHeight) * scrollFactor;
                //        _currentLinkOrigin = new Point(
                //            _currentLinkOrigin.X - deltaX,
                //            _currentLinkOrigin.Y);
                //        _viewCenter = new Point(
                //            _viewCenter.X + (deltaX / _zoom),
                //            _viewCenter.Y);
                //    }
                //    if (clickPosition.Y < 0)
                //    {
                //        double deltaY = (clickPosition.Y / Canvas.ActualHeight) * scrollFactor;
                //        _currentLinkOrigin = new Point(
                //            _currentLinkOrigin.X,
                //            _currentLinkOrigin.Y - deltaY);
                //        _viewCenter = new Point(
                //            _viewCenter.X,
                //            _viewCenter.Y + (deltaY / _zoom));
                //    }
                //    if (clickPosition.Y > Canvas.ActualHeight)
                //    {
                //        double deltaY = ((clickPosition.Y - Canvas.ActualHeight) / Canvas.ActualHeight) * scrollFactor;
                //        _currentLinkOrigin = new Point(
                //            _currentLinkOrigin.X,
                //            _currentLinkOrigin.Y - deltaY);
                //        _viewCenter = new Point(
                //            _viewCenter.X,
                //            _viewCenter.Y + (deltaY / _zoom));
                //    }
                //}
            }
        }

        private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            // positive delta == scrolling up
            _zoom = _zoom * (1 + ((double)e.Delta / 800));
            // todo: alter center position so we can do directional zooming
        }

        #endregion

        #region UI events and component model editing

        private async void LoadFromFileButton_Click(object sender, RoutedEventArgs args)
        {
            LoadFromFileButton.IsEnabled = false;
            try
            {
                ModalDialogPromptFilePath modal = new ModalDialogPromptFilePath();
                modal.ShowDialog();
                if (modal.Submitted)
                {
                    string path = modal.Path;
                    if (string.IsNullOrEmpty(path))
                    {
                        StatusLabel.Content = "You must specify a path";
                        return;
                    }

                    path = path.Trim('\"', '\'');
                    Project newProject = null;
                    if (Directory.Exists(path))
                    {
                        StatusLabel.Content = "Inspecting directory " + path + ", this can take a few minutes!";
                        using (AssemblyInspector inspector = new AssemblyInspector(_logger))
                        {
                            DirectoryInfo inputDir = new DirectoryInfo(path);
                            newProject = await Task.Run(() =>
                            {
                                try
                                {
                                    ISet<DependencyGraphNode> graph = inspector.BuildDependencyGraph(inputDir, _packageCache);
                                    return DependencyGraphConverter.ConvertDependencyGraphToProject(graph, _logger);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine(ex.Message);
                                    return null;
                                }
                            });
                        }
                    }
                    else if (File.Exists(path))
                    {
                        StatusLabel.Content = "Inspecting file " + path + ", please wait!";
                        using (AssemblyInspector inspector = new AssemblyInspector(_logger))
                        {
                            FileInfo inputFile = new FileInfo(path);
                            newProject = await Task.Run(() =>
                            {
                                try
                                {
                                    ISet<DependencyGraphNode> graph = inspector.BuildDependencyGraph(inputFile, _packageCache);
                                    return DependencyGraphConverter.ConvertDependencyGraphToProject(graph, _logger);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine(ex.Message);
                                    return null;
                                }
                            });
                        }
                    }
                    else
                    {
                        StatusLabel.Content = "File / directory not found: " + path;
                        return;
                    }

                    if (newProject == null || newProject.Components.Count <= 1)
                    {
                        StatusLabel.Content = "No assemblies found at " + path;
                        return;
                    }

                    StatusLabel.Content = "Succesfully loaded " + path;
                    Monitor.Enter(_mutex);
                    try
                    {
                        _project = newProject;
                        RebuildUiComponents();
                    }
                    finally
                    {
                        Monitor.Exit(_mutex);
                    }
                }
            }
            finally
            {
                LoadFromFileButton.IsEnabled = true;
            }
        }

        private void UpdateUiElements()
        {
            Monitor.Enter(_mutex);
            try
            {
                Selected_NameTextBox.Text = string.Empty;
                Selected_VersionTextBox.Text = string.Empty;
                Selected_FullNameTextBox.Text = string.Empty;
                Selected_TypeTextBox.Text = string.Empty;
                Selected_PlatformTextBox.Text = string.Empty;
                Selected_FrameworkTextBox.Text = string.Empty;
                Selected_FilePathTextBox.Text = string.Empty;
                Selected_DependenciesTextArea.Document = new System.Windows.Documents.FlowDocument();
                Selected_NugetSourcesTextArea.Document = new System.Windows.Documents.FlowDocument();

                if (_selectedComponentId != Guid.Empty)
                {
                    Component selectedComponent = _project.Components[_selectedComponentId];
                    if (selectedComponent != null &&
                        selectedComponent.AssemblyInfo != null)
                    {
                        Selected_NameTextBox.Text = selectedComponent.AssemblyInfo.AssemblyBinaryName ?? string.Empty;
                        Selected_VersionTextBox.Text = selectedComponent.AssemblyInfo.AssemblyVersion?.ToString() ?? string.Empty;
                        Selected_FullNameTextBox.Text = selectedComponent.AssemblyInfo.AssemblyFullName ?? string.Empty;
                        Selected_TypeTextBox.Text = Enum.GetName(typeof(AssemblyComponentType), selectedComponent.ComponentType);
                        Selected_PlatformTextBox.Text = Enum.GetName(typeof(BinaryPlatform), selectedComponent.AssemblyInfo.Platform);
                        Selected_FrameworkTextBox.Text = selectedComponent.AssemblyInfo.AssemblyFramework ?? string.Empty;
                        Selected_FilePathTextBox.Text = selectedComponent.AssemblyInfo.AssemblyFilePath?.FullName ?? string.Empty;

                        // Enumerate assembly references
                        System.Windows.Documents.FlowDocument dependenciesDocument = new System.Windows.Documents.FlowDocument();
                        System.Windows.Documents.FlowDocument dependentsDocument = new System.Windows.Documents.FlowDocument();
                        System.Windows.Documents.FlowDocument errorsDocument = new System.Windows.Documents.FlowDocument();
                        System.Windows.Documents.Paragraph para = new System.Windows.Documents.Paragraph();

                        bool first = true;
                        foreach (AssemblyReferenceName dependency in selectedComponent.AssemblyInfo.ReferencedAssemblies.OrderBy((r) => r.AssemblyBinaryName))
                        {
                            if (!first)
                            {
                                para.Inlines.Add(new System.Windows.Documents.LineBreak());
                            }

                            first = false;
                            if (!string.IsNullOrEmpty(dependency.AssemblyFullName))
                            {
                                para.Inlines.Add(new System.Windows.Documents.Run(dependency.AssemblyFullName));
                            }
                            else
                            {
                                if (dependency.ReferencedAssemblyVersion != null)
                                {
                                    para.Inlines.Add(new System.Windows.Documents.Run(dependency.AssemblyBinaryName + " v" + dependency.ReferencedAssemblyVersion.ToString()));
                                }
                                else
                                {
                                    para.Inlines.Add(new System.Windows.Documents.Run(dependency.AssemblyBinaryName));
                                }
                            }

                            if (dependency.ReferencedAssemblyVersionAfterBindingOverride != null &&
                                dependency.ReferencedAssemblyVersion != dependency.ReferencedAssemblyVersionAfterBindingOverride)
                            {
                                para.Inlines.Add(new System.Windows.Documents.LineBreak());
                                para.Inlines.Add(new System.Windows.Documents.Run("\tVERSION OVERRIDE: " + dependency.ReferencedAssemblyVersionAfterBindingOverride.ToString())
                                {
                                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 0, 0))
                                });
                            }
                            if (!string.IsNullOrEmpty(dependency.BindingRedirectCodeBasePath))
                            {
                                para.Inlines.Add(new System.Windows.Documents.LineBreak());
                                para.Inlines.Add(new System.Windows.Documents.Run("\tCODEBASE OVERRIDE: " + dependency.BindingRedirectCodeBasePath)
                                {
                                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 0, 0))
                                });
                            }
                        }

                        dependenciesDocument.Blocks.Add(para);
                        Selected_DependenciesTextArea.Document = dependenciesDocument;

                        // Enumerate errors
                        first = true;
                        para = new System.Windows.Documents.Paragraph();

                        if (selectedComponent.Errors != null)
                        {
                            foreach (var error in selectedComponent.Errors)
                            {
                                if (!first)
                                {
                                    para.Inlines.Add(new System.Windows.Documents.LineBreak());
                                }

                                first = false;
                                para.Inlines.Add(new System.Windows.Documents.Run(error));
                            }
                        }

                        errorsDocument.Blocks.Add(para);
                        Selected_ErrorsTextArea.Document = errorsDocument;

                        // Enumerate dependents
                        first = true;
                        para = new System.Windows.Documents.Paragraph();
                        HashSet<Version> dependentVersions = new HashSet<Version>();
                        foreach (var sourceComponent in _project.Components.Values)
                        {
                            if (sourceComponent == null ||
                                sourceComponent.AssemblyInfo == null ||
                                sourceComponent.AssemblyInfo.ReferencedAssemblies == null)
                            {
                                continue;
                            }

                            foreach (var sourceReference in sourceComponent.AssemblyInfo.ReferencedAssemblies)
                            {
                                if (string.Equals(sourceReference.AssemblyBinaryName, selectedComponent.AssemblyInfo.AssemblyBinaryName))
                                {
                                    if (!first)
                                    {
                                        para.Inlines.Add(new System.Windows.Documents.LineBreak());
                                    }

                                    first = false;
                                    if (sourceReference.ReferencedAssemblyVersion != null)
                                    {
                                        if (sourceComponent.AssemblyInfo.AssemblyVersion != null)
                                        {
                                            para.Inlines.Add(new System.Windows.Documents.Run(sourceComponent.AssemblyInfo.AssemblyBinaryName + " v" + sourceComponent.AssemblyInfo.AssemblyVersion.ToString() + " -> v" + sourceReference.ReferencedAssemblyVersion.ToString()));
                                        }
                                        else
                                        {
                                            para.Inlines.Add(new System.Windows.Documents.Run(sourceComponent.AssemblyInfo.AssemblyBinaryName + " -> v" + sourceReference.ReferencedAssemblyVersion.ToString()));
                                        }

                                        if (!dependentVersions.Contains(sourceReference.ReferencedAssemblyVersion))
                                        {
                                            dependentVersions.Add(sourceReference.ReferencedAssemblyVersion);
                                        }
                                    }
                                    else
                                    {
                                        para.Inlines.Add(new System.Windows.Documents.Run(sourceComponent.AssemblyInfo.AssemblyBinaryName));
                                    }

                                    if (sourceReference.ReferencedAssemblyVersionAfterBindingOverride != null &&
                                        sourceReference.ReferencedAssemblyVersion != sourceReference.ReferencedAssemblyVersionAfterBindingOverride)
                                    {
                                        para.Inlines.Add(new System.Windows.Documents.LineBreak());
                                        para.Inlines.Add(new System.Windows.Documents.Run("\tVERSION OVERRIDE: " + sourceReference.ReferencedAssemblyVersionAfterBindingOverride.ToString())
                                        {
                                            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 0, 0))
                                        });
                                    }
                                    if (!string.IsNullOrEmpty(sourceReference.BindingRedirectCodeBasePath))
                                    {
                                        para.Inlines.Add(new System.Windows.Documents.LineBreak());
                                        para.Inlines.Add(new System.Windows.Documents.Run("\tCODEBASE OVERRIDE: " + sourceReference.BindingRedirectCodeBasePath)
                                        {
                                            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 0, 0))
                                        });
                                    }

                                    break;
                                }
                            }
                        }

                        if (dependentVersions.Count == 0 &&
                            selectedComponent.AssemblyInfo != null &&
                            selectedComponent.AssemblyInfo.AssemblyVersion != null)
                        {
                            dependentVersions.Add(selectedComponent.AssemblyInfo.AssemblyVersion);
                        }

                        if (dependentVersions.Count != 0)
                        {
                            if (!first)
                            {
                                para.Inlines.Add(new System.Windows.Documents.LineBreak());
                                para.Inlines.Add(new System.Windows.Documents.LineBreak());
                            }

                            para.Inlines.Add(new System.Windows.Documents.Run("VERSIONS IN USE:"));
                            foreach (Version dependentVersion in dependentVersions.OrderByDescending((x) => x))
                            {
                                para.Inlines.Add(new System.Windows.Documents.LineBreak());
                                para.Inlines.Add(new System.Windows.Documents.Run(dependentVersion.ToString()));
                            }
                        }

                        dependentsDocument.Blocks.Add(para);
                        Selected_DependentsTextArea.Document = dependentsDocument;

                        // Enumerate nuget references
                        System.Windows.Documents.FlowDocument nugetRefsDocument = new System.Windows.Documents.FlowDocument();
                        para = new System.Windows.Documents.Paragraph();

                        first = true;
                        foreach (NugetPackageIdentity nugetPackage in selectedComponent.AssemblyInfo.NugetSourcePackages)
                        {
                            if (!first)
                            {
                                para.Inlines.Add(new System.Windows.Documents.LineBreak());
                            }

                            first = false;
                            para.Inlines.Add(new System.Windows.Documents.Run(nugetPackage.PackageName + " v" + nugetPackage.PackageVersion));
                        }

                        nugetRefsDocument.Blocks.Add(para);
                        Selected_NugetSourcesTextArea.Document = nugetRefsDocument;
                    }
                }
            }
            finally
            {
                Monitor.Exit(_mutex);
            }
        }

        #endregion

        private void FilterTextBox_KeyUp(object sender, KeyEventArgs e)
        {
            lock (_mutex)
            {
                UpdateUIComponentVisibility(FilterTextBox.Text);
            }
        }
    }
}
