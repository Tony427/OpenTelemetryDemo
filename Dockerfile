FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["OpenTelemetryDemo.csproj", "/src/"]
RUN dotnet restore "/src/OpenTelemetryDemo.csproj"
COPY . .
RUN dotnet publish "/src/OpenTelemetryDemo.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OpenTelemetryDemo.dll"]

