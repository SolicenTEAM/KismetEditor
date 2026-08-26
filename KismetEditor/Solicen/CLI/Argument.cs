using System;
using System.Collections.Generic;
using System.Linq;

namespace Solicen.CLI
{
    public class Argument
    {
        public string Key { get; }
        public string ShortKey { get; }
        public string Description { get; }
        public bool HasValue { get; }
        public Action ActionWithoutValue { get; }
        public Action<string> ActionWithValue { get; }

        public string[] Aliases { get; }

        // Конструктор для флагов (без значения)
        public Argument(string key, string shortKey, string description, Action action, params string[] aliases)
        {
            Key = key;
            ShortKey = shortKey;
            Description = description;
            HasValue = false;
            ActionWithoutValue = action;
            Aliases = aliases ?? Array.Empty<string>();
        }

        // Конструктор для аргументов со значением
        public Argument(string key, string shortKey, string description, Action<string> action, params string[] aliases)
        {
            Key = key;
            ShortKey = shortKey;
            Description = description;
            HasValue = true;
            ActionWithValue = action;
            Aliases = aliases ?? Array.Empty<string>();
        }

        // Извлекает часть до '=' (сам ключ). Например: "--mapping=Gori" -> "--mapping"
        public static string GetKeyPart(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return string.Empty;
            int eq = arg.IndexOf('=');
            if (eq != -1) return arg.Substring(0, eq).Trim();
            return arg.Trim();
        }

        // Извлекает часть после '=' если есть. Например: "--mapping=Gori" -> "Gori"
        public static string GetValuePart(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return null;
            int eq = arg.IndexOf('=');
            if (eq != -1) return arg.Substring(eq + 1);
            return null;
        }

        // Проверяет, соответствует ли строковый аргумент этому правилу (строгое сравнение).
        // Ключ сравнивается ТОЧНО, без StartsWith, чтобы избежать коллизий вида -m / -ma / -map.
        public bool Matches(string arg)
        {
            if (string.IsNullOrWhiteSpace(arg)) return false;
            string keyPart = GetKeyPart(arg);
            if (string.IsNullOrEmpty(keyPart)) return false;
            if (Key.Equals(keyPart, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(ShortKey) && ShortKey.Equals(keyPart, StringComparison.OrdinalIgnoreCase)) return true;
            if (Aliases != null)
            {
                foreach (var alias in Aliases)
                    if (!string.IsNullOrEmpty(alias) && alias.Equals(keyPart, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            return false;
        }

        public string GetDisplayKeys()
        {
            var keys = new List<string> { Key, ShortKey };
            if (Aliases != null) keys.AddRange(Aliases.Where(a => !string.IsNullOrEmpty(a)));
            return string.Join(", ", keys.Where(k => !string.IsNullOrEmpty(k)));
        }
    }
}