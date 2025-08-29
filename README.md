## Frontend part of Bingo game

### Created in Unity with C#

I know this part could have been easily done in Android studio or Flutter. But, I wanted to test Unity for development of online multiplayer game.

This part has many bugs and illogical things because I'm a noob.

I have used Websocket-sharp for handling websockets in C#. Logics such as checking for winner, populating Bingo grid with random 2-dimensional 5x5 array, etc. are contained in this part.

Developers who wanna contribute are welcome!

## This game is available on google play store.
### [Click here to view](https://play.google.com/store/apps/details?id=com.Flyprosper.Bingo)

# How to play:

1. This is a two player online version of Bingo game.
2. One player creates a room, receives a room code, and then shares it with another player on another phone to join.
3. Once the connection between the two players is established, the players can start playing.
4. When the room is ready, each player gets a random 2-dimensional BINGO board on which a random value is to be marked.
5. The one who created the room, plays the first move, then the other player, and then again the first player, and so on.
6. A value marked by (say) player 1, will be reflected on boards of both players (player 1 and player 2).
7. On getting 5 continuous values marked (either horizontally, vertically or diagonally), one letter from the word BINGO will be crossed.
8. This goes on until a player gets all letters of BINGO crossed and wins.

### (Switch to backend branch for checking server-side code, which is written in Golang.)