///Countable

namespace TournamentSystem;

/**
TODO:
    Tournament, Sets, Games, Entrants
    Progressing through a tournament - handled per request - multithreading
        Set winner decision defined in JSON (BOx or some other format)
**/

public class Tournament
{
    private Dictionary<string, Set> _sets { get; set; }
    private Dictionary<int, Entrant> _entrants { get; set; }
    private Dictionary<string, string> _data { get; set; }

    public IReadOnlyDictionary<string, Set> Sets => _sets;
    public IReadOnlyDictionary<int, Entrant> Entrants => _entrants;
    public IReadOnlyDictionary<string, string> Data => _data;

    public Tournament()
    {
        _sets = new Dictionary<string, Set>();
        _entrants = new Dictionary<int, Entrant>();
        _data = new Dictionary<string, string>();
    }

    public bool AddSet(Set set)
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

    public bool AddEntrant(Entrant entrant)
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

    public bool ModifyData(string label, string value)
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

    public bool DeleteData(string label)
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

public class Set
{
    private static List<Set> _sets;

    // Id here is a string, as there may be requests to use IDs that are more readable e.g. SF1, QF3
    private string _setId;
    private Entrant? _entrant1;
    private Entrant? _entrant2;
    private SetStatus _status;
    private Set? _setWinnerGoesTo;
    private Set? _setLoserGoesTo;
    private Entrant? _winner;
    private Entrant? _loser;
    private List<Game> _games;
    private IWinnerDecider? _setDecider;
    private string? _setName;

    public string SetId
    {
        get => _setId;
        set => _setId = value;
    }

    public Entrant? Entrant1
    {
        get => _entrant1;
        set => _entrant1 = value;
    }

    public Entrant? Entrant2
    {
        get => _entrant2;
        set => _entrant2 = value;
    }

    public SetStatus Status
    {
        get => _status;
        set => _status = value;
    }

    public Set? SetWinnerGoesTo
    {
        get => _setWinnerGoesTo;
        set => _setWinnerGoesTo = value;
    }

    public Set? SetLoserGoesTo
    {
        get => _setLoserGoesTo;
        set => _setLoserGoesTo = value;
    }

    public Entrant? Winner
    {
        get => _winner;
        set => _winner = value;
    }

    public Entrant? Loser
    {
        get => _loser;
        set => _loser = value;
    }

    public List<Game> Games
    {
        get => _games;
        set => _games = value;
    }

    public IWinnerDecider? SetDecider
    {
        get => _setDecider;
        set => _setDecider = value;
    }

    public string? SetName
    {
        get => _setName;
        set => _setName = value;
    }

    public enum SetStatus { IncompleteSetup, WaitingForEntrantsData, WaitingForStart, InProgress, Finished }

    // I only have a constructor with only ID, as the fields will be set later. This is because
    // of the order in which a tournament must be reconstructed from JSON, but also
    // because these fields will change inherently as a tournament progresses. An entrant on the other
    // hand should not change once it is created, as it is immutable.
    public Set(string id)
    {
        if (_sets is null) _sets = new List<Set>();
        foreach (var set in _sets)
        {
            if (set._setId == id)
            {
                throw new InvalidOperationException("Attempting to create a set with an already existing Id");
            }
        }

        _setId = id;
        _status = SetStatus.IncompleteSetup;
        _games = new List<Game>();

        _sets.Add(this);
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
        public int AmountOfWinsRequired;
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
}

public class Game
{
    public readonly Set ParentSet;
    public readonly int GameNumber;
    // Stored seperately to the sets teams, as for certain games Team 1 and Team 2 may have meanings
    // (e.g. side selection)
    public readonly Entrant Entrant1;
    public readonly Entrant Entrant2;
    public Entrant? GameWinner;

    public readonly GameStatus Status;

    public enum GameStatus { Waiting, InProgress, Finished }

    // Dictionary for the other data, stored as a string, and will be parsed when and if necessary
    // Given that the kind of data stored here can have a variety of formats, no point trying to parse here
    // The parsing can happen when and if needed when working with the game data.
    public Dictionary<string, string> GameData;

    public Game(Set ParentSet, int GameNumber, Entrant Entrant1, Entrant Entrant2, Dictionary<string, string>? GameData = null)
    {
        this.ParentSet = ParentSet;
        this.GameNumber = GameNumber;
        this.Entrant1 = Entrant1;
        this.Entrant2 = Entrant2;
        if (GameData is null) GameData = new Dictionary<string, string>();
        this.GameData = GameData;
    }
}

public abstract record class Entrant : IComparable<Entrant>
{

    static protected List<Entrant>? _entrants;
    public int EntrantId { get; init; }

    // This is a dictionary, as we don't have a set promise from the JSON as to what information 
    // can or can't be included, instead we just store directly from the JSON and any parsing is done
    // when required
    public Dictionary<string, string> EntrantData { get; init; }

    public int CompareTo(Entrant? other)
    {
        if (other == null) return 1;
        return EntrantId.CompareTo(other.EntrantId);
    }
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
        private readonly string _tag;

        public Tag(string tag)
        {
            _tag = tag;
        }

        public override string GetFullName() => _tag;
        public override string GetCondensedName() => _tag;
    }

    public class FullName : Name
    {
        private readonly string _firstName;
        private readonly string _lastName;
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
        EntrantData = data;

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
        EntrantData = data;

        _entrants.Add(this);
    }
}

public record class TeamEntrant : Entrant
{
    public required List<IndividualEntrant> IndividualEntrants { get; init; }

    public TeamEntrant(int Id, List<IndividualEntrant> individualEntrants)
    {
        IndividualEntrants = individualEntrants;
    }
}