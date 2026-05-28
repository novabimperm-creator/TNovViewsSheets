using System.Collections.Generic;

namespace TNovViewsSheets
{
    /// <summary>
    /// Sorts sheet numbers like "A-1, A-2, A-10" instead of "A-1, A-10, A-2".
    /// Compares digit runs as numbers and non-digit runs case-insensitively.
    /// </summary>
    internal class NaturalStringComparer : IComparer<string>
    {
        public static readonly NaturalStringComparer Instance = new NaturalStringComparer();

        public int Compare(string x, string y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int ix = 0, iy = 0;
            while (ix < x.Length && iy < y.Length)
            {
                if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
                {
                    long nx = 0, ny = 0;
                    while (ix < x.Length && char.IsDigit(x[ix])) { nx = nx * 10 + (x[ix] - '0'); ix++; }
                    while (iy < y.Length && char.IsDigit(y[iy])) { ny = ny * 10 + (y[iy] - '0'); iy++; }
                    if (nx != ny) return nx.CompareTo(ny);
                }
                else
                {
                    int c = char.ToUpperInvariant(x[ix]).CompareTo(char.ToUpperInvariant(y[iy]));
                    if (c != 0) return c;
                    ix++; iy++;
                }
            }
            return x.Length.CompareTo(y.Length);
        }
    }
}
