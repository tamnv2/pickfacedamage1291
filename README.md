# PICKFACE DAMAGE 1291

Windows portable application for recording and managing Pickface 1291 damage information.

> Canonical scope is defined in `PROJECT_SCOPE_LOCK.md`.

## Scope

- GitHub repository: `tamnv2/pickfacedamage1291`
- Google Drive root: `16jDCy5_Z1X5cKJyNPR1rn_ZqbExQSAeC`
- Google Cloud project: `pickface-damage-1291`

## Build

```bash
dotnet publish src/PickfaceDamage1291/PickfaceDamage1291.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
