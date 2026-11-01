using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Solicen.JSON;
using System;
using System.IO;
using Xunit;

namespace KismetEditor.Tests;

public class UberJsonTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void OmitsEmptyKeyValueWithoutOmittingTranslationFields(string key)
    {
        var value = new KismetString { KeyValue = key, Original = "Press Start", NewValue = "" };
        var json = JObject.Parse(JsonConvert.SerializeObject(value));
        Assert.False(json.ContainsKey("KeyValue"));
        Assert.Equal("Press Start", json["Original"].Value<string>());
        Assert.Equal("", json["NewValue"].Value<string>());
        Assert.Equal(key, value.KeyValue);
    }

    [Theory]
    [InlineData("Dialog::Greeting")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("  Keep this key unchanged  ")]
    public void PreservesNonEmptyKeyValueExactly(string key)
    {
        var value = new KismetString { KeyValue = key, Original = "Hello", NewValue = "Привет" };
        string json = JsonConvert.SerializeObject(value);
        var reloaded = JsonConvert.DeserializeObject<KismetString>(json);
        Assert.Equal(key, JObject.Parse(json)["KeyValue"].Value<string>());
        Assert.Equal(key, reloaded.KeyValue);
        Assert.Equal("Привет", reloaded.NewValue);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"KeyValue\":\"\"")]
    [InlineData(",\"KeyValue\":null")]
    [InlineData(",\"KeyValue\":\"  \"")]
    public void ReadsLegacyJsonAndOmitsEmptyKeysOnSave(string keyMember)
    {
        string source = "[{\"FileName\":\"UI.uasset\",\"Values\":[{\"Original\":\"Hello\",\"NewValue\":\"Привет\"" + keyMember + "}]}]";
        var entries = UberJSONProcessor.Deserialize(source);
        string path = Path.Combine(Path.GetTempPath(), "KissE-json-" + Guid.NewGuid() + ".json");
        try
        {
            entries.SaveFile(path);
            var json = JArray.Parse(File.ReadAllText(path));
            var value = (JObject)json[0]["Values"][0];
            Assert.False(value.ContainsKey("KeyValue"));
            Assert.Equal("Привет", value["NewValue"].Value<string>());
            Assert.Equal("Hello", UberJSONProcessor.ReadFile(path)[0].Values[0].Original);
        }
        finally { File.Delete(path); }
    }
}
