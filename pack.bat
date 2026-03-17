@echo off
echo Packing EntglDb NuGet Packages...
mkdir nupkgs 2>nul

dotnet pack src/EntglDb.Core/EntglDb.Core.csproj -c Release -o nupkgs
dotnet pack src/EntglDb.Network/EntglDb.Network.csproj -c Release -o nupkgs
dotnet pack src/EntglDb.AspNet/EntglDb.AspNet.csproj -c Release -o nupkgs
dotnet pack src/EntglDb.Persistence/EntglDb.Persistence.csproj -c Release -o nupkgs
dotnet pack src/EntglDb.Persistence.BLite/EntglDb.Persistence.BLite.csproj -c Release -o nupkgs
dotnet pack src/EntglDb.Persistence.EntityFramework/EntglDb.Persistence.EntityFramework.csproj -c Release -o nupkgs

echo.
echo Packages created in 'nupkgs' directory.
dir nupkgs
pause
