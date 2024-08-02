///Countable

using System.Text.Json;
using TournamentSystem;

class TournamentServer()
{
    public static void Main(String[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var app = builder.Build();

        var sh = new ServerHandler();

        app.MapGet("/", () => "Hello");

        app.MapGet("/entrant/{id}", (int id) => sh.GetEntrantById(id));

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
        public EntrantConverter entrantConverter;
        public Tournament? tournament;

        public ServerHandler()
        {
            myFormatConverter = new();
            entrantConverter = new();
        }

        public async Task<IResult> GetEntrantById(int id)
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
    }
}
