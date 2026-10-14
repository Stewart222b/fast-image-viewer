using System;
using System.Collections.Generic;
using System.IO;

namespace FastImageViewer
{
    public static class ImageFiles
    {
        // Unix volumes can contain names that differ only by case, including macOS.
        public static readonly StringComparer PathComparer = Path.DirectorySeparatorChar == '\\'
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        public const string DialogFilter = "图片文件|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.wdp;*.jxr|所有文件|*.*";
        private static readonly HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".wdp", ".jxr"
        };

        public static bool IsSupported(string path)
        {
            return !String.IsNullOrWhiteSpace(path) && Extensions.Contains(System.IO.Path.GetExtension(path));
        }

        public static List<string> InDirectory(string path)
        {
            string directory = Directory.Exists(path) ? path : System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            return FromPaths(Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly));
        }

        public static List<string> FromPaths(IEnumerable<string> paths)
        {
            if (paths == null) throw new ArgumentNullException("paths");
            var results = new List<string>();
            var seen = new HashSet<string>(PathComparer);
            foreach (string path in paths)
            {
                if (String.IsNullOrWhiteSpace(path)) continue;
                string fullPath = System.IO.Path.GetFullPath(path);
                if (Directory.Exists(fullPath))
                {
                    foreach (string image in InDirectory(fullPath))
                        if (seen.Add(image)) results.Add(image);
                }
                else if (IsSupported(fullPath) && seen.Add(fullPath)) results.Add(fullPath);
            }
            results.Sort(ComparePaths);
            return results;
        }

        private static int ComparePaths(string left, string right)
        {
            string a = System.IO.Path.GetFileName(left), b = System.IO.Path.GetFileName(right);
            // Use one consistent ordering for every pair, including very long numbers.
            int result = NaturalCompare(a, b);
            if (result != 0) return result;
            result = StringComparer.OrdinalIgnoreCase.Compare(left, right);
            return result != 0 ? result : StringComparer.Ordinal.Compare(left, right);
        }

        // Compare numeric runs without parsing integers, including arbitrarily long names.
        private static int NaturalCompare(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (a[i] >= '0' && a[i] <= '9' && b[j] >= '0' && b[j] <= '9')
                {
                    int endA = i, endB = j;
                    while (endA < a.Length && a[endA] >= '0' && a[endA] <= '9') endA++;
                    while (endB < b.Length && b[endB] >= '0' && b[endB] <= '9') endB++;
                    while (i < endA && a[i] == '0') i++;
                    while (j < endB && b[j] == '0') j++;
                    int size = (endA - i).CompareTo(endB - j);
                    if (size != 0) return size;
                    while (i < endA)
                    {
                        int digit = a[i++].CompareTo(b[j++]);
                        if (digit != 0) return digit;
                    }
                    i = endA; j = endB;
                }
                else
                {
                    int letter = Char.ToUpperInvariant(a[i++]).CompareTo(Char.ToUpperInvariant(b[j++]));
                    if (letter != 0) return letter;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
}
