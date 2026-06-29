FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY GeneradorTurnos.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish GeneradorTurnos.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV App__SeedDemoData=false

COPY --from=build /app/publish ./

CMD ["sh", "-c", "dotnet GeneradorTurnos.dll --urls http://0.0.0.0:${PORT:-8080}"]
