///Countable

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace TournamentSystem;

/**
TODO:
    Tournament, Sets, Games, Entrants
    Progressing through a tournament - handled per request - multithreading
        Set winner decision defined in JSON (BOx or some other format)
**/

public class Tournament
{
    private Dictionary<int, Set> _sets { get; set; }
    private Dictionary<int, Entrant> _entrants { get; set; }
    private Dictionary<string, string> _data { get; set; }
    private object _setLocker;
    private object _entrantsLocker;
    private object _dataLocker;

    public IReadOnlyDictionary<int, Set> Sets => _sets;
    public IReadOnlyDictionary<int, Entrant> Entrants => _entrants;
    public IReadOnlyDictionary<string, string> Data => _data;

    public Tournament()
    {
        _sets = new Dictionary<int, Set>();
        _entrants = new Dictionary<int, Entrant>();
        _data = new Dictionary<string, string>();
        _setLocker = new();
        _entrantsLocker = new();
        _dataLocker = new();
    }

    public bool AddSet(Set set)
    {
        lock (_setLocker)
        {
            try
            {
                _sets.Add(set.SetId, set);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public bool AddEntrant(Entrant entrant)
    {
        lock (_entrantsLocker)
        {
            try
            {
                _entrants.Add(entrant.EntrantId, entrant);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public bool ModifyData(string label, string value)
    {
        lock (_dataLocker)
        {
            try
            {
                if (_data.ContainsKey(label)) _data[label] = value;
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

    public bool DeleteData(string label)
    {
        lock (_dataLocker)
        {
            if (_data.ContainsKey(label))
            {
                _data.Remove(label); return true;
            }
            else
            {
                return false;
            }
        }
    }
}

public class Set
{
    private static List<Set> _sets = new List<Set>();

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
    private object _locker;

    public int SetId
    {
        get => _setId;
    }

    public string? SetName
    {
        get => _setName;
        set
        {
            lock (_locker)
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
            lock (_locker)
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
            lock (_locker)
            {
                if (_status != SetStatus.IncompleteSetup) throw new InvalidOperationException("Cannot change entrants after set setup.");
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
            lock (_locker)
            {
                if (_status != SetStatus.IncompleteSetup)
                {
                    throw new InvalidOperationException("Cannot change where winner goes after set setup.");
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
            lock (_locker)
            {
                if (_status != SetStatus.IncompleteSetup)
                {
                    throw new InvalidOperationException("Cannot change where loser goes after set setup.");
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
            lock (_locker)
            {
                if (_status != SetStatus.IncompleteSetup)
                {
                    throw new InvalidOperationException("Cannot change winner decider  after set setup.");
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
            lock (_locker)
            {
                _data = value;
            }
        }
    }

    public enum SetStatus { IncompleteSetup, WaitingForStart, InProgress, Finished }

    // I only have a constructor with only ID, as the fields will be set later. This is because
    // of the order in which a tournament must be reconstructed from JSON, but also
    // because these fields will change inherently as a tournament progresses. An entrant on the other
    // hand should not change once it is created, as it is immutable.
    public Set(int id)
    {
        if (_sets is null) _sets = new List<Set>();
        foreach (var set in _sets)
        {
            if (set._setId == id)
            {
                throw new InvalidOperationException("Attempting to create a set with an already existing Id");
            }
        }

        _data = new Dictionary<string, string>();
        _setId = id;
        _status = SetStatus.IncompleteSetup;
        _games = new List<Game>();

        _sets.Add(this);

        _locker = new();
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
                if (game.GameWinner is null) continue;
                if (game.GameWinner.EntrantId == entrant1.EntrantId) entrant1Wins += 1;
                if (game.GameWinner.EntrantId == entrant2.EntrantId) entrant2Wins += 1;
                else { throw new InvalidOperationException($"Winner of game {game.GameNumber} is neither of the passed entrants."); }
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
                throw new InvalidOperationException("Both players have more than enough wins to proceed. Invalid state");
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
    internal void FillSetFromReport(Dictionary<int, Set> sets, IReadOnlyDictionary<int, Entrant> entrants, MyFormatConverter.SetLinksReport report)
    {
        _setName = report.SetName;
        _entrant1 = report.Entrant1Id is null ? null : entrants[(int)report.Entrant1Id];
        _entrant2 = report.Entrant2Id is null ? null : entrants[(int)report.Entrant2Id];
        _status = report.Status;
        if (((_status == SetStatus.Finished) && (_entrant1 is null || _entrant2 is null || _winner is null || _loser is null)) || ((_status == SetStatus.InProgress) && (_entrant1 is null || _entrant2 is null)))
        {
            // Essentially if we read Finished or InProgress without the correct prerequisites to be in that state, then the JSON is invalid
            throw new JsonException("Loading a Finished or InProgress set that does not have the necessary prerequisites to be in that state.");
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
            List<Entrant> reducedSearch = [_entrant1, _entrant2];
            Entrant e1 = reducedSearch.First(x => x.EntrantId == gr.Entrant1Id);
            Entrant e2 = reducedSearch.First(x => x.EntrantId == gr.Entrant2Id);
            Game game = new Game(this, gr.GameNumber, e1, e2, gr.Data);
            Entrant? winner = reducedSearch.FirstOrDefault(x => x.EntrantId == gr.GameWinnerId);
            if (winner is not null) game.SetWinner(winner);
            if (gr.Status is null) { throw new JsonException(); }
            game.SetStatus((Game.GameStatus)gr.Status);
            _games.Add(game);
        }
    }

    /// <summary>
    /// If a set is InProgress, this method will evaluate whether the games are enough to decide a winner
    /// using the provided IWinnerDecider. If a winner is found, Status is set to Finished.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NullReferenceException"></exception>
    public bool UpdateSetBasedOnGames()
    {
        lock (_locker)
        {
            if (_status != SetStatus.InProgress)
            {
                // If the game is not currently in progress, we should not even look at the games
                return false;
            }
            if (_setDecider is null) { throw new NullReferenceException("Set is InProgress without a SetDecider set."); }
            if (_entrant1 is null || _entrant2 is null) { throw new NullReferenceException("Set is InProgress without both entrants set."); }
            Entrant? winner = _setDecider.DecideWinner(_entrant1, _entrant2, Games);
            // If no winner, we return false.
            if (winner is null) return false;
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
            else { throw new NotImplementedException(); }
            _status = SetStatus.Finished;
            return true;
        }
    }

    /// <summary>
    /// Method that tries to move Set to InProgress, if conditions are satisfied (returns true). Otherwise, returns false.
    /// </summary>
    /// <returns></returns>
    public bool TryMoveToInProgress()
    {
        lock (_locker)
        {
            if (_status != SetStatus.WaitingForStart) return false;
            _status = SetStatus.InProgress;
            return true;
        }
    }

    /// <summary>
    /// Method that tries to move Set to WaitingForStart, if all entrants and the winnerDecider are set (and we are in a valid state)
    /// </summary>
    /// <returns></returns>
    public bool TryMoveToWaitingForStart()
    {
        lock (_locker)
        {
            if (_status != SetStatus.IncompleteSetup) return false;
            if (_entrant1 is null || _entrant2 is null || _setDecider is null) return false;
            _status = SetStatus.WaitingForStart;
            return true;
        }
    }
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

    public int GameNumber => _gameNumber;
    public Entrant Entrant1 => _entrant1;
    public Entrant Entrant2 => _entrant2;
    // Must be set via a method to allow for certain possible checks
    public Entrant? GameWinner => _gameWinner;

    public GameStatus Status => _status;

    public enum GameStatus { Waiting, InProgress, Finished }

    // Dictionary for the other data, stored as a string, and will be parsed when and if necessary
    // Given that the kind of data stored here can have a variety of formats, no point trying to parse here
    // The parsing can happen when and if needed when working with the game data.
    private Dictionary<string, string> _data = new();
    public Dictionary<string, string> Data => _data;

    public Game(Set ParentSet, int GameNumber, Entrant Entrant1, Entrant Entrant2, Dictionary<string, string>? Data = null)
    {
        _parentSet = ParentSet;
        _gameNumber = GameNumber;
        _entrant1 = Entrant1;
        _entrant2 = Entrant2;
        if (Data is null) _data = new Dictionary<string, string>();
        _data = Data!;
        _status = GameStatus.Waiting;
    }

    public void SetWinner(Entrant winner)
    {
        if (winner != Entrant1 && winner != Entrant2) throw new InvalidOperationException();
        _gameWinner = winner;
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

public abstract record class Entrant
{

    static protected List<Entrant>? _entrants;
    public int EntrantId { get; init; }

    // This is a dictionary, as we don't have a set promise from the JSON as to what information 
    // can or can't be included, instead we just store directly from the JSON and any parsing is done
    // when required
    protected Dictionary<string, string> _entrantData = new Dictionary<string, string>();
    public Dictionary<string, string> EntrantData => _entrantData;
}

public record class IndividualEntrant : Entrant
{
    // This is kept as an EntrantName type - this is because I allow the JSON to specify the info
    // firstName, lastName if you want to store them seperately, or just name for a string name.
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
        if (_entrants is null) _entrants = new List<Entrant>();
        foreach (var entrant in _entrants)
        {
            if (entrant.EntrantId == Id)
            {
                throw new InvalidOperationException("Attempting to create an entrant with an already existing Id");
            }
        }

        EntrantId = Id;
        EntrantName = new Tag(Tag);

        if (data is null) { data = new Dictionary<string, string>(); }
        _entrantData = data;

        _entrants.Add(this);
    }

    public IndividualEntrant(int Id, string FirstName, string LastName, Dictionary<string, string>? data = null)
    {
        if (_entrants is null) _entrants = new List<Entrant>();
        foreach (var entrant in _entrants)
        {
            if (entrant.EntrantId == Id)
            {
                throw new InvalidOperationException("Attempting to create an entrant with an already existing Id");
            }
        }

        EntrantId = Id;
        EntrantName = new FullName(FirstName, LastName);

        if (data is null) { data = new Dictionary<string, string>(); }
        _entrantData = data;

        _entrants.Add(this);
    }
}

public record class TeamEntrant : Entrant
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