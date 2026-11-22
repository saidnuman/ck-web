# Build aşaması (.NET 10 SDK)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["ProductShowcase.Api.csproj", "./"]
RUN dotnet restore "ProductShowcase.Api.csproj"

COPY . .
RUN dotnet publish "ProductShowcase.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime aşaması (.NET 10 ASP.NET Runtime)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .
RUN mkdir -p /app/wwwroot/uploads

EXPOSE 8080

# Render'ın çalışma anında atadığı PORT ortam değişkenini (yoksa 8080) otomatik dinle
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} exec dotnet ProductShowcase.Api.dll"]
