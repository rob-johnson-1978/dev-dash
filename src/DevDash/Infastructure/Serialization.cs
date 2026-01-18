using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevDash.Infastructure;

internal static class Serializer
{
    private static JsonSerializerOptions? _options;
    private static List<JsonConverter>? _converters;

    public static JsonSerializerOptions Options
    {
        get
        {
            if (_options is null)
            {
                ConfigureSerializerOptions();
            }

            return _options!;
        }
    }

    public static string Serialize(this object source)
    {
        if (_options is null)
        {
            ConfigureSerializerOptions();
        }

        return JsonSerializer.Serialize(source, Options);
    }

    public static T? Deserialize<T>(this string json)
    {
        if (_options is null)
        {
            ConfigureSerializerOptions();
        }

        return JsonSerializer.Deserialize<T>(json, Options);
    }

    public static ImmutableArray<T> DeserializeCollection<T>(this string json)
    {
        if (_options is null)
        {
            ConfigureSerializerOptions();
        }

        return JsonSerializer.Deserialize<ImmutableArray<T>>(json, Options);
    }

    public static void ConfigureSerializerOptions(JsonSerializerOptions? options = null)
    {
        options ??= GetDefaultOptions();
        _options = options;
    }

    public static void AddJsonConverters(params JsonConverter[] converters)
    {
        _converters ??= [];

        foreach (var converter in converters)
        {
            _converters.Add(converter);
        }
    }

    private static JsonSerializerOptions GetDefaultOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,            
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        _converters ??= [];

        foreach (var converter in _converters.Concat([new JsonStringEnumConverter()]))
        {
            options.Converters.Add(converter);
        }

#if DEBUG
        options.WriteIndented = true;
#endif

        return options;
    }
}