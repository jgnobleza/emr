FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY medrec.csproj ./
RUN dotnet restore medrec.csproj

COPY . ./
RUN dotnet publish medrec.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish ./

RUN mkdir -p /app/App_Data/data-protection-keys \
    /app/App_Data/offline-queue \
    /app/wwwroot/uploads/patients \
    /app/wwwroot/uploads/labs \
    /app/wwwroot/uploads/logos \
    /app/wwwroot/uploads/signatures \
    /app/wwwroot/uploads/layout-images

EXPOSE 8080

CMD ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} dotnet medrec.dll
