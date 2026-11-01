using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Solicen.Kismet
{
    internal static class UnrealStringFilter
    {
        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
        private static readonly Regex EscapedWhitespace = new Regex(@"<cf>|\\r\\n|\\[rnt]", Options);
        private static readonly Regex FormattingTags = new Regex(@"<[^<>]*>", Options);
        private static readonly Regex Whitespace = new Regex(@"\s+", Options);

        private static readonly Regex Placeholder = new Regex(
            @"\A(?:(?:rich\s*)?text\s*block(?:\s*(?:rich\s*)?text(?:\s*block)?)*|(?:multi[\s-]*line\s*)?editable\s*text(?:\s*box)?)\z", Options);

        private static readonly HashSet<string> WidgetTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TextBlock", "RichTextBlock", "EditableText", "EditableTextBox", "MultiLineEditableText", "MultiLineEditableTextBox",
            "Button", "Image", "Border", "CanvasPanel", "Overlay", "HorizontalBox", "VerticalBox", "ScrollBox", "ScaleBox",
            "SizeBox", "Spacer", "ProgressBar", "CheckBox", "ComboBoxString", "SpinBox", "Slider", "WidgetSwitcher",
            "RetainerBox", "InvalidationBox", "WrapBox", "UniformGridPanel", "GridPanel", "ListView", "TreeView", "TileView",
            "UserWidget", "NamedSlot", "SafeZone", "MenuAnchor", "ExpandableArea", "InputKeySelector"
        };
        private static readonly Regex NumericSuffix = new Regex(@"\A(?:_[0-9]+)+\z", Options);

        private static readonly Regex GeneratedIdentifier = new Regex(
            @"\A(?:Default__|SKEL_|REINST_|TRASHCLASS_|K2Node_|ExecuteUbergraph_|BndEvt__)[\p{L}\p{Nd}_.$:]+\z", Options);

        private static readonly HashSet<string> EngineEnums = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ESlateVisibility", "ETextJustify", "EHorizontalAlignment", "EVerticalAlignment", "ECheckBoxState", "EWidgetClipping",
            "ETextWrappingPolicy", "ETextOverflowPolicy", "ESlateColorStylingMode", "ETextTransformPolicy", "EOrientation"
        };
        private static readonly Regex EnumValue = new Regex(@"\A[A-Za-z_][A-Za-z0-9_]*\z", Options);

        private static readonly Regex ObjectReference = new Regex(
            @"\A(?:[A-Za-z_][A-Za-z0-9_]*')?/(?:Game|Engine|Script)/[^\s<>']*'?\z", Options);

        public static bool IsSystemString(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            // Compare raw and escaped text consistently, but never change the saved Original.
            string normalized = EscapedWhitespace.Replace(value, " ");
            normalized = FormattingTags.Replace(normalized, " ");
            normalized = Whitespace.Replace(normalized, " ").Trim();
            int suffix = normalized.IndexOf('_');
            int separator = normalized.IndexOf("::", StringComparison.Ordinal);
            return Placeholder.IsMatch(normalized)
                || (suffix > 0 && WidgetTypes.Contains(normalized.Substring(0, suffix)) && NumericSuffix.IsMatch(normalized.Substring(suffix)))
                || GeneratedIdentifier.IsMatch(normalized)
                || (separator > 0 && EngineEnums.Contains(normalized.Substring(0, separator)) && EnumValue.IsMatch(normalized.Substring(separator + 2)))
                || ObjectReference.IsMatch(normalized);
        }
    }
}
