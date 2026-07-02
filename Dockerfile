# syntax=docker/dockerfile:1

# ---- build: restore + publish ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy just the project file first so `dotnet restore` is cached separately from source
# changes — same reasoning as the client Dockerfile's deps stage.
COPY LedgrApi.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app --no-restore

# ---- runner: minimal ASP.NET runtime, no SDK ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runner
WORKDIR /app

# .NET 8+ container images default to port 8080 for HTTP when this is set explicitly.
ENV ASPNETCORE_HTTP_PORTS=8080

# mcr.microsoft.com/dotnet/aspnet is Debian-based (no addgroup/adduser — that's an Alpine/
# busybox thing, which is what the client's Dockerfile uses instead). No need to create a
# user manually either way: .NET 8+ runtime images ship a built-in non-root "app" user
# specifically so consumers don't have to.
COPY --from=build --chown=app:app /app ./

USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "LedgrApi.dll"]
