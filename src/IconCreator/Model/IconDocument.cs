using System.ComponentModel;

namespace IconCreator.Model;

/// <summary>One resolution slice of an icon (e.g. 32×32).</summary>
public sealed class IconSlice
{
    public int Size { get; }
    public PixelBuffer Buffer { get; }
    public bool IncludeInExport { get; set; } = true;

    public IconSlice(int size)
    {
        Size = size;
        Buffer = new PixelBuffer(size, size);
    }

    public string Label => $"{Size} × {Size}";
}

/// <summary>A multi-resolution icon project.</summary>
public sealed class IconDocument : INotifyPropertyChanged
{
    public static readonly int[] StandardSizes = { 16, 24, 32, 48, 64, 128, 256 };

    public List<IconSlice> Slices { get; } = new();

    private string? _filePath;
    public string? FilePath
    {
        get => _filePath;
        set { _filePath = value; OnChanged(nameof(FilePath)); OnChanged(nameof(Title)); }
    }

    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        set { _isDirty = value; OnChanged(nameof(IsDirty)); OnChanged(nameof(Title)); }
    }

    public string Title
    {
        get
        {
            string name = FilePath is null ? "Untitled" : System.IO.Path.GetFileName(FilePath);
            return (_isDirty ? "● " : "") + name + "  —  IconCreator Studio";
        }
    }

    public IconDocument(IEnumerable<int> sizes)
    {
        foreach (var s in sizes.Distinct().OrderBy(x => x))
            Slices.Add(new IconSlice(s));
    }

    public static IconDocument CreateDefault() => new(StandardSizes);

    public IconSlice? Find(int size) => Slices.FirstOrDefault(s => s.Size == size);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}
