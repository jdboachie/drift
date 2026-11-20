# compare.ps1 -Before a.png -After b.png [-Region x,y,w,h] [-Diff diff.png] : compares two captures of the demo by their
# edges (text, icons, borders, fills' outlines), not raw colours: the window's acrylic backdrop shows whatever is
# behind the window, so raw colours change between captures even when nothing in Drift did. Prints how many edge
# pixels differ, and writes an image with them in red over a faded copy of the "after" capture.
param([string]$Before, [string]$After, [string]$Region = '', [string]$Diff = '')
Add-Type -AssemblyName System.Drawing
if (-not ('EdgeDiff' -as [type])) {
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices;
public static class EdgeDiff {
  static int[] Lum(Bitmap b, Rectangle r) {
    BitmapData d = b.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    int[] px = new int[r.Width * r.Height];
    for (int y = 0; y < r.Height; y++) Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), px, y * r.Width, r.Width);
    b.UnlockBits(d);
    int[] l = new int[px.Length];
    for (int i = 0; i < px.Length; i++) { int c = px[i]; l[i] = (((c >> 16) & 255) * 3 + ((c >> 8) & 255) * 6 + (c & 255)) / 10; }
    return l;
  }
  static bool[] Edges(int[] l, int w, int h) {
    bool[] e = new bool[l.Length];
    for (int y = 0; y < h - 1; y++) for (int x = 0; x < w - 1; x++) {
      int i = y * w + x; e[i] = Math.Abs(l[i] - l[i + 1]) + Math.Abs(l[i] - l[i + w]) > 24;
    }
    return e;
  }
  static bool Near(bool[] e, int i, int w) { return e[i] || e[i - 1] || e[i + 1] || e[i - w] || e[i + w]; }
  public static string Run(string before, string after, int x0, int y0, int w, int h, string diff) {
    using (Bitmap a = new Bitmap(before)) using (Bitmap b = new Bitmap(after)) {
      if (a.Width != b.Width || a.Height != b.Height) return "sizes differ: " + a.Width + "x" + a.Height + " vs " + b.Width + "x" + b.Height;
      if (w == 0) { w = a.Width; h = a.Height; }
      Rectangle r = new Rectangle(x0, y0, w, h);
      int[] lb = Lum(b, r);
      bool[] ea = Edges(Lum(a, r), w, h), eb = Edges(lb, w, h), bad = new bool[lb.Length];
      int n = 0, edges = 0;
      for (int y = 1; y < h - 1; y++) for (int x = 1; x < w - 1; x++) {
        int i = y * w + x;
        if (ea[i] || eb[i]) edges++;
        // A 1px shift in antialiasing isn't a change: an edge counts only if the other image has none nearby.
        if ((ea[i] && !Near(eb, i, w)) || (eb[i] && !Near(ea, i, w))) { bad[i] = true; n++; }
      }
      if (diff.Length > 0) {
        using (Bitmap o = new Bitmap(w, h)) {
          for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) {
            int i = y * w + x, g = (255 + lb[i]) / 2;
            o.SetPixel(x, y, bad[i] ? Color.Red : Color.FromArgb(g, g, g));
          }
          o.Save(diff, ImageFormat.Png);
        }
      }
      return n + " of " + edges + " edge pixels differ";
    }
  }
}
'@
}
$x0, $y0, $w, $h = if ($Region) { $Region.Split(',') | ForEach-Object { [int]$_ } } else { 0, 0, 0, 0 }
[EdgeDiff]::Run((Resolve-Path $Before).Path, (Resolve-Path $After).Path, $x0, $y0, $w, $h, $(if ($Diff) { [IO.Path]::GetFullPath($Diff) } else { '' }))
