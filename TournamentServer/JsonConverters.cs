///Countable

using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
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
/** 
TODO:
    Convert from OBF to my format (more used for reconstructing brackets rather than changing them)
    Read my format JSON and load it

    Have to create from OBF in multiple steps
        Read entrants
        Read Sets
        Read Games
        Update Sets with link
**/

public class MyFormatConverter : JsonConverter<Tournament>
{
    internal record class SetLinksReport
    {
        public int SetId { get; set; }
        public int? Entrant1Id { get; set; }
        public int? Entrant2Id { get; set; }
        public Set.SetStatus Status { get; set; }
        public int? WinnerGoesToId { get; set; }
        public int? LoserGoesToId { get; set; }
        public int? Winner { get; set; }
        public int? Loser { get; set; }
        public List<GameLinksReport>? Games { get; set; }
        public Set.IWinnerDecider? WinnerDecider { get; set; }
        public string? SetName { get; set; }
        public Dictionary<string, string>? Data { get; set; }
    }

    internal record class GameLinksReport
    {
        public int GameNumber { get; set; }
        public int? Entrant1Id { get; set; }
        public int? Entrant2Id { get; set; }
        public int? GameWinnerId { get; set; }
        public Game.GameStatus? Status { get; set; }
        public Dictionary<string, string> Data { get; set; }

        public GameLinksReport()
        {
            Data = new Dictionary<string, string>();
        }
    }

    static internal int? GetNumberOrNull(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null) { return null; }
        return reader.GetInt32();
    }

    public override Tournament? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        List<SetLinksReport> setsToLink = new List<SetLinksReport>();
        Dictionary<string, string> data = new();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    Console.WriteLine("Start of object");
                    break;
                case JsonTokenType.EndObject:
                    Console.WriteLine("End of object");
                    break;
                case JsonTokenType.PropertyName:
                    Console.WriteLine($"Property: {reader.GetString()}");
                    if (reader.GetString() == "sets")
                    {
                        SetConverter sc = new();
                        // Convert all the sets, and link them correctly
                        // We have to do it in this weird way, Set1 may reference Set2, which has not been created yet
                        reader.Read(); // Read the StartArray
                        reader.Read(); // Read the StartObject
                        while (reader.TokenType != JsonTokenType.EndArray)
                        {
                            SetLinksReport report = sc.ReadIntoLinksReport(ref reader, typeof(Set), options);
                            setsToLink.Add(report);
                            while (reader.TokenType != JsonTokenType.StartObject && reader.TokenType != JsonTokenType.EndArray)
                            {
                                reader.Read();
                            }
                        }
                    }
                    else if (reader.GetString() == "entrants")
                    {

                    }
                    else if (reader.GetString() == "data")
                    {
                        var readData = (Dictionary<string, string>?)JsonSerializer.Deserialize(ref reader, typeof(Dictionary<string, string>), options);
                        if (readData is null) { throw new JsonException(); }
                        data = readData;
                    }
                    break;
            }
        }

        var tour = new Tournament();
        foreach (var key in data.Keys)
        {
            tour.ModifyData(key, data[key]);
        }
        return tour;
    }

    public override void Write(Utf8JsonWriter writer, Tournament value, JsonSerializerOptions options)
    {
        // TODO: Enable some leakage through of the input settings
        var jsonSettings = new JsonSerializerOptions
        {
            Converters = {
                new SetConverter(),
                new GameConverter(),
                new JsonStringEnumConverter<Set.SetStatus>(),
                new JsonStringEnumConverter<Game.GameStatus>(),
                new SetWinnerDeciderConverter()
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

public class SetConverter : JsonConverter<Set>
{
    public override Set? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return (Set?)JsonSerializer.Deserialize(ref reader, typeof(Set), options);
    }

    internal MyFormatConverter.SetLinksReport ReadIntoLinksReport(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        int propertiesRead = 0;
        bool readStartToken = false;
        var report = new MyFormatConverter.SetLinksReport();
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            readStartToken = true;
        }
        while (readStartToken && reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                string property = reader.GetString()!;
                reader.Read();
                switch (property)
                {
                    case "setId":
                        report.SetId = reader.GetInt32();
                        break;
                    case "setName":
                        report.SetName = reader.GetString();
                        break;
                    case "entrant1":
                        report.Entrant1Id = MyFormatConverter.GetNumberOrNull(ref reader);
                        break;
                    case "entrant2":
                        report.Entrant2Id = MyFormatConverter.GetNumberOrNull(ref reader);
                        break;
                    case "status":
                        Enum.TryParse(reader.GetString()!, true, out Set.SetStatus statusEnum);
                        report.Status = statusEnum;
                        break;
                    case "setWinnerGoesTo":
                        report.WinnerGoesToId = MyFormatConverter.GetNumberOrNull(ref reader);
                        break;
                    case "setLoserGoesTo":
                        report.LoserGoesToId = MyFormatConverter.GetNumberOrNull(ref reader);
                        break;
                    case "winner":
                        report.Winner = MyFormatConverter.GetNumberOrNull(ref reader);
                        break;
                    case "loser":
                        report.Loser = MyFormatConverter.GetNumberOrNull(ref reader);
                        break;
                    case "games":
                        var gc = new GameConverter();
                        report.Games = new List<MyFormatConverter.GameLinksReport>();
                        reader.Read(); // Read to get to the StartObject
                        while (reader.TokenType != JsonTokenType.EndArray)
                        {
                            MyFormatConverter.GameLinksReport gameReport = gc.ReadIntoLinksReport(ref reader, typeof(Game), options);
                            report.Games.Add(gameReport);
                            reader.Read();
                        }
                        break;
                    case "setDecider":
                        var swdc = new SetWinnerDeciderConverter();
                        report.WinnerDecider = swdc.Read(ref reader, typeof(Set.IWinnerDecider), options);
                        break;
                    case "data":
                        report.Data = JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader, options);
                        break;
                }
                propertiesRead += 1;
            }
            if (propertiesRead == 12) break;
        }
        return report;
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

public class SetWinnerDeciderConverter : JsonConverter<Set.IWinnerDecider>
{
    public override Set.IWinnerDecider? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var properties = JsonSerializer.Deserialize<Dictionary<string, object?>>(ref reader, options);
        if (properties is null) { return null; }
        if (properties.TryGetValue("type", out object? typeValue))
        {
            if (typeValue is null) { throw new NullReferenceException(); }
            bool isAString = false;
            string? val = "";
            try
            {
                val = ((JsonElement)typeValue).GetString();
                isAString = true;
            }
            catch { }
            if (isAString && val is not null)
            {
                var type = Assembly.GetExecutingAssembly().GetTypes().First(type => type.Name == val);

                if (type == typeof(Set.BestOfDecider))
                {
                    if (properties.TryGetValue("amountOfWinsRequired", out object? amount))
                    {
                        if (amount is null) throw new NullReferenceException();
                        int amountOfWins = ((JsonElement)amount).GetInt32();
                        return new Set.BestOfDecider(amountOfWins);
                    }
                }
                else
                {
                    throw new NotImplementedException();
                }
            }
        }
        throw new JsonException();
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

    internal MyFormatConverter.GameLinksReport ReadIntoLinksReport(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        bool readStartToken = false;
        int propertiesRead = 0;
        var report = new MyFormatConverter.GameLinksReport();
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            readStartToken = true;
        }
        while (readStartToken && reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                string property = reader.GetString()!;
                reader.Read();
                switch (property)
                {
                    case "gameNumber":
                        report.GameNumber = reader.GetInt32();
                        break;
                    case "entrant1":
                        report.Entrant1Id = MyFormatConverter.GetNumberOrNull(ref reader);
                        break;
                    case "entrant2":
                        report.Entrant2Id = MyFormatConverter.GetNumberOrNull(ref reader);
                        break;
                    case "gameWinner":
                        report.GameWinnerId = MyFormatConverter.GetNumberOrNull(ref reader);
                        break;
                    case "status":
                        Enum.TryParse(reader.GetString()!, true, out Game.GameStatus statusEnum);
                        report.Status = statusEnum;
                        break;
                    case "data":
                        report.Data = JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader, options)!;
                        break;
                }
                propertiesRead += 1;
            }
            if (propertiesRead == 6) break;
        }
        while (reader.TokenType != JsonTokenType.EndObject) { reader.Read(); }
        return report;
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
