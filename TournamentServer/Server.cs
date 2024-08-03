///Countable

using System.Text.Json;
using System.Text.Json.Serialization;
using TournamentSystem;

class TournamentServer()
{
    public static void Main(String[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var app = builder.Build();

        var sh = new ServerHandler();

        app.MapGet("/", () => "Hello");

        app.MapGet("/entrant/{id}", (int id) => sh.GetEntrantByIdAsync(id));
        app.MapGet("/set/{id}", (int id) => sh.GetSetByIdAsync(id));
        app.MapGet("/data", () => sh.GetAllDataAsync());
        app.MapGet("/data/{key}", (string key) => sh.GetDataByKeyAsync(key));
        app.MapGet("/status", () => sh.GetStatusAsync());

        var filePath = "./test.json";
        var mf = new MyFormatConverter();
        byte[] jsonData = File.ReadAllBytes(filePath);
        Utf8JsonReader reader = new(jsonData);
        var reconstructedTour = mf.Read(
            ref reader,
            typeof(Tournament),
            new JsonSerializerOptions()
        );

        sh.tournament = reconstructedTour;

        app.Run();
    }

    /**
    TODO:
        Create all the endpoints with needed information
        Create all the endpoints for changing games

        GET:
            Whole Tournament JSON
            Tournament data + status only
            Set JSON
            Set info only
            Game JSON
            Entrant JSON
        POST:
            Updating info in tournament + status transitions
            Updating info in set + status transitions
            Updating info in game + status transitions
            Creating/updating/deleting entrant during tournament setup
    **/

    public class ServerHandler
    {
        public MyFormatConverter myFormatConverter;
        public SetConverter setConverter;
        public EntrantConverter entrantConverter;
        public Tournament? tournament;

        public ServerHandler()
        {
            myFormatConverter = new();
            setConverter = new();
            entrantConverter = new();
        }

        //public async Task<IResult> GetTournamentAsync()
        //{
        //    if (tournament is null)
        //    {
        //        return Results.BadRequest();
        //    }
        //    var set = await tournament.TryGetSetAsync();
        //    if (set is null)
        //    {
        //        return Results.NotFound();
        //    }
        //    var json = JsonSerializer.Serialize(
        //        set,
        //        new JsonSerializerOptions { WriteIndented = true, Converters = { setConverter } }
        //    );
        //    return Results.Content(json, "application/json");
        //}

        public async Task<IResult> GetSetByIdAsync(int id)
        {
            if (tournament is null)
            {
                return Results.BadRequest();
            }
            var set = await tournament.TryGetSetAsync(id);
            if (set is null)
            {
                return Results.NotFound();
            }
            var json = JsonSerializer.Serialize(
                set,
                new JsonSerializerOptions { WriteIndented = true, Converters = { setConverter } }
            );
            return Results.Content(json, "application/json");
        }

        public async Task<IResult> GetEntrantByIdAsync(int id)
        {
            if (tournament is null)
            {
                return Results.BadRequest();
            }
            var entrant = await tournament.TryGetEntrantAsync(id);
            if (entrant is null)
            {
                return Results.NotFound();
            }
            var json = JsonSerializer.Serialize(
                entrant,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { entrantConverter }
                }
            );
            return Results.Content(json, "application/json");
        }

        public async Task<IResult> GetAllDataAsync()
        {
            if (tournament is null)
            {
                return Results.BadRequest();
            }
            var data = await tournament.GetAllDataAsync();
            var json = JsonSerializer.Serialize(
                data,
                new JsonSerializerOptions { WriteIndented = true, }
            );
            return Results.Content(json, "application/json");
        }

        public async Task<IResult> GetDataByKeyAsync(string key)
        {
            if (tournament is null)
            {
                return Results.BadRequest();
            }
            var data = await tournament.TryGetDataAsync(key);
            if (data is null)
            {
                return Results.NotFound();
            }
            var json = JsonSerializer.Serialize(
                data,
                new JsonSerializerOptions { WriteIndented = true, }
            );
            return Results.Content(json, "application/json");
        }

        public async Task<IResult> GetStatusAsync()
        {
            if (tournament is null)
            {
                return Results.BadRequest();
            }
            var status = await tournament.GetStatusAsync();
            var json = JsonSerializer.Serialize(
                status,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter<Tournament.TournamentStatus>() }
                }
            );
            return Results.Content(json, "application/json");
        }
    }
}
