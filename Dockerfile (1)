# Lantern Lounge online server (ASP.NET Core 8). Render builds this file automatically.
# The whole game (server, website and game rules) arrives in ONE file, site.zip. To update the site,
# replace site.zip in GitHub; nothing else needs to change.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
RUN apt-get update && apt-get install -y --no-install-recommends unzip && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY site.zip ./
RUN unzip -q site.zip && rm site.zip
RUN dotnet publish Server/LanternLounge.Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV PORT=10000
EXPOSE 10000
ENTRYPOINT ["dotnet", "LanternLounge.Server.dll"]
