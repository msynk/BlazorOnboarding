using System.Globalization;
using System.Text.Json.Serialization;

namespace BlazorOnboarding;

/// <summary>An axis-aligned rectangle in CSS viewport pixels.</summary>
public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public static readonly Rect Empty = default;

    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + (Width / 2);
    public double CenterY => Y + (Height / 2);
    public double Area => Width * Height;

    [JsonIgnore]
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static Rect FromEdges(double left, double top, double right, double bottom)
        => new(left, top, right - left, bottom - top);

    /// <summary>Grows the rectangle by <paramref name="amount"/> on every side (negative shrinks).</summary>
    public Rect Inflate(double amount) => Inflate(amount, amount);

    /// <summary>Grows the rectangle horizontally and vertically (negative shrinks).</summary>
    public Rect Inflate(double x, double y) => new(X - x, Y - y, Width + (2 * x), Height + (2 * y));

    /// <summary>Translates the rectangle.</summary>
    public Rect Offset(double dx, double dy) => new(X + dx, Y + dy, Width, Height);

    public bool Contains(double px, double py) => px >= Left && px <= Right && py >= Top && py <= Bottom;

    public bool Contains(Rect other) => other.Left >= Left && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom;

    public bool IntersectsWith(Rect other) => other.Left < Right && other.Right > Left && other.Top < Bottom && other.Bottom > Top;

    /// <summary>The overlapping region, or <see cref="Empty"/> when the rectangles are disjoint.</summary>
    public Rect Intersect(Rect other)
    {
        var left = Math.Max(Left, other.Left);
        var top = Math.Max(Top, other.Top);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top ? Empty : FromEdges(left, top, right, bottom);
    }

    /// <summary>The smallest rectangle containing both.</summary>
    public Rect Union(Rect other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        return FromEdges(Math.Min(Left, other.Left), Math.Min(Top, other.Top), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
    }

    /// <summary>True when both rectangles agree to within <paramref name="tolerance"/> device pixels on every edge.</summary>
    public bool ApproximatelyEquals(Rect other, double tolerance = 0.5)
        => Math.Abs(X - other.X) <= tolerance
        && Math.Abs(Y - other.Y) <= tolerance
        && Math.Abs(Width - other.Width) <= tolerance
        && Math.Abs(Height - other.Height) <= tolerance;

    /// <summary>Rounds every component to <paramref name="digits"/> decimals; keeps generated CSS stable.</summary>
    public Rect Round(int digits = 2)
        => new(Math.Round(X, digits), Math.Round(Y, digits), Math.Round(Width, digits), Math.Round(Height, digits));

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"Rect({X:0.##}, {Y:0.##}, {Width:0.##}x{Height:0.##})");
}

/// <summary>A width/height pair in CSS pixels.</summary>
public readonly record struct Size(double Width, double Height)
{
    public static readonly Size Empty = default;

    [JsonIgnore]
    public bool IsEmpty => Width <= 0 || Height <= 0;
}
