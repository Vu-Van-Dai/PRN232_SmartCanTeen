using PdfSharpCore.Fonts;

namespace API.Services;

/// <summary>
/// Basic font resolver to support Vietnamese characters in generated PDFs.
/// Uses Segoe UI from Windows fonts folder when available.
/// </summary>
public sealed class PdfFontResolver : IFontResolver
{
    public string DefaultFontName => "Segoe UI";

    private static readonly string FontsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

    private static readonly string RegularPath = Path.Combine(FontsDir, "segoeui.ttf");
    private static readonly string BoldPath = Path.Combine(FontsDir, "segoeuib.ttf");
    private static readonly string ItalicPath = Path.Combine(FontsDir, "segoeuii.ttf");
    private static readonly string BoldItalicPath = Path.Combine(FontsDir, "segoeuiz.ttf");

    public byte[] GetFont(string faceName)
    {
        var path = faceName switch
        {
            "SegoeUI#" => RegularPath,
            "SegoeUI#b" => BoldPath,
            "SegoeUI#i" => ItalicPath,
            "SegoeUI#bi" => BoldItalicPath,
            _ => RegularPath,
        };

        if (!File.Exists(path))
            throw new FileNotFoundException($"Font file not found: {path}");

        return File.ReadAllBytes(path);
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        // Map any requested family to Segoe UI (good Unicode coverage on Windows)
        if (isBold && isItalic) return new FontResolverInfo("SegoeUI#bi");
        if (isBold) return new FontResolverInfo("SegoeUI#b");
        if (isItalic) return new FontResolverInfo("SegoeUI#i");
        return new FontResolverInfo("SegoeUI#");
    }

}
