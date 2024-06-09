# Set Working Directory
Split-Path $MyInvocation.MyCommand.Path | Push-Location
[Environment]::CurrentDirectory = $PWD

Remove-Item "$env:RELOADEDIIMODS/riri.globalredirector/*" -Force -Recurse
dotnet publish "./riri.globalredirector.csproj" -c Release -o "$env:RELOADEDIIMODS/riri.globalredirector" /p:OutputPath="./bin/Release" /p:ReloadedILLink="true"

# Restore Working Directory
Pop-Location