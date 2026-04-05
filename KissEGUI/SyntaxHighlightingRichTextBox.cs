﻿using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace KissEGUI
{
    public class SyntaxHighlightingRichTextBox : RichTextBox
    {
        private static readonly SolidColorBrush DefaultBrush = Brushes.White;
        private static readonly SolidColorBrush EscapeCharBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#d0a758"));
        private static readonly SolidColorBrush TagBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#579cd3"));

        // Регулярное выражение для поиска всех наших токенов
        private static readonly Regex TokenizerRegex = new Regex(
            @"(\\[nrt\\""])|(<[^>]+>)|({[^}]+})|(""[^""]*"")|('[^']*')",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            "Text", typeof(string), typeof(SyntaxHighlightingRichTextBox),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public SyntaxHighlightingRichTextBox()
        {
            this.TextChanged += OnInternalTextChanged;
            this.CaretBrush = Brushes.White; // Устанавливаем цвет курсора

            // Устанавливаем шрифт по умолчанию, чтобы он соответствовал остальному UI
            this.FontFamily = SystemFonts.MessageFontFamily;
        }

        private bool _isInternalChange = false;

        private void OnInternalTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInternalChange) return;

            // Обновляем свойство Text, когда пользователь вводит текст
            Text = new TextRange(this.Document.ContentStart, this.Document.ContentEnd).Text.TrimEnd('\r', '\n');
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var rtb = (SyntaxHighlightingRichTextBox)d;
            rtb.UpdateHighlighting();
        }

        private void UpdateHighlighting()
        {
            _isInternalChange = true;
            this.Document.BeginInit();
            try
            {
                // Сохраняем позицию курсора перед обновлением
                var caretOffset = this.CaretPosition.DocumentStart.GetOffsetToPosition(this.CaretPosition);

                var document = this.Document;
                document.Blocks.Clear();

                var text = this.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    // Передаем шрифт из контрола в метод создания параграфа
                    var paragraph = CreateFormattedParagraph(text, this.FontFamily);
                    document.Blocks.Add(paragraph);
                }

                // Восстанавливаем позицию курсора
                var newCaretPosition = document.ContentStart.GetPositionAtOffset(caretOffset, LogicalDirection.Forward);
                this.CaretPosition = newCaretPosition ?? document.ContentEnd;
            }
            finally
            {
                this.Document.EndInit();
                _isInternalChange = false;
            }
        }

        public static Paragraph CreateFormattedParagraph(string text, FontFamily fontFamily)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0), FontFamily = fontFamily }; // Устанавливаем шрифт и убираем отступы
            if (string.IsNullOrEmpty(text))
            {
                return paragraph;
            }
            paragraph.Inlines.AddRange(CreateFormattedInlines(text, fontFamily));
            return paragraph;
        }

        private static List<Inline> CreateFormattedInlines(string text, FontFamily fontFamily)
        {
            var inlines = new List<Inline>();
            var lastIndex = 0;

            foreach (Match match in TokenizerRegex.Matches(text))
            {
                // Добавляем текст перед найденным токеном
                if (match.Index > lastIndex)
                {
                    inlines.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)) 
                        { Foreground = DefaultBrush, BaselineAlignment = BaselineAlignment.Top });
                }

                var value = match.Value;
                Brush brush = DefaultBrush; // По умолчанию цвет обычный

                // Определяем цвет в зависимости от типа токена
                if (value.StartsWith("\"") || value.StartsWith("'"))
                {
                    // Текст в кавычках - обычный цвет, но мы его все равно добавляем как отдельный Run
                    brush = DefaultBrush; 
                }
                else if (value.StartsWith("\\"))
                {
                    // Управляющие последовательности
                    brush = EscapeCharBrush;
                }
                else if (value.StartsWith("<") || value.StartsWith("{"))
                {
                    // Теги
                    brush = TagBrush;
                }

                inlines.Add(new Run(value) { Foreground = brush, BaselineAlignment = BaselineAlignment.Top });
                lastIndex = match.Index + match.Length;
            }

            // Добавляем оставшийся текст после последнего токена
            if (lastIndex < text.Length)
            {
                inlines.Add(new Run(text.Substring(lastIndex)) 
                    { Foreground = DefaultBrush, BaselineAlignment = BaselineAlignment.Top });
            }
            return inlines;
        }
    }
}
