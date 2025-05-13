# Use the official Microsoft .NET SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine-amd64v8 AS build-env
WORKDIR /app

COPY ./ARC3 .
RUN dotnet restore
RUN dotnet publish -c Release -o out -p:PublishSingleFile=true --self-contained true

# Build the runtime image using the official Microsoft .NET runtime image
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine-amd64v8
WORKDIR /app

COPY --from=build-env /app/out/arc3 .

ENTRYPOINT ["./arc3"]
