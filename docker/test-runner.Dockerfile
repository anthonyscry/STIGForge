FROM mcr.microsoft.com/dotnet/sdk:8.0

WORKDIR /workspace

ENV DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_CLI_HOME=/tmp/.dotnet \
    NUGET_PACKAGES=/root/.nuget/packages

CMD ["dotnet", "test", "tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj", "--framework", "net8.0", "--configuration", "Release", "--nologo"]
