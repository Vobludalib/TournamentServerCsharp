///Countable

using System.Data;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nito.AsyncEx;

namespace TournamentSystem;

/// <summary>
/// Converter that is used for serializing a tournament into my specific JSON format
/// </summary>
/// <remarks>
/// To use it, pass it into the converters of JsonSerializerOptions when using JsonSerializer.Deserialize/Serialize()
/// </remarks>
public class MyFormatConverter : JsonConverter<Tournament>
{
    /// <summary>
    /// Class used as intermediate step when reconstructing a set from JSON
    /// </summary>
    /// <remarks>
    /// This is necessary, as my JSON format uses IDs to replace nested references - reads IDs and all info into the SetLinksReport which can then be used later to create the sets.
    /// </remarks>
    public record class SetLinksReport
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

    /// <summary>
    /// Class used as intermediate step when reconstructing a game from JSON
    /// </summary>
    /// <remarks>
    /// This is necessary, as my JSON format uses IDs to replace nested references - reads IDs and all info into the GameLinksReport which can then be used later to create the game.
    /// </remarks>
    public record class GameLinksReport
    {
        public int GameNumber { get; set; }
        public int? Entrant1Id { get; set; }
        public int? Entrant2Id { get; set; }
        public int? GameWinnerId { get; set; }
        public Set.Game.GameStatus? Status { get; set; }
        public Dictionary<string, string> Data { get; set; }

        public GameLinksReport()
        {
            Data = new Dictionary<string, string>();
        }
    }

    internal static int? GetNumberOrNull(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }
        return reader.GetInt32();
    }

    /// <summary>
    /// JSONConverter method that is then called when you use JsonSerializer.Deserialize{Tournament}(jsonBody).
    /// </summary>
    /// <remarks>Injects all necessary custom converters - tries to preserve as much of the outside JsonSerializerOptions as possible</remarks>
    /// <param name="reader"></param>
    /// <param name="typeToConvert"></param>
    /// <param name="options"></param>
    /// <returns>Tournament? - Tournament on success, null on failure</returns>
    /// <exception cref="JsonException"></exception>
    /// <exception cref="NullReferenceException"></exception>
    public override Tournament? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        // Preserve all settings, but adding custom converters
        options = new JsonSerializerOptions(options)
        {
            Converters =
            {
                new SetConverter(),
                new GameConverter(),
                new JsonStringEnumConverter<Tournament.TournamentStatus>(),
                new JsonStringEnumConverter<Set.SetStatus>(),
                new JsonStringEnumConverter<Set.Game.GameStatus>(),
                new SetWinnerDeciderConverter(),
                new EntrantConverter()
            }
        };

        List<SetLinksReport> setsToLink = new List<SetLinksReport>();
        List<Entrant> entrants = new();
        Dictionary<string, string> data = new();
        Tournament.TournamentStatus status = Tournament.TournamentStatus.Setup;
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    if (reader.GetString() == "sets")
                    {
                        SetLinksConverter sc = new();
                        // Convert all the sets, and link them correctly
                        // We have to do it in this weird way, Set1 may reference Set2, which has not been created yet
                        reader.Read(); // Read the StartArray
                        reader.Read(); // Read the StartObject
                        while (reader.TokenType != JsonTokenType.EndArray)
                        {
                            SetLinksReport report =
                                sc.Read(ref reader, typeof(Set), options)
                                ?? throw new JsonException();
                            setsToLink.Add(report);
                            while (
                                reader.TokenType != JsonTokenType.StartObject
                                && reader.TokenType != JsonTokenType.EndArray
                            )
                            {
                                reader.Read();
                            }
                        }
                    }
                    else if (reader.GetString() == "entrants")
                    {
                        reader.Read(); // Read the StartArray
                        reader.Read(); // Read the StartObject
                        while (reader.TokenType != JsonTokenType.EndArray)
                        {
                            var entrant = JsonSerializer.Deserialize(
                                ref reader,
                                typeof(Entrant),
                                options
                            );
                            if (entrant is null)
                                throw new JsonException();
                            entrants.Add((Entrant)entrant);
                            while (
                                reader.TokenType != JsonTokenType.StartObject
                                && reader.TokenType != JsonTokenType.EndArray
                            )
                            {
                                reader.Read();
                            }
                        }
                    }
                    else if (reader.GetString() == "data")
                    {
                        var readData = (Dictionary<string, string>?)
                            JsonSerializer.Deserialize(
                                ref reader,
                                typeof(Dictionary<string, string>),
                                options
                            );
                        if (readData is null)
                        {
                            throw new JsonException();
                        }
                        data = readData;
                    }
                    else if (reader.GetString() == "status")
                    {
                        var readStatus = JsonSerializer.Deserialize(
                            ref reader,
                            typeof(Tournament.TournamentStatus),
                            options
                        );
                        if (readStatus is null)
                        {
                            throw new JsonException();
                        }
                        status = (Tournament.TournamentStatus)readStatus;
                    }
                    break;
            }
        }

        var tour = new Tournament();

        foreach (var key in data.Keys)
        {
            // Handling here for what should be a synchronous call - there are no contenders for the locks, as the tournament, sets etc. have just been created
            AsyncContext.Run(() => tour.AddOrEditDataAsync(key, data[key]));
        }

        Dictionary<int, Entrant> entrantDict = new();
        foreach (var entrant in entrants)
        {
            AsyncContext.Run(() => tour.AddEntrantAsync(entrant));
            entrantDict.Add(entrant.EntrantId, entrant);
            if (entrant is TeamEntrant teamEntrant)
            {
                foreach (IndividualEntrant iE in teamEntrant.IndividualEntrants)
                {
                    if (AsyncContext.Run(() => tour.TryGetEntrantAsync(iE.EntrantId)) is null)
                    {
                        AsyncContext.Run(() => tour.AddEntrantAsync(iE));
                        entrantDict.Add(iE.EntrantId, iE);
                    }
                }
            }
        }

        // Everything is read, now it's time to go through the set links reconstruction process
        // First, let's generate all the Set objects with the correct Ids, then we just put in all the relevant information,
        Dictionary<int, Set> sets = new();
        foreach (var setReport in setsToLink)
        {
            sets.Add(setReport.SetId, new Set(setReport.SetId));
        }
        // We can do this now, as we can change the Ids into the object references (which we know now exist)
        foreach (var setReport in setsToLink)
        {
            if (!sets.TryGetValue(setReport.SetId, out Set? set))
            {
                throw new NullReferenceException();
            }
            set.FillSetFromReport(sets, entrantDict, setReport);
        }
        foreach (Set set in sets.Values)
        {
            AsyncContext.Run(() => tour.AddSetAsync(set));
        }

        tour.SetStatus(status);
        if (AsyncContext.Run(() => tour.GetStatusAsync()) != Tournament.TournamentStatus.Setup)
        {
            if (!AsyncContext.Run(() => tour.VerifyStructureAsync()))
            {
                throw new JsonException("Tournament structure of JSON being loaded is not valid.");
            }
        }

        return tour;
    }

    /// <summary>
    /// JsonConverter method that is called when you use JsonSerializer.Serialize{Tournament}(Tournament tournament).
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <param name="options"></param>
    public override void Write(
        Utf8JsonWriter writer,
        Tournament value,
        JsonSerializerOptions options
    )
    {
        options = new JsonSerializerOptions(options)
        {
            Converters =
            {
                new SetConverter(),
                new GameConverter(),
                new JsonStringEnumConverter<Tournament.TournamentStatus>(),
                new JsonStringEnumConverter<Set.SetStatus>(),
                new JsonStringEnumConverter<Set.Game.GameStatus>(),
                new SetWinnerDeciderConverter(),
                new EntrantConverter()
            },
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        writer.WriteStartObject();

        // Prevent serializing into dict-like, but just two lists of objects
        var sets = value.Sets.Values;
        writer.WritePropertyName("sets");
        JsonSerializer.Serialize(writer, sets, options);

        var entrants = value.Entrants.Values;
        Dictionary<int, Entrant> topLevelEntrants = new();
        // We want to have nesting so as to not have id-based writing in the entrants
        // this means removing any entrants that are part of a team
        // First we put all entrants in a dict by their ID
        foreach (Entrant entrant in entrants)
        {
            topLevelEntrants.Add(entrant.EntrantId, entrant);
        }
        // We go over all the teamEntrants, and remove all their individual entrants from the Dict
        foreach (Entrant entrant in entrants)
        {
            if (entrant is TeamEntrant)
            {
                foreach (Entrant lowerLevelEntrant in ((TeamEntrant)entrant).IndividualEntrants)
                {
                    topLevelEntrants.Remove(lowerLevelEntrant.EntrantId);
                }
            }
        }
        // Now, this gives all entrants still left in the dict are top-level entrants.
        entrants = topLevelEntrants.Values;
        writer.WritePropertyName("entrants");
        JsonSerializer.Serialize(writer, entrants, options);

        writer.WritePropertyName("data");
        JsonSerializer.Serialize(writer, value.Data, options);

        writer.WritePropertyName("status");
        JsonSerializer.Serialize(writer, value.Status, options);
        writer.WriteEndObject();
    }
}

/// <summary>
/// JSONConverter for reading JSON into SetLinksReport
/// </summary>
/// <remarks>
/// There is no write method, as you should never be serializing SetLinksReports - convert to a proper Set first, then serialize.
/// </remarks>
public class SetLinksConverter : JsonConverter<MyFormatConverter.SetLinksReport>
{
    /// <summary>
    /// JsonConverter method used when deserializing JSON into SetLinksReport
    /// </summary>
    /// <remarks>
    /// Uses property number counting - if adding or removing a property in the future, don't forget to update the figure or figure out a more elegant solution.
    /// At this current stage, this means that to deserialize JSON into a SetLinksReport, all properties must be appear.
    /// </remarks>
    /// <param name="reader"></param>
    /// <param name="typeToConvert"></param>
    /// <param name="options"></param>
    /// <returns>SetLinksReport? - SetLinkReport on success, null on failure</returns>
    /// <exception cref="JsonException"></exception>
    public override MyFormatConverter.SetLinksReport? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
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
                        var gc = new GameLinksConverter();
                        report.Games = new List<MyFormatConverter.GameLinksReport>();
                        reader.Read(); // Read to get to the StartObject
                        while (reader.TokenType != JsonTokenType.EndArray)
                        {
                            MyFormatConverter.GameLinksReport gameReport =
                                gc.Read(
                                    ref reader,
                                    typeof(MyFormatConverter.GameLinksReport),
                                    options
                                ) ?? throw new JsonException();
                            report.Games.Add(gameReport);
                        }
                        break;
                    case "setDecider":
                        var swdc = new SetWinnerDeciderConverter();
                        report.WinnerDecider = swdc.Read(
                            ref reader,
                            typeof(Set.IWinnerDecider),
                            options
                        );
                        break;
                    case "data":
                        report.Data = JsonSerializer.Deserialize<Dictionary<string, string>>(
                            ref reader,
                            options
                        );
                        break;
                }
                propertiesRead += 1;
            }
            if (propertiesRead == 12)
                break;
        }
        return report;
    }

    /// <summary>
    /// Not implemented - you should be serializing Set objects, not SetLinksReport objects.
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <param name="options"></param>
    /// <exception cref="NotImplementedException"></exception>
    public override void Write(
        Utf8JsonWriter writer,
        MyFormatConverter.SetLinksReport value,
        JsonSerializerOptions options
    )
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// JSONConverter for writing Sets to JSON
/// </summary>
/// <remarks>
/// There is no read method, as you should never be deserializing into a Set directly - use SetLinksReport for that, as my format uses Id references instead of nesting.
/// </remarks>
public class SetConverter : JsonConverter<Set>
{
    /// <summary>
    /// Not implemented - you should be deserializing into SetLinksReport.
    /// </summary>
    /// <param name="reader"></param>
    /// <param name="typeToConvert"></param>
    /// <param name="options"></param>
    /// <returns></returns>
    public override Set? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        return (Set?)JsonSerializer.Deserialize(ref reader, typeof(Set), options);
    }

    /// <summary>
    /// JsonConverter method used when serializing sets into JSON.
    /// </summary>
    /// <remarks>
    /// Uses Ids to replace nested references for entrants and other sets.
    ///
    /// Currently, uses serialization of IWinnerDeciders by casting them into IWinnerDecider, and calling SetWinnerDeciderConverter methods.
    /// In the future, can either be replaced by a reflection search for an appropriate method to use instead given a custom class that implements IWinnerDecider, or expanding SetWinnerDeciderConverter code in this assembly.
    /// </remarks>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <param name="options"></param>
    public override void Write(Utf8JsonWriter writer, Set value, JsonSerializerOptions options)
    {
        var newOptions = new JsonSerializerOptions(options);
        if (
            options
                .Converters
                .Where(t1 => t1.GetType() == typeof(SetWinnerDeciderConverter))
                .Count() < 1
        )
        {
            newOptions.Converters.Add(new SetWinnerDeciderConverter());
        }
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
            else if (
                property.Name.Contains("Entrant")
                || property.Name == "Winner"
                || property.Name == "Loser"
            )
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
                { // TODO: Replace this with a reflection search for a more appropriate method before defaulting to this
                    // this includes changing how the serializer looks
                    JsonSerializer.Serialize(writer, (Set.IWinnerDecider)sD, newOptions);
                }
            }
            else
            {
                var propertyValue = property.GetValue(value);
                JsonSerializer.Serialize(
                    writer,
                    propertyValue,
                    propertyValue?.GetType() ?? typeof(object),
                    newOptions
                );
            }
        }
        writer.WriteEndObject();
    }
}

/// <summary>
/// JsonConverter for serializing/deserializing IWinnerDecider objects
/// </summary>
public class SetWinnerDeciderConverter : JsonConverter<Set.IWinnerDecider>
{
    /// <summary>
    /// Method for reading JSON into IWinnerDecider
    /// </summary>
    /// <remarks>
    /// Given that we need the type property before we decide what properties to expect, we read the entire object first, only then parsing it appropriately. This differs from how other Read methods work.
    /// When reading "type" property, will do a reflection search in the executing assembly to try to find the correct type to instantiate. Can be changed if we want to implement custom IWinnerDeciders outside of this assembly.
    /// </remarks>
    /// <param name="reader"></param>
    /// <param name="typeToConvert"></param>
    /// <param name="options"></param>
    /// <returns>IWinnerDecider? - IWinnerDecider on success, false on failure</returns>
    /// <exception cref="NullReferenceException"></exception>
    /// <exception cref="NotImplementedException"></exception>
    /// <exception cref="JsonException"></exception>
    public override Set.IWinnerDecider? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        var properties = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            ref reader,
            options
        );
        if (properties is null)
        {
            return null;
        }
        if (properties.TryGetValue("type", out object? typeValue))
        {
            if (typeValue is null)
            {
                throw new NullReferenceException();
            }
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
                var type = Assembly
                    .GetExecutingAssembly()
                    .GetTypes()
                    .First(type => type.Name == val);

                if (type == typeof(Set.BestOfDecider))
                {
                    if (properties.TryGetValue("amountOfWinsRequired", out object? amount))
                    {
                        if (amount is null)
                            throw new NullReferenceException();
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

    /// <summary>
    /// Method for serializing IWinnerDeciders into JSON
    /// </summary>
    /// <remarks>
    /// Currently only supports BestOfDecider, with other IWinnerDeciders throwing an exception. This can be changed so that instead it simply serializes all the properties using reflection. I opted not to do this for now, given we have
    /// all the control over IWinnerDeciders 'inhouse', but if custom IWinnerDeciders pop up outside what we know, we can rethink this (along with letting custom IWinnerDeciders have their own converters).
    /// </remarks>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <param name="options"></param>
    /// <exception cref="JsonException"></exception>
    public override void Write(
        Utf8JsonWriter writer,
        Set.IWinnerDecider value,
        JsonSerializerOptions options
    )
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

/// <summary>
/// JsonConverter for deserializing JSON into GameLinksReport
/// </summary>
public class GameLinksConverter : JsonConverter<MyFormatConverter.GameLinksReport>
{
    /// <summary>
    /// Method for deserializing JSON into GameLinksReport
    /// </summary>
    /// <remarks>
    /// Use this when reading serialized games, this handles Id-based referencing that is used in my JSON format.
    /// </remarks>
    /// <param name="reader"></param>
    /// <param name="typeToConvert"></param>
    /// <param name="options"></param>
    /// <returns>GameLinksReport? - GameLinksReport on success, null on failure</returns>
    public override MyFormatConverter.GameLinksReport? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
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
                        Enum.TryParse(
                            reader.GetString()!,
                            true,
                            out Set.Game.GameStatus statusEnum
                        );
                        report.Status = statusEnum;
                        break;
                    case "data":
                        report.Data = JsonSerializer.Deserialize<Dictionary<string, string>>(
                            ref reader,
                            options
                        )!;
                        break;
                }
                propertiesRead += 1;
            }
            if (propertiesRead == 6)
                break;
        }
        while (reader.TokenType != JsonTokenType.EndObject)
        {
            reader.Read();
        }
        reader.Read();
        return report;
    }

    /// <summary>
    /// Not implemented - you should be serializing Game objects, not GameLinksReport objects.
    /// </summary>
    /// <param name="reader"></param>
    /// <param name="typeToConvert"></param>
    /// <param name="options"></param>
    public override void Write(
        Utf8JsonWriter writer,
        MyFormatConverter.GameLinksReport value,
        JsonSerializerOptions options
    )
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// JsonConverter for serializing Game objects to JSON
/// </summary>
public class GameConverter : JsonConverter<Set.Game>
{
    /// <summary>
    /// Not implemented - you should be deserializing into GameLinksReport
    /// </summary>
    /// <param name="reader"></param>
    /// <param name="typeToConvert"></param>
    /// <param name="options"></param>
    /// <exception cref="NotImplementedException"></exception>
    public override Set.Game? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// JsonConverter method for serializing Game into JSON
    /// </summary>
    /// <remarks>
    /// Replaces nested references with Ids as per my JSON format.
    /// </remarks>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <param name="options"></param>
    public override void Write(Utf8JsonWriter writer, Set.Game value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var property in value.GetType().GetProperties())
        {
            writer.WritePropertyName(JsonNamingPolicy.CamelCase.ConvertName(property.Name));
            if (property.Name.Contains("Entrant") || property.Name.Contains("Winner"))
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
                JsonSerializer.Serialize(
                    writer,
                    propertyValue,
                    propertyValue?.GetType() ?? typeof(object),
                    options
                );
            }
        }
        writer.WriteEndObject();
    }
}

/// <summary>
/// JsonConverter for Entrant objects
/// </summary>
public class EntrantConverter : JsonConverter<Entrant>
{
    /// <summary>
    /// JsonConverter method for deserializing JSON into Entrant
    /// </summary>
    /// <remarks>
    /// Has to read the entire object first so it can retrieve the "type" property, then select the appropriate properties it needs to find based on
    /// whether it is an IndividualEntrant or TeamEntrant.
    /// </remarks>
    /// <param name="reader"></param>
    /// <param name="typeToConvert"></param>
    /// <param name="options"></param>
    /// <returns>Entrant? - Entrant on success, null on failure</returns>
    /// <exception cref="JsonException"></exception>
    public override Entrant? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        var properties = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            ref reader,
            options
        );
        if (properties is null)
        {
            return null;
        }
        if (properties.TryGetValue("type", out object? typeValue))
        {
            if (typeValue is null)
            {
                throw new JsonException();
            }
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
                var type = Assembly
                    .GetExecutingAssembly()
                    .GetTypes()
                    .First(type => type.Name == val);
                if (!properties.TryGetValue("entrantId", out object? idObj) || idObj is null)
                {
                    throw new JsonException();
                }
                int id = ((JsonElement)idObj).GetInt32();
                Dictionary<string, string> data = new();
                if (
                    properties.TryGetValue("entrantData", out object? dataObj)
                    && dataObj is not null
                )
                {
                    data = JsonParseHelper.ParseJsonElementIntoDict((JsonElement)dataObj);
                }
                if (type == typeof(IndividualEntrant))
                {
                    properties.TryGetValue("individualName", out object? indNameObj);
                    if (indNameObj is null)
                        throw new JsonException();

                    var nameDict = JsonParseHelper.ParseJsonElementIntoDict(
                        (JsonElement)indNameObj
                    );
                    if (nameDict.TryGetValue("tag", out string? tag) && tag is not null)
                    {
                        return new IndividualEntrant(id, tag, data);
                    }
                    else if (
                        nameDict.TryGetValue("firstName", out string? firstName)
                        && nameDict.TryGetValue("lastName", out string? lastName)
                    )
                    {
                        if (firstName is null || lastName is null)
                        {
                            throw new JsonException();
                        }
                        return new IndividualEntrant(id, firstName, lastName, data);
                    }
                }
                else if (type == typeof(TeamEntrant))
                {
                    properties.TryGetValue("teamName", out object? teamNameObj);
                    if (teamNameObj is null)
                        throw new JsonException();
                    string teamName =
                        ((JsonElement)teamNameObj).GetString() ?? throw new JsonException();

                    properties.TryGetValue(
                        "individualEntrants",
                        out object? individualEntrantsObjects
                    );
                    if (individualEntrantsObjects is null)
                        throw new JsonException();
                    List<IndividualEntrant> individualEntrants = new();
                    foreach (var obj in ((JsonElement)individualEntrantsObjects).EnumerateArray())
                    {
                        individualEntrants.Add(ReadIndividualEntrant(obj));
                    }
                    return new TeamEntrant(id, teamName, individualEntrants);
                }
                else
                {
                    throw new JsonException();
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Method used for parsing a given JsonElement (here being used to pass the representation of a Json object) into IndividualEntrant
    /// </summary>
    /// <remarks>
    /// The checks for whether or not the JsonElement truly represents an IndividualEntrant are done elsewhere.
    /// </remarks>
    /// <param name="jE"></param>
    /// <returns></returns>
    /// <exception cref="JsonException"></exception>
    private IndividualEntrant ReadIndividualEntrant(JsonElement jE)
    {
        Dictionary<string, JsonElement> properties = new();
        foreach (var val in jE.EnumerateObject())
        {
            properties.Add(val.Name, val.Value);
        }
        if (!properties.TryGetValue("entrantId", out JsonElement idObj))
        {
            throw new JsonException();
        }
        int id = idObj.GetInt32();
        Dictionary<string, string> data = new();
        if (properties.TryGetValue("entrantData", out JsonElement dataObj))
        {
            data = JsonParseHelper.ParseJsonElementIntoDict(dataObj);
        }
        if (!properties.TryGetValue("individualName", out JsonElement indNameObj))
        {
            throw new JsonException();
        }

        var nameDict = JsonParseHelper.ParseJsonElementIntoDict(indNameObj);
        if (nameDict.TryGetValue("tag", out string? tag) && tag is not null)
        {
            return new IndividualEntrant(id, tag, data);
        }
        else if (
            nameDict.TryGetValue("firstName", out string? firstName)
            && nameDict.TryGetValue("lastName", out string? lastName)
        )
        {
            if (firstName is null || lastName is null)
            {
                throw new JsonException();
            }
            return new IndividualEntrant(id, firstName, lastName, data);
        }
        throw new JsonException();
    }

    /// <summary>
    /// JsonConverter method for serializing an Entrant object into JSON
    /// </summary>
    /// <remarks>
    /// Different handling done here for IndividualEntrant vs TeamEntrant
    /// </remarks>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <param name="options"></param>
    /// <exception cref="JsonException"></exception>
    public override void Write(Utf8JsonWriter writer, Entrant value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case IndividualEntrant _:
                writer.WriteString("type", "IndividualEntrant");
                break;
            case TeamEntrant _:
                writer.WriteString("type", "TeamEntrant");
                break;
        }
        // Placing Id as first element, so it is serialized first
        var properties = value.GetType().GetProperties();
        var id = properties.Where(x => x.Name == "EntrantId");
        var rest = properties.Where(x => x.Name != "EntrantId");
        var sortedProperties = id.Concat(rest);
        foreach (var property in sortedProperties)
        {
            if (property.Name.Contains("Name"))
            {
                switch (value)
                {
                    case IndividualEntrant ie:
                        writer.WritePropertyName("individualName");
                        switch (ie.EntrantName)
                        {
                            case IndividualEntrant.Tag tag:
                                writer.WriteStartObject();
                                writer.WriteString("tag", tag._tag);
                                writer.WriteEndObject();
                                break;
                            case IndividualEntrant.FullName fn:
                                writer.WriteStartObject();
                                writer.WriteString("firstName", fn._firstName);
                                writer.WriteString("lastName", fn._lastName);
                                writer.WriteEndObject();
                                break;
                        }
                        break;
                    case TeamEntrant te:
                        writer.WriteString("teamName", te.TeamName);
                        break;
                }
            }
            else if (property.Name.Contains("IndividualEntrants"))
            {
                writer.WritePropertyName(JsonNamingPolicy.CamelCase.ConvertName(property.Name));
                var propertyValue = property.GetValue(value);
                if (propertyValue is null)
                {
                    throw new JsonException();
                }
                var listOfIndividuals = (List<IndividualEntrant>)propertyValue;
                EntrantConverter ec = new();
                writer.WriteStartArray();
                foreach (Entrant entrant in listOfIndividuals)
                {
                    ec.Write(writer, entrant, options);
                }
                writer.WriteEndArray();
            }
            else
            {
                writer.WritePropertyName(JsonNamingPolicy.CamelCase.ConvertName(property.Name));
                var propertyValue = property.GetValue(value);
                JsonSerializer.Serialize(
                    writer,
                    propertyValue,
                    propertyValue?.GetType() ?? typeof(object),
                    options
                );
            }
        }
        writer.WriteEndObject();
    }
}

internal static class JsonParseHelper
{
    /// <summary>
    /// Helper method I used for working with JsonElements that parse it into a dictionary of properties and string values.
    /// </summary>
    /// <param name="jE"></param>
    /// <returns></returns>
    /// <exception cref="JsonException"></exception>
    internal static Dictionary<string, string> ParseJsonElementIntoDict(JsonElement jE)
    {
        var dict = new Dictionary<string, string>();
        var jsonObj = jE.EnumerateObject();
        foreach (JsonProperty property in jsonObj)
        {
            dict[property.Name] = property.Value.GetString() ?? throw new JsonException();
        }
        return dict;
    }
}
