FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY EventService.Domain/*.csproj EventService.Domain/
COPY EventService.Application/*.csproj EventService.Application/
COPY EventService.Infrastructure/*.csproj EventService.Infrastructure/
COPY EventService.Api/*.csproj EventService.Api/
RUN dotnet restore EventService.Api/EventService.Api.csproj

COPY EventService.Domain/ EventService.Domain/
COPY EventService.Application/ EventService.Application/
COPY EventService.Infrastructure/ EventService.Infrastructure/
COPY EventService.Api/ EventService.Api/
RUN dotnet publish EventService.Api/EventService.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "EventService.Api.dll"]
