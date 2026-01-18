using Newtonsoft.Json.Linq;
using UAssetAPI.Kismet.Bytecode;
using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
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

        // Порядок как в исходном типе
        members.Sort((a, b) => a.member.MetadataToken.CompareTo(b.member.MetadataToken));

        foreach (var (member, value, memberType) in members)
        {
            var token = SerializeProperty(
                property: value,
                declaredType: memberType,
                memberName: member.Name,
                parentType: type
            );

            // Добавляем ВСЕ, включая null
            res.Add(member.Name, token);
        }
    }

    private bool IsSkippedMember(string name)
    {
        // "New" убираем полностью
        return name == "RawValue" || name == "Inst" || name == "Tag" || name == "Token" || name == "New";
    }

    private JToken SerializeProperty(object property, Type declaredType, string memberName, Type parentType)
    {
        // === NULL-handling с учетом контекста ===
        if (property == null)
        {
            // В KismetPropertyPointer.Old хотим 0 вместо null
            if (parentType == typeof(KismetPropertyPointer) &&
                memberName == "Old" &&
                declaredType == typeof(FPackageIndex))
            {
                return new JValue(0);
            }

            return null;
        }
        // =======================================

        // KismetExpression и наследники
        if (property is KismetExpression expression)
            return SerializeExpression(expression);

        var propType = property.GetType();

        // Массивы
        if (propType.IsArray)
            return SerializeEnumerable((Array)property);

        // IEnumerable (но не string)
        if (property is System.Collections.IEnumerable enumerable && property is not string)
            return SerializeEnumerable(enumerable);

        // ====== ENUM -> строка (и спец-кейс EBlueprintTextLiteralType) ======
        if (propType.IsEnum)
        {
            if (propType.FullName == "UAssetAPI.Kismet.Bytecode.EBlueprintTextLiteralType")
            {
                // имя enum (обычно будет "LocalizedText")
                string enumName = Enum.GetName(propType, property);

                // fallback, если почему-то имени нет
                if (enumName == null)
                {
                    int valInt = Convert.ToInt32(property);
                    enumName = valInt switch
                    {
                        0 => "None",
                        1 => "LocalizedText",
                        2 => "StringTable",
                        3 => "StringTableWithKeyFallback",
                        _ => valInt.ToString()
                    };
                }

                return new JValue(enumName);
            }

            return new JValue(property.ToString());
        }
        // ===================================================================

        // FPackageIndex: контекстная логика
        if (property is FPackageIndex fPackageIndex)
        {
            // В FScriptText.StringTableAsset: 0 трактуем как null (как в эталоне)
            if (parentType == typeof(FScriptText) && memberName == "StringTableAsset")
            {
                // У UAssetAPI "пустая ссылка" часто хранится как Index == 0
                if (fPackageIndex.Index == 0)
                    return null;
            }

            return new JValue(fPackageIndex.Index);
        }

        // FName -> строка
        if (property is FName fname)
            return new JValue(fname.ToString());

        // float (signed zero + точность как double)
        if (property is float f)
            return SerializeFloat(f);

        // double (signed zero)
        if (property is double d)
            return SerializeDouble(d);

        // Kismet-типы (не Expression, не enum)
        if (IsKismetType(propType))
            return SerializeKismetObject(property);

        // Простые типы
        if (propType.IsPrimitive || propType == typeof(string) || propType == typeof(decimal))
            return new JValue(property);

        // Остальные объекты рекурсивно
        JObject objRes = new JObject();
        string objTypeName = propType.FullName;
        objRes.Add("$type", objTypeName + ", UAssetAPI");
        ProcessMembers(property, objRes);
        return objRes;
    }

    private bool IsKismetType(Type type)
    {
        if (type.IsEnum) return false;
        string ns = type.Namespace;
        return ns != null && ns.StartsWith("UAssetAPI.Kismet") &&
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
            // Внутри массивов/листов обычно контекст не нужен
            var token = SerializeProperty(item, item?.GetType(), memberName: null, parentType: null);
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
            if (serialized != null) array.Add(serialized);
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