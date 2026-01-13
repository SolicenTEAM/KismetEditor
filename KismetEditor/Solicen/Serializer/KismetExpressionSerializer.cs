using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UAssetAPI.Kismet.Bytecode;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

    /// <summary>
    /// Сериализует KismetExpression в JObject, рекурсивно обходя все его свойства и поля.
    /// Пропускает RawValue и использует Value. Добавляет $type для Kismet-объектов.
    /// </summary>
    /// <param name="expression">Выражение для сериализации.</param>
    /// <returns>JObject, представляющий выражение.</returns>
    public JObject SerializeExpression(KismetExpression expression)
    {
        if (expression == null) return null;

        JObject res = new JObject();
        string typeName = expression.GetType().FullName;
        res.Add("$type", typeName + ", UAssetAPI");

        // Гибридный подход: сначала свойства, потом поля
        ProcessMembers(expression, res);

        return res;
    }

    /// <summary>
    /// Общий метод для обработки свойств и полей объекта (с пропуском RawValue, Inst и служебных).
    /// </summary>
    private void ProcessMembers(object obj, JObject res)
    {
        var type = obj.GetType();

        // Сначала публичные свойства
        PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (PropertyInfo prop in properties)
        {
            if (IsSkippedMember(prop.Name)) continue;
            try
            {
                object val = prop.GetValue(obj);
                JToken token = SerializeProperty(val);
                if (token != null) res.Add(prop.Name, token);
            }
            catch { /* Игнорируем ошибки */ }
        }

        // Потом публичные поля
        FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        foreach (FieldInfo field in fields)
        {
            if (IsSkippedMember(field.Name)) continue;
            try
            {
                object val = field.GetValue(obj);
                JToken token = SerializeProperty(val);
                if (token != null) res.Add(field.Name, token);
            }
            catch { /* Игнорируем ошибки */ }
        }
    }

    /// <summary>
    /// Проверяет, нужно ли пропустить член (RawValue, Inst, Tag, Token).
    /// </summary>
    private bool IsSkippedMember(string name)
    {
        return name == "RawValue" || name == "Inst" || name == "Tag" || name == "Token";
    }

    /// <summary>
    /// Вспомогательный метод, который определяет тип свойства и вызывает соответствующий метод сериализации.
    /// </summary>
    private JToken SerializeProperty(object property)
    {
        if (property == null) return null;

        // KismetExpression и наследники
        if (property is KismetExpression expression)
        {
            return SerializeExpression(expression);
        }

        var propType = property.GetType();

        // Массивы
        if (propType.IsArray)
        {
            return SerializeEnumerable((Array)property);
        }

        // IEnumerable (List<T>, etc.)
        if (property is System.Collections.IEnumerable enumerable && (property is string) == false)
        {
            return SerializeEnumerable(enumerable);
        }

        // FPackageIndex -> просто Index как число
        if (property is FPackageIndex fPackageIndex)
        {
            return new JValue(fPackageIndex.Index);
        }

        // KismetPropertyPointer и другие Kismet-объекты (не Expression)
        if (IsKismetType(propType))
        {
            return SerializeKismetObject(property);
        }

        // FName
        if (property is FName fname)
        {
            return new JValue(fname.ToString());
        }

        // Простые типы
        if (propType.IsPrimitive || propType == typeof(string) || propType == typeof(decimal))
        {
            return new JValue(property);
        }

        // Для всех остальных объектов (рекурсивно свойства/поля)
        JObject objRes = new JObject();
        string objTypeName = propType.FullName;
        objRes.Add("$type", objTypeName + ", UAssetAPI");
        ProcessMembers(property, objRes);
        return objRes;
    }

    /// <summary>
    /// Проверяет, является ли тип Kismet-типом (UAssetAPI.Kismet.* но не KismetExpression).
    /// </summary>
    private bool IsKismetType(Type type)
    {
        string ns = type.Namespace;
        return ns != null && ns.StartsWith("UAssetAPI.Kismet") && !typeof(KismetExpression).IsAssignableFrom(type);
    }

    /// <summary>
    /// Сериализует Kismet-объект (как KismetPropertyPointer) с $type и рекурсивными полями/свойствами.
    /// </summary>
    private JObject SerializeKismetObject(object obj)
    {
        JObject res = new JObject();
        string typeName = obj.GetType().FullName;
        res.Add("$type", typeName + ", UAssetAPI");
        ProcessMembers(obj, res);
        return res;
    }

    /// <summary>
    /// Сериализует IEnumerable в JArray.
    /// </summary>
    private JArray SerializeEnumerable(System.Collections.IEnumerable enumerable)
    {
        JArray array = new JArray();
        foreach (var item in enumerable)
        {
            JToken token = SerializeProperty(item);
            if (token != null)
            {
                array.Add(token);
            }
        }
        return array;
    }

    /// <summary>
    /// Сериализует массив KismetExpression в JArray (без Inst).
    /// </summary>
    public JArray SerializeExpressionArray(KismetExpression[] expressions)
    {
        if (expressions == null) return null;
        JArray array = new JArray();
        foreach (KismetExpression expr in expressions)
        {
            JToken serialized = SerializeExpression(expr);
            if (serialized != null)
            {
                array.Add(serialized);
            }
        }
        return array;
    }

    /// <summary>
    /// Сериализует полный скрипт (массив KismetExpression[]) в JArray с Inst.
    /// </summary>
    public JArray SerializeScript(KismetExpression[] script)
    {
        if (script == null) return null;
        JArray scriptArray = new JArray();
        foreach (KismetExpression expression in script)
        {
            if (expression == null) continue;
            JObject entry = new JObject();
            entry.Add("Inst", expression.Inst.ToString());
            entry.Add("Expression", SerializeExpression(expression));
            scriptArray.Add(entry);
        }
        return scriptArray;
    }
}