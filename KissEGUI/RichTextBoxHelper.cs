using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace KissEGUI
{
    public static class RichTextBoxHelper
    {
        public static readonly DependencyProperty DocumentProperty =
            DependencyProperty.RegisterAttached(
                "Document",
                typeof(FlowDocument),
                typeof(RichTextBoxHelper),
                new FrameworkPropertyMetadata(null, OnDocumentChanged));

        public static void SetDocument(DependencyObject d, FlowDocument value)
        {
            d.SetValue(DocumentProperty, value);
        }

        public static FlowDocument GetDocument(DependencyObject d)
        {
            return (FlowDocument)d.GetValue(DocumentProperty);
        }

        private static void OnDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is RichTextBox rtb)
            {
                rtb.Document = e.NewValue as FlowDocument;
            }
        }
    }
}
