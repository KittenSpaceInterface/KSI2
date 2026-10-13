# KSI2 Agent Instructions

Do not create commits unless the user explicitly asks for a commit. Once asked, commit only the changes within the requested scope.

The `tools/build-install.sh` script runs geometry checks by default. Set
`SKIP_GEOMETRY_CHECKS=1` when building and installing without those checks; the
script still restores, builds Release, and installs the mod. For example, from
`KSI2/` in PowerShell:

```powershell
& 'C:\Program Files\Git\bin\bash.exe' -c 'SKIP_GEOMETRY_CHECKS=1 ./tools/build-install.sh'
```
