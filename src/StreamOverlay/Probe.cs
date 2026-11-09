using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace StreamOverlay;

/// <summary>Finds the game's types and members by name, so the overlay names them as strings and references no game
/// assembly. Every miss answers null: a renamed field blanks a value instead of throwing.</summary>
internal static class Probe
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly ConcurrentDictionary<string, Type?> types = new();

    /// <summary>The loaded type of that full name, or null in a game that has none.</summary>
    public static Type? Find(string name)
    {
        if (types.TryGetValue(name, out Type? found))
            return found;
        found = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);
        types.TryAdd(name, found);
        return found;
    }

    public static object? Static(Type? type, string name) => type == null ? null : Read(type, null, name);

    public static object? Member(object? target, string name) => target == null ? null : Read(target.GetType(), target, name);

    public static int? Int(object? value) => value switch
    {
        int i => i,
        uint u => (int)u,
        long l => (int)l,
        short s => s,
        ushort us => us,
        byte b => b,
        sbyte sb => sb,
        float f => (int)f,
        double d => (int)d,
        _ => null,
    };

    private static object? Read(Type type, object? target, string name)
    {
        for (Type? t = type; t != null; t = t.BaseType)
        {
            FieldInfo? field = t.GetField(name, Any | BindingFlags.DeclaredOnly);
            if (field != null)
                return field.GetValue(field.IsStatic ? null : target);
            PropertyInfo? property = t.GetProperty(name, Any | BindingFlags.DeclaredOnly);
            if (property != null && property.GetIndexParameters().Length == 0 && property.CanRead)
                return property.GetValue(property.GetGetMethod(true)!.IsStatic ? null : target);
        }
        return null;
    }
}
