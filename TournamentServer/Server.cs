///Countable

namespace TournamentSystem;

class TournamentServer()
{
    public static void Main(String[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var app = builder.Build();

        app.MapGet("/", () => "Hello");

        app.Run();
    }

    /**
    TODO:
        Create all the endpoints with needed information
        Create all the endpoints for changing games
    **/
}
