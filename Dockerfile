FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy everything first to ensure analyzers and references are correctly resolved
COPY . .
RUN dotnet restore Beloved.ControlPlane/Beloved.ControlPlane.csproj
RUN dotnet publish Beloved.ControlPlane/Beloved.ControlPlane.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Non-root user for security
RUN adduser --disabled-password --gecos "" beloveduser && chown -R beloveduser /app
USER beloveduser

COPY --from=build /app/publish .

# Expose Control Plane port
EXPOSE 3000

ENV ASPNETCORE_URLS=http://+:3000
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Beloved.ControlPlane.dll"]
