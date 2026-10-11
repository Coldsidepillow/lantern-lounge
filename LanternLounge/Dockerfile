# Lantern Lounge online server (ASP.NET Core 8). Render builds this file automatically.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Shared/Runtime ./Shared/Runtime
COPY DotNet ./DotNet
COPY Server ./Server
RUN dotnet publish Server/LanternLounge.Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV PORT=10000
EXPOSE 10000
ENTRYPOINT ["dotnet", "LanternLounge.Server.dll"]
