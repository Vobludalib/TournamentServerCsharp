# Technical documentation

The entire backend is built from three main parts:
The model (in Model.cs)
The JsonConverers (in JsonConverters.cs)
and the ServerHandler (in ServerHandler).cs

Server.cs is there to expose the correct methods and handle the ASP.NET logic, but all the actual work is done in the three aforementioned parts.

## Model
Model contains all the logic for the tournament.
Each tournament is made up of several thing:
<ul>
<li>Sets, which represent a 1v1 encounter between two entrants, with one overall winner and loser decided after all games are played. The set then defines where the winner and losers go (this is how the bracket is defined, allowing free control of weird and wacky tournament formats).</li>
<li>Entrants, which can either be Individuals or Teams of Individuals. These are the people that enter your tournament to compete.</li>
<li>Data, which is a 'dumping ground' for other data that is relevant to the tournament, but not directly related to the bracket (e.g. name, date etc.)</li>
<li>Status - whether the tournament is being setup (which allows the most modification), InProgress (allowing limited modification) or Finished (should allow essentially no modification)</li>
</ul>

Because the model interacts fairly directly with the ServerHandler (which has to handle async stuff), a lot of the methods in the model are async too. Be careful here to thoroughly consider how you lock things.

#### Locking

In the model, I use AsyncEx ReaderWriterLocks that allow locking similar to ReaderWriterLockSlim from C#, but with async methods for getting the locks (allowing better performance, as we don't block threads waiting for lock). These can usually be access through a LockHandler (see code for details), but for Game can be accessed directly. The point of the LockHandlers is too allow a wrapper for locking for possible logging - currently they do not do this, they are more a proof of concept.

These AsyncEx locks when acquired return an IDisposable, which, when disposed, free up the lock.

To prevent deadlocks here, ensure that a consistent locking order is maintained.

In Tournament, always lock in the order: Sets, Entrants, Data, Status.

When locking multiple sets, games, or entrants at once, always lock in ascending Id (or gameNumber) order.

Do not try to explicitly lock using lock() {}. This will cause problems for async things + removes the possibility of multiple reads at the same time.

The reason ReaderWriter locks were chosen was that, in most cases, the majority of requests will be reading only, without editing, so allowing better parallelisation of this usecase was important.

In some cases, (see the code for a better understanding - most of the cases of this are in ServerHandler methods) locking is handled directly inside the class (e.g. Tournament), while when traversing class boundaries (e.g. locking certain things in the tournament, but also some sets), control of locking can and sometimes is left to the ServerHandler method to manage.

#### Interesting or computationally intensive methods in Model

Verifying that a Tournament is valid (Tournament.VerifyStructureAsync()) can be slower than other methods when the tournament is large.

This method checks that, when creating the bracket, every set will (at some point) have exactly 2 entrants. These can be either already filled in, or be entrants that will be moved to it as winners/losers from other sets.

Additionally, it checks that the bracket progression graph contains no cycles using DFS. Again, for larger brackets this can be longer than expected.

## ServerHandler
This class was created with the purpose of being a handler for the server requests and serving as a bridge between what the endpoints want, and how the model works. Additionally, it handles locking logic when crossing class boundaries.

When an instance is created, it instantiates all the relevant JSON converters to prevent unneccesary object creations.

Additionally, it handles error-checking requests and providing appropriate HTTP responses with messages to clarify usage.

## JsonConverters
This entire file is just for all the custom JsonConverters for the Model objects that change behaviour from what would be default (i.e. serializing and deserializing by properties).

The reason why these converters exist is to tackle Id-based referencing in the Json. For example, each Set object can contain references to other sets, which, by default, would also be serialized, creating nesting in the JSON that could have great depth. To combat this, references are replaced with the relevant Ids, which are then read in multiple steps when deserializing.

When deserializing anything with Id-based referencing, we first read all the information into what I called a LinksReport, which stores all the relevant info, but instead of referencing objects (which don't exist yet), save the Ids. Then, once all these LinksReports are created, we create the relevant objects with Ids (but not filling in the other info yet), so that we have objects to link to now. Then, we finally add all the other info and references.

Additionally, we have to handle IWinnerDeciders, which all have custom properties when serializing (I have only implemented BestOfDecider, which uses a number to store the amount of wins necessary to be declared winner). This is handled by serializing the type along with the necessary info (in a different way for each different IWinnerDecider).
When deserializing, we then first look for this type, and read the info we need based on the type of the IWinnerDecider. In this, we use reflection to look for the type.

These converters can be passed into the JsonSerializerOptions when using JsonSerializer.Serialize/Deserialize(object/jsonbody, options), and these custom methods will be used for the (de)serialization.

As you can see in the code, the Read methods are working with JSON on a fairly low-level, so they can get quite long.

Again, when (de)serializing don't forget to lock the relevant things in the correct order.

## Other
As stated in the Readme.md, we can pass a path as the only command line argument when using 'dotnet run', which will try to deserialize a tournament json on startup.

## To do:
- Implement more IWinnerDeciders and figure out a better way for serializing/deserializing that doesn't rely on reflection.
- Implement serializing/deserializing to other JSON formats
- Formal definition of my JSON format
- Expose more model methods via endpoints, and create more endpoints that modify the model in a more granular way than uploading whole sets/games.
- Research how MVC works with ASP.NET and consider a more robust, expandable way of managing endpoints/requests. The current solution has no routing etc.