FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY RestaurantPOS.csproj ./
RUN dotnet restore RestaurantPOS.csproj
COPY . .
RUN dotnet publish RestaurantPOS.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
# Runs as the image's non-root user. keys: sign-in keys, logs: log files (both on volumes in production).
RUN mkdir -p /app/keys /app/logs && chown -R $APP_UID /app/keys /app/logs
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "RestaurantPOS.dll"]
