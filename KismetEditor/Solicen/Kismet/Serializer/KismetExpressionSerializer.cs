using Newtonsoft.Json.Linq;
using UAssetAPI.Kismet.Bytecode;
using System;
using System.Reflection;
using System.Collections.Generic;
using UAssetAPI.Kismet.Bytecode.Expressions;
using UAssetAPI;
using UAssetAPI.UnrealTypes;

public class KismetExpressionSerializer
{
    public UAsset Asset;
    public KismetExpressionSerializer(UAsset asset)
    {
        Asset = asset;
    }

    public JObject SerializeExpression(KismetExpression expression)
    {
        if (expression == null) return null;

        JObject res = new JObject();
        string typeName = expression.GetType().FullName;
        res.Add("$type", typeName + ", UAssetAPI");

        ProcessMembers(expression, res);
        return res;
    }

    private void ProcessMembers(object obj, JObject res)
    {
        var type = obj.GetType();

        // Спец‑обработка KismetPropertyPointer: либо Old, либо New
        if (type.FullName == "UAssetAPI.Kismet.Bytecode.KismetPropertyPointer")
        {
            ProcessKismetPropertyPointer(obj, res, type);
            return;
        }

        var members = new List<(MemberInfo member, object value, Type memberType)>();

        // Свойства
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in properties)
        {
            if (IsSkippedMember(prop.Name)) continue;
            try
            {
                var val = prop.GetValue(obj);
                members.Add((prop, val, prop.PropertyType));
            }
            catch { }
        }

        // Поля
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        foreach (var field in fields)
        {
            if (IsSkippedMember(field.Name)) continue;
            try
            {
                var val = field.GetValue(obj);
                members.Add((field, val, field.FieldType));
            }
            catch { }
        }

        // Сохраняем порядок объявления (важно для Struct/StructSize/Value и т.п.)
        members.Sort((a, b) => a.member.MetadataToken.CompareTo(b.member.MetadataToken));

        foreach (var (member, value, memberType) in members)
        {
            // Спец‑кейс: не писать New:null внутри KismetPropertyPointer (handle в ProcessKismetPropertyPointer),
            // но сюда такие случаи уже не попадут.

            var token = SerializeProperty(value, memberType, type, member.Name);
            res.Add(member.Name, token);
        }
    }

    /// <summary>
    /// Для KismetPropertyPointer: если New != null → сериализуем только New,
    /// иначе сериализуем только Old.
    /// </summary>
    private void ProcessKismetPropertyPointer(object obj, JObject res, Type type)
    {
        var oldField = type.GetField("Old", BindingFlags.Public | BindingFlags.Instance);
        var newField = type.GetField("New", BindingFlags.Public | BindingFlags.Instance);

        object oldVal = oldField?.GetValue(obj);
        object newVal = newField?.GetValue(obj);

        // Если New задан (UE5/FFieldPath) → пишем только New
        if (newField != null && newVal != null)
        {
            var newToken = SerializeProperty(newVal, newField.FieldType, type, "New");
            res.Add("New", newToken);
        }
        // Иначе используем Old (UE4/FPackageIndex)
        else if (oldField != null)
        {
            var oldToken = SerializeProperty(oldVal, oldField.FieldType, type, "Old");
            res.Add("Old", oldToken);
        }
    }

    private bool IsSkippedMember(string name)
    {
        // ВАЖНО: "New" больше НЕ пропускаем, он нужен для FFieldPath
        return name == "RawValue" || name == "Inst" || name == "Tag" || name == "Token";
    }

    private JToken SerializeProperty(object property, Type declaredType, Type ownerType, string memberName)
    {
        // === NULL ===
        if (property == null)
        {
            // Спец‑логика для FPackageIndex (Old, StringTableAsset, и т.п.)
            if (declaredType == typeof(FPackageIndex))
            {
                // FScriptText.StringTableAsset → всегда null
                if (ownerType != null &&
                    ownerType.FullName == "UAssetAPI.Kismet.Bytecode.FScriptText" &&
                    memberName == "StringTableAsset")
                {
                    return JValue.CreateNull();
                }

                // Все остальные FPackageIndex (например KismetPropertyPointer.Old) → 0
                return new JValue(0);
            }

            // Остальные null → null
            return JValue.CreateNull();
        }
        // =======================

        // Вложенное выражение
        if (property is KismetExpression expression)
            return SerializeExpression(expression);

        var propType = property.GetType();

        // Массивы
        if (propType.IsArray)
            return SerializeEnumerable((Array)property);

        // IEnumerable (List<...> и т.п.), но не string
        if (property is System.Collections.IEnumerable enumerable && property is not string)
            return SerializeEnumerable(enumerable);

        // FPackageIndex → число, но с учётом FScriptText.StringTableAsset
        if (property is FPackageIndex fPackageIndex)
        {
            if (ownerType != null &&
                ownerType.FullName == "UAssetAPI.Kismet.Bytecode.FScriptText" &&
                memberName == "StringTableAsset")
            {
                if (fPackageIndex == null || fPackageIndex.IsNull())
                    return JValue.CreateNull();
            }

            return new JValue(fPackageIndex.Index);
        }

        // FName → строка
        if (property is FName fname)
            return new JValue(fname.ToString());

        // ===== ENUM'Ы =====
        Type enumType = null;
        if (declaredType != null && declaredType.IsEnum)
            enumType = declaredType;
        else if (propType.IsEnum)
            enumType = propType;

        if (enumType != null)
        {
            string fullName = enumType.FullName;

            // EBlueprintTextLiteralType → строка ("LocalizedText", и т.п.)
            if (fullName == "UAssetAPI.Kismet.Bytecode.EBlueprintTextLiteralType")
            {
                return new JValue(property.ToString());
            }

            // Остальные enum'ы → целое значение
            return new JValue(Convert.ToInt32(property));
        }

        // float с signed zero и полной точностью через double
        if (property is float f)
            return SerializeFloat(f);

        // double с signed zero
        if (property is double d)
            return SerializeDouble(d);

        // Любой Kismet-тип (кроме самих выражений)
        if (IsKismetType(propType))
            return SerializeKismetObject(property);

        // Примитивы
        if (propType.IsPrimitive || propType == typeof(string) || propType == typeof(decimal))
            return new JValue(property);

        // Сложные объекты рекурсивно
        JObject objRes = new JObject();
        string objTypeName = propType.FullName;
        objRes.Add("$type", objTypeName + ", UAssetAPI");
        ProcessMembers(property, objRes);
        return objRes;
    }

    private bool IsKismetType(Type type)
    {
        string ns = type.Namespace;
        return ns != null &&
               ns.StartsWith("UAssetAPI.Kismet") &&
               !typeof(KismetExpression).IsAssignableFrom(type);
    }

    private JObject SerializeKismetObject(object obj)
    {
        JObject res = new JObject();
        string typeName = obj.GetType().FullName;
        res.Add("$type", typeName + ", UAssetAPI");
        ProcessMembers(obj, res);
        return res;
    }

    private JArray SerializeEnumerable(System.Collections.IEnumerable enumerable)
    {
        JArray array = new JArray();
        foreach (var item in enumerable)
        {
            var token = SerializeProperty(item, null, null, null);
            array.Add(token);
        }
        return array;
    }

    private static JToken SerializeFloat(float f)
    {
        if (f == 0.0f)
        {
            bool isNegativeZero = BitConverter.SingleToInt32Bits(f) == unchecked((int)0x80000000);
            return new JValue(isNegativeZero ? "-0" : "+0");
        }
        // Каст в double, чтобы Json.NET выводил полную точность (0.6600000262260437 и т.п.)
        return new JValue((double)f);
    }

    private static JToken SerializeDouble(double d)
    {
        if (d == 0.0d)
        {
            long bits = BitConverter.DoubleToInt64Bits(d);
            bool isNegativeZero = bits == unchecked((long)0x8000000000000000);
            return new JValue(isNegativeZero ? "-0" : "+0");
        }
        return new JValue(d);
    }

    public JArray SerializeExpressionArray(KismetExpression[] expressions)
    {
        if (expressions == null) return null;
        JArray array = new JArray();
        foreach (var expr in expressions)
        {
            var serialized = SerializeExpression(expr);
            if (serialized != null)
                array.Add(serialized);
        }
        return array;
    }

    public JArray SerializeScript(KismetExpression[] script)
    {
        if (script == null) return null;
        JArray scriptArray = new JArray();
        foreach (var expression in script)
        {
            if (expression == null) continue;
            JObject entry = new JObject
            {
                { "Inst", expression.Inst.ToString() },
                { "Expression", SerializeExpression(expression) }
            };
            scriptArray.Add(entry);
        }
        return scriptArray;
    }
}