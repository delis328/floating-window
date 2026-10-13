# TXT Floating Reader

Windows lightweight transparent TXT reader built with .NET 8 WPF.

## Build

Install the .NET 8 SDK, then run:

```powershell
dotnet build .\FloatingTxtReader\FloatingTxtReader.csproj -c Release
dotnet publish .\FloatingTxtReader\FloatingTxtReader.csproj -c Release -r win-x64 --self-contained false -o .\publish
```

The framework-dependent publication requires the .NET 8 Desktop Runtime on the target machine.

## Use

Right-click the tray icon to open a TXT file, jump to the next detected chapter, lock or unlock the floating window, and open reading settings.
