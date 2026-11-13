FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY RestaurantPOS.csproj ./
RUN dotnet restore RestaurantPOS.csproj
COPY . .
RUN dotnet publish RestaurantPOS.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "RestaurantPOS.dll"]