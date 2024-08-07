# Tournament Bracket System
By: Simon Libricky - [simonlibricky.com](www.simonlibricky.com)

As part of Charles University Advanced C# Project

## Project outline

The project is designed to be the backend framework for managing bracketed tournament systems (imagine the Wimbledon bracket).

The entire tournament runs on an ASP.NET server that exposes several endpoints that can be used to send/load a tournament in JSON form and manipulate it in many ways. The only fully supported workflow is the workflow of:
You as user create a tournament is the setup State (see /examples/ for example Jsons)
Upload it to the server
Manipulate the tournament through state transitions for the tournament, its sets, and their games.
Query the server for specific JSONs that represent the tournament in its current state.

One example usecase for this is to maintain these endpoints, so that an external application that broadcasts the tournament can have up-to-date information about the tournament progress.

## How to use

You must have .NET 8 installed.

If you want to run the server locally, you can simply navigate into the /TournamentServer/ folder on the command line and run:
```
dotnet run [path/to/json.json]
```

The application takes one optional command line argument, which can be a path to a .json file of a tournament to load on startup.

This will launch the server. You can then use https://localhost:8080 or http://localhost:8081 (by default) as the URL to start making you GET and POST requests to.

Given this is an ASP.NET server, this can be hosted in a plethora of ways. Again, this is a 'starting point' for a backend of a much more expansive application, so the real end user here are future developers wishing to use this as a start point for their own frontends or fullstack applications using the code I wrote as a starting point.

## Endpoint documentation

Too see all available server endpoints, open up [Endpoint documentation](/TournamentServer/Documentation/Endpoints.md)

## C# Developer documentation
To read the developer documentation, open up [Dev documentation](/TournamentServer/Documentation/html/index.html)

The above automatically generated documentation from XML comments in the source code - additional comments can be found in the source code where relevant.

For an explanation of the project from a technical side, see [Explanation and guide](/TournamentServer/Documentation/Explanation.md)