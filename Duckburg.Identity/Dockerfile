# --- build stage ---
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Duckburg.Identity.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o /app

# --- runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app ./
EXPOSE 8001
ENV ASPNETCORE_URLS=http://0.0.0.0:8001
# NOTE: private keys are NOT baked in. Provide them at runtime via env Rp__PrivateKeys
# or a mounted secret file referenced by Rp__PrivateKeysFile.
ENTRYPOINT ["dotnet", "Duckburg.Identity.dll"]
