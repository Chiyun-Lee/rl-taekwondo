# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A first-person taekwondo sparring game with RL-trained bot opponents. Ported from Unity to **Godot 4.6 .NET**. Eventual goal: stream trained country-flagged bots sparring on Twitch (winner stays on).

**Three target scenes:**
- **Sandbox** — development scene, human vs placeholder AI
- **Training** — two RL agents, accelerated time, visible rewards
- **Evaluation** — human vs trained bot model, full match rules

---

## Tech Stack

- **Engine:** Godot 4.6 .NET, Forward+ renderer, Jolt Physics
- **Language:** C# (.NET, all scripts use `public partial class`)
- **Multiplayer:** TBD (Photon Fusion 2 is Unity-only; Godot alternatives: Nakama, Fish-Net via GodotSharp, or Godot's built-in multiplayer)
- **RL Training:** TBD (ML-Agents is Unity-only; Godot alternatives: Godot RL Agents, or custom Python socket bridge)

---

## Godot Conventions

- All scripts extending Godot types **must** be `public partial class` (required by source generators)
- Inspector fields use `[Export]` attribute; group them with `[ExportGroup("Name")]`
- Unity → Godot lifecycle: `Awake/Start` → `_Ready()`, `Update` → `_Process(double delta)`, `FixedUpdate` → `_PhysicsProcess(double delta)`, `OnDestroy` → `_ExitTree()`
- Input events handled in `_Input(InputEvent @event)` — fires once per event, not every frame
- `Time.time` → `Time.GetTicksMsec() / 1000f`
- `GetComponent<T>()` → `GetNode<T>("NodeName")` or `GetParent<T>()`
- `GetComponentInChildren<T>()` → `FindChild("*", true)` or a typed `GetNodeOrNull`
- `localScale` → `Scale` (on `Node3D`)
- `Vector3.sqrMagnitude` → `Vector3.LengthSquared()`
- `Vector3.normalized` → `Vector3.Normalized()`
- `Mathf.Rad2Deg` → `Mathf.RadToDeg(float)`
- `Mathf.Deg2Rad` → `Mathf.DegToRad(float)`
- `Physics.gravity` → `ProjectSettings.GetSetting("physics/3d/default_gravity").As<float>()`

---

## Character Architecture

### Input abstraction (RL-ready)
All character control flows through `ICharacterInput`. Never read input directly from the state machine or movement script — only through the interface.

```
ICharacterInput
  ├── HumanInput   — Godot _Input() events (mouse scroll + clicks + keyboard)
  ├── AgentInput   — RL agent action buffers (stub, wired to Python bridge later)
  └── NetworkInput — multiplayer-synced input [not yet implemented]
```

`ICharacterInput` exposes **consuming** properties — reading clears the value:
- `LinearInput` (int): `-2`=DashBack, `-1`=StepBack, `0`=None, `1`=StepForward, `2`=DashForward
- `SideStepInput` (int): `-1`=Left, `0`=None, `1`=Right
- `SwapStanceTriggered` (bool): true for one `_PhysicsProcess` tick when Space is pressed
- `KickTriggered` (bool): true for one `_PhysicsProcess` tick when S is pressed

`PlayerMovement` scans its children at startup for any `ICharacterInput` implementor and exposes it as `PlayerMovement.Input`. Swapping `HumanInput` for `AgentInput` in the scene requires no code change. `CharacterStateMachine` reads `_movement.Input` rather than doing its own discovery.

For `AgentInput`, map four discrete action branches:
- Branch 0 (linear): 0=None, 1=StepFwd, 2=StepBack, 3=DashFwd, 4=DashBack
- Branch 1 (sidestep): 0=None, 1=Left, 2=Right
- Branch 2 (swap): 0=None, 1=Swap
- Branch 3 (kick): 0=None, 1=Kick

### State machine
`CharacterStateMachine` reads `ICharacterInput` and drives the `AnimationTree`. Combo timing is tracked internally.

```
Idle ↔ Dash
Idle ↔ Step
Idle ↔ SideStep
Idle → RaisedFrontLeg* → FrontSideKickToBody [→ SlidingFrontSideKickToBody] → Idle
Idle → RaisedRearLeg*  → [S within window]               → RearTurningKickToBody [→ SlidingTurningKickToBody] → Idle
                        → [window expires, no kick input] → LowerRear → Idle (stance flipped)
```
*RaisedFrontLeg and RaisedRearLeg are transitionary only — entered automatically, cannot be held.

**RaisedRearLeg branching:** Combo window opens the moment `RaisedRearLeg` starts. S within the window → `RearTurningKickToBody`. Window expires with no kick → `LowerRear` plays. `OnStanceMidpoint` fires at frame 0 of `LowerRear` (leg still at peak = symmetric = flip is invisible), flipping `CharacterVisual.Scale.X`. `OnStanceSwapComplete` fires at last frame, clearing `_moving`.

**Mirroring approach:** Stance is mirrored by scaling `CharacterVisual` to `(-1, 1, 1)` on local X. All child nodes (skeleton, mesh, future foot collision shapes) flip automatically. Generic animations drive local bone rotations which produce mirrored world positions under the negative parent scale. Mesh material must be double-sided to handle flipped normals.

### Input map
| Input | Action |
|---|---|
| Scroll (fast) | Dash (toward/away from CameraTarget) |
| Scroll (slow) | Step (toward/away from CameraTarget) |
| Left click | SideStep Left |
| Right click | SideStep Right |
| S | FrontSideKickToBody (chains through RaisedFrontLeg) |
| S + Dash (within window) | SlidingFrontSideKickToBody |
| Space | SwapStance |
| Space + S (within window) | RearTurningKickToBody |
| Space + S + Dash (within window) | SlidingTurningKickToBody |

**Scroll detection (`HumanInput`):** Godot mouse wheel always fires `Factor = 1.0` per notch — magnitude-based Dash/Step distinction doesn't apply. Instead: 5 same-direction notches within 100 ms → **Dash**. Fewer notches → **Step** committed after 80 ms settle. Opposite-direction notch resets the gesture. After any commit, 250 ms cooldown blocks further scroll.

### Energy system
- Max 15, passive regen +1/sec
- FrontSideKickToBody, RearTurningKickToBody: −2
- Sliding variants: −3
- Insufficient energy = input refused (action blocked)

---

## Current Implementation (Sandbox scene)

### Scripts (`Scripts/`)

**`ICharacterInput.cs`** — pure C# interface, no Godot dependency. Consuming properties (reading clears the value):
- `LinearInput`, `SideStepInput` — owned by **PlayerMovement** (except when CharacterStateMachine is busy, which consumes them to block movement during kicks)
- `KickTriggered`, `SwapStanceTriggered` — owned by **CharacterStateMachine** always

**`HumanInput.cs`** — extends `Node`. Implements `ICharacterInput`. Keyboard: Space → swap, S → kick, Escape → release cursor. Mouse wheel: 5 notches within 100 ms → Dash; otherwise Step after 80 ms settle. Scroll cooldown 250 ms. Left click → SideStep Left, Right click → SideStep Right. Cursor locked on `_Ready`.

**`AgentInput.cs`** — extends `Node`. Implements `ICharacterInput`. Receives discrete action branches from the RL bridge via `SetActions(branchLinear, branchSide, branchSwap, branchKick)`. Branch mappings documented in the file header. Swap this node in place of `HumanInput` to switch a character to RL control.

**`CharacterStateMachine.cs`** — extends `Node`. Child of Player. Reads `KickTriggered` and `SwapStanceTriggered` each `_PhysicsProcess`. Manages `State` enum (Idle/RaisedFrontLeg/FrontSideKickToBody/SlidingFSKtB/RaisedRearLeg/RearTurningKickToBody/SlidingRTKtB). Tracks energy (max 15, regen 1/s; kick costs: front/rear=2, sliding=3). When busy (kick animation playing), also consumes LinearInput/SideStepInput to block movement. Emits `StateChanged(int)` and `EnergyChanged(float,float)` signals. `OnKickAnimationComplete()` to be called by AnimationPlayer method track.

**`PlayerMovement.cs`** — extends `CharacterBody3D`. Discovers its input source by scanning child nodes for any `ICharacterInput` implementor; exposes it as `public ICharacterInput Input`. Reads `LinearInput` and `SideStepInput` in `_PhysicsProcess` (kick/swap owned by CharacterStateMachine). Smooth movement via `MovePhase` state machine (Idle/Moving/Pausing). Rotates to face `CameraTarget` via quaternion slerp. Exposes `TriggerSwapStance()`, `OnStanceMidpoint()`, `OnStanceSwapComplete()` as callbacks for CharacterStateMachine and AnimationPlayer method tracks. Sidestep moves along a constant-radius arc around CameraTarget.

**`AnimationSetup.cs`** — extends `Node3D`. Attached to `CharacterVisual`. Builds the `AnimationPlayer` library and `AnimationTree` state machine entirely at runtime in `_Ready()` — no `.tres` files. Exposes `AnimationSetup.SwapStanceCondition` as a shared string constant used by both this class (AnimationTree `AdvanceCondition`) and `PlayerMovement` (AnimationTree parameter path). `SkeletonPath` export lets you point to a different rig's skeleton without code changes. Loads an external Mixamo FBX idle animation if `IdleAnimationPath` is set, remapping bone track paths to match `SkeletonPath`.

**`FloorSetup.cs`** — `[Tool]` script on Floor (StaticBody3D). Loads `Assets/Materials/CheckerFloor.gdshader` and applies it as `MaterialOverride` on the FloorMesh child.

**`ActionLog.cs`** — extends `CanvasLayer`. Static debug overlay. `ActionLog.Log(string)` from anywhere. Maintains last 12 entries in a `LinkedList<string>`; renders newest-first. Uses a pooled `StringBuilder` field to avoid per-log allocation.

### Scene setup (Sandbox)
```
Sandbox (scene root)
  ├── DirectionalLight3D
  ├── WorldEnvironment (ProceduralSky)
  ├── Floor (StaticBody3D + FloorSetup script)
  │   ├── FloorMesh (MeshInstance3D — CheckerFloor.gdshader applied at runtime)
  │   └── FloorCollision (CollisionShape3D — WorldBoundaryShape3D)
  ├── CameraTarget (MeshInstance3D sphere, visible for diagnostics)
  ├── Player (CharacterBody3D + PlayerMovement script)
  │   ├── CollisionShape3D (CapsuleShape3D r=0.3, h=1.8)
  │   ├── HumanInput (Node + HumanInput script)          ← swap for AgentInput for RL
  │   ├── CharacterStateMachine (Node + CharacterStateMachine script)
  │   ├── CharacterVisual (Node3D, Y=−0.9 to align feet with floor)
  │   │   ├── XBot (instanced from Assets/Models/X Bot@T-Pose.fbx)
  │   │   │   └── Skeleton3D + MeshInstance3D (mixamorig_ bones)
  │   │   ├── AnimationPlayer (root_node=".." so method tracks reach Player)
  │   │   └── AnimationTree  (anim_player="../AnimationPlayer")
  │   └── CameraRoot (Node3D, Y=0.45 → world eye height 1.35 m)
  │       └── Camera3D
  └── ActionLog (CanvasLayer + ActionLog script)
```

**Player Y=0.9:** CharacterBody3D origin is the capsule centre (half-height). CharacterVisual at Y=−0.9 puts world Y=0 at the XBot's feet.

**NodePath exports on Player:** `camera_target = NodePath("../CameraTarget")` and `character_visual = NodePath("CharacterVisual")` are set in the .tscn. `_Ready` resolves them by path as a fallback if Godot clears them on reload.

### Animation

Animations are built entirely at runtime by `AnimationSetup.cs`. There are no `.tres` clip files.

**Shared constant:** `AnimationSetup.SwapStanceCondition = "swap_stance"`. The AnimationTree condition name and the PlayerMovement parameter path (`parameters/conditions/swap_stance`) both derive from this — change it once to rename everywhere.

**Clips built by AnimationSetup:**
- `Idle` — external Mixamo FBX if `IdleAnimationPath` set; otherwise empty 1 s looping placeholder (T-pose)
- `RaiseRear` — empty one-shot placeholder. Add bone keyframes in the AnimationPlayer editor: `mixamorig_RightUpLeg` +85° X and `mixamorig_RightLeg` −100° X at t=`RaiseRearDuration`
- `LowerRear` — empty one-shot with two Call Method track events: frame 0 → `PlayerMovement.OnStanceMidpoint`, last frame → `PlayerMovement.OnStanceSwapComplete`
- Kick clips — not yet built. Must call `OnKickAnimationComplete` at animation end via method track

**Mixamo FBX idle workflow:**
1. Download from mixamo.com: search "Breathing Idle" → FBX for Unity, With Skin, 30 fps
2. Drop into `Assets/Animations/`
3. Select CharacterVisual in Inspector → set `Idle Animation Path` to `res://Assets/Animations/BreathingIdle.fbx`

**AnimationTree state machine:**
```
Idle ──[swap_stance condition]──► RaiseRear ──[Auto]──► LowerRear ──[Auto]──► Idle
```
Kick states (FrontSideKickToBody etc.) are not yet wired into the AnimationTree.

### Animation method calls (replaces Unity's AnimationEventRelay)
Godot's `AnimationPlayer` has a **Call Method** track that references any node by scene path. No relay script needed. `AnimationPlayer.root_node = NodePath("..")` (CharacterVisual's parent = Player), so method tracks targeting `NodePath("..")` call methods on `PlayerMovement`.

### Visuals / Styling

**Character** — X Bot from Mixamo. FBX imported into `Assets/Models/`. Material must be set to double-sided (Cull Mode = Disabled) to handle flipped normals from the (−1,1,1) stance mirror.

**Floor** — checker pattern via `Assets/Materials/CheckerFloor.gdshader`. World-space XZ coordinates from `INV_VIEW_MATRIX * vec4(VERTEX, 1.0)`, divided by `square_size` (default 1 m), checker via `(cell.x + cell.y) % 2`. Colors tunable via shader uniforms.

**CameraTarget** — visible sphere mesh kept in scene for development diagnostics.

**Rig** — Mixamo X Bot, Generic rig (no retargeting). Bone names use `mixamorig_` prefix (underscore, not colon). Skeleton at `Player/CharacterVisual/XBot/Skeleton3D`.

### Camera
- **In Idle**: `PlayerMovement.FaceTarget()` smoothly rotates Player toward CameraTarget every `_PhysicsProcess` tick via quaternion slerp capped at `TrackingSpeed` deg/s.
- **On action start**: movement/kick direction is computed from CameraTarget position at that moment — opponent sidestepping mid-kick doesn't redirect the move.

### Hit detection (not yet implemented)
- **Hitboxes**: two `Area3D` nodes per foot — base of foot (side kicks), top of foot (turning kicks). Call Method tracks gate which is active.
- **Scoring targets**: `Area3D` on chest (+2) and head (+3) of opponent.
- **Scoring windows**: `score_start` / `score_end` Call Method track entries — contact outside this window does not score.
- All foot Area3D nodes must be children of `CharacterVisual` so they flip correctly with the stance mirror.
