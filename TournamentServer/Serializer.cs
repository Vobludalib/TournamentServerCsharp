///Countable

using System.Runtime.Serialization.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TournamentSystem;

/** 
    TODO:
       Create an OBF JSON from my format (in progress or finished)
       Create my format JSON for saving tournament
       Serialize select information into my own JSON format for server-sending for API
    **/

interface ITournamentSerializer
{
    public void Serialize(Tournament tournament, string path);
}

public class MyFormatSerializer : ITournamentSerializer
{
    public void Serialize(Tournament tournament, string path)
    {
        var jsonSettings = new JsonSerializerOptions
        {
            Converters = { new SetSerializer(),
            new JsonStringEnumConverter<Set.SetStatus>(),
            new JsonStringEnumConverter<Game.GameStatus>() },
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        using (TextWriter writer = new StreamWriter(path))
        {
            string jsonString = JsonSerializer.Serialize(tournament, jsonSettings);
            writer.Write(jsonString);
        }
    }
}

public class SetSerializer : JsonConverter<Set>
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
                    writer.WriteStringValue(value.SetWinnerGoesTo.SetId);
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
                    writer.WriteStringValue(value.SetLoserGoesTo.SetId);
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
            else
            {
                var propertyValue = property.GetValue(value);
                JsonSerializer.Serialize(writer, propertyValue, propertyValue?.GetType() ?? typeof(object), options);
            }
        }
        writer.WriteEndObject();
    }
}