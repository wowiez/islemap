using System.IO;
using System.Windows.Media;
using System.Xml.Linq;

namespace TheIsleOverlay.App.Tests;

/// <summary>
/// The bundled brand font (Lexend) must resolve to four real weights, be hinted
/// for small Windows text, keep a readable word space and cover Vietnamese, so no
/// word ever mixes two typefaces (Outfit lacked tone marks and smeared at 9-11px).
/// </summary>
public sealed class BrandFontTests
{
    private static string Asset(params string[] parts) => Path.Combine([AppContext.BaseDirectory, "TestAssets", .. parts]);

    private static GlyphTypeface[] Faces() =>
        Directory.GetFiles(Asset("Fonts"), "*.ttf").Select(file => new GlyphTypeface(new Uri(file))).ToArray();

    [Fact]
    public void BrandFont_ShipsFourStaticWeightsOfTheFamilyAppXamlNames()
    {
        var faces = Faces();
        Assert.Equal(new[] { 400, 500, 600, 700 }, faces.Select(face => face.Weight.ToOpenTypeWeight()).Order().ToArray());
        // WPF groups the four files by their typographic family name; App.xaml must ask for it.
        Assert.All(faces, face => Assert.Contains("Lexend", face.FamilyNames.Values));
        Assert.Contains("./Assets/Fonts/#Lexend,", File.ReadAllText(Asset("App.xaml")));
        Assert.All(faces, face => Assert.Equal(StyleSimulations.None, face.StyleSimulations));
        Assert.All(faces, face => Assert.InRange(face.AdvanceWidths[face.CharacterToGlyphMap[' ']], 0.24, 0.32));
    }

    [Fact]
    public void BrandFont_IsHintedSoSmallTextStaysCrisp()
    {
        // Google's variable sources ship without TrueType hinting; the bundled
        // static weights are ttfautohint-ed so 9-11px HUD text does not smear.
        foreach (var file in Directory.GetFiles(Asset("Fonts"), "*.ttf"))
        {
            var bytes = File.ReadAllBytes(file);
            var tables = Enumerable.Range(0, (bytes[4] << 8) | bytes[5])
                .Select(index => System.Text.Encoding.ASCII.GetString(bytes, 12 + index * 16, 4))
                .ToHashSet();
            Assert.True(tables.IsSupersetOf(["fpgm", "prep"]), $"{Path.GetFileName(file)} is unhinted");
        }
    }

    [Fact]
    public void BrandFont_CoversVietnameseAndEveryCharacterInTheWindows()
    {
        const string vietnamese = "aàảãáạăằẳẵắặâầẩẫấậbcdđeèẻẽéẹêềểễếệghiìỉĩíịklmnoòỏõóọôồổỗốộơờởỡớợpqrstuùủũúụưừửữứựvxyỳỷỹýỵ";
        var required = new HashSet<char>(vietnamese + vietnamese.ToUpperInvariant() + "0123456789·—–%&+/#");
        XName[] textAttributes = ["Text", "Content", "Header", "Title"];
        foreach (var file in new[] { "HomeWindow.xaml", "GuideWindow.xaml", "MainWindow.xaml", "LargeMapWindow.xaml", "LoginWindow.xaml", "UpdateAvailableWindow.xaml", "IslePilotSteamLoginWindow.xaml" })
            foreach (var value in XDocument.Load(Asset(file)).Descendants().SelectMany(e => textAttributes.Select(a => (string?)e.Attribute(a))))
                if (value is not null && !value.StartsWith('{'))
                    foreach (var c in value.Where(c => char.IsLetterOrDigit(c) || char.IsPunctuation(c))) required.Add(c);
        foreach (var face in Faces())
        {
            var missing = required.Where(c => !face.CharacterToGlyphMap.ContainsKey(c)).ToArray();
            Assert.True(missing.Length == 0, $"{Path.GetFileName(face.FontUri.LocalPath)} lacks: {new string(missing)}");
        }
    }
}
