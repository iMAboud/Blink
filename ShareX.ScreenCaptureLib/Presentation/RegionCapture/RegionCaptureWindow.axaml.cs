#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.
*/

#endregion License Information (GPL v3)

#nullable enable

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using ShareX.AvaloniaUI.Imaging;
using ShareX.AvaloniaUI.Input;
using ShareX.AvaloniaUI.Theming;
using ShareX.HelpersLib;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCanvas = Avalonia.Controls.Canvas;
using DrawingColor = System.Drawing.Color;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;

namespace ShareX.ScreenCaptureLib.Presentation.RegionCapture;

public partial class RegionCaptureWindow : Window
{
    private readonly TaskCompletionSource<AvaloniaRegionCaptureResult?> _completionSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private AvaloniaRegionCaptureRequest? _request;
    private AvaloniaRegionCaptureResult? _pendingResult;
    private Image _backgroundImage = null!;
    private Grid _regionInputSurface = null!;
    private RegionSelectionOverlay _regionOverlay = null!;
    private AvaloniaCanvas _regionResizeNodeCanvas = null!;
    private LayoutTransformControl _regionTransform = null!;
    private StackPanel _magnifierPanel = null!;
    private Grid _magnifierView = null!;
    private Image _magnifierImage = null!;
    private MagnifierPixelGrid _magnifierPixelGrid = null!;
    private Border _pointerInfoPanel = null!;
    private TextBlock _pointerInfoText = null!;
    private Border _selectionInfoPanel = null!;
    private TextBlock _selectionInfoText = null!;
    private WriteableBitmap? _magnifierBitmap;
    private IReadOnlyList<SimpleWindowInfo> _windows = Array.Empty<SimpleWindowInfo>();
    private readonly Dictionary<SelectionResizeNodeKind, Border> _regionResizeNodes = [];
    private SimpleWindowInfo? _hoverCandidate;
    private SimpleWindowInfo? _selectedCandidate;
    private RegionInteraction _interaction;
    private SelectionResizeNodeKind? _resizeNode;
    private Point _pressPoint;
    private Point _lastPointerPoint;
    private Point _lastCreationPoint;
    private Rect _interactionStartRectangle;
    private readonly TranslateTransform _magnifierTransform = new();
    private int _imageWidth;
    private int _imageHeight;
    private bool _keyboardInputEnabled;
    private bool _isMovingSelectionDuringCreation;
    private bool _wasControlHeldDuringCreation;
    private bool _suppressNextRightButtonReleaseAction;
    private bool _closing;
    private Exception? _startupException;

    public RegionCaptureWindow()
    {
        RequestedThemeVariant = ThemeManager.GetCurrentTheme();
        AvaloniaXamlLoader.Load(this);
#if !DEBUG
        Topmost = true;
#endif
        ResolveControls();
    }

    public RegionCaptureWindow(AvaloniaRegionCaptureRequest request) : this()
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _imageWidth = Math.Max(1, request.ScreenBounds.Width);
        _imageHeight = Math.Max(1, request.ScreenBounds.Height);
        if (request.RegionCaptureOptions.ActiveMonitorMode)
        {
            Helpers.LockCursorToWindow(this);
        }
        InitializeCaptureWorkspace();
        Opened += OnOpened;
    }

    public Task<AvaloniaRegionCaptureResult?> CaptureAsync()
    {
        if (_request == null)
        {
            throw new InvalidOperationException("A capture request is required.");
        }

        ConfigureInitialPixelBounds();
        Show();
        return _completionSource.Task;
    }

    protected override void OnClosed(EventArgs e)
    {
        Exception? closeException = _startupException;

        try
        {
            _magnifierBitmap?.Dispose();
            _magnifierBitmap = null;
            _request?.Screenshot.Dispose();
            _request?.CursorBitmap?.Dispose();
        }
        catch (Exception ex)
        {
            closeException ??= ex;
        }
        finally
        {
            if (closeException != null)
            {
                _completionSource.TrySetException(closeException);
            }
            else
            {
                _completionSource.TrySetResult(_pendingResult);
            }

            base.OnClosed(e);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!_keyboardInputEnabled || _closing || _request == null)
        {
            return;
        }

        if (_interaction == RegionInteraction.Creating &&
            (e.Key is Key.LeftShift or Key.RightShift))
        {
            UpdateSelectionDuringCreation(_lastCreationPoint, e.KeyModifiers | KeyModifiers.Shift);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && HasValidSelection())
        {
            e.Handled = true;
            Complete(_regionOverlay.SelectionRectangle);
            return;
        }

        if (e.Key == Key.Space)
        {
            e.Handled = true;
            Complete(new Rect(0, 0, _imageWidth, _imageHeight), includeWindowInfo: false);
            return;
        }

        if (e.Key == Key.OemTilde)
        {
            e.Handled = true;
            CompleteActiveMonitor();
            return;
        }

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            int distance = e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                ? RegionCaptureOptions.MoveSpeedMaximum
                : RegionCaptureOptions.MoveSpeedMinimum;
            int dx = e.Key == Key.Left ? -distance : e.Key == Key.Right ? distance : 0;
            int dy = e.Key == Key.Up ? -distance : e.Key == Key.Down ? distance : 0;
            System.Windows.Forms.Cursor.Position = System.Windows.Forms.Cursor.Position.Add(dx, dy);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && HasValidSelection())
        {
            ClearSelection();
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if ((_interaction is RegionInteraction.Creating or RegionInteraction.PendingHover) &&
            (e.Key is Key.LeftCtrl or Key.RightCtrl))
        {
            _isMovingSelectionDuringCreation = false;
            _wasControlHeldDuringCreation = false;
        }

        if (_keyboardInputEnabled && !_closing && _request != null &&
            _interaction == RegionInteraction.Creating && (e.Key is Key.LeftShift or Key.RightShift))
        {
            UpdateSelectionDuringCreation(_lastCreationPoint, e.KeyModifiers & ~KeyModifiers.Shift);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || _closing)
        {
            return;
        }

        e.Handled = true;
        CancelCapture();
    }

    private void ResolveControls()
    {
        _backgroundImage = this.FindControl<Image>("BackgroundImage")!;
        _regionInputSurface = this.FindControl<Grid>("RegionInputSurface")!;
        _regionOverlay = this.FindControl<RegionSelectionOverlay>("RegionOverlay")!;
        _regionResizeNodeCanvas = this.FindControl<AvaloniaCanvas>("RegionResizeNodeCanvas")!;
        _regionTransform = this.FindControl<LayoutTransformControl>("RegionTransform")!;
        _magnifierPanel = this.FindControl<StackPanel>("MagnifierPanel")!;
        _magnifierPanel.RenderTransform = _magnifierTransform;
        _magnifierView = this.FindControl<Grid>("MagnifierView")!;
        _magnifierImage = this.FindControl<Image>("MagnifierImage")!;
        _magnifierPixelGrid = this.FindControl<MagnifierPixelGrid>("MagnifierPixelGrid")!;
        _pointerInfoPanel = this.FindControl<Border>("PointerInfoPanel")!;
        _pointerInfoText = this.FindControl<TextBlock>("PointerInfoText")!;
        _selectionInfoPanel = this.FindControl<Border>("SelectionInfoPanel")!;
        _selectionInfoText = this.FindControl<TextBlock>("SelectionInfoText")!;
    }

    private void InitializeCaptureWorkspace()
    {
        if (_request == null)
        {
            return;
        }

        _regionInputSurface.Width = _imageWidth;
        _regionInputSurface.Height = _imageHeight;
        _regionOverlay.DimAlpha = GetDimAlpha(_request.RegionCaptureOptions);
        _regionOverlay.ShowCenterCrosshair = _request.RegionCaptureOptions.ShowCenterCrosshair;
        _regionOverlay.ShowCursorCrosshair = _request.RegionCaptureOptions.ShowScreenCrosshair;
        _regionInputSurface.Cursor = CursorAssetLoader.GetCrosshairCursor(GetInitialScaling());
        InitializeRegionResizeNodes();

        _magnifierView.IsVisible = _request.RegionCaptureOptions.ShowMagnifier;
        ApplyMagnifierShape();
        _pointerInfoPanel.IsVisible = _request.RegionCaptureOptions.ShowInfo;
        _magnifierPanel.IsVisible = _magnifierView.IsVisible || _pointerInfoPanel.IsVisible;
        Title = Localization.Strings.BaseRegionForm_InitializeComponent_Region_capture;
    }

    private void ApplyMagnifierShape()
    {
        if (_request == null)
        {
            return;
        }

        RegionCaptureOptions options = _request.RegionCaptureOptions;
        int magnifierSize = Math.Clamp(options.MagnifierSize,
            RegionCaptureOptions.MagnifierSizeMinimum,
            RegionCaptureOptions.MagnifierSizeMaximum);
        options.MagnifierSize = magnifierSize;
        double outerSize = magnifierSize + 2;
        bool useSquare = options.UseSquareMagnifier;

        _magnifierView.Width = outerSize;
        _magnifierView.Height = outerSize;

        Ellipse circleOuter = this.FindControl<Ellipse>("MagnifierCircleOuter")!;
        Border squareOuter = this.FindControl<Border>("MagnifierSquareOuter")!;
        circleOuter.IsVisible = !useSquare;
        squareOuter.IsVisible = useSquare;
        circleOuter.Width = outerSize;
        circleOuter.Height = outerSize;
        squareOuter.Width = outerSize;
        squareOuter.Height = outerSize;

        Ellipse circleInner = this.FindControl<Ellipse>("MagnifierCircleInner")!;
        Border squareInner = this.FindControl<Border>("MagnifierSquareInner")!;
        circleInner.IsVisible = !useSquare;
        squareInner.IsVisible = useSquare;
        circleInner.Width = magnifierSize;
        circleInner.Height = magnifierSize;
        squareInner.Width = magnifierSize;
        squareInner.Height = magnifierSize;

        Grid magnifierContent = this.FindControl<Grid>("MagnifierContent")!;
        magnifierContent.Width = magnifierSize;
        magnifierContent.Height = magnifierSize;
        _magnifierImage.Width = magnifierSize;
        _magnifierImage.Height = magnifierSize;
        magnifierContent.Clip = useSquare
            ? null
            : new EllipseGeometry(new Rect(0, 0, magnifierSize, magnifierSize));
    }

    private void InitializeRegionResizeNodes()
    {
        double scaling = GetInitialScaling();
        foreach (SelectionResizeNodeKind node in SelectionResizeNode.RectangleNodes)
        {
            Border control = SelectionResizeNode.Create(
                0,
                0,
                node,
                CursorAssetLoader.GetOpenHandCursor(scaling));
            _regionResizeNodeCanvas.Children.Add(control);
            _regionResizeNodes.Add(node, control);
        }
    }

    private void SetRegionResizeNodesVisible(bool visible)
    {
        _regionResizeNodeCanvas.IsVisible = visible;
        if (visible)
        {
            UpdateRegionResizeNodePositions();
        }
    }

    private void UpdateRegionResizeNodePositions()
    {
        Rect selection = _regionOverlay.SelectionRectangle;
        if (!RegionSelectionOverlay.IsValid(selection))
        {
            return;
        }

        foreach ((SelectionResizeNodeKind node, Border control) in _regionResizeNodes)
        {
            SelectionResizeNode.SetPosition(control, SelectionResizeNode.GetPosition(selection, node));
        }
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        try
        {
            if (_request == null)
            {
                CancelCapture();
                return;
            }

            ApplyPixelSize();
            UpdatePixelTransforms();

            if (_request.CursorBitmap != null)
            {
                using SKCanvas canvas = new(_request.Screenshot);
                canvas.DrawBitmap(_request.CursorBitmap, _request.CursorPosition.X, _request.CursorPosition.Y);
            }

            _backgroundImage.Source = BitmapConversionHelpers.ToAvaloniBitmap(_request.Screenshot);

            Activate();
            Focus();
            _regionInputSurface.Focus();

            DrawingPoint cursorPosition = System.Windows.Forms.Control.MousePosition;
            _lastPointerPoint = ClampPoint(new Point(
                cursorPosition.X - _request.ScreenBounds.X,
                cursorPosition.Y - _request.ScreenBounds.Y));
            UpdateHud(_lastPointerPoint);

            _ = LoadWindowRegionsAsync();

            int inputDelay = Math.Max(0, RegionCaptureOptions.InputDelay);
            if (inputDelay > 0)
            {
                await Task.Delay(inputDelay);
            }

            if (!_closing)
            {
                _keyboardInputEnabled = true;
            }
        }
        catch (Exception ex)
        {
            _startupException = ex;
            CancelCapture();
        }
    }

    private async Task LoadWindowRegionsAsync()
    {
        if (_request == null || !_request.RegionCaptureOptions.DetectWindows)
        {
            return;
        }

        try
        {
            IntPtr ignoredHandle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            WindowsRectangleList windows = new WindowsRectangleList
            {
                IncludeChildWindows = _request.RegionCaptureOptions.DetectControls,
                Timeout = 5000
            };

            if (ignoredHandle != IntPtr.Zero)
            {
                windows.IgnoreHandleList.Add(ignoredHandle);
            }

            IReadOnlyList<SimpleWindowInfo> result = await Task.Run(windows.GetWindowInfoList);
            if (!_closing)
            {
                _windows = result;
                UpdateHover(_lastPointerPoint);
            }
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex);
        }
    }

    private void OnRegionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_closing || _request == null)
        {
            return;
        }

        Point point = ClampPoint(e.GetPosition(_regionInputSurface));
        _lastPointerPoint = point;
        PointerPointProperties properties = e.GetCurrentPoint(_regionInputSurface).Properties;

        if (TryCancelRegionCreation(e, point))
        {
            return;
        }

        if (properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            e.Handled = true;
            return;
        }

        if (properties.IsMiddleButtonPressed)
        {
            RunCaptureAction(_request.RegionCaptureOptions.RegionCaptureActionMiddleClick);
            e.Handled = true;
            return;
        }

        if (properties.IsXButton1Pressed)
        {
            RunCaptureAction(_request.RegionCaptureOptions.RegionCaptureActionX1Click);
            e.Handled = true;
            return;
        }

        if (properties.IsXButton2Pressed)
        {
            RunCaptureAction(_request.RegionCaptureOptions.RegionCaptureActionX2Click);
            e.Handled = true;
            return;
        }

        if (properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount >= 2 && HasValidSelection() && _regionOverlay.SelectionRectangle.Contains(point))
        {
            Complete(_regionOverlay.SelectionRectangle);
            e.Handled = true;
            return;
        }

        _pressPoint = point;
        _interactionStartRectangle = _regionOverlay.SelectionRectangle;
        _resizeNode = SelectionResizeNode.TryGetKind((e.Source as Control)?.Tag, out SelectionResizeNodeKind resizeNode)
            ? resizeNode
            : null;

        if (_resizeNode.HasValue)
        {
            _interaction = RegionInteraction.Resizing;
        }
        else if (HasValidSelection() && _regionOverlay.SelectionRectangle.Contains(point))
        {
            _interaction = RegionInteraction.Moving;
        }
        else if (RegionSelectionOverlay.IsValid(_regionOverlay.HoverRectangle))
        {
            SetSelection(_regionOverlay.HoverRectangle, _hoverCandidate);
            _interactionStartRectangle = _regionOverlay.SelectionRectangle;
            _interaction = RegionInteraction.PendingHover;
        }
        else
        {
            SetSelection(default, null);
            _interaction = RegionInteraction.Creating;
        }

        if (_interaction is RegionInteraction.Creating or RegionInteraction.PendingHover)
        {
            _lastCreationPoint = point;
            _isMovingSelectionDuringCreation = false;
            _wasControlHeldDuringCreation = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        }

        SetRegionResizeNodesVisible(false);
        if (_interaction is RegionInteraction.Moving or RegionInteraction.Resizing)
        {
            _regionInputSurface.Cursor = CursorAssetLoader.GetClosedHandCursor(Math.Max(1, RenderScaling));
        }
        e.Pointer.Capture(_regionInputSurface);
        e.Handled = true;
    }

    private void OnRegionPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_closing || _request == null)
        {
            return;
        }

        Point point = ClampPoint(e.GetPosition(_regionInputSurface));
        _lastPointerPoint = point;

        if (TryCancelRegionCreation(e, point))
        {
            return;
        }

        if (TryHandleRightButtonRelease(e))
        {
            return;
        }

        UpdateHud(point);

        switch (_interaction)
        {
            case RegionInteraction.None:
                UpdateHover(point);
                break;
            case RegionInteraction.PendingHover:
                if (Distance(_pressPoint, point) >= 4)
                {
                    _interaction = RegionInteraction.Creating;
                    _selectedCandidate = null;
                    SetSelection(default, null);
                    UpdateSelectionDuringCreation(point, e.KeyModifiers);
                }
                break;
            case RegionInteraction.Creating:
                UpdateSelectionDuringCreation(point, e.KeyModifiers);
                break;
            case RegionInteraction.Moving:
                MoveSelection(point.X - _pressPoint.X, point.Y - _pressPoint.Y, _interactionStartRectangle);
                break;
            case RegionInteraction.Resizing:
                ResizeSelection(point);
                break;
        }

        e.Handled = true;
    }

    private void OnRegionPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_closing || _request == null)
        {
            return;
        }

        Point point = ClampPoint(e.GetPosition(_regionInputSurface));
        _lastPointerPoint = point;

        if (TryCancelRegionCreation(e, point))
        {
            return;
        }

        PointerPointProperties properties = e.GetCurrentPoint(_regionInputSurface).Properties;
        PointerUpdateKind updateKind = properties.PointerUpdateKind;
        if (TryHandleRightButtonRelease(e))
        {
            return;
        }

        if (_interaction == RegionInteraction.None)
        {
            if (updateKind == PointerUpdateKind.LeftButtonReleased && !properties.IsRightButtonPressed)
            {
                _suppressNextRightButtonReleaseAction = false;
            }

            return;
        }

        if (updateKind != PointerUpdateKind.LeftButtonReleased)
        {
            return;
        }

        e.Pointer.Capture(null);
        _interaction = RegionInteraction.None;
        _resizeNode = null;
        _regionInputSurface.Cursor = CursorAssetLoader.GetCrosshairCursor(Math.Max(1, RenderScaling));
        ResetCreationModifiers();

        Rect selection = _regionOverlay.SelectionRectangle;
        if (selection.Width < RegionCaptureOptions.MinimumSize || selection.Height < RegionCaptureOptions.MinimumSize)
        {
            if (RegionSelectionOverlay.IsValid(_regionOverlay.HoverRectangle))
            {
                SetSelection(_regionOverlay.HoverRectangle, _hoverCandidate);
            }
            else
            {
                ClearSelection();
            }
        }

        if (HasValidSelection() && _request.RegionCaptureOptions.QuickCapture)
        {
            Complete(_regionOverlay.SelectionRectangle);
        }
        else
        {
            SetRegionResizeNodesVisible(HasValidSelection());
        }

        _suppressNextRightButtonReleaseAction = false;
        e.Handled = true;
    }

    private bool TryCancelRegionCreation(PointerEventArgs e, Point point)
    {
        PointerUpdateKind updateKind = e.GetCurrentPoint(_regionInputSurface).Properties.PointerUpdateKind;
        if (updateKind is not PointerUpdateKind.RightButtonPressed and not PointerUpdateKind.RightButtonReleased ||
            _interaction is not (RegionInteraction.Creating or RegionInteraction.PendingHover))
        {
            return false;
        }

        _suppressNextRightButtonReleaseAction = updateKind == PointerUpdateKind.RightButtonPressed;
        e.Pointer.Capture(null);
        ClearSelection();
        UpdateHud(point);
        e.Handled = true;
        return true;
    }

    private bool TryHandleRightButtonRelease(PointerEventArgs e)
    {
        if (e.GetCurrentPoint(_regionInputSurface).Properties.PointerUpdateKind != PointerUpdateKind.RightButtonReleased)
        {
            return false;
        }

        if (_suppressNextRightButtonReleaseAction)
        {
            _suppressNextRightButtonReleaseAction = false;
        }
        else if (_request != null)
        {
            RunCaptureAction(_request.RegionCaptureOptions.RegionCaptureActionRightClick);
        }

        e.Handled = true;
        return true;
    }

    private void OnRegionPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_request == null)
        {
            return;
        }

        int delta = e.Delta.Y > 0 ? -2 : 2;
        int count = NormalizeMagnifierPixelCount(_request.RegionCaptureOptions.MagnifierPixelCount + delta);

        _request.RegionCaptureOptions.MagnifierPixelCount = count;
        _request.RegionCaptureOptions.ShowMagnifier = true;
        RecreateMagnifierBitmap();
        UpdateHud(_lastPointerPoint);
        e.Handled = true;
    }

    private void UpdateHover(Point imagePoint)
    {
        if (_request == null || HasValidSelection() || _interaction != RegionInteraction.None)
        {
            _regionOverlay.HoverRectangle = default;
            _hoverCandidate = null;
            return;
        }

        double screenX = _request.ScreenBounds.X + Math.Round(imagePoint.X);
        double screenY = _request.ScreenBounds.Y + Math.Round(imagePoint.Y);

        Rect activeMonitorRect = default;
        PixelPoint desktopPoint = new PixelPoint((int)Math.Round(screenX), (int)Math.Round(screenY));
        Screen? activeScreen = Screens.ScreenFromPoint(desktopPoint) ??
            Screens.All.FirstOrDefault(s =>
                desktopPoint.X >= s.Bounds.X && desktopPoint.X < s.Bounds.X + s.Bounds.Width &&
                desktopPoint.Y >= s.Bounds.Y && desktopPoint.Y < s.Bounds.Y + s.Bounds.Height);

        if (activeScreen != null)
        {
            double mLeft = Math.Max(0, activeScreen.Bounds.X - _request.ScreenBounds.X);
            double mTop = Math.Max(0, activeScreen.Bounds.Y - _request.ScreenBounds.Y);
            double mRight = Math.Min(_imageWidth, activeScreen.Bounds.X - _request.ScreenBounds.X + activeScreen.Bounds.Width);
            double mBottom = Math.Min(_imageHeight, activeScreen.Bounds.Y - _request.ScreenBounds.Y + activeScreen.Bounds.Height);
            if (mRight > mLeft && mBottom > mTop)
            {
                activeMonitorRect = new Rect(mLeft, mTop, mRight - mLeft, mBottom - mTop);
            }
        }

        SimpleWindowInfo? candidate = _windows.FirstOrDefault(window =>
            ContainsPoint(window.Rectangle, screenX, screenY));

        if (candidate == null)
        {
            _hoverCandidate = null;
            _regionOverlay.HoverRectangle = activeMonitorRect;
            return;
        }

        DrawingRectangle candidateRectangle = candidate.Rectangle;
        bool spansMultipleMonitors = Screens.All.Count(s =>
        {
            DrawingRectangle mBounds = new DrawingRectangle(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height);
            return mBounds.IntersectsWith(candidateRectangle);
        }) > 1;

        if (spansMultipleMonitors)
        {
            _hoverCandidate = null;
            _regionOverlay.HoverRectangle = activeMonitorRect;
            return;
        }

        double candidateLeft = (double)candidateRectangle.X - _request.ScreenBounds.X;
        double candidateTop = (double)candidateRectangle.Y - _request.ScreenBounds.Y;
        double left = Math.Max(0, candidateLeft);
        double top = Math.Max(0, candidateTop);
        double right = Math.Min(_imageWidth, candidateLeft + candidateRectangle.Width);
        double bottom = Math.Min(_imageHeight, candidateTop + candidateRectangle.Height);
        Rect hover = right > left && bottom > top
            ? new Rect(left, top, right - left, bottom - top)
            : default;

        _hoverCandidate = RegionSelectionOverlay.IsValid(hover) ? candidate : null;
        _regionOverlay.HoverRectangle = hover;
    }

    private static bool ContainsPoint(DrawingRectangle rectangle, double x, double y)
    {
        return rectangle.Width > 0 && rectangle.Height > 0 &&
            x >= rectangle.X && y >= rectangle.Y &&
            x < (double)rectangle.X + rectangle.Width &&
            y < (double)rectangle.Y + rectangle.Height;
    }

    private void SetSelection(Rect rectangle, SimpleWindowInfo? candidate)
    {
        _regionOverlay.SelectionRectangle = RegionSelectionOverlay.Intersect(
            rectangle,
            new Rect(0, 0, GetImageSize().Width, GetImageSize().Height));
        _selectedCandidate = candidate;
        if (_regionResizeNodeCanvas.IsVisible)
        {
            UpdateRegionResizeNodePositions();
        }
        UpdateSelectionInfo();
    }

    private void ClearSelection()
    {
        _interaction = RegionInteraction.None;
        ResetCreationModifiers();
        _selectedCandidate = null;
        _regionOverlay.SelectionRectangle = default;
        SetRegionResizeNodesVisible(false);
        _selectionInfoPanel.IsVisible = false;
        UpdateHover(_lastPointerPoint);
    }

    private void MoveSelection(double dx, double dy)
    {
        MoveSelection(dx, dy, _regionOverlay.SelectionRectangle);
    }

    private void MoveSelection(double dx, double dy, Rect source)
    {
        Size bounds = GetImageSize();
        double x = Math.Clamp(source.X + dx, 0, Math.Max(0, bounds.Width - source.Width));
        double y = Math.Clamp(source.Y + dy, 0, Math.Max(0, bounds.Height - source.Height));
        SetSelection(new Rect(x, y, source.Width, source.Height), null);
    }

    private void UpdateSelectionDuringCreation(Point point, KeyModifiers modifiers)
    {
        bool controlHeld = modifiers.HasFlag(KeyModifiers.Control);
        if (!controlHeld)
        {
            _isMovingSelectionDuringCreation = false;
        }
        else if (RegionSelectionOverlay.IsValid(_regionOverlay.SelectionRectangle) &&
            (_isMovingSelectionDuringCreation || !_wasControlHeldDuringCreation))
        {
            MoveSelectionDuringCreation(point);
            _isMovingSelectionDuringCreation = true;
            _lastCreationPoint = point;
            _wasControlHeldDuringCreation = true;
            return;
        }

        bool constrainToSquare = modifiers.HasFlag(KeyModifiers.Shift);
        SetSelection(CreateSelectionRectangle(_pressPoint, point, GetImageSize(), constrainToSquare), null);
        _lastCreationPoint = point;
        _wasControlHeldDuringCreation = controlHeld;
    }

    private void MoveSelectionDuringCreation(Point point)
    {
        Rect before = _regionOverlay.SelectionRectangle;
        MoveSelection(point.X - _lastCreationPoint.X, point.Y - _lastCreationPoint.Y, before);
        Rect after = _regionOverlay.SelectionRectangle;
        _pressPoint = ClampPoint(new Point(
            _pressPoint.X + after.X - before.X,
            _pressPoint.Y + after.Y - before.Y));
    }

    private static Rect CreateSelectionRectangle(Point first, Point second, Size bounds, bool constrainToSquare)
    {
        if (!constrainToSquare)
        {
            return RegionSelectionOverlay.NormalizeAndClamp(first, second, bounds);
        }

        double deltaX = second.X - first.X;
        double deltaY = second.Y - first.Y;
        double side = Math.Max(Math.Abs(deltaX), Math.Abs(deltaY));
        double availableWidth = deltaX < 0 ? first.X : bounds.Width - first.X;
        double availableHeight = deltaY < 0 ? first.Y : bounds.Height - first.Y;
        side = Math.Max(0, Math.Min(side, Math.Min(availableWidth, availableHeight)));

        Point constrained = new(
            first.X + (deltaX < 0 ? -side : side),
            first.Y + (deltaY < 0 ? -side : side));
        return RegionSelectionOverlay.NormalizeAndClamp(first, constrained, bounds);
    }

    private void ResetCreationModifiers()
    {
        _isMovingSelectionDuringCreation = false;
        _wasControlHeldDuringCreation = false;
    }

    private void ResizeSelection(Point point)
    {
        if (!_resizeNode.HasValue)
        {
            return;
        }

        Vector delta = point - _pressPoint;
        Rect resized = SelectionResizeNode.Resize(_regionOverlay.SelectionRectangle, _resizeNode.Value, delta);
        SetSelection(resized, null);
        _pressPoint = point;
    }

    private void UpdateHud(Point imagePoint)
    {
        if (_request == null)
        {
            return;
        }

        _regionOverlay.CursorPosition = imagePoint;

        bool showMagnifier = _request.RegionCaptureOptions.ShowMagnifier;
        bool showInfo = _request.RegionCaptureOptions.ShowInfo;

        if (showMagnifier || showInfo)
        {
            if (showMagnifier)
            {
                UpdateMagnifier(imagePoint);
            }

            _magnifierView.IsVisible = showMagnifier;
            _pointerInfoPanel.IsVisible = showInfo;
            _magnifierPanel.IsVisible = true;
            _pointerInfoText.Text = GetPointerInfoText(imagePoint);

            double scale = Math.Max(1, RenderScaling);
            Point pointer = new Point(imagePoint.X / scale, imagePoint.Y / scale);
            PositionPanelNearPointer(_magnifierPanel, pointer, 18);
            _magnifierPanel.InvalidateVisual();
        }
        else
        {
            _magnifierPanel.IsVisible = false;
        }

        UpdateSelectionInfo();
    }

    private string GetPointerInfoText(Point imagePoint)
    {
        if (_request == null)
        {
            return string.Empty;
        }

        int imageX = Math.Clamp((int)Math.Round(imagePoint.X), 0, _imageWidth - 1);
        int imageY = Math.Clamp((int)Math.Round(imagePoint.Y), 0, _imageHeight - 1);
        DrawingPoint screenPosition = new(
            _request.ScreenBounds.X + imageX,
            _request.ScreenBounds.Y + imageY);

        RegionCaptureOptions options = _request.RegionCaptureOptions;
        if (options.UseCustomInfoText)
        {
            SKColor pixel = _request.Screenshot.GetPixel(imageX, imageY);
            DrawingColor color = DrawingColor.FromArgb(pixel.Alpha, pixel.Red, pixel.Green, pixel.Blue);

            if (!string.IsNullOrEmpty(options.CustomInfoText))
            {
                return CodeMenuEntryPixelInfo.Parse(options.CustomInfoText, color, screenPosition);
            }

            return $"RGB: {color.R}, {color.G}, {color.B}{Environment.NewLine}" +
                $"Hex: {ColorHelpers.ColorToHex(color)}{Environment.NewLine}" +
                $"X: {screenPosition.X} Y: {screenPosition.Y}";
        }

        return $"X: {screenPosition.X} Y: {screenPosition.Y}";
    }

    private unsafe void UpdateMagnifier(Point imagePoint)
    {
        if (_request == null)
        {
            return;
        }

        EnsureMagnifierBitmap();
        if (_magnifierBitmap == null)
        {
            return;
        }

        int count = _magnifierBitmap.PixelSize.Width;
        int radius = count / 2;
        int centerX = (int)Math.Round(imagePoint.X);
        int centerY = (int)Math.Round(imagePoint.Y);

        using ILockedFramebuffer framebuffer = _magnifierBitmap.Lock();
        byte* destination = (byte*)framebuffer.Address;

        for (int y = 0; y < count; y++)
        {
            byte* row = destination + y * framebuffer.RowBytes;
            int sourceY = Math.Clamp(centerY + y - radius, 0, _imageHeight - 1);

            for (int x = 0; x < count; x++)
            {
                int sourceX = Math.Clamp(centerX + x - radius, 0, _imageWidth - 1);
                SKColor color = _request.Screenshot.GetPixel(sourceX, sourceY);
                int offset = x * 4;
                row[offset] = color.Blue;
                row[offset + 1] = color.Green;
                row[offset + 2] = color.Red;
                row[offset + 3] = color.Alpha;
            }
        }
    }

    private void EnsureMagnifierBitmap()
    {
        if (_request == null)
        {
            return;
        }

        int count = NormalizeMagnifierPixelCount(_request.RegionCaptureOptions.MagnifierPixelCount);

        if (_magnifierBitmap?.PixelSize == new PixelSize(count, count))
        {
            return;
        }

        RecreateMagnifierBitmap(count);
    }

    private void RecreateMagnifierBitmap(int? requestedCount = null)
    {
        if (_request == null)
        {
            return;
        }

        int count = NormalizeMagnifierPixelCount(requestedCount ?? _request.RegionCaptureOptions.MagnifierPixelCount);
        _request.RegionCaptureOptions.MagnifierPixelCount = count;

        if (_magnifierBitmap?.PixelSize == new PixelSize(count, count))
        {
            _magnifierPixelGrid.PixelCount = count;
            return;
        }

        _magnifierBitmap?.Dispose();
        _magnifierBitmap = new WriteableBitmap(
            new PixelSize(count, count),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        _magnifierImage.Source = _magnifierBitmap;
        _magnifierPixelGrid.PixelCount = count;
    }

    private int NormalizeMagnifierPixelCount(int requestedCount)
    {
        double scale = double.IsFinite(RenderScaling) && RenderScaling > 0 ? RenderScaling : 1;
        int magnifierSize = Math.Clamp(_request?.RegionCaptureOptions.MagnifierSize ?? RegionCaptureOptions.MagnifierSizeMinimum,
            RegionCaptureOptions.MagnifierSizeMinimum,
            RegionCaptureOptions.MagnifierSizeMaximum);
        int sizeLimitedMaximum = (int)Math.Floor(
            magnifierSize * scale / RegionCaptureOptions.MagnifierPixelSizeMinimum);
        int maximum = Math.Clamp(
            sizeLimitedMaximum,
            RegionCaptureOptions.MagnifierPixelCountMinimum,
            RegionCaptureOptions.MagnifierPixelCountMaximum);

        if ((maximum & 1) == 0)
        {
            maximum--;
        }

        int count = Math.Clamp(requestedCount, RegionCaptureOptions.MagnifierPixelCountMinimum, maximum);
        if ((count & 1) == 0)
        {
            count = count < maximum ? count + 1 : count - 1;
        }

        return count;
    }

    private void UpdateSelectionInfo()
    {
        if (_request?.RegionCaptureOptions.ShowInfo != true)
        {
            _selectionInfoPanel.IsVisible = false;
            return;
        }

        Rect selection = _regionOverlay.SelectionRectangle;
        if (!RegionSelectionOverlay.IsValid(selection))
        {
            _selectionInfoPanel.IsVisible = false;
            return;
        }

        int width = Math.Max(1, (int)Math.Round(selection.Width));
        int height = Math.Max(1, (int)Math.Round(selection.Height));
        _selectionInfoText.Text = $"{width} × {height}";
        _selectionInfoPanel.IsVisible = true;

        double scale = Math.Max(1, RenderScaling);
        Point anchor = new Point(selection.Left / scale, selection.Bottom / scale);
        double x = Math.Clamp(anchor.X, 0, Math.Max(0, Bounds.Width - 100));
        double y = anchor.Y + 8;
        if (y + 36 > Bounds.Height)
        {
            y = Math.Max(0, selection.Top / scale - 36);
        }

        AvaloniaCanvas.SetLeft(_selectionInfoPanel, x);
        AvaloniaCanvas.SetTop(_selectionInfoPanel, y);
    }

    private void PositionPanelNearPointer(Control panel, Point pointer, double offset)
    {
        bool isMagnifierPanel = ReferenceEquals(panel, _magnifierPanel);
        double fallbackWidth = isMagnifierPanel ? _magnifierView.Width : 170;
        double fallbackHeight = isMagnifierPanel ? _magnifierView.Height + 53 : 205;
        double width = panel.Bounds.Width > 0 ? panel.Bounds.Width : fallbackWidth;
        double height = panel.Bounds.Height > 0 ? panel.Bounds.Height : fallbackHeight;
        double x = pointer.X + offset;
        double y = pointer.Y + offset;

        if (x + width > Bounds.Width)
        {
            x = pointer.X - width - offset;
        }

        if (y + height > Bounds.Height)
        {
            y = pointer.Y - height - offset;
        }

        double targetX = Math.Clamp(x, 0, Math.Max(0, Bounds.Width - width));
        double targetY = Math.Clamp(y, 0, Math.Max(0, Bounds.Height - height));

        if (isMagnifierPanel)
        {
            double scale = Math.Max(1, RenderScaling);
            targetX = Math.Round(targetX * scale) / scale;
            targetY = Math.Round(targetY * scale) / scale;
            _magnifierTransform.X = targetX;
            _magnifierTransform.Y = targetY;
            _magnifierPixelGrid.InvalidateVisual();
        }
        else
        {
            AvaloniaCanvas.SetLeft(panel, targetX);
            AvaloniaCanvas.SetTop(panel, targetY);
        }
    }

    private void RunCaptureAction(RegionCaptureAction action)
    {
        if (_request == null)
        {
            return;
        }

        switch (action)
        {
            case RegionCaptureAction.None:
                break;
            case RegionCaptureAction.CancelCapture:
                CancelCapture();
                break;
            case RegionCaptureAction.RemoveShapeCancelCapture:
            case RegionCaptureAction.RemoveShape:
                if (HasValidSelection() && _regionOverlay.SelectionRectangle.Contains(_lastPointerPoint))
                {
                    ClearSelection();
                }
                else if (action == RegionCaptureAction.RemoveShapeCancelCapture)
                {
                    CancelCapture();
                }
                break;
            case RegionCaptureAction.SwapToolType:
                break;
            case RegionCaptureAction.CaptureFullscreen:
                Complete(new Rect(0, 0, _imageWidth, _imageHeight), includeWindowInfo: false);
                break;
            case RegionCaptureAction.CaptureActiveMonitor:
                CompleteActiveMonitor();
                break;
            case RegionCaptureAction.CaptureLastRegion:
                CompleteLastRegion();
                break;
        }
    }

    private void CompleteLastRegion()
    {
        if (_request == null || RegionCaptureIntegration.LastRegionRectangle.IsEmpty)
        {
            return;
        }

        DrawingRectangle screenRectangle = DrawingRectangle.Intersect(
            RegionCaptureIntegration.LastRegionRectangle,
            _request.ScreenBounds);
        if (screenRectangle.IsEmpty)
        {
            return;
        }

        Complete(new Rect(
            screenRectangle.X - _request.ScreenBounds.X,
            screenRectangle.Y - _request.ScreenBounds.Y,
            screenRectangle.Width,
            screenRectangle.Height), includeWindowInfo: false);
    }

    private void CompleteActiveMonitor()
    {
        if (_request == null)
        {
            return;
        }

        PixelPoint desktopPoint = new PixelPoint(
            _request.ScreenBounds.X + (int)Math.Round(_lastPointerPoint.X),
            _request.ScreenBounds.Y + (int)Math.Round(_lastPointerPoint.Y));
        Screen? screen = Screens.ScreenFromPoint(desktopPoint);
        if (screen == null)
        {
            return;
        }

        Rect relative = new Rect(
            screen.Bounds.X - _request.ScreenBounds.X,
            screen.Bounds.Y - _request.ScreenBounds.Y,
            screen.Bounds.Width,
            screen.Bounds.Height);
        Complete(RegionSelectionOverlay.Intersect(relative,
            new Rect(0, 0, _imageWidth, _imageHeight)),
            includeWindowInfo: false);
    }

    private void Complete(Rect selection, bool includeWindowInfo = true)
    {
        if (_closing || _request == null || !RegionSelectionOverlay.IsValid(selection))
        {
            return;
        }

        int left = Math.Clamp((int)Math.Floor(selection.Left), 0, _imageWidth - 1);
        int top = Math.Clamp((int)Math.Floor(selection.Top), 0, _imageHeight - 1);
        int right = Math.Clamp((int)Math.Ceiling(selection.Right), left + 1, _imageWidth);
        int bottom = Math.Clamp((int)Math.Ceiling(selection.Bottom), top + 1, _imageHeight);

        int width = right - left;
        int height = bottom - top;
        SKBitmap? output = GetSnapshot(new Rect(left, top, width, height));
        if (output == null)
        {
            CancelCapture();
            return;
        }

        DrawingRectangle screenRectangle = new DrawingRectangle(
            _request.ScreenBounds.X + left,
            _request.ScreenBounds.Y + top,
            width,
            height);
        WindowInfo? windowInfo = includeWindowInfo ? FindTopLevelWindowInfo(screenRectangle) : null;

        _pendingResult = new AvaloniaRegionCaptureResult(
            output,
            screenRectangle,
            windowInfo,
            false);
        RegionCaptureIntegration.LastRegionRectangle = screenRectangle;
        _closing = true;
        Close();
    }

    private SKBitmap? GetSnapshot(Rect sourceRectangle)
    {
        if (_request?.Screenshot == null)
        {
            return null;
        }

        SKBitmap sourceImage = _request.Screenshot;
        int left = Math.Clamp((int)Math.Floor(sourceRectangle.Left), 0, sourceImage.Width - 1);
        int top = Math.Clamp((int)Math.Floor(sourceRectangle.Top), 0, sourceImage.Height - 1);
        int right = Math.Clamp((int)Math.Ceiling(sourceRectangle.Right), left + 1, sourceImage.Width);
        int bottom = Math.Clamp((int)Math.Ceiling(sourceRectangle.Bottom), top + 1, sourceImage.Height);

        int width = right - left;
        int height = bottom - top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        SKBitmap output = new(new SKImageInfo(width, height, sourceImage.ColorType, sourceImage.AlphaType));
        using SKCanvas canvas = new(output);
        canvas.DrawBitmap(sourceImage, new SKRect(left, top, right, bottom), new SKRect(0, 0, width, height));
        return output;
    }

    private WindowInfo? FindTopLevelWindowInfo(DrawingRectangle selectedRectangle)
    {
        if (_selectedCandidate is { IsWindow: true })
        {
            return _selectedCandidate.WindowInfo;
        }

        DrawingPoint point = new DrawingPoint(
            selectedRectangle.Left + selectedRectangle.Width / 2,
            selectedRectangle.Top + selectedRectangle.Height / 2);
        return _windows.FirstOrDefault(window => window.IsWindow && window.Rectangle.Contains(point))?.WindowInfo;
    }

    private void CancelCapture()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _pendingResult = null;
        Close();
    }

    private bool HasValidSelection()
    {
        if (_request == null)
        {
            return false;
        }

        Rect selection = _regionOverlay.SelectionRectangle;
        return selection.Width >= RegionCaptureOptions.MinimumSize &&
            selection.Height >= RegionCaptureOptions.MinimumSize;
    }

    private Point ClampPoint(Point point)
    {
        Size size = GetImageSize();
        return new Point(
            Math.Clamp(point.X, 0, size.Width),
            Math.Clamp(point.Y, 0, size.Height));
    }

    private Size GetImageSize()
    {
        return _request == null
            ? default
            : new Size(_imageWidth, _imageHeight);
    }

    private void ConfigureInitialPixelBounds()
    {
        if (_request == null)
        {
            return;
        }

        Position = new PixelPoint(_request.ScreenBounds.X, _request.ScreenBounds.Y);
        ApplyPixelSize();
    }

    private void ApplyPixelSize()
    {
        if (_request == null)
        {
            return;
        }

        double scaling = double.IsFinite(RenderScaling) && RenderScaling > 0 ? RenderScaling : 1;
        Width = _request.ScreenBounds.Width / scaling;
        Height = _request.ScreenBounds.Height / scaling;
    }

    private void UpdatePixelTransforms()
    {
        double scaling = double.IsFinite(RenderScaling) && RenderScaling > 0 ? RenderScaling : 1;
        _regionTransform.LayoutTransform = new ScaleTransform(1 / scaling, 1 / scaling);
        _regionInputSurface.Cursor = CursorAssetLoader.GetCrosshairCursor(scaling);
        foreach (Border node in _regionResizeNodes.Values)
        {
            node.Cursor = CursorAssetLoader.GetOpenHandCursor(scaling);
        }
    }

    private double GetInitialScaling()
    {
        if (_request == null)
        {
            return 1;
        }

        PixelPoint topLeft = new PixelPoint(_request.ScreenBounds.X, _request.ScreenBounds.Y);
        return Screens.ScreenFromPoint(topLeft)?.Scaling ?? Screens.Primary?.Scaling ?? 1;
    }

    private static byte GetDimAlpha(RegionCaptureOptions options)
    {
        if (options.BackgroundDimStrength <= 0)
        {
            return 0;
        }

        return (byte)Math.Clamp((int)Math.Round(options.BackgroundDimStrength / 100d * 255), 0, 255);
    }

    private static double Distance(Point first, Point second)
    {
        double dx = second.X - first.X;
        double dy = second.Y - first.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private enum RegionInteraction
    {
        None,
        PendingHover,
        Creating,
        Moving,
        Resizing
    }
}
