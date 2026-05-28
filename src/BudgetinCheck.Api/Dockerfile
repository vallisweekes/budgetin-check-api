FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY BudgetinCheck.sln ./
COPY src/BudgetinCheck.Api/BudgetinCheck.Api.csproj src/BudgetinCheck.Api/
RUN dotnet restore src/BudgetinCheck.Api/BudgetinCheck.Api.csproj

COPY . .
RUN dotnet publish src/BudgetinCheck.Api/BudgetinCheck.Api.csproj \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BudgetinCheck.Api.dll"]