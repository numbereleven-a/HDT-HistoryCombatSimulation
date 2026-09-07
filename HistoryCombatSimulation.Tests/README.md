# Tests

Build the solution in Release/x64 with the HDT dependencies configured through `HdtDir` or the explicit `HdtExe`, `HearthDbPath` and `HearthMirrorPath` properties.

Run `bin/x64/Release/net472/HistoryCombatSimulation.Tests.exe` for the core regression suite.

For the additional controller, source-binding and dispatcher lifecycle tests, set `HDT_TEST_RUNTIME` to the directory containing `HearthstoneDeckTracker.exe` and its runtime DLL dependencies, then run the test executable with `--lifecycle`. Run this suite in a separate process because it creates a WPF Application. It does not start HDT or Hearthstone. The unload fixture suppresses settings persistence and exercises the session reset used on reload without loading user settings or initializing the host.

These isolated tests do not replace a Solo Battlegrounds session covering plugin disable/re-enable, reconnects and delayed Bob's Buddy results.
