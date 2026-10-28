using System.Text.Json;
using Inkstone.Web.Services;

namespace Inkstone.Api.Tests;

public sealed class OperationValidationTests
{
    private static JsonElement Json(string value) => JsonSerializer.Deserialize<JsonElement>(value);

    public static IEnumerable<object[]> InvalidOperations()
    {
        foreach (var json in new[] {
            "null", "[]", "{}", "{\"type\":\"execute\"}",
            """{"type":"replaceText","find":"x","replacement":"y","expectedMatches":-1}""",
            """{"type":"replaceText","find":"x","replacement":"y","expectedMatches":"1"}""",
            """{"type":"replaceText","find":"x","replacement":"y","expectedMatch":1}""",
            """{"type":"replaceText","find":"","replacement":"y"}""",
            """{"type":"replaceText","find":"x","replacement":"y","wholeWord":"true"}""",
            """{"type":"replaceText","find":"x","replacement":3}""",
            """{"type":"replaceText","find":"x","replacement":"y","scope":"headers"}""",
            """{"type":"replaceRange","target":{},"text":"x"}""",
            """{"type":"replaceRange","target":{"ref":"b.0","start":-1},"text":"x"}""",
            """{"type":"replaceRange","target":{"ref":"b.0","start":4,"end":2},"text":"x"}""",
            """{"type":"replaceRange","target":{"ref":"b.0","bookmark":"Name"},"text":"x"}""",
            """{"type":"replaceRange","target":{"ref":"b.0","strat":0},"text":"x"}""",
            """{"type":"insertText","target":{"ref":"b.0"},"text":"x","paragraphs":1}""",
            """{"type":"format","target":{"ref":"b.0"},"values":{"font":12}}""",
            """{"type":"format","target":{"ref":"b.0"},"values":{"bold":"false"}}""",
            """{"type":"format","target":{"ref":"b.0"},"values":{"color":"zzzzzz"}}""",
            """{"type":"format","target":{"ref":"b.0"},"values":{"size":401}}""",
            """{"type":"format","target":{"ref":"b.0"},"values":{"spacing":{"befor":10}}}""",
            """{"type":"format","target":{"ref":"b.0"},"values":{"lineSpacing":{"exactly":10,"value":2}}}""",
            """{"type":"fillBookmarks","values":{"Name":2}}""",
            """{"type":"fillBookmarks","values":{}}""",
            """{"type":"fillControls","values":{"Name":null}}""",
            """{"type":"fillControls","values":{"Rows":[]}}""",
            """{"type":"updateTable","ref":"b.0","cells":{}}""",
            """{"type":"updateTable","ref":"b.0","cells":[{"row":-1,"column":0,"text":"x"}]}""",
            """{"type":"updateTable","ref":"b.0","appendRows":[[]]}""",
            """{"type":"updateTable","ref":"b.0"}""",
            """{"type":"insertImage","target":{"ref":"b.0"},"data":"invalid"}""",
            """{"type":"replaceImage","ref":"b.0","data":"AA=="}""",
            """{"type":"replaceImage","ref":"b.0@0","data":"AA==","width":0}""",
            """{"type":"properties","values":{"typo":"x"}}""",
            """{"type":"properties","values":{"custom":{"A":false}}}""",
            """{"type":"normalize","rules":["latinDigits","persianDigits"]}""",
            """{"type":"normalize","rules":null}""",
            """{"type":"addComment","target":{"ref":"b.0"},"text":""}""",
            """{"type":"replyComment","text":"x"}""",
            """{"type":"deleteComment","id":4294967296}""",
            """{"type":"resolveComment","id":1,"resolved":null}""",
            """{"type":"trackChanges","enabled":null}""",
            """{"type":"resolveChanges","decision":"accept","all":"true"}""",
            """{"type":"resolveChanges","decision":"keep","all":true}"""
        }) yield return [json];
    }

    [Theory, MemberData(nameof(InvalidOperations))]
    public void Rejects_malformed_operations_without_engine_work(string json)
        => Assert.Throws<RequestValidationException>(() => Automation.ValidateOperations([Json(json)]));

    public static IEnumerable<object[]> ValidOperations()
    {
        foreach (var json in new[] {
            """{"type":"replaceText","find":"Name","replacement":"علی","expectedMatches":1,"scope":"all"}""",
            """{"type":"insertText","target":{"position":{"story":"body","path":[0],"off":0}},"text":"Hello"}""",
            """{"type":"replaceRange","target":{"ref":"p1.0","start":0,"end":3},"text":"New"}""",
            """{"type":"insertParagraph","after":{"bookmark":"Body"},"text":"New","style":"Normal"}""",
            """{"type":"format","target":{"path":[1,0,0,0],"story":{"part":1}},"values":{"rtl":true,"size":12,"color":"A0B1C2","spacing":{"before":0,"after":6}}}""",
            """{"type":"fillBookmarks","values":{"Name":"علی","Empty":""}}""",
            """{"type":"fillControls","values":{"Name":"علی","Approved":true,"Rows":[{"Name":"A"}]}}""",
            """{"type":"updateTable","ref":"b.0","cells":[{"row":0,"column":1,"text":"A"}],"appendRows":[["B","C"]]}""",
            """{"type":"insertImage","target":{"ref":"b.0","start":0,"end":0},"data":"AA==","width":4,"height":4000}""",
            """{"type":"replaceImage","ref":"b.0@3","data":"AA==","alt":"Logo"}""",
            """{"type":"properties","values":{"custom":{"Keep":"Text","Remove":null},"language":"fa-IR"}}""",
            """{"type":"normalize","rules":["persianLetters","halfSpace"]}""",
            """{"type":"addComment","target":{"ref":"b.0"},"text":"Review"}""",
            """{"type":"replyComment","id":0,"text":"Done"}""",
            """{"type":"resolveComment","id":0,"resolved":false}""",
            """{"type":"deleteComment","id":0}""",
            """{"type":"trackChanges","enabled":false}""",
            """{"type":"resolveChanges","decision":"reject","all":true,"stopTracking":true}"""
        }) yield return [json];
    }

    [Theory, MemberData(nameof(ValidOperations))]
    public void Accepts_each_documented_operation(string json) => Automation.ValidateOperations([Json(json)]);

    [Fact]
    public void Rejects_duplicate_keys_at_any_nesting_level()
    {
        var input = Json("""{"type":"properties","values":{"custom":{"A":"first","A":"second"}}}""");
        Assert.Equal(400, Assert.Throws<RequestValidationException>(() => Automation.ValidateOperations([input])).Status);
    }

    [Fact]
    public void Bounds_operation_count_and_serialized_size()
    {
        var operation = Json("""{"type":"trackChanges","enabled":true}""");
        Automation.ValidateOperations(Enumerable.Repeat(operation, 256).ToArray());
        Assert.Throws<RequestValidationException>(() => Automation.ValidateOperations(Enumerable.Repeat(operation, 257).ToArray()));
        var large = Automation.Json(new { type = "replaceText", find = "x", replacement = new string('a', 512 * 1024) });
        Assert.Equal(413, Assert.Throws<RequestValidationException>(() => Automation.ValidateOperations(Enumerable.Repeat(large, 7).ToArray())).Status);
    }

    [Fact]
    public void Rejects_malformed_or_cross_story_ranges()
    {
        foreach (var target in new[] {
            """{"position":{"story":"body","path":[0,1],"off":0}}""",
            """{"range":{"start":{"story":"body","path":[0],"off":0},"end":{"story":{"part":1},"path":[0],"off":0}}}""",
            """{"ref":"p4294967296.0"}""", """{"ref":"b.-1"}"""
        }) Assert.Throws<RequestValidationException>(() => OperationValidation.Target(Json(target), "target"));
    }

    [Fact]
    public void Story_comparison_uses_values_rather_than_json_whitespace()
        => OperationValidation.Target(Json("""{"range":{"start":{"story":{"part":1},"path":[0],"off":0},"end":{"story":{ "part": 1 },"path":[0],"off":4}}}"""), "target");

    [Fact]
    public void Image_limit_counts_decoded_bytes_and_accepts_standard_base64_line_wrapping()
    {
        var data = Convert.ToBase64String(new byte[512 * 1024], Base64FormattingOptions.InsertLineBreaks);
        OperationValidation.Image(Automation.Json(new { data }), "image");
        var tooLarge = Convert.ToBase64String(new byte[512 * 1024 + 1]);
        Assert.Equal(413, Assert.Throws<RequestValidationException>(() => OperationValidation.Image(Automation.Json(new { data = tooLarge }), "image")).Status);
    }
}
