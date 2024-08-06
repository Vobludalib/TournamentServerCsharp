///Countable

using System.Text.Json;
using Nito.AsyncEx;

namespace TournamentSystem;

public class Tournament
{
    private Dictionary<int, Set> _sets { get; set; }
    private Dictionary<int, Entrant> _entrants { get; set; }
    private Dictionary<string, string> _data { get; set; }
    private TournamentStatus _status;
    private TournamentLockHandler _lockHandler;

    // ONLY TO BE USED IN SYNCHRONOUS CONTEXTS
    public IReadOnlyDictionary<int, Set> Sets => _sets;
    public IReadOnlyDictionary<int, Entrant> Entrants => _entrants;
    public IReadOnlyDictionary<string, string> Data => _data;
    public TournamentStatus Status => _status;
    internal TournamentLockHandler LockHandler => _lockHandler;

    public enum TournamentStatus
    {
        Setup,
        InProgress,
        Finished
    }

    public class TournamentLockHandler
    {
        private AsyncReaderWriterLock _setLocker = new();
        private AsyncReaderWriterLock _entrantsLocker = new();
        private AsyncReaderWriterLock _dataLocker = new();
        private AsyncReaderWriterLock _statusLocker = new();

        public async Task<IDisposable> LockSetsReadAsync()
        {
            IDisposable setLock = await _setLocker.ReaderLockAsync();
            return setLock;
        }

        public async Task<IDisposable> LockSetsWriteAsync()
        {
            IDisposable setLock = await _setLocker.WriterLockAsync();
            return setLock;
        }

        public async Task<IDisposable> LockEntrantsReadAsync()
        {
            IDisposable entrantsLock = await _entrantsLocker.ReaderLockAsync();
            return entrantsLock;
        }

        public async Task<IDisposable> LockEntrantsWriteAsync()
        {
            IDisposable entrantsLock = await _entrantsLocker.WriterLockAsync();
            return entrantsLock;
        }

        public async Task<IDisposable> LockDataReadAsync()
        {
            IDisposable dataLock = await _dataLocker.ReaderLockAsync();
            return dataLock;
        }

        public async Task<IDisposable> LockDataWriteAsync()
        {
            IDisposable dataLock = await _dataLocker.WriterLockAsync();
            return dataLock;
        }

        public async Task<IDisposable> LockStatusReadAsync()
        {
            IDisposable statusLock = await _statusLocker.ReaderLockAsync();
            return statusLock;
        }

        public async Task<IDisposable> LockStatusWriteAsync()
        {
            IDisposable statusLock = await _statusLocker.WriterLockAsync();
            return statusLock;
        }
    }

    public Tournament()
    {
        _sets = new Dictionary<int, Set>();
        _entrants = new Dictionary<int, Entrant>();
        _data = new Dictionary<string, string>();
        _status = TournamentStatus.Setup;
        _lockHandler = new();
    }

    /// <summary>
    /// This method is used for finalizing the setup of a tournament. Returns true on success, otherwise false.
    /// Part of this method is verifying the structure of the tournament is valid.
    /// </summary>
    /// <returns></returns>
    public async Task<bool> TryMoveToInProgressAsync()
    {
        using (IDisposable statusLock = await _lockHandler.LockStatusReadAsync())
        {
            if (_status != TournamentStatus.Setup)
                return false;
        }

        using (var setLock = await _lockHandler.LockSetsReadAsync())
        using (var entrantsLock = await _lockHandler.LockEntrantsReadAsync())
        using (var statusLock = await _lockHandler.LockStatusWriteAsync())
        {
            // Status can change during the time it takes to get all the locks - first check just helps eliminate the vast majority of cases early
            if (_status != TournamentStatus.Setup)
                return false;

            if (await VerifyStructureAsync())
            {
                _status = TournamentStatus.InProgress;
                return true;
            }
            return false;
        }
    }

    public async Task<bool> TryMoveToFinishedAsync()
    {
        // Writer lock is exclusive, so we can also read safely
        using (var statusLock = await _lockHandler.LockStatusWriteAsync())
        {
            if (_status != TournamentStatus.InProgress)
                return false;
            _status = TournamentStatus.Finished;
            return true;
        }
    }

    /// <summary>
    /// Method used only for JSON conversion - hard override of the tournament status
    /// </summary>
    /// <param name="status"></param>
    internal void SetStatus(TournamentStatus status)
    {
        _status = status;
    }

    internal async Task<bool> VerifyStructureAsync()
    {
        // Acquire locks of every set, locking sets and entrants dictionaries and status should be already locked
        var allSets = _sets.Values.ToList();
        // Sorting by setId to guarantee consistent locking order
        allSets.Sort((x1, x2) => x1.SetId.CompareTo(x2.SetId));
        List<IDisposable> setLocks = new();
        foreach (Set set in allSets)
        {
            setLocks.Add(await set.LockHandler.LockSetReadAsync());
        }

        try
        {
            // Verify each set has correct amount of entrants incoming/already in the set
            if (!VerifyAmountOfEntrants())
                return false;
            // Verify no cycles
            if (!VerifyNoCycles())
                return false;

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            // Unlock in reverse order of locking
            setLocks.Reverse();
            // Unlock all the locks no matter what happens
            foreach (IDisposable setLock in setLocks)
            {
                setLock.Dispose();
            }
        }
    }

    private bool VerifyAmountOfEntrants()
    {
        Dictionary<Set, int> amountOfEntrants = new();
        foreach (Set set in _sets.Values)
        {
            if (set.Entrant1 is not null)
                amountOfEntrants[set] = amountOfEntrants.GetValueOrDefault(set, 0) + 1;
            if (set.Entrant2 is not null)
                amountOfEntrants[set] = amountOfEntrants.GetValueOrDefault(set, 0) + 1;
            if (set.SetWinnerGoesTo is not null)
                amountOfEntrants[set.SetWinnerGoesTo] =
                    amountOfEntrants.GetValueOrDefault(set.SetWinnerGoesTo, 0) + 1;
            if (set.SetLoserGoesTo is not null)
                amountOfEntrants[set.SetLoserGoesTo] =
                    amountOfEntrants.GetValueOrDefault(set.SetLoserGoesTo, 0) + 1;
        }
        foreach (int amount in amountOfEntrants.Values)
        {
            if (amount != 2)
                return false;
        }
        return true;
    }

    private bool VerifyNoCycles()
    {
        Dictionary<int, List<int>> adjacents = new();
        foreach (Set set in _sets.Values)
        {
            adjacents[set.SetId] = new();
        }
        foreach (Set set in _sets.Values)
        {
            if (set.SetWinnerGoesTo is not null)
            {
                adjacents[set.SetId].Add(set.SetWinnerGoesTo.SetId);
            }
            if (set.SetLoserGoesTo is not null)
            {
                adjacents[set.SetId].Add(set.SetLoserGoesTo.SetId);
            }
        }

        // Use DFS to check for cycles
        Stack<int> stack = new();
        HashSet<int> currPath = new();
        HashSet<int> visited = new();
        foreach (var node in adjacents.Keys)
        {
            if (!visited.Contains(node))
            {
                stack.Push(node);

                while (stack.Count > 0)
                {
                    int current = stack.Peek();

                    if (!visited.Contains(current))
                    {
                        visited.Add(current);
                        currPath.Add(current);
                    }

                    bool foundUnvisitedNeighbor = false;

                    foreach (var neighbor in adjacents[current])
                    {
                        if (!visited.Contains(neighbor))
                        {
                            stack.Push(neighbor);
                            foundUnvisitedNeighbor = true;
                            break;
                        }
                        else if (currPath.Contains(neighbor))
                        {
                            // A cycle is detected
                            return false;
                        }
                    }

                    if (!foundUnvisitedNeighbor)
                    {
                        int nodeOutOfRecStack = stack.Pop();
                        currPath.Remove(nodeOutOfRecStack);
                    }
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Method for adding a Set to the tournament.
    /// </summary>
    /// <param name="set"></param>
    /// <returns></returns>
    public async Task<bool> AddSetAsync(Set set)
    {
        using (var statusLock = await _lockHandler.LockStatusReadAsync())
        {
            if (_status != TournamentStatus.Setup)
                return false;

            using (var setLock = await _lockHandler.LockSetsWriteAsync())
            {
                try
                {
                    if (_sets.Keys.Contains(set.SetId))
                        return false;
                    _sets.Add(set.SetId, set);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    /// <summary>
    /// Method for removing sets for the tournament (by id). Returns true on success, otherwise false
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public async Task<bool> TryRemoveSetAsync(int id)
    {
        using (var statusLock = await _lockHandler.LockStatusReadAsync())
        {
            if (_status != TournamentStatus.Setup)
                return false;

            using (var setLock = await _lockHandler.LockSetsWriteAsync())
            {
                if (_sets.ContainsKey(id))
                {
                    _sets.Remove(id);
                    return true;
                }
                return false;
            }
        }
    }

    public async Task<Set?> TryGetSetAsync(int id)
    {
        using (var setLock = await _lockHandler.LockSetsReadAsync())
        {
            bool success = _sets.TryGetValue(id, out var set);
            if (success)
                return set;
            return null;
        }
    }

    public async Task<bool> AddEntrantAsync(Entrant entrant)
    {
        var statusLock = await _lockHandler.LockStatusReadAsync();
        {
            if (_status != TournamentStatus.Setup)
                return false;

            using (var entrantsLock = await _lockHandler.LockEntrantsReadAsync())
            {
                try
                {
                    if (_entrants.Keys.Contains(entrant.EntrantId))
                        return false;
                    _entrants.Add(entrant.EntrantId, entrant);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    /// <summary>
    /// Method for removing entrants for the tournament (by id). Returns true on success, otherwise false
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public async Task<bool> TryRemoveEntrantAsync(int id)
    {
        using (var statusLock = await _lockHandler.LockStatusReadAsync())
        {
            if (_status != TournamentStatus.Setup)
                return false;

            using (var entrantsLock = await _lockHandler.LockEntrantsWriteAsync())
            {
                try
                {
                    if (_entrants.ContainsKey(id))
                    {
                        _entrants.Remove(id);
                        return true;
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    public async Task<Entrant?> TryGetEntrantAsync(int id)
    {
        using (var entrantLock = await _lockHandler.LockEntrantsReadAsync())
        {
            bool success = _entrants.TryGetValue(id, out var entrant);
            if (success)
                return entrant;
            return null;
        }
    }

    /// <summary>
    /// Method for adding or editing a specific key value pair of Data. If the key does not yet exist, it is added, otherwise the existing value is just overwritten.
    /// Returns true on a success, otherwise false.
    /// </summary>
    /// <param name="label"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    public async Task<bool> AddOrEditDataAsync(string label, string value)
    {
        using (var statusLock = await _lockHandler.LockStatusReadAsync())
        {
            if (_status == TournamentStatus.Finished)
                return false;

            using (var dataLock = await _lockHandler.LockDataWriteAsync())
            {
                try
                {
                    if (_data.ContainsKey(label))
                        _data[label] = value;
                    else
                    {
                        _data.Add(label, value);
                    }
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    /// <summary>
    /// Method for deleting from data. Return true on success, otherwise false.
    /// </summary>
    /// <param name="label"></param>
    /// <returns></returns>
    public async Task<bool> DeleteDataAsync(string label)
    {
        using (var statusLock = await _lockHandler.LockStatusReadAsync())
        {
            if (_status == TournamentStatus.Finished)
                return false;

            using (var dataLock = await _lockHandler.LockDataWriteAsync())
            {
                try
                {
                    if (_data.ContainsKey(label))
                    {
                        _data.Remove(label);
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    public async Task<string?> TryGetDataAsync(string label)
    {
        using (var dataLock = await _lockHandler.LockDataReadAsync())
        {
            bool success = _data.TryGetValue(label, out var value);
            if (success)
                return value;
            return null;
        }
    }

    public async Task<Dictionary<string, string>> GetAllDataAsync()
    {
        // We have to clone the dict and create a snapshot at the time, so we can free up the lock while ensuring the dict does not change after this method returns.
        Dictionary<string, string> clonedDict = new();
        using (var dataLock = await _lockHandler.LockDataReadAsync())
        {
            // Not doing Parallel.Foreach, as every loop is really simple, and the overhead would be too large.
            foreach (var key in _data.Keys)
            {
                // Strings are immutable, so we don't need to copy - these references will never change
                clonedDict.Add(key, _data[key]);
            }
        }
        return clonedDict;
    }

    public async Task<TournamentStatus> GetStatusAsync()
    {
        using (var statusLock = await _lockHandler.LockStatusReadAsync())
        {
            return _status;
        }
    }
}

public class Set
{
    private int _setId;
    private string? _setName;
    private Entrant? _entrant1;
    private Entrant? _entrant2;
    private SetStatus _status;
    private Set? _setWinnerGoesTo;
    private Set? _setLoserGoesTo;
    private Entrant? _winner;
    private Entrant? _loser;
    private List<Game> _games;
    private IWinnerDecider? _setDecider;
    private Dictionary<string, string> _data;
    private SetLockHandler _lockHandler;

    public int SetId
    {
        get => _setId;
    }

    public string? SetName
    {
        get => _setName;
        set
        {
            using (_lockHandler.LockSetWrite())
            {
                if (_status != SetStatus.IncompleteSetup)
                {
                    throw new InvalidOperationException("Cannot change set name after set setup");
                }
                _setName = value;
            }
        }
    }

    public Entrant? Entrant1
    {
        get => _entrant1;
        set
        {
            using (_lockHandler.LockSetWrite())
            {
                if (_status != SetStatus.IncompleteSetup)
                {
                    throw new InvalidOperationException("Cannot change entrants after set setup.");
                }
                _entrant2 = value;
            }
        }
    }

    public Entrant? Entrant2
    {
        get => _entrant2;
        set
        {
            using (_lockHandler.LockSetWrite())
            {
                if (_status != SetStatus.IncompleteSetup)
                    throw new InvalidOperationException("Cannot change entrants after set setup.");
                _entrant2 = value;
            }
        }
    }

    public SetStatus Status
    {
        get => _status;
        // No setter, state transitions are handled via the methods
    }

    public Set? SetWinnerGoesTo
    {
        get => _setWinnerGoesTo;
        set
        {
            using (_lockHandler.LockSetWrite())
            {
                if (_status != SetStatus.IncompleteSetup)
                {
                    throw new InvalidOperationException(
                        "Cannot change where winner goes after set setup."
                    );
                }
                _setWinnerGoesTo = value;
            }
        }
    }

    public Set? SetLoserGoesTo
    {
        get => _setLoserGoesTo;
        set
        {
            using (_lockHandler.LockSetWrite())
            {
                if (_status != SetStatus.IncompleteSetup)
                {
                    throw new InvalidOperationException(
                        "Cannot change where loser goes after set setup."
                    );
                }
                _setLoserGoesTo = value;
            }
        }
    }

    public Entrant? Winner
    {
        get => _winner;
    }

    public Entrant? Loser
    {
        get => _loser;
    }

    public List<Game> Games
    {
        get => _games;
    }

    public IWinnerDecider? SetDecider
    {
        get => _setDecider;
        set
        {
            using (_lockHandler.LockSetWrite())
            {
                if (_status != SetStatus.IncompleteSetup)
                {
                    throw new InvalidOperationException(
                        "Cannot change winner decider  after set setup."
                    );
                }
                _setDecider = value;
            }
        }
    }

    public Dictionary<string, string> Data
    {
        get => _data;
        set
        {
            using (_lockHandler.LockSetWrite())
            {
                _data = value;
            }
        }
    }

    public enum SetStatus
    {
        IncompleteSetup,
        WaitingForStart,
        InProgress,
        Finished
    }

    public SetLockHandler LockHandler => _lockHandler;

    public class SetLockHandler
    {
        private AsyncReaderWriterLock _setLocker = new();

        public async Task<IDisposable> LockSetReadAsync()
        {
            Console.WriteLine("Awaiting locking set for read");
            IDisposable setLock = await _setLocker.ReaderLockAsync();
            Console.WriteLine($"Locked set with lock {setLock.GetHashCode()} for read");
            return setLock;
        }

        public async Task<IDisposable> LockSetWriteAsync()
        {
            Console.WriteLine("Awaiting locking set for write");
            IDisposable setLock = await _setLocker.WriterLockAsync();
            Console.WriteLine($"Locked set with lock {setLock.GetHashCode()} for write");
            return setLock;
        }

        // Only to be used in synchronous contexts
        public IDisposable LockSetRead()
        {
            IDisposable setLock = _setLocker.ReaderLock();
            Console.WriteLine($"Locked set with lock {setLock.GetHashCode()} for read");
            return setLock;
        }

        public IDisposable LockSetWrite()
        {
            IDisposable setLock = _setLocker.WriterLock();
            Console.WriteLine($"Locked set with lock {setLock.GetHashCode()} for write");
            return setLock;
        }
    }

    // I only have a constructor with only ID, as the fields will be set later. This is because
    // of the order in which a tournament must be reconstructed from JSON, but also
    // because these fields will change inherently as a tournament progresses. An entrant on the other
    // hand should not change once it is created, as it is immutable.
    public Set(int Id)
    {
        _data = new Dictionary<string, string>();
        _setId = Id;
        _status = SetStatus.IncompleteSetup;
        _games = new List<Game>();
        _lockHandler = new();
    }

    /// <summary>
    /// IWinnerDecider is an interface used for specifying custom set-winner conditions.
    ///
    /// E.g. tennis will have a WinnerDecider that works with the win-by-two condition, while most sports
    /// just have a first to X wins condition.
    /// </summary>
    public interface IWinnerDecider
    {
        public Entrant? DecideWinner(Entrant entrant1, Entrant entrant2, List<Game> games);
    }

    /// <summary>
    /// BestOfDecider models the behaviour of a best of X format, where X is specified when created.
    /// </summary>
    public class BestOfDecider : IWinnerDecider
    {
        public int AmountOfWinsRequired { get; init; }

        public BestOfDecider(int requiredWins)
        {
            AmountOfWinsRequired = requiredWins;
        }

        public Entrant? DecideWinner(Entrant entrant1, Entrant entrant2, List<Game> games)
        {
            int entrant1Wins = 0;
            int entrant2Wins = 0;
            foreach (Game game in games)
            {
                if (game.GameWinner is null || game.Status != Game.GameStatus.Finished)
                    continue;
                else if (game.GameWinner.EntrantId == entrant1.EntrantId)
                    entrant1Wins += 1;
                else if (game.GameWinner.EntrantId == entrant2.EntrantId)
                    entrant2Wins += 1;
                else
                {
                    throw new InvalidOperationException(
                        $"Winner of game {game.GameNumber} is neither of the passed entrants."
                    );
                }
            }

            if (entrant1Wins < AmountOfWinsRequired && entrant2Wins < AmountOfWinsRequired)
            {
                return null;
            }
            else if (entrant1Wins >= AmountOfWinsRequired && entrant2Wins < AmountOfWinsRequired)
            {
                return entrant1;
            }
            else if (entrant1Wins < AmountOfWinsRequired && entrant2Wins >= AmountOfWinsRequired)
            {
                return entrant2;
            }
            else if (entrant1Wins >= AmountOfWinsRequired && entrant2Wins >= AmountOfWinsRequired)
            {
                throw new InvalidOperationException(
                    "Both players have more than enough wins to proceed. Invalid state"
                );
            }

            throw new NotImplementedException("Other alternative states not handled currently");
        }
    }

    /// <summary>
    /// Method used only for JSON conversion - required due to JSON using ID based storage of entrants and sets
    /// along with Status and the like being inaccesible from outside the Set class
    /// </summary>
    /// <param name="sets"></param>
    /// <param name="entrants"></param>
    /// <param name="report"></param>
    /// <exception cref="JsonException"></exception>
    internal void FillSetFromReport(
        Dictionary<int, Set> sets,
        IReadOnlyDictionary<int, Entrant> entrants,
        MyFormatConverter.SetLinksReport report
    )
    {
        _setName = report.SetName;
        _entrant1 = report.Entrant1Id is null ? null : entrants[(int)report.Entrant1Id];
        _entrant2 = report.Entrant2Id is null ? null : entrants[(int)report.Entrant2Id];
        _status = report.Status;
        if (
            (
                (_status == SetStatus.Finished)
                && (_entrant1 is null || _entrant2 is null || _winner is null || _loser is null)
            ) || ((_status == SetStatus.InProgress) && (_entrant1 is null || _entrant2 is null))
        )
        {
            // Essentially if we read Finished or InProgress without the correct prerequisites to be in that state, then the JSON is invalid
            throw new JsonException(
                "Loading a Finished or InProgress set that does not have the necessary prerequisites to be in that state."
            );
        }
        _setWinnerGoesTo = report.WinnerGoesToId is null ? null : sets[(int)report.WinnerGoesToId];
        _setLoserGoesTo = report.LoserGoesToId is null ? null : sets[(int)report.LoserGoesToId];
        _winner = report.Winner is null ? null : entrants[(int)report.Winner];
        _loser = report.Loser is null ? null : entrants[(int)report.Loser];
        _setDecider = report.WinnerDecider;
        _data = report.Data ?? new Dictionary<string, string>();
        // Create all relevant games
        foreach (MyFormatConverter.GameLinksReport gr in report.Games!)
        {
            List<Entrant> reducedSearch = [];
            if (_entrant1 is not null)
                reducedSearch.Add(_entrant1);
            if (_entrant2 is not null)
                reducedSearch.Add(_entrant2);
            Entrant e1 = reducedSearch.First(x => x.EntrantId == gr.Entrant1Id);
            Entrant e2 = reducedSearch.First(x => x.EntrantId == gr.Entrant2Id);
            Game game = new Game(this, gr.GameNumber, e1, e2, gr.Data);
            Entrant? winner = reducedSearch.FirstOrDefault(x => x.EntrantId == gr.GameWinnerId);
            if (winner is not null)
                AsyncContext.Run(() => game.SetWinnerAsync(winner));
            if (gr.Status is null)
            {
                throw new JsonException();
            }
            game.SetStatus((Game.GameStatus)gr.Status);
            _games.Add(game);
        }
    }

    /// <summary>
    /// If a set is InProgress, this method will evaluate whether the games are enough to decide a winner
    /// using the provided IWinnerDecider. If a winner is found, Status is set to Finished.
    ///
    /// This method assumes that the set it is working with is appropriately locked already.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NullReferenceException"></exception>
    public bool UpdateSetBasedOnGames()
    {
        if (_status != SetStatus.InProgress)
        {
            // If the game is not currently in progress, we should not even look at the games
            return false;
        }
        if (_setDecider is null)
        {
            throw new NullReferenceException("Set is InProgress without a SetDecider set.");
        }
        if (_entrant1 is null || _entrant2 is null)
        {
            throw new NullReferenceException("Set is InProgress without both entrants set.");
        }
        Entrant? winner = _setDecider.DecideWinner(_entrant1, _entrant2, Games);
        // If no winner, we return false.
        if (winner is null)
            return false;
        // Otherwise, we set the appropriate winner and loser properties
        // We do not move players to their next set in this step, that is handled elsewhere
        // there may be cases where we wish for these properties to be filled, but not do anything with them yet
        if (winner == _entrant1)
        {
            _winner = _entrant1;
            _loser = _entrant2;
        }
        else if (winner == _entrant2)
        {
            _winner = _entrant2;
            _loser = _entrant1;
        }
        else
        {
            throw new NotImplementedException();
        }
        _status = SetStatus.Finished;
        return true;
    }

    /// <summary>
    /// Method that tries to move Set to WaitingForStart, if all entrants and the winnerDecider are set (and we are in a valid state)
    ///
    /// This method assumes that the set it is working with is appropriately locked already.
    /// </summary>
    /// <returns></returns>
    public bool TryMoveToWaitingForStart()
    {
        if (_status != SetStatus.IncompleteSetup)
            return false;
        if (_entrant1 is null || _entrant2 is null || _setDecider is null)
            return false;
        _status = SetStatus.WaitingForStart;
        return true;
    }

    /// <summary>
    /// Method that tries to move Set to InProgress, if conditions are satisfied (returns true). Otherwise, returns false.
    ///
    /// This method assumes that the set it is working with is appropriately locked already.
    /// </summary>
    /// <returns></returns>
    public bool TryMoveToInProgress()
    {
        if (_status != SetStatus.WaitingForStart)
            return false;
        _status = SetStatus.InProgress;
        return true;
    }

    /// <summary>
    /// Method for moving winner and loser to the sets they play in next (if any). Returns true on success, false otherwise.
    ///
    /// This method assumes that the set it is working with is appropriately locked already.
    /// </summary>
    /// <returns></returns>
    public bool TryProgressingWinnerAndLoser()
    {
        if (_status != SetStatus.Finished)
            return false;
        if (_winner is null || _loser is null)
            return false;
        if (_setWinnerGoesTo is not null)
        {
            if (_setWinnerGoesTo._entrant1 is null)
                _setWinnerGoesTo._entrant1 = _winner;
            else if (_setWinnerGoesTo._entrant2 is null)
                _setWinnerGoesTo._entrant2 = _winner;
            else if (_setWinnerGoesTo._entrant1 != _winner && _setWinnerGoesTo._entrant2 != _winner)
            {
                throw new InvalidOperationException("Moving an entrant to an already filled set");
            }
        }
        if (_setLoserGoesTo is not null)
        {
            if (_setLoserGoesTo._entrant1 is not null)
                _setLoserGoesTo._entrant1 = _loser;
            else if (_setLoserGoesTo._entrant2 is not null)
                _setLoserGoesTo._entrant2 = _loser;
            else if (_setLoserGoesTo._entrant1 != _loser && _setLoserGoesTo._entrant2 != _loser)
            {
                throw new InvalidOperationException("Moving an entrant to an already filled set");
            }
        }
        return true;
    }

    public class Game
    {
        public readonly Set _parentSet;
        private readonly int _gameNumber;

        // Stored separately to the sets teams, as for certain games Team 1 and Team 2 may have meanings
        // (e.g. side selection)
        private readonly Entrant _entrant1;
        private readonly Entrant _entrant2;
        private Entrant? _gameWinner;
        private GameStatus _status;

        // Omitting whole LockHandler here - no need for the overhead here, can be added if needed.
        private AsyncReaderWriterLock _lock;

        public int GameNumber => _gameNumber;
        public Entrant Entrant1 => _entrant1;
        public Entrant Entrant2 => _entrant2;
        public Entrant? GameWinner => _gameWinner;
        public GameStatus Status => _status;
        public AsyncReaderWriterLock Lock => _lock;

        public enum GameStatus
        {
            Waiting,
            InProgress,
            Finished
        }

        // Dictionary for the other data, stored as a string, and will be parsed when and if necessary
        // Given that the kind of data stored here can have a variety of formats, no point trying to parse here
        // The parsing can happen when and if needed when working with the game data.
        private Dictionary<string, string> _data = new();
        public Dictionary<string, string> Data => _data;

        public Game(
            Set ParentSet,
            int GameNumber,
            Entrant Entrant1,
            Entrant Entrant2,
            Dictionary<string, string>? Data = null
        )
        {
            _parentSet = ParentSet;
            _gameNumber = GameNumber;
            _entrant1 = Entrant1;
            _entrant2 = Entrant2;
            if (Data is null)
                _data = new Dictionary<string, string>();
            _data = Data!;
            _status = GameStatus.Waiting;
            _lock = new();
        }

        // Used when deserializing a game
        internal Game(
            Set ParentSet,
            int GameNumber,
            Entrant Entrant1,
            Entrant Entrant2,
            Entrant? Winner,
            GameStatus Status,
            Dictionary<string, string>? Data = null
        )
        {
            _parentSet = ParentSet;
            _gameNumber = GameNumber;
            _entrant1 = Entrant1;
            _entrant2 = Entrant2;
            _gameWinner = Winner;
            if (Data is null)
                _data = new Dictionary<string, string>();
            _data = Data!;
            _status = Status;
            _lock = new();
        }

        /// <summary>
        /// Method to move from InProgress to Waiting - should only be used to roll-back misclicks. Returns true if succesful, otherwise false.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> TryMovingToWaitingAsync()
        {
            using (await _lock.WriterLockAsync())
            {
                if (_status == GameStatus.InProgress)
                {
                    _status = GameStatus.Waiting;
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Method to move from Waiting to InProgress. Returns true if succesful, otherwise false.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> TryMovingToInProgressAsync()
        {
            using (await _lock.WriterLockAsync())
            {
                if (_status == GameStatus.Waiting && _entrant1 is not null && _entrant2 is not null)
                {
                    _status = GameStatus.InProgress;
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// This assumes that the parent set is locked for writing already.
        /// </summary>
        /// <param name="winner"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task SetWinnerAsync(Entrant winner)
        {
            using (await _lock.WriterLockAsync())
            {
                if (winner != Entrant1 && winner != Entrant2)
                    throw new InvalidOperationException();
                _gameWinner = winner;
                _status = GameStatus.Finished;
            }
        }

        /// <summary>
        /// Overrides the checks - DO NOT USE UNLESS YOU ARE LOADING A GAME FROM SOMEWHERE
        /// </summary>
        /// <param name="status"></param>
        internal void SetStatus(GameStatus status)
        {
            _status = status;
        }
    }
}

public abstract class Entrant
{
    public int EntrantId { get; init; }

    // This is a dictionary, as we don't have a set promise from the JSON as to what information
    // can or can't be included, instead we just store directly from the JSON and any parsing is done
    // when required
    protected Dictionary<string, string> _entrantData = new Dictionary<string, string>();
    public Dictionary<string, string> EntrantData => _entrantData;
}

public class IndividualEntrant : Entrant
{
    // This is kept as an EntrantName type - this is because I allow the JSON to specify the info
    // firstName, lastName if you want to store them seperately, or just tag for a single string.
    public Name EntrantName { get; init; }

    public abstract class Name
    {
        public abstract string GetFullName();

        // For now, we do hard-coded condensed versions, see relevant method, but
        // this can be expanded to allow custom definitions of 'condensed' form in the JSON itself
        public abstract string GetCondensedName();
    }

    public class Tag : Name
    {
        internal readonly string _tag;

        public Tag(string tag)
        {
            _tag = tag;
        }

        public override string GetFullName() => _tag;

        public override string GetCondensedName() => _tag;
    }

    public class FullName : Name
    {
        internal readonly string _firstName;
        internal readonly string _lastName;

        public FullName(string firstName, string lastName)
        {
            _firstName = firstName;
            _lastName = lastName;
        }

        public override string GetFullName() => _firstName + " " + _lastName;

        public override string GetCondensedName()
        {
            if (_firstName.Length > 0)
            {
                return _lastName + " " + _firstName[0] + ".";
            }
            else
            {
                return _lastName;
            }
        }
    }

    public IndividualEntrant(int Id, string Tag, Dictionary<string, string>? data = null)
    {
        EntrantId = Id;
        EntrantName = new Tag(Tag);

        if (data is null)
        {
            data = new Dictionary<string, string>();
        }
        _entrantData = data;
    }

    public IndividualEntrant(
        int Id,
        string FirstName,
        string LastName,
        Dictionary<string, string>? data = null
    )
    {
        EntrantId = Id;
        EntrantName = new FullName(FirstName, LastName);

        if (data is null)
        {
            data = new Dictionary<string, string>();
        }
        _entrantData = data;
    }
}

public class TeamEntrant : Entrant
{
    public List<IndividualEntrant> IndividualEntrants { get; init; }
    public string? TeamName { get; init; }

    public TeamEntrant(int Id, string? Name, List<IndividualEntrant> individualEntrants)
    {
        EntrantId = Id;
        TeamName = Name;
        IndividualEntrants = individualEntrants;
    }

    public TeamEntrant(int Id, List<IndividualEntrant> individualEntrants)
    {
        EntrantId = Id;
        IndividualEntrants = individualEntrants;
    }
}
