# Cats and Mouse online multiplayer game (API)

## Demo URL

[https://www.marcosgimeno.com/cats-and-mouse](https://www.marcosgimeno.com/cats-and-mouse)

## What is this?

Cats & Mouse is a game which is played on a chessboard.

5 pieces are used, 1 white to represent the mouse and 4 black to represent the cats.

**How to move the pieces?**

All pieces move diagonally on the black squares, but cats cannot move backwards, while the mouse can do so.

**What is the goal?**

The mouse is to reach the last row of the board, whereas the cats are to corner the mouse so that it's not able to make any movement.

## Languages

Available in English, Spanish, French, Portuguese, German & Italian.

## Development

Built with Angular 22 & SignalR. Responsive design for mobile/tablet/desktop.

API built using .NET 11.0 and SignalR, published as a Native AOT executable: no .NET runtime on the
server, startup in tens of milliseconds and a fraction of the memory of the JIT build.

Requires the .NET 11 SDK (pinned in `global.json`).

```bash
dotnet run                                     # http://localhost:53000, for the UI's dev server
dotnet test --solution CatsAndMouseApi.sln     # Microsoft.Testing.Platform runner (see global.json)
dotnet publish -r linux-arm64                  # Native AOT; build on Linux, as CI does
scripts/smoke-test.sh http://localhost:53000   # plays a full game against a running API
```

CI publishes the executable on an Azure Linux 3.0 image (glibc 2.38, older than the server's), plays a
game against it with `scripts/smoke-test.sh`, and deploys it to the Raspberry Pi. Each build compiles in
the latest .NET 11 patch, so redeploying is how runtime security fixes reach the server.
