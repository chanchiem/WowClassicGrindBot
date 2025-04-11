# WowClassicGrindBot Architecture Analysis for AI Agents

This document provides an architectural overview and high-level algorithmic analysis of the WowClassicGrindBot project, intended to orient AI agents or developers for feature development, bug fixing, or behavior adjustments.

## 1. High-Level Overview

WowClassicGrindBot is a C#/.NET application designed to automate gameplay in World of Warcraft Classic versions (SoM, TBC, WotLK). It interacts with the game primarily through:

*   **Screen Reading:** An in-game addon (modified Happy-Pixels) displays game state information encoded as colored pixels in a specific screen area. The bot captures the screen (using DXGI) and reads these pixel colors.
*   **Input Simulation:** The bot sends keyboard and mouse inputs to the WoW process to control the character.
*   **No Memory Tampering:** The bot avoids reading game memory or injecting DLLs, relying solely on visual information and input simulation.

The core logic uses a **Goal-Oriented Action Planning (GOAP)** system to make decisions about what actions to perform based on the perceived game state and a configurable set of goals and actions.

## 2. Core Architecture

The system is built using .NET (currently targeting .NET 9) and follows a modular design heavily reliant on Dependency Injection (DI).

### 2.1. Main Components

*   **`Core` Project:** Contains the central bot logic, including the GOAP system, goal implementations, game state readers, input handling, navigation control, and configuration loading.
*   **`Game` Project:** Likely contains lower-level interactions with the WoW process and screen (e.g., `WowProcess`, `WowScreenDXGI`, `WowProcessInput`).
*   **`PPather` Project:** Contains the local pathfinding service implementation.
*   **`BlazorServer` Project:** Provides a web-based UI (ASP.NET Core Blazor) for monitoring and configuring the bot.
*   **`HeadlessServer` Project:** Allows running the bot without a UI, controlled via command-line arguments.
*   **`Frontend` Project:** Contains shared Razor components used by `BlazorServer`.
*   **`Json/` Directory:** Stores configuration files (class profiles, path files, addon layout) and potentially cached data (path info, history).
*   **`Addons/` Directory:** Contains the in-game Lua addon source code.

### 2.2. Orchestration (`Core/BotController.cs`)

*   The `BotController` class acts as the main orchestrator.
*   It initializes and manages core services obtained via DI.
*   It runs background threads for continuous tasks:
    *   `addonThread`: Reads addon pixel data via `WowScreenDXGI` and `AddonReader`.
    *   `screenshotThread`: Updates NPC/Minimap detection (`NpcNameFinder`, `MinimapNodeFinder`).
    *   `remotePathing` (optional): Visualizes paths via a remote service.
*   It loads `ClassConfiguration` and `Path` files based on user selection or defaults.
*   Crucially, it creates and manages a **session-scoped** `GoapAgent` instance whenever a profile is loaded. The `BotController` itself doesn't make high-level decisions but delegates this to the `GoapAgent`.
*   It handles starting/stopping the bot (`ToggleBotStatus`) by controlling the `GoapAgent`'s active state.

### 2.3. Dependency Injection (`Core/DependencyInjection.cs`)

*   The application uses Microsoft.Extensions.DependencyInjection extensively.
*   Services are registered primarily as singletons.
*   Extension methods group registrations logically (e.g., `AddCoreNormal`, `AddAddonComponents`).
*   Interfaces are used heavily (`IWowScreen`, `IPPather`, `IReader`, `IBotController`) for loose coupling.
*   Conditional registration allows selecting implementations based on startup configuration (e.g., choosing between local/remote pathing).
*   A critical `AddWoWProcess` step validates the WoW process, addon version, and screen configuration (`FrameConfig`) on startup.

### 2.4. Goal-Oriented Action Planning (GOAP) (`Core/GOAP/`, `Core/Goals/`)

This is the heart of the bot's decision-making process.

*   **`GoapAgent`:**
    *   Runs the main GOAP loop in a dedicated thread.
    *   Maintains the current `WorldState` (a `BitVector32` representing boolean conditions like `incombat`, `hastarget`, `shouldloot`).
    *   Continuously updates `WorldState` based on data from various `AddonReader` components and internal state (`GoapAgentState`).
    *   Holds a list of `AvailableGoals` (instances of `GoapGoal` subclasses, loaded based on `ClassConfiguration`).
    *   Uses a `GoapPlanner` to find a sequence (`Plan`) of goals to achieve a desired state (typically an idle/ready state).
    *   Executes the current `Plan` by popping goals one by one and calling their `Update()` method.
    *   Manages goal lifecycle (`OnEnter`, `OnExit`).
    *   Handles events (`GoapEventArgs`) emitted by goals to update state or trigger other actions (like managing POIs).
*   **`GoapGoal` (Abstract Base Class):**
    *   Defines `Preconditions` (required `WorldState` flags to run) and `Effects` (flags changed upon completion).
    *   Has a `Cost` used by the planner.
    *   Defines lifecycle methods (`OnEnter`, `Update`, `OnExit`, `CanRun`).
    *   Can be associated with `KeyAction`s.
    *   Can emit `GoapEventArgs`.
*   **Concrete Goals (e.g., `CombatGoal`, `LootGoal`, `FollowRouteGoal`, `AdhocGoal`, `NPCGoal`):**
    *   Inherit from `GoapGoal`.
    *   Implement specific behaviors by interacting with game state readers and the `ConfigurableInput` service.
    *   Define specific preconditions, effects, and costs.
    *   Often iterate through sequences of `KeyAction`s defined in the `ClassConfiguration`.

## 3. Key Subsystems

### 3.1. Game State Reading (`Core/Addon/`, `Core/DataFrame/`, `Core/WoWScreen/`)

*   **Addon (`Addons/DataToColor/`):** A Lua addon runs in-game, collecting game state (player stats, target info, cooldowns, buffs, debuffs, bag contents, etc.) and displaying it as colored pixels in a predefined screen area.
*   **Screen Capture (`Core/WoWScreen/WowScreenDXGI.cs`):** Uses DXGI Output Duplication to capture the game screen efficiently.
*   **DataFrame (`Core/DataFrame/`):** Defines the layout (`frame_config.json`) of the addon's pixels. `WowScreenDXGI` reads pixel colors based on this layout.
*   **Addon Data Provider (`IAddonDataProvider` implemented by `WowScreenDXGI`):** Reads the addon pixel colors and converts them into a raw integer array (`Data`).
*   **Addon Readers (`Core/Addon/`):**
    *   `AddonReader`: Consumes the raw `Data` array.
    *   Specific Readers (`PlayerReader`, `BagReader`, `CombatLog`, `ActionBarCostReader`, etc.): Inherit from `IReader` and interpret specific indices within the `Data` array to provide structured game state information (e.g., `playerReader.HealthPercent`, `bits.Target()`).

### 3.2. Input Simulation (`Core/Input/`, `Game/Input/`)

*   **`WowProcessInput` (`Game/Input/`):** Lower-level service responsible for sending keyboard and mouse events directly to the WoW process window (likely using Windows API calls like `SendInput` or `PostMessage`).
*   **`ConfigurableInput` (`Core/Input/`):** Wraps `WowProcessInput`. Reads key bindings from the `ClassConfiguration` and provides high-level action methods (e.g., `PressInteract`, `PressJump`) used by `GoapGoal` implementations.

### 3.3. Navigation (`Core/PPather/`, `PPather/`, `PathingAPI/`)

*   **Path Representation:** Paths are stored as JSON arrays of {X, Y, Z} coordinates in `Json/path/`.
*   **Pathing Interface (`IPPather`):** Defines the contract for pathfinding services.
*   **Implementations:**
    *   `LocalPathingApi`: In-process pathfinding using the `PPatherService` (from the `PPather` project), which likely loads map geometry from MPQ files and uses A*.
    *   `RemotePathingAPI`: Client for a V1 remote pathing service (implemented in the `PathingAPI` project).
    *   `RemotePathingAPIV3`: Client for a V3 remote pathing service (AmeisenNavigation, C++ based).
    *   The active implementation is chosen via DI based on configuration.
*   **`Navigation` Class (`Core/Navigation` - *Assumed location, not explicitly checked*):** Used by `FollowRouteGoal`. Takes a list of waypoints and handles the low-level movement logic (likely using `WowProcessInput` for movement keys or potentially Click-To-Move) to follow the path, including stuck detection and recovery.
*   **`FollowRouteGoal`:** Manages the high-level navigation logic, feeding waypoints from the loaded path file to the `Navigation` component and handling path reversals or finding the closest point.

### 3.4. Configuration (`Json/`, `Core/ClassConfig/`)

*   **Class Configuration (`Json/class/*.json`, `Core/ClassConfig/ClassConfiguration.cs`):** Defines class-specific behavior, including:
    *   Key bindings for actions.
    *   Sequences of `KeyAction`s for different goals (Pull, Combat, Adhoc, NPC).
    *   Requirements (conditions) for each `KeyAction`.
    *   Settings like `Loot`, `UseMount`, `PathFilename`, `Mode`.
    *   Custom variables (`IntVariables`).
*   **Path Files (`Json/path/*.json`):** Simple JSON arrays defining sequences of map coordinates.
*   **Addon Layout (`frame_config.json`):** Defines the pixel coordinates read by `WowScreenDXGI`. Generated via the BlazorServer UI.
*   **Addon Config (`addon_config.json`):** Configures the in-game addon (author, title). Generated via the BlazorServer UI.
*   **Data Config (`data_config.json`):** Specifies paths for external data (DBC, MPQ, profiles).

### 3.5. User Interface (`BlazorServer/`, `Frontend/`)

*   **`BlazorServer`:** An ASP.NET Core Blazor Server application providing a web UI for:
    *   Monitoring bot state (player info, current goal, logs, route map).
    *   Starting/Stopping the bot.
    *   Selecting class and path profiles.
    *   Initial addon and frame configuration.
    *   Editing loaded profiles (potentially).
    *   Recording new paths.
*   **`Frontend`:** Contains shared Razor components.
*   **`HeadlessServer`:** Allows running the bot logic without the Blazor UI, configured via JSON files and command-line arguments.

## 4. Execution Flow (Simplified Grind Mode Example)

1.  **Startup:** `Program.cs` (in `BlazorServer` or `HeadlessServer`) sets up DI, validates WoW process/addon/frame config.
2.  **Profile Load:** User selects profiles (or defaults used). `BotController` loads `ClassConfiguration` and path files.
3.  **Session Creation:** `BotController` creates a DI scope and instantiates `GoapAgent` with the loaded config and available goals.
4.  **Bot Start:** User starts the bot. `BotController` sets `GoapAgent.Active = true`.
5.  **GOAP Loop (in `GoapAgent` thread):**
    *   `UpdateWorldState()`: Reads current game state from `AddonReader` components.
    *   `GoapPlanner.Plan()`: If the current plan is empty, finds a new sequence of goals based on `WorldState` and available goal costs/preconditions/effects. The highest priority goal is likely `FollowRouteGoal` if not in combat/danger.
    *   `Plan.Pop()`: Gets the next goal (e.g., `FollowRouteGoal`).
    *   `CurrentGoal.OnEnter()`: `FollowRouteGoal` starts the `Navigation` component and the target-finding background thread.
    *   `CurrentGoal.Update()`: `FollowRouteGoal` calls `Navigation.Update()`. The background thread searches for targets.
6.  **Target Found:** The `FollowRouteGoal` background thread finds a valid target via `TargetFinder`. It cancels itself (`sideActivityCts.Cancel()`).
7.  **GOAP Re-plans:**
    *   `UpdateWorldState()`: State now reflects `hastarget = true`.
    *   `GoapPlanner.Plan()`: Generates a new plan, likely starting with `PullGoal` or `CombatGoal`.
    *   `CurrentGoal.OnExit()`: `FollowRouteGoal.Abort()` stops navigation.
    *   `Plan.Pop()`: Gets `PullGoal` (or `CombatGoal`).
    *   `CurrentGoal.OnEnter()`: `PullGoal` starts its logic.
    *   `CurrentGoal.Update()`: `PullGoal` executes its `KeyAction` sequence (e.g., casting a ranged attack) using `ConfigurableInput` and `CastingHandler`.
8.  **Combat:**
    *   `UpdateWorldState()`: State reflects `incombat = true`.
    *   Planner likely transitions to `CombatGoal`.
    *   `CombatGoal` executes its `KeyAction` sequence until the target is dead (`targetisalive = false`).
9.  **Post-Combat:**
    *   `CombatLog` detects kill, `GoapAgent.OnKillCredit` updates `State` (`producedcorpse = true`, `LootableCorpseCount++`). `CombatGoal` might send `CorpseEvent`.
    *   `UpdateWorldState()`: Reflects `producedcorpse = true`, `shouldloot = true` (if `LootableCorpseCount > 0`).
    *   Planner likely transitions to `LootGoal`.
    *   `LootGoal` executes, attempts to target/approach/loot the corpse, potentially sends `SkinCorpseEvent`.
10. **Return to Route:** Once looting/skinning is done, `shouldloot` becomes false. The planner likely selects `FollowRouteGoal` again, and the cycle repeats.

## 5. Extensibility Points

*   **Adding New Behaviors:** Create new subclasses of `GoapGoal`, implement their logic, define preconditions/effects, and ensure they are registered in DI (likely via `GoalFactory`).
*   **Modifying Rotations:** Edit the `Sequence` arrays within the `Pull`, `Combat`, `Adhoc`, etc., sections of the JSON class configuration files. Add/remove/reorder `KeyAction`s and adjust their `Requirements`.
*   **Adding New Requirements:** Modify the `Requirement.cs` parser (and potentially relevant `AddonReader` components if new game state is needed) to support new keywords or logic.
*   **Creating New Paths:** Use the BlazorServer UI's path recording feature.
*   **Supporting New Classes/Specs:** Create new JSON class configuration files, defining appropriate `KeyAction` sequences and requirements.
*   **Improving Game State Reading:** Modify the in-game Lua addon (`DataToColor`) to expose more data via pixels, update `frame_config.json` accordingly, and add new `IReader` implementations in `Core/Addon/` to interpret the new data.

This analysis should provide a solid starting point for understanding and modifying the WowClassicGrindBot.
