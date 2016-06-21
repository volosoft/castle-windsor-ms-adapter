@ECHO OFF
SET /P VERSION_SUFFIX=Please enter version-suffix (can be left empty): 

dotnet "pack" "..\src\Castle.Windsor.MsDependencyInjection" -c "Release" -o "." --version-suffix "%VERSION_SUFFIX%"

pause
