FROM mcr.microsoft.com/dotnet/runtime:10.0

WORKDIR /app
COPY . .

ENV DOTNET_EnableDiagnostics=0
ENV DOTNET_GCHeapHardLimit=0x10000000

USER $APP_UID

ENTRYPOINT ["dotnet", "Runesgalder.Sandbox.Worker.dll"]