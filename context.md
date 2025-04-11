# Session Context Summary (WowClassicGrindBot)

This document summarizes the key findings, changes, and architectural insights gained during the current development session.

## Goals Achieved

1.  **Resolved Vendor Interaction Timeout:** Fixed an issue where the bot closed the vendor window too quickly during NPC sell interactions.
2.  **Implemented Auto-Start Feature:** Added functionality to automatically start the bot with a specific profile when launching the BlazorServer application via `run.bat` using an environment variable.

## 1. Vendor Interaction Timeout Issue

*   **Problem:** When using the `Mage_SOD_Farm.json` profile, the bot interacted with vendors (for selling items) for only a brief moment before closing the window, preventing the associated addon from selling all items.
*   **Analysis:**
    *   Reviewed `agent_analysis.md` to understand the GOAP architecture and how NPC interactions (`AdhocNPCGoal`) are triggered based on JSON configurations (`Json/class/*.json`).
    *   Examined `Mage_SOD_Farm.json` and initially hypothesized a missing `Wait` property in the "Sell" `KeyAction`.
    *   Inspected `Core/Goals/AdhocNPCGoal.cs` and discovered the wait logic for selling was handled internally by the `OpenMerchantWindow` method, using a hardcoded `TIMEOUT` constant (initially 5000ms) while waiting for `gossipReader.MerchantWindowSellingFinished`.
*   **Solution:**
    *   Increased the `TIMEOUT` constant in `Core/Goals/AdhocNPCGoal.cs` from `5000` to `15000` (15 seconds).
    *   Removed a previously added (and ultimately unused for this goal) `Wait` property from the "Sell" action in `Json/class/Mage_SOD_Farm.json`.

## 2. Auto-Start Feature Implementation

*   **Requirement:** Launch the BlazorServer application using `BlazorServer/run.bat` such that if an environment variable `autoStart` is set to `true`, the bot automatically loads the `Mage_SOD_Farm.json` profile and starts running.
*   **Implementation Details:**
    *   **`BlazorServer/run.bat`:**
        *   Modified to check if `%autoStart%` equals `true` (case-insensitive).
        *   If true, it executes `dotnet run --configuration Release --autostart --profile "..\Json\class\Mage_SOD_Farm.json"`.
        *   If false or not set, it runs `dotnet run --configuration Release` as before.
    *   **`BlazorServer/Program.cs`:**
        *   Modified `Main` to be `async Task Main(string[] args)`.
        *   Added logic after host creation to parse command-line arguments (`args`).
        *   If `--autostart` and `--profile <path>` are found:
            1.  Retrieves necessary services (`IBotController`, `PlayerReader`, `CancellationTokenSource`, `AddonReader`, `ExecGameCommand`, `AddonConfigurator`).
            2.  Waits 1.5 seconds (`Task.Delay(1500)`) to allow the `AddonThread` initial run time.
            3.  Enters a loop (`while (playerReader.HealthMax() == 0)`) waiting for the addon to become responsive (using `HealthMax > 0` as an indicator), checking every 100ms.
            4.  Executes an explicit InitState sequence (based on `InitButton.razor` logic):
                *   `addonReader.FullReset();`
                *   `exec.Run("");`
                *   `exec.Run($"/{addonConfigurator.Config.CommandFlush}");`
            5.  Waits 0.5 seconds (`Task.Delay(500)`).
            6.  Loads the specified class profile: `botController.LoadClassProfile(profilePath);`
            7.  Waits 0.1 seconds (`Task.Delay(100)`).
            8.  Starts the bot: `botController.ToggleBotStatus();`
        *   Modified `host.Run()` to `await host.RunAsync(cts!.Token)` for proper async execution and cancellation.
        *   Multiple build errors were encountered and fixed during this implementation (missing namespaces, async/await issues, incorrect method calls, scope problems, syntax errors).

## Files Modified

*   `Core/Goals/AdhocNPCGoal.cs` (Increased `TIMEOUT` constant)
*   `Json/class/Mage_SOD_Farm.json` (Removed unused `Wait` property)
*   `BlazorServer/Program.cs` (Added auto-start logic, initialization checks, InitState sequence, async Main)
*   `BlazorServer/run.bat` (Added environment variable check and argument passing)

## Key Architectural Insights

*   The bot's core logic is driven by a GOAP system (`Core/GOAP/`).
*   Behavior sequences (`KeyAction`s) are defined in JSON class configuration files (`Json/class/`).
*   NPC interactions are primarily handled by `Core/Goals/AdhocNPCGoal.cs`.
*   Game state is read from an in-game addon via screen pixel reading (`Core/AddonReader`, `Core/PlayerReader`, `Core/WowScreen`).
*   Initialization involves background threads (`AddonThread`) reading game state and requires confirmation that the addon is responsive. An explicit sequence (`FullReset`, `Run`, `Flush`) can be triggered for thorough state initialization.
*   The BlazorServer UI is launched via `dotnet run` and can accept command-line arguments parsed in `Program.cs`.
*   Dependency Injection is heavily used (`IServiceProvider`, `host.Services.GetRequiredService`).

## Next Steps

*   Rebuild the application (`dotnet build`) to apply the C# changes.
*   Test the vendor interaction fix.
*   Test the auto-start feature by setting the environment variable (`set autoStart=true`) and running `BlazorServer/run.bat`.
