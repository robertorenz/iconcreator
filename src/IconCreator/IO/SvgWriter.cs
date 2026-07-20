using System.IO;
using System.Text;
using IconCreator.Vector;

namespace IconCreator.IO;

/// <summary>Serialises authored <see cref="VShape"/>s to a standalone SVG document.</summary>
public static class SvgWriter
{
    public static string Build(int size, IEnumerable<VShape> shapes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" " +
                      $"width=\"{size}\" height=\"{size}\" viewBox=\"0 0 {size} {size}\">");
        foreach (var shape in shapes)
            sb.AppendLine("  " + shape.ToSvg());
        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    public static void Save(string path, int size, IEnumerable<VShape> shapes)
        => File.WriteAllText(path, Build(size, shapes));
}
