FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY nuget.config ./
COPY Networking/ ./Networking/
COPY Backend/ ./Backend/
RUN dotnet publish Backend/GameConsoleServer.csproj -c Release -o /publish

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /publish/ ./
USER $APP_UID
EXPOSE 26051/tcp 26052/udp
ENTRYPOINT ["dotnet", "GameConsoleServer.dll"]
