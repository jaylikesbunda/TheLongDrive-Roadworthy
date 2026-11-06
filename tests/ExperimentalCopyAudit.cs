using System;
using System.Collections.Generic;
using System.Reflection;

// Offline diagnostic: probes scalar public fields only. It does not establish
// deep-copy fidelity for arrays, nested values, scenes or real saves.
internal static class ExperimentalCopyAudit
{
    public static int Main()
    {
        int checkedFields = 0, problems = 0, records = 0;
        foreach (var listField in typeof(itemDataClass).GetFields())
        {
            Type list = listField.FieldType;
            if (!list.IsGenericType || list.GetGenericTypeDefinition() != typeof(List<>)) continue;
            Type type = list.GetGenericArguments()[0];
            if (!typeof(iid).IsAssignableFrom(type) && !typeof(iid2).IsAssignableFrom(type)) continue;
            var copyMethod = type.GetMethod("Copy", Type.EmptyTypes);
            if (copyMethod == null || !type.IsAssignableFrom(copyMethod.ReturnType)) { Console.WriteLine("NO_COPY " + type.Name); problems++; continue; }
            try
            {
                object source = Activator.CreateInstance(type); records++;
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    object value = null;
                    if (field.FieldType == typeof(int)) value = 123;
                    else if (field.FieldType == typeof(uint)) value = (uint)123;
                    else if (field.FieldType == typeof(float)) value = 12.5f;
                    else if (field.FieldType == typeof(double)) value = 12.5;
                    else if (field.FieldType == typeof(bool)) value = true;
                    else if (field.FieldType == typeof(string)) value = "audit";
                    if (value != null) field.SetValue(source, value);
                }
                object copy = copyMethod.Invoke(source, null);
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    Type t = field.FieldType;
                    if (!t.IsValueType && field.GetValue(source) == null && field.GetValue(copy) != null)
                    {
                        Console.WriteLine("NULL_CHANGED " + type.Name + "." + field.Name); problems++;
                    }
                    if (t != typeof(int) && t != typeof(uint) && t != typeof(float) && t != typeof(double) && t != typeof(bool) && t != typeof(string)) continue;
                    checkedFields++;
                    if (!object.Equals(field.GetValue(source), field.GetValue(copy))) { Console.WriteLine("DIFF " + type.Name + "." + field.Name); problems++; }
                }
            }
            catch (Exception error) { Console.WriteLine("UNTESTED " + type.Name + " " + error.GetType().Name); problems++; }
        }
        Console.WriteLine("Scalar copy audit: records=" + records + " fields=" + checkedFields + " findings=" + problems + "; arrays and nested data not assessed.");
        return 0;
    }
}
