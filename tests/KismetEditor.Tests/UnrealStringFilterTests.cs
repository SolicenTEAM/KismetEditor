using Newtonsoft.Json;
using Solicen.JSON;
using Solicen.Kismet;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace KismetEditor.Tests;

public sealed class UnrealStringFilterTests : IDisposable
{
    private readonly bool previousIgnore = MapParser.IgnoreStringFilter;
    private readonly bool previousUnderscore = MapParser.AllowUnderscore;

    public UnrealStringFilterTests()
    {
        MapParser.IgnoreStringFilter = false;
        MapParser.AllowUnderscore = true;
    }

    public void Dispose()
    {
        MapParser.IgnoreStringFilter = previousIgnore;
        MapParser.AllowUnderscore = previousUnderscore;
    }

    public static IEnumerable<object[]> SystemStrings => new[]
    {
        "Text Block", "TextBlock", " text   block ", "TEXT BLOCK",
        "Text\r\nBlock", "Text\nBlock", "Text\tBlock", @"Text\r\nBlock", @"Text\nBlock",
        "Text<cf>Block", "Text<CF>Block<Text>", "Text<cf>Block<cf>Text",
        "Text<cf>Block<cf>Text<cf>Block", "Text Block Text Block Text",
        "<Default>Text</> <Default>Block</>", "<Text>Text Block</>",
        "Rich Text Block", "RichTextBlock", "Rich<cf>Text<cf>Block",
        "Editable Text", "EditableTextBox", "Editable Text Box",
        "Multi-Line Editable Text", "Multi Line Editable Text Box", "MultiLineEditableTextBox",
        "TextBlock_123", "RichTextBlock_3", "Button_42", "Image_12", "CanvasPanel_0",
        "ProgressBar_3", "EditableTextBox_17", "WidgetSwitcher_5", "VerticalBox_1_2",
        "Default__BP_Player_C", "SKEL_WBP_Menu_C", "REINST_WBP_Menu_C_12",
        "TRASHCLASS_BP_Player_12", "K2Node_CallFunction_42", "ExecuteUbergraph_WBP_Menu",
        "BndEvt__Button_0_K2Node_ComponentBoundEvent_0_OnClicked__DelegateSignature",
        "Default__BP_Игрок_C", "ESlateVisibility::Collapsed", "ETextJustify::Center",
        "EHorizontalAlignment::HAlign_Center", "ECheckBoxState::Checked",
        "/Game/UI/WBP_Menu.WBP_Menu_C", "/Engine/EngineFonts/Roboto.Roboto",
        "/Script/UMG.TextBlock", "Class'/Script/Engine.Actor'",
        "WidgetBlueprintGeneratedClass'/Game/UI/WBP_Menu.WBP_Menu_C'"
    }.Select(value => new object[] { value });

    [Theory]
    [MemberData(nameof(SystemStrings))]
    public void RecognizesSystemStringsInRawAndEscapedForms(string value)
    {
        Assert.True(UnrealStringFilter.IsSystemString(value));
        Assert.True(UnrealStringFilter.IsSystemString(value.Escape()));
        Assert.True(MapParser.IsNotAllowedString(value));
        Assert.True(MapParser.IsNotAllowedString(value.Escape()));
    }

    [Theory]
    [MemberData(nameof(SystemStrings))]
    public void NoFilterBypassesNewRules(string value)
    {
        MapParser.IgnoreStringFilter = true;
        Assert.False(MapParser.IsNotAllowedString(value));
        Assert.False(MapParser.IsNotAllowedString(value.Escape()));
    }

    public static IEnumerable<object[]> GameStrings => new[]
    {
        "Button", "Image", "Text", "Default", "Money", "Press Start",
        "Enter text here", "Button 2", "Image 42", "Text Block 2",
        "Read the Text Block before continuing.", "The Text Block is locked.",
        "Editable text is available here", "This Button_42 opens the door",
        "Use Default__BP_Player_C for testing", "Visibility::Visible",
        "<Red>Attack</>", "Welcome <Text>Commander</>",
        "Judge <cf>{Number}", "Hello {PlayerName}!", "Yargıç: <cf>Neden ambulansı aramadınız?",
        "First line\r\nSecond line", @"First line\r\nSecond line",
        "Text<cf>Block<cf>Start the game", "Block the attack!"
    }.Select(value => new object[] { value });

    [Theory]
    [MemberData(nameof(GameStrings))]
    public void DoesNotClassifyGameTextAsSystemText(string value)
    {
        Assert.False(UnrealStringFilter.IsSystemString(value));
        Assert.False(UnrealStringFilter.IsSystemString(value.Escape()));
    }

    [Theory]
    [InlineData("Button")]
    [InlineData("Image")]
    [InlineData("Text")]
    [InlineData("Button 2")]
    [InlineData("Read the Text Block before continuing.")]
    [InlineData("<Red>Attack now</>")]
    [InlineData("Judge <cf>{Number}")]
    [InlineData("Hello {PlayerName}!")]
    [InlineData("First line\r\nSecond line")]
    public void DefaultFilterPreservesOrdinaryGameText(string value)
    {
        Assert.False(MapParser.IsNotAllowedString(value));
    }

    [Fact]
    public void FilteringDoesNotNormalizeSavedOriginalOrKeys()
    {
        string original = "Hello there<cf><Red>{PlayerName}</>";
        var value = new KismetString { Original = original, KeyValue = "Dialogue::Greeting", NewValue = "Привет" };
        Assert.False(MapParser.IsNotAllowedString(original));
        var json = JsonConvert.SerializeObject(value);
        var restored = JsonConvert.DeserializeObject<KismetString>(json);
        Assert.Equal(original, restored.Original);
        Assert.Equal("Dialogue::Greeting", restored.KeyValue);
        Assert.Equal("Привет", restored.NewValue);
    }

    [Fact]
    public void MergeRemovesOldAndNewSystemStringsButPreservesTranslationsAndKeys()
    {
        var original = new UberJSON("UI.uasset");
        original.Values.Add(new KismetString { Original = "Hello", NewValue = "Привет", KeyValue = "Dialogue::Hello" });
        original.Add("Text<cf>Block", "Old placeholder translation");
        var added = new UberJSON("UI.uasset");
        added.Values.Add(new KismetString { Original = "Hello", NewValue = "", KeyValue = "" });
        added.Add("Button_42");
        added.Add("Press Start");
        var newFile = new UberJSON("Other.uasset");
        newFile.Add("Rich Text Block");
        newFile.Add("Welcome back");
        var onlySystem = new UberJSON("Placeholder.uasset");
        onlySystem.Add("Text Block");
        var empty = new UberJSON("PreviouslyEmpty.uasset");
        var merged = new[] { original, empty }.Merge(new[] { added, newFile, onlySystem });

        Assert.Equal(new[] { "UI.uasset", "PreviouslyEmpty.uasset", "Other.uasset" }, merged.Select(x => x.FileName));
        var hello = merged[0].Values.Single(x => x.Original == "Hello");
        Assert.Equal("Привет", hello.NewValue);
        Assert.Equal("Dialogue::Hello", hello.KeyValue);
        Assert.Equal(new[] { "Hello", "Press Start" }, merged[0].Values.Select(x => x.Original));
        Assert.Equal("Welcome back", Assert.Single(merged[2].Values).Original);
        Assert.DoesNotContain(merged.SelectMany(x => x.Values), x => UnrealStringFilter.IsSystemString(x.Original));
    }

    [Fact]
    public void MergeFiltersWhenThereIsNoPreviousJson()
    {
        var added = new UberJSON("UI.uasset");
        added.Add("Text Block");
        added.Add("Hello");
        var merged = UberJSONProcessor.Merge(null, new[] { added });
        Assert.Equal("Hello", Assert.Single(Assert.Single(merged).Values).Original);
    }

    [Fact]
    public void NoFilterRetainsSystemStringsDuringMerge()
    {
        MapParser.IgnoreStringFilter = true;
        var original = new UberJSON("UI.uasset");
        original.Add("Text<cf>Block", "Existing translation");
        var added = new UberJSON("Other.uasset");
        added.Add("Button_42");
        var merged = new[] { original }.Merge(new[] { added });
        Assert.Equal("Existing translation", merged[0].Values[0].NewValue);
        Assert.Equal("Button_42", merged[1].Values[0].Original);
    }
}
