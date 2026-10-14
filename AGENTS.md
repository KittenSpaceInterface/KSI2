# KSI2 Agent Instructions

Do not create commits unless the user explicitly asks for a commit. Once asked, commit only the changes within the requested scope.

Use Conventional Commit subjects for KSI2 changes, with clear descriptions that
can become release notes: `feat(navball): add heading markers` for a feature,
`fix(altitude): correct unit label` for a fix, and `feat!: revise layout` for a
breaking change. A `BREAKING CHANGE:` footer is another way to
mark a breaking change. Release Please reads eligible commits since the last
release to draft the changelog. Before `1.0.0`, features and breaking changes
propose minor versions; fixes propose patch versions. `docs:` and `chore:`
commits alone do not propose a release.

The `tools/build-install.sh` script runs geometry checks by default. Set
`SKIP_GEOMETRY_CHECKS=1` when building and installing without those checks; the
script still restores, builds Release, and installs the mod. For example, from
`KSI2/` in PowerShell:

```powershell
& 'C:\Program Files\Git\bin\bash.exe' -c 'SKIP_GEOMETRY_CHECKS=1 ./tools/build-install.sh'
```
