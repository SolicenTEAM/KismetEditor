using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UAssetAPI.Kismet.Bytecode;
using System;
using System.Collections.Generic;
using System.Reflection;
using UAssetAPI.Kismet.Bytecode.Expressions;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
public class KismetExpressionSerializer
{
    public  UAsset Asset;
    public KismetExpressionSerializer(UAsset asset)
    { Asset = asset; }

    /// <summary>
    /// Сериализует KismetExpression в JObject, рекурсивно обходя все его поля
    /// для создания подробного представления, аналогичного UAssetGUI.
    /// </summary>
    /// <param name="expression">Выражение для сериализации.</param>
    /// <returns>JObject, представляющий выражение.</returns>
    private JObject SerializeExpression(KismetExpression expression)
    {
        if (expression == null) return null;

        JObject res = new JObject();
        string typeName = expression.GetType().FullName;
        res.Add("$type", typeName + ", UAssetAPI");

        // Гибридный подход: используем рефлексию для получения ВСЕХ полей,
        // чтобы гарантировать, что никакие данные не будут потеряны.
        // Это решает проблему с пропущенными полями, как в EX_Context.
        FieldInfo[] fields = expression.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public);
        foreach (FieldInfo field in fields)
        {
            // Пропускаем поля, которые не должны сериализоваться в JSON
            // if (field.IsDefined(typeof(JsonIgnoreAttribute), false)) continue;
            // Пропускаем внутреннее свойство "Inst", так как мы добавляем его отдельно и в более удобном виде.
            if (field.Name == "Inst") continue;

            string fieldName = field.Name;
            object val = field.GetValue(expression);

            JToken token = SerializeProperty(val);
            if (token != null) res.Add(fieldName, token);
        }

        return res;
    }

    /// <summary>
    /// Вспомогательный метод, который определяет тип свойства и вызывает соответствующий метод сериализации.
    /// </summary>
    /// <param name="property">Свойство для сериализации.</param>
    /// <returns>JToken, представляющий свойство.</returns>
    private JToken SerializeProperty(object property)
    {
        if (property == null) return null;

        if (property is KismetExpression expression)
        {
            return SerializeExpression(expression);
        }

        if (property is KismetExpression[] expressionArray)
        {
            JArray array = new JArray();
            foreach (KismetExpression expr in expressionArray)
            {
                array.Add(SerializeExpression(expr));
            }
            return array;
        }

        // Обработка FPackageIndex для корректного отображения ссылок на объекты
        if (property is FPackageIndex fPackageIndex)
        {
            JObject packageIndexObj = new JObject();
            packageIndexObj.Add("Index", fPackageIndex.Index);
            if (Asset != null)
            {
                var import = fPackageIndex.IsImport() && (fPackageIndex.Index * -1 - 1) < Asset.Imports.Count ? Asset.Imports[fPackageIndex.Index * -1 - 1] : null;
                packageIndexObj.Add("ObjectName", import?.ObjectName.ToString());

            }
            return packageIndexObj;
        }

        var propType = property.GetType();
        if (propType.IsArray)
        {
            JArray array = new JArray();
            foreach (var item in (Array)property)
            {
                array.Add(SerializeProperty(item));
            }
            return array;
        }

        // Обработка специфичных для Kismet типов, таких как KismetPropertyPointer
        if (property is List<KismetPropertyPointer> pointers)
        {
            JArray array = new JArray();
            foreach (var item in pointers)
            {
                array.Add(JToken.FromObject(item.Old.Index));
            }
            return array;
        }

        // Обработка FName, чтобы избежать ошибки с FNameJsonConverter
        if (property is FName fname)
        {
            return JToken.FromObject(fname.ToString());
        }

        // Для всех остальных "простых" типов (int, string, bool, enum и т.д.)
        return JToken.FromObject(property);
    }

    /// <summary>
    /// Сериализует полный скрипт (массив KismetExpression[]) в JArray.
    /// </summary>
    /// <param name="script">Байт-код скрипта для сериализации.</param>
    /// <returns>JArray, представляющий полный скрипт.</returns>
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