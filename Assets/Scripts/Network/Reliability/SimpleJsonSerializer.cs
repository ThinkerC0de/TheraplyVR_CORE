using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

public static class SimpleJsonSerializer
{
    public static string Serialize(object value)
    {
        StringBuilder builder = new StringBuilder(256);
        WriteValue(builder, value);
        return builder.ToString();
    }

    private static void WriteValue(StringBuilder builder, object value)
    {
        if (value == null)
        {
            builder.Append("null");
            return;
        }

        if (value is string stringValue)
        {
            WriteString(builder, stringValue);
            return;
        }

        if (value is char charValue)
        {
            WriteString(builder, charValue.ToString());
            return;
        }

        if (value is bool boolValue)
        {
            builder.Append(boolValue ? "true" : "false");
            return;
        }

        if (value is DateTime dateTimeValue)
        {
            WriteString(builder, ReliableProtocolUtility.ToUtcString(dateTimeValue));
            return;
        }

        if (value is Guid guidValue)
        {
            WriteString(builder, guidValue.ToString("D"));
            return;
        }

        if (value is byte[] bytes)
        {
            WriteString(builder, Convert.ToBase64String(bytes));
            return;
        }

        if (value is Enum enumValue)
        {
            WriteString(builder, enumValue.ToString());
            return;
        }

        if (IsNumeric(value))
        {
            builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            return;
        }

        if (value is UnityEngine.Object unityObject)
        {
            WriteString(builder, unityObject != null ? unityObject.name : string.Empty);
            return;
        }

        if (value is IDictionary dictionary)
        {
            WriteDictionary(builder, dictionary);
            return;
        }

        if (value is IEnumerable enumerable)
        {
            WriteArray(builder, enumerable);
            return;
        }

        WriteObject(builder, value);
    }

    private static bool IsNumeric(object value)
    {
        switch (Type.GetTypeCode(value.GetType()))
        {
            case TypeCode.Byte:
            case TypeCode.SByte:
            case TypeCode.UInt16:
            case TypeCode.UInt32:
            case TypeCode.UInt64:
            case TypeCode.Int16:
            case TypeCode.Int32:
            case TypeCode.Int64:
            case TypeCode.Decimal:
            case TypeCode.Double:
            case TypeCode.Single:
                return true;
            default:
                return false;
        }
    }

    private static void WriteDictionary(StringBuilder builder, IDictionary dictionary)
    {
        builder.Append('{');
        bool isFirst = true;

        foreach (DictionaryEntry entry in dictionary)
        {
            if (!isFirst)
            {
                builder.Append(',');
            }

            WriteString(builder, Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
            builder.Append(':');
            WriteValue(builder, entry.Value);
            isFirst = false;
        }

        builder.Append('}');
    }

    private static void WriteArray(StringBuilder builder, IEnumerable enumerable)
    {
        builder.Append('[');
        bool isFirst = true;

        foreach (object item in enumerable)
        {
            if (!isFirst)
            {
                builder.Append(',');
            }

            WriteValue(builder, item);
            isFirst = false;
        }

        builder.Append(']');
    }

    private static void WriteObject(StringBuilder builder, object value)
    {
        builder.Append('{');
        bool isFirst = true;
        Type type = value.GetType();

        FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
        for (int i = 0; i < fields.Length; i++)
        {
            FieldInfo field = fields[i];
            if (field.IsNotSerialized)
            {
                continue;
            }

            if (!isFirst)
            {
                builder.Append(',');
            }

            WriteString(builder, field.Name);
            builder.Append(':');
            WriteValue(builder, field.GetValue(value));
            isFirst = false;
        }

        bool includeProperties = IsAnonymousType(type) || fields.Length == 0;
        if (!includeProperties)
        {
            builder.Append('}');
            return;
        }

        PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        for (int i = 0; i < properties.Length; i++)
        {
            PropertyInfo property = properties[i];
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (!isFirst)
            {
                builder.Append(',');
            }

            WriteString(builder, property.Name);
            builder.Append(':');
            WriteValue(builder, property.GetValue(value, null));
            isFirst = false;
        }

        builder.Append('}');
    }

    private static bool IsAnonymousType(Type type)
    {
        return Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute), false) &&
               type.IsGenericType &&
               type.Name.Contains("AnonymousType");
    }

    private static void WriteString(StringBuilder builder, string value)
    {
        builder.Append('"');

        if (!string.IsNullOrEmpty(value))
        {
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                switch (current)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (current < 32)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)current).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(current);
                        }
                        break;
                }
            }
        }

        builder.Append('"');
    }
}
