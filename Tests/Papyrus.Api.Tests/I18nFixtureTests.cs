using Papyrus.Web.Services;

namespace Papyrus.Api.Tests;

public sealed class I18nFixtureTests
{
    private static string Fixture(string name)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "i18n", name));

    public static TheoryData<string, string> Models => new()
    {
        { "en-us-basic.json", DocumentLanguage.English },
        { "fa-ir-basic.json", DocumentLanguage.Persian },
        { "ar-sa-basic.json", DocumentLanguage.Arabic },
        { "ur-pk-basic.json", DocumentLanguage.Urdu },
        { "mixed-bidi.json", DocumentLanguage.Persian },
    };

    [Theory]
    [MemberData(nameof(Models))]
    public void Fixture_models_resolve_document_language(string file, string expected)
    {
        var bytes = File.ReadAllBytes(Fixture(file));
        Assert.Equal(expected, DocumentLanguage.FromModel(bytes));
    }

    [Fact]
    public void Fixture_text_file_splits_scripts_for_detection()
    {
        var text = File.ReadAllText(Fixture("mixed-scripts.txt"));
        Assert.Equal(DocumentLanguage.Persian, DocumentLanguage.FromText(text.Split('\n')[0]));
        Assert.Equal(DocumentLanguage.Arabic, DocumentLanguage.FromText(text.Split('\n')[1]));
        Assert.Equal(DocumentLanguage.Urdu, DocumentLanguage.FromText(text.Split('\n')[2]));
    }
}