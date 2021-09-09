FROM mcr.microsoft.com/dotnet/sdk:6.0.100-preview.7-bullseye-slim-amd64 AS build-env
WORKDIR /app

COPY ./*sln ./

COPY ./OpenFTTH.RelationalProjector/*.csproj ./OpenFTTH.RelationalProjector/

RUN dotnet restore --packages ./packages

COPY . ./
WORKDIR /app/OpenFTTH.RelationalProjector
RUN dotnet publish -c Release -o out --packages ./packages

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime:6.0.0-preview.7-bullseye-slim-amd64
WORKDIR /app

COPY --from=build-env /app/OpenFTTH.RelationalProjector/out .
ENTRYPOINT ["dotnet", "OpenFTTH.RelationalProjector.dll"]