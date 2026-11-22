using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CoinDetailCompare.App.Models;

namespace CoinDetailCompare.App.Controls;

/// <summary>
/// Draw-over-image canvas for the vector annotation layer.
/// Items are stored in normalized 0..1 coordinates; rendering scales
/// them to the current render size. Handles mouse drawing of the
/// currently selected tool.
/// </summary>
public class AnnotationCanvas : FrameworkElement
{
    private readonly Pen _pen = new() { Thickness = 2 };
    private AnnotationItem? _draft;
    private ObservableCollection<AnnotationItem> _items;

    public AnnotationCanvas()
    {
        Focusable = true;
        _items = new ObservableCollection<AnnotationItem>();
        _items.CollectionChanged += OnItemsCollectionChanged;
    }

    /// <summary>Items to render + edit. XAML may bind this to a view-model collection.</summary>
    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(nameof(Items), typeof(ObservableCollection<AnnotationItem>), typeof(AnnotationCanvas),
            new PropertyMetadata(OnItemsChanged));

    public static readonly DependencyProperty ToolProperty =
        DependencyProperty.Register(nameof(Tool), typeof(AnnotationTool), typeof(AnnotationCanvas),
            new PropertyMetadata(AnnotationTool.Path, OnInvalidate));

    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(nameof(Color), typeof(string), typeof(AnnotationCanvas),
            new PropertyMetadata("#FFFF3030", OnColorChanged));

    public ObservableCollection<AnnotationItem> Items
    {
        get => GetValue(ItemsProperty) as ObservableCollection<AnnotationItem> ?? _items;
        set => SetValue(ItemsProperty, value);
    }

    public AnnotationTool Tool
    {
        get => (AnnotationTool)GetValue(ToolProperty);
        set => SetValue(ToolProperty, value);
    }

    public string Color
    {
        get => (string)GetValue(ColorProperty) ?? string.Empty;
        set => SetValue(ColorProperty, value);
    }

    private static void OnInvalidate(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((AnnotationCanvas)d).InvalidateVisual();

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (AnnotationCanvas)d;
        if (e.NewValue is string s && !string.IsNullOrEmpty(s))
            c._pen.Brush = Parse(s);
        c.InvalidateVisual();
    }

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (AnnotationCanvas)d;
        if (ReferenceEquals(e.OldValue, c._items) && e.OldValue is ObservableCollection<AnnotationItem> oldColl)
        {
            oldColl.CollectionChanged -= c.OnItemsCollectionChanged;
            c._items = new ObservableCollection<AnnotationItem>();
            c._items.CollectionChanged += c.OnItemsCollectionChanged;
        }
        if (e.NewValue is ObservableCollection<AnnotationItem> newColl)
        {
            c._items = newColl;
            newColl.CollectionChanged += c.OnItemsCollectionChanged;
        }
        c.InvalidateVisual();
    }

    private void OnItemsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => InvalidateVisual();

    /// <summary>Raised when the user finishes a shape (before it is committed to Items).</summary>
    public event Action<AnnotationItem>? ShapeFinished;
    /// <summary>Raised when a shape starts (for undo grouping, etc.).</summary>
    public event Action? ShapeStarted;

    public void ClearAll()
    {
        Items.Clear();
        InvalidateVisual();
    }

    public void RemoveLast()
    {
        if (Items.Count > 0)
        {
            Items.RemoveAt(Items.Count - 1);
            InvalidateVisual();
        }
    }

    private static Brush Parse(string hex)
    {
        var c = ColorConverter.ConvertFromString(hex) is Color cc ? cc : Colors.Red;
        return new SolidColorBrush(c);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        foreach (var item in Items)
            RenderItem(dc, item, w, h);
        if (_draft != null)
            RenderItem(dc, _draft, w, h);
    }

    private void RenderItem(DrawingContext dc, AnnotationItem item, double w, double h)
    {
        _pen.Brush = Parse(item.Color);
        if (item.Points.Count < 2) return;
        var pts = new PointCollection();
        for (int i = 0; i + 1 < item.Points.Count; i += 2)
            pts.Add(new Point(item.Points[i] * w, item.Points[i + 1] * h));

        if (item.Shape == "ellipse" && pts.Count >= 2)
        {
            var min = new Point(Math.Min(pts[0].X, pts[1].X), Math.Min(pts[0].Y, pts[1].Y));
            var max = new Point(Math.Max(pts[0].X, pts[1].X), Math.Max(pts[0].Y, pts[1].Y));
            dc.DrawEllipse(null, _pen, new Point((min.X + max.X) / 2, (min.Y + max.Y) / 2),
                (max.X - min.X) / 2, (max.Y - min.Y) / 2);
            return;
        }

        if (item.Shape == "arrow" && pts.Count >= 2)
        {
            var a = pts[0];
            var b = pts[^1];
            dc.DrawLine(_pen, a, b);
            // arrowhead
            double angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
            const double len = 14, spread = 0.4;
            var p1 = new Point(b.X - len * Math.Cos(angle - spread), b.Y - len * Math.Sin(angle - spread));
            var p2 = new Point(b.X - len * Math.Cos(angle + spread), b.Y - len * Math.Sin(angle + spread));
            var fig = new PathFigure { StartPoint = p1 };
            fig.Segments.Add(new LineSegment(b, true));
            fig.Segments.Add(new LineSegment(p2, true));
            var head = new PathGeometry(new[] { fig });
            dc.DrawGeometry(null, _pen, head);
            return;
        }

        // path: polyline
        for (int i = 0; i + 1 < pts.Count; i++)
            dc.DrawLine(_pen, pts[i], pts[i + 1]);
    }

    private Point Norm(Point p) => new(p.X / ActualWidth, p.Y / ActualHeight);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var p = Norm(e.GetPosition(this));
        ShapeStarted?.Invoke();
        _draft = new AnnotationItem { Color = Color, ZOrder = Items.Count };
        _draft.Shape = Tool switch
        {
            AnnotationTool.Ellipse => "ellipse",
            AnnotationTool.Arrow => "arrow",
            _ => "path",
        };
        _draft.Points.Add((float)p.X);
        _draft.Points.Add((float)p.Y);
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_draft == null) return;
        var p = Norm(e.GetPosition(this));
        switch (Tool)
        {
            case AnnotationTool.Path:
                _draft.Points.Add((float)p.X);
                _draft.Points.Add((float)p.Y);
                break;
            default: // ellipse / arrow: single moving endpoint
                _draft.Points[1] = (float)p.X;
                if (_draft.Points.Count >= 4)
                    _draft.Points[3] = (float)p.Y;
                break;
        }
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_draft == null) return;
        var p = Norm(e.GetPosition(this));
        if (_draft.Shape != "path")
        {
            _draft.Points[1] = (float)p.X;
            _draft.Points.Add((float)p.Y);
        }
        else
        {
            _draft.Points.Add((float)p.X);
            _draft.Points.Add((float)p.Y);
        }
        Items.Add(_draft);
        ShapeFinished?.Invoke(_draft);
        _draft = null;
        InvalidateVisual();
    }
}
