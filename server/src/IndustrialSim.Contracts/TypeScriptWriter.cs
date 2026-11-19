using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IndustrialSim.Contracts;

/// <summary>
/// Gera os tipos TypeScript do contrato cliente–servidor (D05, D40) a partir dos tipos públicos deste assembly.
/// O arquivo <c>web/src/net/contracts.generated.ts</c> é a saída deste gerador, e um teste garante que os dois
/// não divergem. Para regerar: <c>npm run contracts</c> (na pasta <c>web</c>).
/// </summary>
/// <remarks>
/// Regras:
/// <list type="bullet">
/// <item>Record → <c>interface</c>, com as propriedades na ordem do construtor e os nomes em camelCase, como o
/// <c>System.Text.Json</c> os grava com <see cref="JsonSerializerDefaults.Web"/>.</item>
/// <item>Record com <see cref="JsonPolymorphicAttribute"/> → união dos tipos derivados; cada derivado ganha o
/// campo discriminador com o valor literal.</item>
/// <item>Classe estática com constantes → objeto <c>as const</c>.</item>
/// <item>Números → <c>number</c> (atenção: <c>ulong</c> acima de 2^53 perde precisão no JavaScript);
/// <see cref="Guid"/> e <see cref="DateTimeOffset"/> → <c>string</c>; listas → arrays; anuláveis →
/// <c>| null</c>.</item>
/// </list>
/// </remarks>
public static class TypeScriptWriter
{
    private static readonly JsonNamingPolicy Naming = JsonNamingPolicy.CamelCase;

    public static string Write()
    {
        Type[] types = [.. typeof(TypeScriptWriter).Assembly.GetExportedTypes()
            .Where(t => t.Namespace == typeof(TypeScriptWriter).Namespace && t != typeof(TypeScriptWriter))
            .OrderBy(t => t.Name, StringComparer.Ordinal)];

        // Tipo derivado → (campo discriminador, valor).
        var discriminators = new Dictionary<Type, (string Property, string Value)>();
        foreach (Type type in types)
        {
            if (type.GetCustomAttribute<JsonPolymorphicAttribute>() is { } polymorphic)
            {
                string property = polymorphic.TypeDiscriminatorPropertyName ?? "$type";
                foreach (JsonDerivedTypeAttribute derived in type.GetCustomAttributes<JsonDerivedTypeAttribute>())
                {
                    discriminators[derived.DerivedType] = (property, Convert.ToString(derived.TypeDiscriminator, CultureInfo.InvariantCulture)!);
                }
            }
        }

        var text = new StringBuilder();
        text.Append("// Gerado por IndustrialSim.Contracts.TypeScriptWriter a partir do contrato C# (D05, D40).\n");
        text.Append("// Não edite à mão: rode `npm run contracts` na pasta web.\n");

        foreach (Type type in types)
        {
            text.Append('\n');
            if (type.IsAbstract && type.IsSealed)
            {
                WriteConstants(text, type);
            }
            else if (type.GetCustomAttribute<JsonPolymorphicAttribute>() is not null)
            {
                WriteUnion(text, type);
            }
            else
            {
                WriteInterface(text, type, discriminators.TryGetValue(type, out var d) ? d : null);
            }
        }

        return text.ToString();
    }

    private static void WriteConstants(StringBuilder text, Type type)
    {
        text.Append(CultureInfo.InvariantCulture, $"export const {type.Name} = {{\n");
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral))
        {
            object value = field.GetRawConstantValue()!;
            string literal = value switch
            {
                string s => Quote(s),
                bool b => b ? "true" : "false",
                _ => Convert.ToString(value, CultureInfo.InvariantCulture)!,
            };
            text.Append(CultureInfo.InvariantCulture, $"  {Naming.ConvertName(field.Name)}: {literal},\n");
        }

        text.Append("} as const;\n");
    }

    private static void WriteUnion(StringBuilder text, Type type)
    {
        text.Append(CultureInfo.InvariantCulture, $"export type {type.Name} =\n");
        Type[] derived = [.. type.GetCustomAttributes<JsonDerivedTypeAttribute>().Select(a => a.DerivedType)];
        for (int i = 0; i < derived.Length; i++)
        {
            text.Append(CultureInfo.InvariantCulture, $"  | {derived[i].Name}{(i == derived.Length - 1 ? ";" : string.Empty)}\n");
        }
    }

    private static void WriteInterface(StringBuilder text, Type type, (string Property, string Value)? discriminator)
    {
        text.Append(CultureInfo.InvariantCulture, $"export interface {type.Name} {{\n");
        if (discriminator is var (discriminatorName, discriminatorValue))
        {
            text.Append(CultureInfo.InvariantCulture, $"  {discriminatorName}: {Quote(discriminatorValue)};\n");
        }

        var nullability = new NullabilityInfoContext();
        foreach (ParameterInfo parameter in PrimaryConstructor(type).GetParameters())
        {
            PropertyInfo property = type.GetProperty(parameter.Name!)
                ?? throw new InvalidOperationException($"{type.Name}: o parâmetro '{parameter.Name}' não tem propriedade.");
            NullabilityInfo info = nullability.Create(property);
            text.Append(CultureInfo.InvariantCulture, $"  {Naming.ConvertName(property.Name)}: {TypeName(property.PropertyType, info)};\n");
        }

        text.Append("}\n");
    }

    private static ConstructorInfo PrimaryConstructor(Type type) =>
        type.GetConstructors()
            .Where(c => !(c.GetParameters() is [{ } only] && only.ParameterType == type)) // construtor de cópia do record
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

    private static string TypeName(Type type, NullabilityInfo info)
    {
        string name;
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return ScalarName(underlying) + " | null";
        }

        if (type.IsArray)
        {
            name = ElementName(info.ElementType!) + "[]";
        }
        else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            name = ElementName(info.GenericTypeArguments[0]) + "[]";
        }
        else
        {
            name = ScalarName(type);
        }

        return info.ReadState == NullabilityState.Nullable ? name + " | null" : name;
    }

    private static string ElementName(NullabilityInfo element)
    {
        string name = TypeName(element.Type, element);
        return name.Contains('|', StringComparison.Ordinal) ? $"({name})" : name;
    }

    private static string ScalarName(Type type)
    {
        if (type == typeof(string) || type == typeof(Guid) || type == typeof(DateTimeOffset))
        {
            return "string";
        }

        if (type == typeof(bool))
        {
            return "boolean";
        }

        if (type.IsPrimitive || type == typeof(decimal))
        {
            return "number";
        }

        if (type.Namespace == typeof(TypeScriptWriter).Namespace)
        {
            return type.Name;
        }

        throw new NotSupportedException($"Tipo sem correspondência em TypeScript: {type.FullName}.");
    }

    private static string Quote(string value) => "'" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";
}
