# 1. Build aşaması
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY ["Handly.WebAPI.csproj", "./"]

COPY . .

RUN dotnet publish "Handly.WebAPI.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false 


# 2. Çalıştırma aşaması
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* 


COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Handly.WebAPI.dll"]