namespace TournamentSystem;

public class Program
{
    public static void Main(string[] args)
    {
        var tour = new Tournament();
        Entrant e1 = new IndividualEntrant(1, "Simon", "Libricky");
        tour.AddEntrant(e1);
        Entrant e2 = new IndividualEntrant(2, "DonB");
        tour.AddEntrant(e2);
        Entrant e3 = new IndividualEntrant(3, "Alena", "Libricka");
        tour.AddEntrant(e3);
        Set set1 = new Set("Loser's Final");
        tour.AddSet(set1);
        Set set2 = new Set("Grand Final");
        tour.AddSet(set2);

        set1.Entrant1 = e1;
        set1.Entrant2 = e2;
        set2.Entrant1 = e3;
        set1.SetWinnerGoesTo = set2;

        MyFormatSerializer mf = new();
        mf.Serialize(tour, "./test.json");
    }
}