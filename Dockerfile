FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
# 先複製 csproj 以利用還原快取
COPY ["src/Presentation/OpenTelemetryDemo.WebApi/OpenTelemetryDemo.csproj", "src/Presentation/OpenTelemetryDemo.WebApi/"]
RUN dotnet restore "src/Presentation/OpenTelemetryDemo.WebApi/OpenTelemetryDemo.csproj"
# 複製其餘原始碼
COPY . .
WORKDIR /src/src/Presentation/OpenTelemetryDemo.WebApi
RUN dotnet publish "OpenTelemetryDemo.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OpenTelemetryDemo.dll"]

