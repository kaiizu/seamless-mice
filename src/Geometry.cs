using System;

namespace SeamlessMice
{
    // All geometry is immutable after construction. The input callback only reads it.
    public struct PixelPoint
    {
        public int X, Y;
        public PixelPoint(int x, int y) { X = x; Y = y; }
    }

    public struct PixelRect
    {
        public int Left, Top, Right, Bottom;
        public PixelRect(int left, int top, int right, int bottom)
        { Left = left; Top = top; Right = right; Bottom = bottom; }
        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
        public bool Contains(PixelPoint p)
        { return p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom; }
    }

    public sealed class Panel
    {
        public readonly string Id, Name;
        public readonly PixelRect Pixels;
        public readonly double WidthMm, HeightMm, TopMm, LeftMm, MmPerPixelX, MmPerPixelY;
        public Panel(string id, string name, PixelRect pixels, double widthMm, double heightMm, double topMm, double leftMm = Double.NaN)
        {
            if (pixels.Width <= 1 || pixels.Height <= 1 || !Finite(widthMm) || !Finite(heightMm) ||
                !Finite(topMm) || widthMm < 50 || heightMm < 50 || widthMm > 5000 || heightMm > 5000 ||
                Math.Abs(topMm) > 10000 || (!Double.IsNaN(leftMm) && (!Finite(leftMm) || Math.Abs(leftMm) > 10000))) throw new ArgumentException("Invalid screen dimensions or position.");
            Id = id; Name = name; Pixels = pixels; WidthMm = widthMm; HeightMm = heightMm; TopMm = topMm; LeftMm = leftMm;
            MmPerPixelX = widthMm / pixels.Width; MmPerPixelY = heightMm / pixels.Height;
        }
        internal static bool Finite(double x) { return !Double.IsNaN(x) && !Double.IsInfinity(x); }
        public static double WidthFromDiagonal(double inches, PixelRect pixels)
        {
            double w = pixels.Width, h = pixels.Height;
            return inches * 25.4 * w / Math.Sqrt(w * w + h * h);
        }
        public static double HeightFromDiagonal(double inches, PixelRect pixels)
        {
            double w = pixels.Width, h = pixels.Height;
            return inches * 25.4 * h / Math.Sqrt(w * w + h * h);
        }
    }

    public struct Transition
    {
        public bool Reposition;
        public int Screen;
        public PixelPoint Point;
        public Transition(bool reposition, int screen, PixelPoint point)
        { Reposition = reposition; Screen = screen; Point = point; }
    }

    public sealed class Layout
    {
        public readonly Panel[] Panels;
        public Layout(Panel[] panels)
        {
            if (panels == null || panels.Length == 0 || panels.Length > 16)
                throw new ArgumentException("Expected between one and sixteen screens.");
            Panels = (Panel[])panels.Clone();
            Array.Sort(Panels, delegate(Panel a, Panel b) { return a.Pixels.Left.CompareTo(b.Pixels.Left); });
            double left = 0;
            for (int i = 0; i < Panels.Length; i++)
            {
                Panel p = Panels[i];
                if (Double.IsNaN(p.LeftMm)) Panels[i] = new Panel(p.Id, p.Name, p.Pixels, p.WidthMm, p.HeightMm, p.TopMm, left);
                left = Panels[i].LeftMm + p.WidthMm;
                for (int j = 0; j < i; j++)
                {
                    Panel q = Panels[j], a = Panels[i];
                    if (Math.Min(a.LeftMm + a.WidthMm, q.LeftMm + q.WidthMm) - Math.Max(a.LeftMm, q.LeftMm) > 0.01 &&
                        Math.Min(a.TopMm + a.HeightMm, q.TopMm + q.HeightMm) - Math.Max(a.TopMm, q.TopMm) > 0.01)
                        throw new ArgumentException("The screen rectangles overlap. Drag them apart before applying.");
                    if (a.Id == q.Id) throw new ArgumentException("Duplicate screen identity.");
                }
            }
        }
        public int Find(PixelPoint p)
        {
            for (int i = 0; i < Panels.Length; i++) if (Panels[i].Pixels.Contains(p)) return i;
            return -1;
        }

        // No allocations, OS calls, locks or IO. Rectangles use exclusive right/bottom edges.
        public Transition Move(int source, PixelPoint proposed)
        {
            if (source < 0 || source >= Panels.Length) return new Transition(false, Find(proposed), proposed);
            Panel from = Panels[source];
            PixelRect bounds = from.Pixels;
            if (bounds.Contains(proposed)) return new Transition(false, source, proposed);
            int direction = proposed.X < bounds.Left ? 0 : proposed.X >= bounds.Right ? 1 : proposed.Y < bounds.Top ? 2 : 3;
            bool horizontal = direction < 2;
            double orthogonal = horizontal ? from.TopMm + ((double)proposed.Y - bounds.Top) * from.MmPerPixelY : from.LeftMm + ((double)proposed.X - bounds.Left) * from.MmPerPixelX;
            int next = -1; double nearest = Double.MaxValue;
            for (int i = 0; i < Panels.Length; i++)
            {
                if (i == source) continue;
                Panel candidate = Panels[i];
                double start = horizontal ? candidate.TopMm : candidate.LeftMm;
                double extent = horizontal ? candidate.HeightMm : candidate.WidthMm;
                if (orthogonal < start || orthogonal >= start + extent) continue;
                double gap = direction == 0 ? from.LeftMm - candidate.LeftMm - candidate.WidthMm :
                    direction == 1 ? candidate.LeftMm - from.LeftMm - from.WidthMm :
                    direction == 2 ? from.TopMm - candidate.TopMm - candidate.HeightMm : candidate.TopMm - from.TopMm - from.HeightMm;
                if (gap >= -0.01 && gap < nearest) { nearest = gap; next = i; }
            }
            if (next < 0)
            {
                PixelPoint blocked = new PixelPoint(Clamp(proposed.X, bounds.Left, bounds.Right - 1), Clamp(proposed.Y, bounds.Top, bounds.Bottom - 1));
                return new Transition(blocked.X != proposed.X || blocked.Y != proposed.Y, source, blocked);
            }
            Panel to = Panels[next];
            double overshoot = direction == 0 ? (double)bounds.Left - 1 - proposed.X : direction == 1 ? (double)proposed.X - bounds.Right : direction == 2 ? (double)bounds.Top - 1 - proposed.Y : (double)proposed.Y - bounds.Bottom;
            double scale = horizontal ? from.MmPerPixelX / to.MmPerPixelX : from.MmPerPixelY / to.MmPerPixelY;
            int step = (int)Math.Min((horizontal ? to.Pixels.Width : to.Pixels.Height) - 1, Math.Max(0, overshoot * scale));
            int x = horizontal ? (direction == 1 ? to.Pixels.Left + step : to.Pixels.Right - 1 - step) : Clamp((int)Math.Floor(to.Pixels.Left + (orthogonal - to.LeftMm) / to.MmPerPixelX), to.Pixels.Left, to.Pixels.Right - 1);
            int y = horizontal ? Clamp((int)Math.Floor(to.Pixels.Top + (orthogonal - to.TopMm) / to.MmPerPixelY), to.Pixels.Top, to.Pixels.Bottom - 1) : (direction == 3 ? to.Pixels.Top + step : to.Pixels.Bottom - 1 - step);
            return new Transition(true, next, new PixelPoint(x, y));
        }
        public static int Clamp(int value, int min, int max) { return value < min ? min : value > max ? max : value; }
    }
}
