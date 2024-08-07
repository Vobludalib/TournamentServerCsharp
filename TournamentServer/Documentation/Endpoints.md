# Endpoints

## GET
```
/
```
Redirects you to the Github repo homepage.
```
/tournament
```
Returns a JSON with the serialized form of the tournament.
```
/entrant/{id}
```
Returns a JSON with serialized form of the entrant with that given id. If an entrant with that id does not exist, returns a 404 HTTP response.
{id} should be an integer.
```
/set/{id}
```
Returns a JSON with serialized form of the set with that given id. If a set with that id does not exist, returns a 404 HTTP response.
{id} should be an integer.
```
/data
```
Returns a JSON with serialized form of the tournament data dictionary.
```
/data/{key}
```
Returns a JSON with serialized form of the value associated with that given key in the tournament data dictionary.
If {key} is not a key in the data dictionary, returns a 404 HTTP response.
```
/status
```
Returns a JSON with serialized form of the tournament status.

## POST
```
/tournament
```
Expects the request body to contain a JSON of a serialized tournament. The server will deserialize the tournament, and load it as the current tournament.
```
/tournament/transitionTo/{status}
```
No request body requirements. Attempts to move the tournament into the given state. If it succeeds, returns a 200 HTTP response. If it fails, it returns a 400 HTTP request, with details as to how you can fix it.

If trying to move to "InProgress", there is a possibly lengthy check validating the tournament structure.

{status} can be: "InProgress", "Finished".
```
/set/{id}/addGame
```
Expects the request body to contain a JSON of a serialized game. The server will deserialize the game, and try to add it to the games of the set with the given id. Returns a 400 HTTP response in case the game trying to be added is not valid to be added to the set (e.g. wrong gameNumber).
```
/set/{id}/progress
```
Attempts to move the set with the given id from InProgress to Finished by checking that the WinnerDecider conditions have been met. As part of this, it assigns the set a winner and loser if the set is ready to be called Finished.

This DOES NOT move the winner and loser to their next respective sets.
```
/set/{id}/moveWinnerAndLoser
```
Attempts to move the winner and loser of the set with the given id to their next sets (as defined in the set by setWinnerGoesTo and setLoserGoesTo). Has checking to make sure the winner/loser can actually be moved into the set. If there is no winner and loser, still returns 200 HTTP response, with body explaining that nothing changed.
```
/set/{id}/transition/{state}
```
Attempts to move the set with the given id into the given state, checking along the way that this is a valid thing to do at the current state of the set.

{state} can be: "WaitingForStart", "InProgress"
For transitioning to Finished, use /set/{id}/progress
```
/set/{setId}/game/{gameNumber}/transitionTo/{state}
```
Attempts to transition the game with given gameNumber in set with given setId to state {state}, checking that this is a valid thing to do given the current state of the game.

{state} can be: "Waiting", "InProgress"
```
/set/{setId}/game/{gameNumber}/setWinner/{entrantId}
```
Attempts to set entrant with the given entrantId as the game with given gameNumber belonging to set with given setId as the game's winner, checking that this entrant is part of the game in question and the game is in a valid state.

Transitions game state to Finished if succesful.