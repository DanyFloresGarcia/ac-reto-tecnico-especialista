FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY NotificationService.Domain/*.csproj NotificationService.Domain/
COPY NotificationService.Application/*.csproj NotificationService.Application/
COPY NotificationService.Infrastructure/*.csproj NotificationService.Infrastructure/
COPY NotificationService.Worker/*.csproj NotificationService.Worker/
RUN dotnet restore NotificationService.Worker/NotificationService.Worker.csproj

COPY NotificationService.Domain/ NotificationService.Domain/
COPY NotificationService.Application/ NotificationService.Application/
COPY NotificationService.Infrastructure/ NotificationService.Infrastructure/
COPY NotificationService.Worker/ NotificationService.Worker/
RUN dotnet publish NotificationService.Worker/NotificationService.Worker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "NotificationService.Worker.dll"]
