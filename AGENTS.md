# Borderus Project Instructions

@C:\Users\x\.codex\RTK.md

## Project Root

- The canonical project directory is `D:\dev\Borderus`.
- Do not edit or build copies of Borderus outside this directory.

## Working Mode

- Use Ponytail in full mode for this project.
- Keep changes small, direct, and consistent with the existing WPF application.
- Do code without any questions and plan approval, do it yourself the best.

## Verification

- Run shell commands through `rtk`.
- Close or kill current runned borderus.exe process first
- Build with `dotnet build Borderus.csproj -c Debug` after any steps with code changes.
- Publish release builds from this project directory only.
- Increment the project version before every published release: patch for fixes, minor for new features, major for breaking changes.
- Include the version in the release archive filename.
- Store each version's release build in its own `builds/vX.Y.Z/` directory.
- Increment version on every commit: `vX.Y.Z` → `vX.Y.(Z+1)`. If Z = 10, then Y+1 and after that Z = 0. If Y = 10, then X+1 and after that Y=0.
- Always refresh `builds/current/` so it contains the latest release build.
- Run d:\apps\borderus\borderus.exe

## Language policy

- Always respond in English, regardless of the language the user writes in (including Russian).
- Exception: if the user explicitly asks for a reply in Russian (e.g. "ответь по-русски" / "напиши на русском"), give exactly that one reply in Russian.
- Immediately after that one Russian reply, return to English for all subsequent responses, unless the user again explicitly asks for Russian.

## Version bump                                                                                

- Increment patch on every commit: `vX.Y.Z` → `vX.Y.(Z+1)`. No exceptions for a normal commit.
- Bump minor instead (reset patch to 0) only when the user explicitly says "minor".
- Bump major instead (reset minor and patch to 0) only when the user explicitly says "major".

## Commit and push

- Commit after any steps with code changes

```bash
git add -A
git commit -m "vX.Y.Z | type | description"
git push origin main
```
