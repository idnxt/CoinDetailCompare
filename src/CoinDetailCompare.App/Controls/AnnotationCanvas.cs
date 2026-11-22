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
    private AnnotationTool _tool = AnnotationTool.Path;
    private string _color = "#FFFF3030";

    public AnnotationCanvas()
    {
        Focusable = true;
    }

    /// <summary>Items to render + edit.</summary>
    public ObservableCollection<AnnotationItem> Items { get; } = new();

    public AnnotationTool Tool
    {
        get => _tool;
        set { _tool = value; InvalidateVisual(); }
    }

    public string Color
    {
        get => _color;
        set { _color = value; _pen.Brush = Parse(value); InvalidateVisual(); }
    }

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
        var c = (Color)ColorConverter.ConvertFromString(hex) ?? Colors.Red;
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
            var head = new PointCollection { p1, b, p2 };
            dc.DrawGeometry(null, _pen, head);
            return;
        }

        // path: polyline
        if (pts.Count >= 2)
            dc.DrawLines(_pen, pts);
    }

    private Point Norm(Point p) => new(p.X / ActualWidth, p.Y / ActualHeight);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var p = Norm(e.GetPosition(this));
        ShapeStarted?.Invoke();
        _draft = new AnnotationItem { Color = _color, ZOrder = Items.Count };
        _draft.Shape = _tool switch
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
        switch (_tool)
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
