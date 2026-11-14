# ArcaneThai

## Platform
Windows desktop, x64, .NET Framework 4.8 / WinForms.

## Users and purpose
Thai-speaking Hogwarts Legacy players install and remove Thai ArcaneMP menus and a partial Thai game-text pack using one portable EXE.

## Confirmed workflow
The user requested a new GUI with one ArcaneMP folder selection, then a choice of menu translation, game translation, or both. Keep direct Steam/Epic game installation available separately.

## Constraints
Keep official client DLLs, game executable, original PAKs, saves, and other mods intact. Reuse the existing patch engines, verified backups, mods/ArcaneThai storage, and native loader link. Do not silently launch or close games. Game text coverage is 40,520 / 56,112 entries, about 72%; there is no Thai voice pack.

## Evidence
Existing engine checks and source live under installer/ and tests/. Menu previews in docs/ are Chromium renders with demonstration data, not gameplay screenshots. Directly loading the translated DLL from mods caused CEF subprocess launch failures (error 63) and a libcef crash. Loading the same DLL through a root hard link restored the original module directory; logs showed a working menu and the user confirmed joining the server. Both the menu DLL and game PAK remain stored in mods but must use their native root/scan hard links. Do not configure the launcher to load the subfolder DLL directly.
