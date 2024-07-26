///Countable

using System.Text.Json;
using System.Text.Json.Serialization;

namespace TournamentSystem;

/** 
    TODO:
       Create an OBF JSON from my format (in progress or finished)
       Create my format JSON for saving tournament
       Serialize select information into my own JSON format for server-sending for API
       Allow more JSON options to affect the output
    **/

interface ITournamentSerializer
{
    public void Serialize(Tournament tournament, string path);
}

interface ITournamentDeserializer
{
    public Tournament Deserialize(string filePath);
}

public class MyFormatConverter : JsonConverter<Tournament>
{
    public override Tournament? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }

    public override void Write(Utf8JsonWriter writer, Tournament value, JsonSerializerOptions options)
    {
        // Enable some leakage through of the input settings
        var jsonSettings = new JsonSerializerOptions
        {
            Converters = {
                new SetConverter(),
                new GameConverter(),
                new JsonStringEnumConverter<Set.SetStatus>(),
                new JsonStringEnumConverter<Game.GameStatus>(),
                new SetWinnerConverter()
            },
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        writer.WriteStartObject();
        // Prevent serializing into dict-like, but just two lists of objects
        var sets = value.Sets.Values;
        var entrants = value.Entrants.Values;
        writer.WritePropertyName("sets");
        JsonSerializer.Serialize(writer, sets, jsonSettings);
        writer.WritePropertyName("entrants");
        JsonSerializer.Serialize(writer, entrants, jsonSettings);
        writer.WritePropertyName("data");
        JsonSerializer.Serialize(writer, value.Data, jsonSettings);
        writer.WriteEndObject();
    }
}

public class MyFormatDeserializer : ITournamentDeserializer
{
    public Tournament Deserialize(string filePath)
    {
        throw new NotImplementedException();
    }
}

public class SetConverter : JsonConverter<Set>
{
    public override Set? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }

    public override void Write(Utf8JsonWriter writer, Set value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var property in value.GetType().GetProperties())
        {
            writer.WritePropertyName(JsonNamingPolicy.CamelCase.ConvertName(property.Name));
            if (property.Name == nameof(value.SetWinnerGoesTo))
            {
                if (value.SetWinnerGoesTo != null)
                {
                    writer.WriteNumberValue(value.SetWinnerGoesTo.SetId);
                }
                else
                {
                    writer.WriteNullValue();
                }
            }
            else if (property.Name == nameof(value.SetLoserGoesTo))
            {
                if (value.SetLoserGoesTo != null)
                {
                    writer.WriteNumberValue(value.SetLoserGoesTo.SetId);
                }
                else
                {
                    writer.WriteNullValue();
                }
            }
            else if (property.Name.Contains("Entrant"))
            {
                if (property.GetValue(value) != null)
                {
                    var e = property.GetValue(value);
                    var eType = e!.GetType().BaseType;
                    var eIdProperty = eType!.GetProperty("EntrantId");
                    var eIdValue = eIdProperty!.GetValue(e!);
                    writer.WriteNumberValue((int)eIdValue!);
                }
                else
                {
                    writer.WriteNullValue();
                }
            }
            else if (property.Name == "SetDecider")
            {
                var sD = property.GetValue(value);
                if (sD is null)
                {
                    writer.WriteNullValue();
                }
                else
                {   // TODO: Replace this with a reflection search for a more appropriate method before defaulting to this
                    // this includes changing how the serializer looks
                    JsonSerializer.Serialize(writer, (Set.IWinnerDecider)sD, options);
                }
            }
            else
            {
                var propertyValue = property.GetValue(value);
                JsonSerializer.Serialize(writer, propertyValue, propertyValue?.GetType() ?? typeof(object), options);
            }
        }
        writer.WriteEndObject();
    }
}

public class SetWinnerConverter : JsonConverter<Set.IWinnerDecider>
{
    public override Set.IWinnerDecider? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }

    public override void Write(Utf8JsonWriter writer, Set.IWinnerDecider value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("type", value.GetType().Name);
        switch (value)
        {
            case Set.BestOfDecider bod:
                writer.WriteNumber("amountOfWinsRequired", bod.AmountOfWinsRequired);
                break;
            default:
                throw new JsonException("Trying to serialize an unknown type");
        }
        writer.WriteEndObject();
    }
}

public class GameConverter : JsonConverter<Game>
{
    public override Game? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }

    public override void Write(Utf8JsonWriter writer, Game value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var property in value.GetType().GetProperties())
        {
            writer.WritePropertyName(JsonNamingPolicy.CamelCase.ConvertName(property.Name));
            if (property.Name.Contains("Entrant"))
            {
                if (property.GetValue(value) != null)
                {
                    var e = property.GetValue(value);
                    var eType = e!.GetType().BaseType;
                    var eIdProperty = eType!.GetProperty("EntrantId");
                    var eIdValue = eIdProperty!.GetValue(e!);
                    writer.WriteNumberValue((int)eIdValue!);
                }
                else
                {
                    writer.WriteNullValue();
                }
            }
            else
            {
                var propertyValue = property.GetValue(value);
                JsonSerializer.Serialize(writer, propertyValue, propertyValue?.GetType() ?? typeof(object), options);
            }
        }
        writer.WriteEndObject();
    }
}
