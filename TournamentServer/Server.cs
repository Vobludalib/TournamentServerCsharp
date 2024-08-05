///Countable

using System.Text;
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

        app.MapGet("/tournament", () => sh.GetTournamentAsync());
        app.MapPost(
            "/tournament",
            (Delegate)((HttpContext context) => sh.HandleTournamentPost(context))
        );
        app.MapGet("/entrant/{id}", (int id) => sh.GetEntrantByIdAsync(id));
        app.MapGet("/set/{id}", (int id) => sh.GetSetByIdAsync(id));
        app.MapGet("/data", () => sh.GetAllDataAsync());
        app.MapGet("/data/{key}", (string key) => sh.GetDataByKeyAsync(key));
        app.MapGet("/status", () => sh.GetStatusAsync());

        //First clarg is path to JSON to load - if nothing is passed, the no JSON is read

        if (args.Length > 1)
        {
            throw new ArgumentException();
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

        public async Task<IResult> GetTournamentAsync()
        {
            if (tournament is null)
            {
                return Results.BadRequest("No tournament exists");
            }
            var setLock = await tournament.LockHandler.LockSetsReadAsync();
            var entrantsLock = await tournament.LockHandler.LockEntrantsReadAsync();
            var dataLock = await tournament.LockHandler.LockDataReadAsync();
            var statusLock = await tournament.LockHandler.LockStatusReadAsync();
            var allSets = tournament.Sets.Values.ToList();
            // Sorting by setId to guarantee consistent locking order
            allSets.Sort((x1, x2) => x1.SetId.CompareTo(x2.SetId));
            List<IDisposable> setLocks = new();
            foreach (Set set in allSets)
            {
                setLocks.Add(await set.LockHandler.LockSetReadAsync());
            }
            try
            {
                var json = JsonSerializer.Serialize(
                    tournament,
                    new JsonSerializerOptions()
                    {
                        Converters = { myFormatConverter },
                        WriteIndented = true
                    }
                );
                return Results.Content(json, "application/json");
            }
            finally
            {
                setLocks.Reverse();
                foreach (var lockToUnlock in setLocks)
                {
                    lockToUnlock.Dispose();
                }
                tournament.LockHandler.UnlockStatusLock(statusLock);
                tournament.LockHandler.UnlockDataLock(dataLock);
                tournament.LockHandler.UnlockEntrantsLock(entrantsLock);
                tournament.LockHandler.UnlockSetsLock(setLock);
            }
        }

        public async Task<IResult> GetSetByIdAsync(int id)
        {
            if (tournament is null)
            {
                return Results.BadRequest("No tournament exists");
            }
            var set = await tournament.TryGetSetAsync(id);
            if (set is null)
            {
                return Results.NotFound();
            }
            using (set.LockHandler.LockSetReadAsync())
            {
                var json = JsonSerializer.Serialize(
                    set,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Converters = { setConverter }
                    }
                );
                return Results.Content(json, "application/json");
            }
        }

        public async Task<IResult> GetEntrantByIdAsync(int id)
        {
            if (tournament is null)
            {
                return Results.BadRequest("No tournament exists");
            }
            var entrant = await tournament.TryGetEntrantAsync(id);
            if (entrant is null)
            {
                return Results.NotFound();
            }
            // No locking needed, as Entrants are immutable
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
                return Results.BadRequest("No tournament exists");
            }
            var data = await tournament.GetAllDataAsync();
            // No need to lock, as GetAllDataAsync returns a copy (and handles locking)
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
                return Results.BadRequest("No tournament exists");
            }
            var data = await tournament.TryGetDataAsync(key);
            if (data is null)
            {
                return Results.NotFound();
            }
            // No locking to worry about, as strings are immutable
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
                return Results.BadRequest("No tournament exists");
            }
            var status = await tournament.GetStatusAsync();
            // No locking, as enum is a value-type, so the return value means a copy.
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

        public async Task<IResult> HandleTournamentPost(HttpContext context)
        {
            using (StreamReader reader = new StreamReader(context.Request.Body, Encoding.UTF8))
            {
                string jsonBody = await reader.ReadToEndAsync();
                if (jsonBody is null)
                {
                    return Results.BadRequest("Request has no body");
                }
                Tournament newTournament =
                    JsonSerializer.Deserialize<Tournament>(jsonBody, new JsonSerializerOptions() { Converters = { myFormatConverter } }) ?? throw new JsonException();
                tournament = newTournament;
                return Results.Ok("Tournament successfully loaded");
            }
        }
    }
}
