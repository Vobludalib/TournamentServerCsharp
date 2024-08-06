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

    public class ServerHandler
    {
        public MyFormatConverter myFormatConverter;
        public SetConverter setConverter;
        public EntrantConverter entrantConverter;
        public GameConverter gameConverter;
        public GameLinksConverter gameLinksConverter;
        public Tournament? tournament;

        public ServerHandler()
        {
            myFormatConverter = new();
            setConverter = new();
            entrantConverter = new();
            gameConverter = new();
            gameLinksConverter = new();
        }

        public async Task<IResult> GetTournamentAsync()
        {
            if (tournament is null)
            {
                return Results.BadRequest("No tournament exists");
            }
            using (var setLock = await tournament.LockHandler.LockSetsReadAsync())
            using (var entrantsLock = await tournament.LockHandler.LockEntrantsReadAsync())
            using (var dataLock = await tournament.LockHandler.LockDataReadAsync())
            using (var statusLock = await tournament.LockHandler.LockStatusReadAsync())
            {
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
                }
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

        public async Task<IResult> HandleTournamentPostAsync(HttpContext context)
        {
            using (StreamReader reader = new StreamReader(context.Request.Body, Encoding.UTF8))
            {
                string jsonBody = await reader.ReadToEndAsync();
                if (jsonBody is null)
                {
                    return Results.BadRequest("Request has no body");
                }
                Tournament newTournament =
                    JsonSerializer.Deserialize<Tournament>(
                        jsonBody,
                        new JsonSerializerOptions() { Converters = { myFormatConverter } }
                    ) ?? throw new JsonException();
                tournament = newTournament;
                return Results.Ok("Tournament successfully loaded");
            }
        }

        public async Task<IResult> HandleTournamentStatusTransitionAsync(string transitionTo)
        {
            if (tournament is null)
            {
                return Results.BadRequest("No tournament exists");
            }
            switch (transitionTo)
            {
                case "InProgress":
                    var result = await tournament.TryMoveToInProgressAsync();
                    if (result)
                    {
                        return Results.Ok("Moved to InProgress");
                    }
                    return Results.Problem(
                        "Failed to move to InProgress. Check that tournament structure is valid and the tournament status is Setup."
                    );
                case "Finished":
                    result = await tournament.TryMoveToFinishedAsync();
                    if (result)
                    {
                        return Results.Ok("Moved to Finished");
                    }
                    return Results.Problem(
                        "Failed to move to Finished. Check that the tournament status is InProgress."
                    );
                default:
                    return Results.BadRequest($"Invalid state to transition to {transitionTo}.");
            }
        }

        public async Task<IResult> HandleGamePostAsync(int setId, HttpContext context)
        {
            if (tournament is null)
            {
                return Results.BadRequest("No tournament exists");
            }
            using (var setsLock = await tournament.LockHandler.LockSetsReadAsync())
            {
                var setToTieTo = await tournament.TryGetSetAsync(setId);
                if (setToTieTo is null)
                    return Results.BadRequest("Set game is being added to does not exist");
                if (setToTieTo.Status != Set.SetStatus.InProgress)
                {
                    return Results.BadRequest("Set has to be InProgress.");
                }
                using (StreamReader reader = new StreamReader(context.Request.Body, Encoding.UTF8))
                {
                    string jsonBody = await reader.ReadToEndAsync();
                    if (jsonBody is null)
                    {
                        return Results.BadRequest("Request has no body");
                    }
                    Console.WriteLine(jsonBody);
                    MyFormatConverter.GameLinksReport? glr =
                        JsonSerializer.Deserialize<MyFormatConverter.GameLinksReport>(
                            jsonBody,
                            new JsonSerializerOptions() { Converters = { gameLinksConverter } }
                        );
                    if (glr is null)
                        return Results.BadRequest("Failed to deserialize game");

                    using (await setToTieTo.LockHandler.LockSetWriteAsync())
                    {
                        var entrant1 = await tournament.TryGetEntrantAsync((int)glr.Entrant1Id!);
                        var entrant2 = await tournament.TryGetEntrantAsync((int)glr.Entrant2Id!);

                        if (entrant1 is null || entrant2 is null)
                            return Results.BadRequest("One of the entrants is null");

                        Entrant? winner = null;
                        if (entrant1.EntrantId == glr.GameWinnerId)
                            winner = entrant1;
                        else if (entrant2.EntrantId == glr.GameWinnerId)
                            winner = entrant2;

                        Set.Game game = new Set.Game(
                            setToTieTo,
                            glr.GameNumber,
                            entrant1,
                            entrant2,
                            winner,
                            (Set.Game.GameStatus)glr.Status!,
                            glr.Data
                        );

                        var amountOfMatchingSets = setToTieTo
                            .Games
                            .Where(x => x.GameNumber == game.GameNumber)
                            .Count();

                        if (amountOfMatchingSets == 1)
                        {
                            var gameToReplace = setToTieTo
                                .Games
                                .Where(x => x.GameNumber == game.GameNumber)
                                .First();
                            var index = setToTieTo.Games.IndexOf(gameToReplace);
                            setToTieTo.Games[index] = game;
                            return Results.Ok(
                                $"Replaced an already existing game with the same game number for set {setId}"
                            );
                        }
                        else if (amountOfMatchingSets == 0)
                        {
                            // Get highest gameNumber already existing, the game we want to create has to be the subsequent game number
                            if (setToTieTo.Games.Count == 0 && game.GameNumber != 1)
                                return Results.BadRequest(
                                    "First game must have gameNumber set to 1."
                                );
                            else if (
                                setToTieTo.Games.Count > 0
                                && glr.GameNumber != setToTieTo.Games.Max(x => x.GameNumber) + 1
                            )
                            {
                                return Results.BadRequest(
                                    "GameNumber is not the directly subsequent game for the given set."
                                );
                            }
                            setToTieTo.Games.Add(game);
                            return Results.Ok("Game added to set");
                        }
                        return Results.BadRequest();
                    }
                }
            }
        }

        public async Task<IResult> UpdateSetBasedOnGamesAsync(int setId)
        {
            if (tournament is null)
            {
                return Results.BadRequest("No tournament exists");
            }
            using (var setsLock = await tournament.LockHandler.LockSetsReadAsync())
            {
                var set = await tournament.TryGetSetAsync(setId);
                if (set is null)
                {
                    return Results.NotFound();
                }

                using (await set.LockHandler.LockSetWriteAsync())
                {
                    if (set.Status != Set.SetStatus.InProgress)
                    {
                        return Results.BadRequest(
                            "Set has to be InProgress to be updated based on games."
                        );
                    }

                    try
                    {
                        if (set.UpdateSetBasedOnGames())
                        {
                            return Results.Ok("Set was updated.");
                        }
                        return Results.Ok("Set was not updated.");
                    }
                    catch
                    {
                        return Results.Problem("An error occured when checking the games.");
                    }
                }
            }
        }

        public async Task<IResult> HandleSetStatusTransitionAsync(int setId, string transitionTo)
        {
            if (tournament is null)
            {
                return Results.BadRequest("No tournament exists");
            }
            using (var setsLock = await tournament.LockHandler.LockSetsReadAsync())
            {
                var set = await tournament.TryGetSetAsync(setId);
                if (set is null)
                {
                    return Results.NotFound();
                }

                using (await set.LockHandler.LockSetWriteAsync())
                {
                    switch (transitionTo)
                    {
                        case "WaitingForStart":
                            bool success = set.TryMoveToWaitingForStart();
                            if (success)
                            {
                                return Results.Ok("Moved the set to WaitingForStart");
                            }
                            return Results.BadRequest(
                                "The set was not able to be moved to WaitingForStart. Check that all entrants are filled, the SetWinnerDecider is filled, and the set is in IncompleteSetup."
                            );
                        case "InProgress":
                            success = set.TryMoveToInProgress();
                            if (success)
                            {
                                return Results.Ok("Moved the set to InProgress");
                            }
                            return Results.BadRequest(
                                "The set was not able to be moved to InProgress. Check that the set is in WaitingForStart."
                            );
                        default:
                            return Results.BadRequest("Not a valid status.");
                    }
                }
            }
        }

        public async Task<IResult> HandleSetMoveWinnersAndLoserAsync(int setId)
        {
            if (tournament is null)
            {
                return Results.BadRequest("No tournament exists");
            }
            using (var setsLock = await tournament.LockHandler.LockSetsReadAsync())
            using (var entrantsLock = await tournament.LockHandler.LockEntrantsReadAsync())
            {
                var set = await tournament.TryGetSetAsync(setId);
                if (set is null)
                {
                    return Results.NotFound();
                }

                List<Set> otherSets = [];
                if (set.SetWinnerGoesTo is not null)
                    otherSets.Add(set.SetWinnerGoesTo);
                if (set.SetLoserGoesTo is not null)
                    otherSets.Add(set.SetLoserGoesTo);
                otherSets.Sort((x1, x2) => x1.SetId.CompareTo(x2.SetId));
                List<IDisposable> setLocks = new();
                setLocks.Add(await set.LockHandler.LockSetReadAsync());
                foreach (Set s in otherSets)
                {
                    setLocks.Add(await s.LockHandler.LockSetWriteAsync());
                }

                try
                {
                    bool success = set.TryProgressingWinnerAndLoser();
                    if (success)
                        return Results.Ok(
                            "Set winner and loser have been moved to their next sets."
                        );
                    return Results.Ok("No changes.");
                }
                finally
                {
                    setLocks.Reverse();
                    foreach (IDisposable l in setLocks)
                    {
                        l.Dispose();
                    }
                }
            }
        }

        public async Task<IResult> HandleGameStatusTransitionAsync(
            int setId,
            int gameNumber,
            string transitionTo
        )
        {
            if (tournament is null)
            {
                return Results.BadRequest("No tournament exists");
            }
            using (var setsLock = await tournament.LockHandler.LockSetsReadAsync())
            {
                var set = await tournament.TryGetSetAsync(setId);
                if (set is null)
                {
                    return Results.NotFound();
                }

                using (await set.LockHandler.LockSetReadAsync())
                {
                    var game = set.Games.Where(g => g.GameNumber == gameNumber).FirstOrDefault();
                    if (game is null)
                    {
                        return Results.NotFound(
                            "That set does not contain that a game with that number."
                        );
                    }

                    switch (transitionTo)
                    {
                        case "Waiting":
                            bool success = await game.TryMovingToWaitingAsync();
                            if (success)
                            {
                                return Results.Ok("Game has been moved back to Waiting.");
                            }
                            return Results.BadRequest(
                                "The game was not able to be moved to Waiting. Check that the game is in InProgress."
                            );
                        case "InProgress":
                            success = await game.TryMovingToInProgressAsync();
                            if (success)
                            {
                                return Results.Ok("Moved the set to InProgress");
                            }
                            return Results.BadRequest(
                                "The set was not able to be moved to InProgress. Check that the game has all entrants filled and is in Waiting."
                            );
                        default:
                            return Results.BadRequest("Not a valid status.");
                    }
                }
            }
        }

        public async Task<IResult> HandleGameUpdateWinnerAsync(
            int setId,
            int gameNumber,
            int entrantId
        )
        {
            {
                if (tournament is null)
                {
                    return Results.BadRequest("No tournament exists");
                }
                using (var setsLock = await tournament.LockHandler.LockSetsReadAsync())
                using (var entrantsLock = await tournament.LockHandler.LockEntrantsReadAsync())
                {
                    var set = await tournament.TryGetSetAsync(setId);
                    if (set is null)
                    {
                        return Results.NotFound("Could not find the set.");
                    }

                    var entrant = await tournament.TryGetEntrantAsync(entrantId);
                    if (entrant is null)
                    {
                        return Results.NotFound("Could not find the entrant.");
                    }

                    using (await set.LockHandler.LockSetReadAsync())
                    {
                        var game = set.Games
                            .Where(g => g.GameNumber == gameNumber)
                            .FirstOrDefault();
                        if (game is null)
                        {
                            return Results.NotFound(
                                "That set does not contain that a game with that number."
                            );
                        }

                        if (entrant != game.Entrant1 && entrant != game.Entrant2)
                            return Results.BadRequest(
                                "That new 'winner' is not a participant of the game."
                            );

                        await game.SetWinnerAsync(entrant);
                        return Results.Ok("Winner was set");
                    }
                }
            }
        }
    }
}
