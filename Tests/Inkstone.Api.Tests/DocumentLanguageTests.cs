using System.Text;
using Inkstone.Web.Services;

namespace Inkstone.Api.Tests;

public sealed class DocumentLanguageTests
{
    [Theory]
    [InlineData("Hello", "en-US")]
    [InlineData("نامه فارسی", "fa-IR")]
    [InlineData("اردو ٹیسٹ", "ur-PK")]
    [InlineData("مرحبا بكم", "ar-SA")]
    [InlineData("", "en-US")]
    public void Text_script_selects_the_language(string text, string language) => Assert.Equal(language, DocumentLanguage.FromText(text));

    [Fact]
    public void File_tags_select_the_language_and_text_overrides_a_plain_english_default()
    {
        var persian = """{"styles":{"defaultChr":{"lang":"fa-IR"}},"body":[{"text":"سلام"}]}"""u8.ToArray();
        Assert.Equal("fa-IR", DocumentLanguage.FromModel(persian));
        var written = """{"styles":{"defaultChr":{"lang":"en-US"}},"body":[{"text":"متن فارسی"}]}"""u8.ToArray();
        Assert.Equal("fa-IR", DocumentLanguage.FromModel(written));
        Assert.Equal("en-US", DocumentLanguage.FromModel([]));
    }

    [Fact]
    public void A_letter_follows_its_body_file_before_the_template()
    {
        var body = Encoding.UTF8.GetBytes("""{"body":[{"text":"English body"}]}""");
        var template = Encoding.UTF8.GetBytes("""{"styles":{"defaultChr":{"lang":"fa-IR"}},"body":[{"text":"فارسی"}]}""");
        Assert.Equal("en-US", DocumentLanguage.ForLetter(null, body, template, null, "موضوع", "نامه"));
        Assert.Equal("fa-IR", DocumentLanguage.ForLetter(null, null, template, null, "Subject", "Letter"));
        Assert.Equal("fa-IR", DocumentLanguage.ForLetter(null, null, null, "متن نامه", "Subject", "Letter"));
    }
}
