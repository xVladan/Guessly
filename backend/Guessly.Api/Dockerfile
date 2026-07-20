# Multi-stage build: SDK image compiles/publishes, runtime image just runs the result.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Guessly.Api.csproj .
RUN dotnet restore Guessly.Api.csproj

COPY . .
RUN dotnet publish Guessly.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Overridden at deploy time via `docker run -e` / compose env — see embeddings/README.md
# for how EmbeddingsData is generated; it ships inside the image via the .csproj's
# Content items, so no volume mount is required for it to work out of the box.
ENV PORT=5000
ENV ALLOWED_ORIGINS=http://localhost:5500

EXPOSE 5000

ENTRYPOINT ["dotnet", "Guessly.Api.dll"]
