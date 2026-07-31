FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["Beloved.ControlPlane/Beloved.ControlPlane.csproj", "Beloved.ControlPlane/"]
COPY ["Beloved.AssemblyEngine/Beloved.AssemblyEngine.csproj", "Beloved.AssemblyEngine/"]
RUN dotnet restore "Beloved.ControlPlane/Beloved.ControlPlane.csproj"

COPY . .
WORKDIR "/src/Beloved.ControlPlane"
RUN dotnet build "Beloved.ControlPlane.csproj" -c Release -o /app/build
RUN dotnet publish "Beloved.ControlPlane.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:3000
EXPOSE 3000
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Beloved.ControlPlane.dll"]
