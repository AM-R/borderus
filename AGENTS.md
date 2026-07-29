# Borderus Project Instructions

@C:\Users\x\.codex\RTK.md
@D:\dev\.config\.karpathy\instructions.md

## Project Root

- The canonical project directory is `D:\dev\Borderus`.
- Do not edit or build copies of Borderus outside this directory.

## Working Mode

- Use Ponytail in full mode for this project.
- Keep changes small, direct, and consistent with the existing WPF application.

## Verification

- Run shell commands through `rtk`.
- Build with `dotnet build Borderus.csproj -c Debug` after code changes.
- Publish release artifacts from this project directory only.
- Increment the project version before every published release: patch for fixes, minor for new features, major for breaking changes.
- Include the version in the release archive filename.

## Language policy

- Always respond in English, regardless of the language the user writes in (including Russian).
- Exception: if the user explicitly asks for a reply in Russian (e.g. "ответь по-русски" / "напиши на русском"), give exactly that one reply in Russian.
- Immediately after that one Russian reply, return to English for all subsequent responses, unless the user again explicitly asks for Russian.

## Version bump                                                                                                                                                         █
- Increment patch on every commit: `vX.Y.Z` → `vX.Y.(Z+1)`. No exceptions for a normal commit.                                                                             █
- Bump minor instead (reset patch to 0) only when the user explicitly says "minor".                                                                                        █
- Bump major instead (reset minor and patch to 0) only when the user explicitly says "major".                                                                              █
                                                                                                                                                                           █
## Update all version markers (must match exactly, same vX.Y.Z everywhere)                                                                                              █
| # | Location | Format |                                                                                                                                                  █
|---|----------|--------|                                                                                                                                                  █
| 1 | HTML comment | `<!-- Nodus vX.Y.Z ... -->` |                                                                                                                         █
| 2 | Top of every changed JS file | `// Nodus vX.Y.Z ...` |                                                                                                               █
| 3 | Profile UI | `<span id="vtag">vX.Y.Z</span>` |                                                                                                                       █
| 4 | Page title | `<title>Nodus (vX.Y.Z)</title>` |                                                                                                                       █
                                                                                                                                                                           █
## Commit and push                                                                                                                                                      ░
```bash                                                                                                                                                                    ░
git add -A                                                                                                                                                                 ░
git commit -m "vX.Y.Z | type | description"                                                                                                                                ░
git push origin main                                                                                                                                                       ░
```                                                                                                                                                                        ░
