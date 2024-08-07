//Countable

using System.Text.Json;
using TournamentSystem;

class TournamentServer()
{
    /// <summary>
    /// Allows passing of one command line argument - that being the path of the JSON of the tournament to load on startup.
    /// </summary>
    /// <param name="args"></param>
    /// <exception cref="ArgumentException"></exception>
    public static void Main(String[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var app = builder.Build();

        var sh = new ServerHandler();

        var redirect = () =>
        {
            return Results.Redirect("https://github.com/Vobludalib/TournamentServerCsharp");
        };

        app.MapGet("/", redirect);
        app.MapGet("/tournament", () => sh.GetTournamentAsync());
        app.MapPost(
            "/tournament",
            (Delegate)((HttpContext context) => sh.HandleTournamentPostAsync(context))
        );
        app.MapGet("/entrant/{id}", (int id) => sh.GetEntrantByIdAsync(id));
        app.MapGet("/set/{id}", (int id) => sh.GetSetByIdAsync(id));
        app.MapGet("/data", () => sh.GetAllDataAsync());
        app.MapGet("/data/{key}", (string key) => sh.GetDataByKeyAsync(key));
        app.MapGet("/status", () => sh.GetStatusAsync());
        app.MapPost(
            "/tournament/transitionTo/{status}",
            (string status) => sh.HandleTournamentStatusTransitionAsync(status)
        );
        app.MapPost(
            "/set/{id}/addGame",
            (int id, HttpContext context) => sh.HandleGamePostAsync(id, context)
        );
        app.MapPost("/set/{id}/progress", (int id) => sh.UpdateSetBasedOnGamesAsync(id));
        app.MapPost(
            "set/{id}/transition/{state}",
            (int id, string state) => sh.HandleSetStatusTransitionAsync(id, state)
        );
        app.MapPost(
            "/set/{id}/moveWinnerAndLoser",
            (int id) => sh.HandleSetMoveWinnersAndLoserAsync(id)
        );
        app.MapPost(
            "/set/{setId}/game/{gameId}/transitionTo/{state}",
            (int setId, int gameId, string state) =>
                sh.HandleGameStatusTransitionAsync(setId, gameId, state)
        );
        app.MapPost(
            "/set/{setId}/game/{gameId}/setWinner/{entrantId}",
            (int setId, int gameId, int entrantId) =>
                sh.HandleGameUpdateWinnerAsync(setId, gameId, entrantId)
        );

        //First clarg is path to JSON to load - if nothing is passed, the no JSON is read

        if (args.Length > 1)
        {
            throw new ArgumentException("Too many command line arguments!");
        }

        bool loadFromArgs = false;
        if (args.Length == 1 && File.Exists(args[0]) && Path.GetExtension(args[0]) == ".json")
        {
            loadFromArgs = true;
        }

        if (loadFromArgs)
        {
            var filePath = args[0];
            var mf = new MyFormatConverter();
            byte[] jsonData = File.ReadAllBytes(filePath);
            Utf8JsonReader reader = new(jsonData);
            var reconstructedTour = mf.Read(
                ref reader,
                typeof(Tournament),
                new JsonSerializerOptions()
            );

            sh.tournament = reconstructedTour;
        }

        app.Run();
    }
}
