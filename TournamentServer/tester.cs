using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TournamentSystem;

public class Program
{
    public static void Main(string[] args)
    {
        // var tour = new Tournament();
        // Entrant e1 = new IndividualEntrant(1, "Simon", "Libricky");
        // e1.EntrantData.Add("dateOfBirth", "11/10/2004");
        // tour.AddEntrant(e1);
        // Entrant e2 = new IndividualEntrant(2, "DonB");
        // tour.AddEntrant(e2);
        // Entrant e3 = new IndividualEntrant(3, "Alena", "Libricka");
        // tour.AddEntrant(e3);
        // Entrant e4 = new TeamEntrant(4, "Good team", [(IndividualEntrant)e1, (IndividualEntrant)e2]);
        // tour.AddEntrant(e4);
        // Set set1 = new Set(1);
        // tour.AddSet(set1);
        // Set set2 = new Set(2);
        // tour.AddSet(set2);
        // set1.SetDecider = new Set.BestOfDecider(3);

        // set1.Entrant1 = e1;
        // set1.Entrant2 = e2;
        // set2.Entrant1 = e3;
        // set1.SetWinnerGoesTo = set2;
        // set1.Games.Add(new Game(set1, 1, set1.Entrant1!, set1.Entrant2!));

        var filePath = "./test.json";
        var mf = new MyFormatConverter();
        // using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        // {
        //     var writerOptions = new JsonWriterOptions() { Indented = true };
        //     using (var writer = new Utf8JsonWriter(fileStream, writerOptions))
        //     {
        //         mf.Write(writer, tour, new JsonSerializerOptions());
        //     }
        // }

        //PropertyNameCaseInsensitive: True
        //JsonNamingPolicy: System.Text.Json.JsonCamelCaseNamingPolicy
        //NumberHandling: AllowReadingFromString

        byte[] jsonData = File.ReadAllBytes(filePath);
        Utf8JsonReader reader = new(jsonData);
        var reconstructedTour = mf.Read(ref reader, typeof(Tournament), new JsonSerializerOptions());
        ;
    }
}
