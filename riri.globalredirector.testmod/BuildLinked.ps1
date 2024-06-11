# Set Working Directory
Split-Path $MyInvocation.MyCommand.Path | Push-Location
[Environment]::CurrentDirectory = $PWD

Remove-Item "$env:RELOADEDIIMODS/riri.globalredirector.testmod/*" -Force -Recurse
dotnet publish "./riri.globalredirector.testmod.csproj" -c Release -o "$env:RELOADEDIIMODS/riri.globalredirector.testmod" /p:OutputPath="./bin/Release" /p:ReloadedILLink="true"

# Restore Working Directory
Pop-Location