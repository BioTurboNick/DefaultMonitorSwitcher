# DefaultMonitorSwitcher — Specification

## 1. Problem Statement

The user has three monitors: two desktop monitors and one HDTV. When the HDTV is
physically on but displaying a different input (e.g. cable TV), Windows still
considers it connected and will keep it as the primary display if it was last set
that way. This causes:

- The Windows login screen to appear on the unseen TV
- The Start menu and other shell UI to open on the TV
- General confusion about which screen is "in front"

The goal of this application is to automatically revert the primary display to one
of the two desktop monitors when the TV is no longer the locus of the user's PC
activity, without requiring manual intervention.

---

## 2. Scope

### In Scope
- Detecting when PC activity on the HDTV has ceased and reverting the primary
  monitor to a desktop monitor automatically
- Detecting when activity migrates exclusively to the HDTV and switching the
  primary monitor to the HDTV automatically
- Detecting an earlier-than-idle revert when activity shifts exclusively from the
  HDTV to the desktop monitors
- Configuring which desktop monitor is preferred as primary
- A configurable idle timeout before revert triggers
- An optional mode to suppress the revert (e.g. when intentionally watching a TV
  show on a desktop monitor while the HDTV is active)
- Switching the Windows default audio playback device alongside display reverts
- Brief, transient toast notifications on automatic switches

### Out of Scope (deferred)
- Any integration with display hardware (DDC/CI, CEC)

---

## 3. Concepts and Terminology

| Term | Definition |
|---|---|
| **HDTV** | The television connected via HDMI, designated as the "gaming display" |
| **Desktop monitors** | The two smaller monitors at the user's desk; treated as a logical unit |
| **Primary monitor** | The Windows "main display" — where the taskbar, login screen, and new windows default to |
| **Active monitor** | The monitor where meaningful user activity (mouse, focused window) is currently occurring |
| **Revert** | The act of switching the Windows primary display (and default audio device) from HDTV to the desktop configuration |
| **Forward switch** | The act of switching the Windows primary display (and default audio device) from the desktop configuration to the HDTV |
| **Idle timeout** | Configurable duration of inactivity on the HDTV after which a revert is triggered |
| **Gaming session** | The period during which the HDTV is primary and the user is interacting with it |
| **HDTV audio device** | The Windows audio playback device associated with the HDTV (typically its HDMI audio endpoint) |
| **Desktop audio device** | The Windows audio playback device preferred when at the desk (headphones, speakers, etc.) |

---

## 4. Activity Detection

### 4.1 What Counts as Activity

Activity is attributed to a monitor based on two signals, both of which are
polled on a configurable interval (default: 5 seconds):

1. **Mouse cursor position** — the monitor the cursor currently resides on
2. **Foreground window location** — the monitor that contains the foreground
   (focused) window's center point or largest overlap

Each signal independently contributes to determining the "active monitor."

### 4.2 Activity Attribution Rules

- If both signals agree on the same monitor → that monitor is unambiguously active
- If the signals disagree (e.g. cursor on TV, focused window on desktop) → the
  foreground window takes precedence, as it reflects intentional keyboard
  interaction
- Brief mouse excursions across monitors (e.g. moving a window) are smoothed via
  a short **mouse dwell threshold** (default: 10 seconds) before the mouse signal
  is considered to reflect intent

### 4.3 "Desktop Monitors" as a Unit

The two desktop monitors are treated as a single logical zone. Activity on either
desktop monitor is equivalent — the distinction between them only matters for
selecting which one is primary (see Section 6).

### 4.4 HDTV Engagement Detection

When `hdtvEngagementDetectionEnabled` is true, three supplemental signals are sampled
on each poll tick to determine whether the HDTV is actively in use beyond mere
window presence:

1. **DXGI frame activity** — The app creates an `IDXGIOutputDuplication` on the
   HDTV's DXGI output and calls `AcquireNextFrame` with a zero timeout. A result
   of S_OK indicates a new composed frame was available; the `LastPresentTime`
   field of `DXGI_OUTDUPL_FRAME_INFO` is checked to confirm desktop *content* was
   updated (non-zero), filtering out cursor-only updates which set only
   `LastMouseUpdateTime`. This signal does not apply to exclusive full-screen
   OpenGL applications, which bypass DWM's composition pipeline. DRM-protected
   content (e.g. Hulu, Movies & TV) blocks DXGI duplication entirely; the other
   two signals cover this case.

2. **WASAPI audio peak** — `IAudioMeterInformation::GetPeakValue()` is called on the
   HDTV's audio endpoint. A non-zero peak level indicates audio is actively being
   output to the HDTV. This signal is independent of rendering API.

3. **Windows Media Session (SMTC)** — `GlobalSystemMediaTransportControlsSessionManager`
   is queried via `GetSessions()` for any session whose `PlaybackStatus` is `Playing`.
   This covers media apps that register with the Windows system media transport controls
   (e.g. Edge, Hulu, Netflix, Movies & TV, Spotify, VLC) regardless of which display or
   audio device they are using. This signal is immune to DRM restrictions that block DXGI
   duplication, making it the primary guard against idle reverts during full-screen video
   playback. Requires Windows 10 version 1903 or later; gracefully absent on older versions.

Any signal independently marks the HDTV as **engaged**. The engagement state is
evaluated inside each poll tick and factors into the idle timeout condition (§5.1).

---

## 5. Switch Conditions

This section covers all conditions that trigger either a **revert** (HDTV →
desktop) or a **forward switch** (desktop → HDTV).

### 5.1 HDTV Idle Timeout

- **Default idle timeout**: 5 minutes
- **Configurable range**: 1 minute to 60 minutes

#### When `hdtvEngagementDetectionEnabled` is true (default)

- **Trigger**: The mouse cursor has not moved for a continuous duration exceeding
  the **idle timeout**, **and** the HDTV engagement signals (§4.4) are both
  inactive (no new DXGI content frames, zero audio peak) throughout that window.
- **Idle signal**: Mouse-cursor position is polled each tick via `GetCursorPos`.
  If the position is unchanged since the previous tick, the stationary duration
  accumulates. Any cursor movement resets the accumulator. Keyboard input,
  controller activity, and other HID events do not affect this counter, making
  idle detection robust to background devices that generate continuous system
  input events (e.g. game controllers keeping `GetLastInputInfo` at zero).
- **Engagement hold-off**: If either engagement signal becomes active during an
  idle countdown (§HdtvIdleCountdown), the countdown is cancelled and the state
  returns to `HdtvActive`. A game actively rendering or playing audio to the
  HDTV will not trigger a revert even if the mouse has been stationary.
- **Rationale**: Foreground window location alone cannot distinguish "user is at
  the TV" from "a launcher has focus on the TV after the user walked away."
  Mouse-position idle is a reliable, hardware-agnostic signal for user presence.
  The engagement signals guard against reverting while a game or video is
  genuinely running.

#### When `hdtvEngagementDetectionEnabled` is false

- **Trigger**: No activity (by the zone-attribution rules in §4.1–4.2) has been
  attributed to the HDTV for a continuous duration exceeding the **idle timeout**.
  This is the original behavior.
- **Rationale**: Fallback for users who prefer the simpler zone-based detection or
  encounter issues with DXGI duplication (e.g., always-on exclusive full-screen
  OpenGL setups).

### 5.2 Exclusive Desktop Activity (Early Revert)

- **Trigger**: Activity has been attributed exclusively to the desktop monitor
  zone for a continuous duration exceeding the **desktop dwell threshold**, while
  the HDTV is still set as the primary display
- **Default desktop dwell threshold**: 2 minutes
- **Configurable range**: 30 seconds to 30 minutes
- **Rationale**: Catches the case where the user has clearly returned to their
  desk (exclusive keyboard + mouse use on desktop monitors) before the idle
  timeout would fire
- **Exclusivity requirement**: A revert is only triggered if there has been *no*
  activity on the HDTV during this window, not merely less activity. A single
  detected HDTV event (cursor movement, focus) resets the desktop dwell counter.

### 5.3 Session-Ending Revert

- **Trigger**: Windows is about to log out, shut down, or restart
- **Rationale**: Once the user session ends, the application cannot run. If the
  HDTV is primary at that moment, the login screen and any subsequent session
  will start on the TV — the exact problem this application exists to prevent.
- **Mechanism**: The application subscribes to the Windows `WM_QUERYENDSESSION` /
  `WM_ENDSESSION` messages (or the equivalent `SystemEvents.SessionEnding` event
  in managed code). Upon receiving a session-ending notification, the app
  performs a synchronous revert before yielding to the shutdown sequence.
- **Timing constraint**: Windows allows a limited window (~5 seconds for
  non-service processes) to handle `WM_ENDSESSION`. The revert call must complete
  within this budget; if it does not, Windows will force-terminate the process
  and the display configuration will remain unchanged.
- **TV-Show Mode interaction**: The session-ending revert fires regardless of
  whether TV-Show Mode is active. TV-Show Mode suppresses the activity-based
  revert conditions only; it does not suppress the session-ending revert.
- **No primary change needed**: If the HDTV is not currently the primary monitor
  when the session ends, no action is taken.

### 5.3a Workstation Lock Revert

- **Trigger**: The user locks the workstation (Win+L, screen lock timeout, or fast user switching)
- **Rationale**: When the workstation is locked, the lock screen is displayed on the primary
  monitor. If the HDTV is primary, the lock screen (and any subsequent unlock prompt) appears
  on the TV rather than on the desk monitor — the exact problem this application exists to prevent.
  Waiting for the idle timeout (up to 5+ minutes) is too slow; the revert should happen on unlock.
- **Mechanism**: The application subscribes to `SystemEvents.SessionSwitch`.
  - On `SessionSwitchReason.SessionLock`: if the HDTV is currently the primary monitor, a
    deferred-revert flag is set. No display call is made at lock time because Windows returns
    `ERROR_ACCESS_DENIED` (5) from `SetDisplayConfig` while the secure desktop is active.
  - On `SessionSwitchReason.SessionUnlock`: if the deferred flag is set, an immediate revert
    is performed via `Task.Run`. The display switch completes before the user's session is
    visible. The activity sampler also treats a locked session as `ActivityZone.None` so the
    state machine cannot accumulate HDTV dwell while locked.
- **Current limitation**: The lock screen itself will still appear on the HDTV for the duration
  of the lock. The revert fires on unlock, not at lock time. See §11 for the planned improvement.
- **TV-Show Mode interaction**: The workstation-lock revert fires regardless of TV-Show Mode.
- **No primary change needed**: If the HDTV is not currently the primary monitor when the
  workstation is locked, no action is taken.

### 5.4 Audio Device Switching on Revert

When a revert occurs (from any condition in 5.1–5.3a), the app also switches the
Windows default audio playback device from the HDTV audio device to the
desktop audio device.

#### Auto-detection

Audio endpoints for HDMI/DisplayPort-connected monitors are named by Windows
using the monitor's EDID model name (e.g. the TV's audio endpoint is
`QBQ90 (NVIDIA High Definition Audio)`; the desktop monitors' endpoints are
`DELL S2721QS (NVIDIA High Definition Audio)`). The app resolves audio endpoints
automatically by case-insensitive substring matching of the display's EDID name
against the audio endpoint's friendly name. No manual audio device configuration
is required.

- **HDTV audio**: matched from the HDTV display's EDID name
- **Desktop audio**: matched from the preferred primary desktop monitor's EDID
  name; if that endpoint is not found, the other desktop monitor's endpoint is
  tried as fallback
- **Manual override**: auto-detected values can be overridden in Settings if the
  match is incorrect or ambiguous (e.g. two different monitors with identical
  model names)

#### Behavior

- **API**: Windows does not expose a public API for changing the default audio
  device. The app will use the `IAudioPolicyConfig` COM interface, which is an
  undocumented but widely-used Windows internal interface (used by EarTrumpet,
  NirCmd, AudioSwitcher, and many other utilities). This interface is available
  on Windows 10 1803+ and is the minimum OS requirement for this application.
- **Condition**: the audio switch fires unconditionally by default, overriding
  whatever device is currently active. If the `respectManualAudioOverride`
  option is enabled, the switch is skipped when the current default audio device
  does not match the expected source device (indicating the user has manually
  changed audio), preserving that manual choice.
- **Graceful degradation**: If the audio switch fails (device not found, COM
  error), the display revert still proceeds. The audio failure is noted in the
  toast notification.
- **Opt-out**: Audio switching can be disabled independently of display switching
  via a configuration toggle.

### 5.5 Startup Revert

- **Trigger**: On application startup, the HDTV is detected as the current
  Windows primary monitor
- **Rationale**: Covers the case where the app was not running when the display
  configuration was last changed (e.g. power loss, crash, or the app not yet
  having run during boot). Without this check, the problematic state persists
  indefinitely.
- **Mechanism**: Immediately after the app initialises its state, it evaluates
  the current primary monitor. If the HDTV is primary, it performs an
  unconditional revert before entering normal polling.
- **TV-Show Mode interaction**: The startup revert fires regardless of
  TV-Show Mode. TV-Show Mode is session-level state; it does not persist across
  restarts.

### 5.6 Revert Guard: TV-Show Mode

An optional toggle (see Section 7) suppresses all automatic reverts. This is
intended for situations where the user is intentionally watching content on a
desktop monitor while the HDTV remains primary. When this mode is active:

- Neither revert condition fires (5.1 and 5.2 only; the session-ending revert
  in 5.3 still fires unconditionally)
- A visible indicator in the system tray communicates the suppressed state
- The mode must be manually disabled to restore automatic revert behavior

### 5.7 Forward Switch: Exclusive HDTV Activity

- **Trigger**: Activity (by the rules in Section 4) has been attributed
  exclusively to the HDTV for a continuous duration exceeding the **HDTV dwell
  threshold**, while a desktop monitor is currently the primary display
- **Post-idle-revert guard**: After an idle-timeout revert (§5.1), the forward
  switch is suppressed until the cursor physically enters the HDTV zone
  (`CursorZone == Hdtv`). Foreground window zone alone (e.g. a launcher still
  focused on the HDTV) is insufficient. This prevents the ping-pong pattern where
  an idle revert is immediately reversed by the still-present launcher window.
  The guard is lifted as soon as the cursor dwells on the HDTV for `mouseDwellSeconds`,
  after which normal dwell-based switching resumes. A manual forward switch via
  the tray menu bypasses this guard.
- **Default HDTV dwell threshold**: 60 seconds
- **Configurable range**: 15 seconds to 10 minutes
- **Exclusivity requirement**: A single attributed activity event on a desktop
  monitor resets the dwell counter. The full dwell period must elapse with all
  activity on the HDTV and none on the desktop monitors.
- **Audio**: A forward switch also switches the default audio device from the
  desktop audio device to the HDTV audio device, subject to the same
  auto-detection and graceful-degradation rules as the revert (Section 5.4).
  The audio switch fires unconditionally by default. If `respectManualAudioOverride`
  is enabled, the switch is skipped when the current default audio device does
  not match the desktop audio device (indicating the user has manually changed
  audio).
- **TV-Show Mode interaction**: TV-Show Mode does not suppress forward switches.
  TV-Show Mode is only relevant when the HDTV is already primary.

### 5.8 Forward Switch: Window-Move Elevated Polling

Window move events to the HDTV do not directly trigger a forward switch, but
they signal that the user *may* be beginning a session on the TV and that more
responsive detection is warranted.

- **Mechanism**: The app subscribes to `EVENT_SYSTEM_MOVESIZEEND` via
  `SetWinEventHook`. When a top-level window finishes moving and
  `MonitorFromWindow` resolves to the HDTV, the polling interval is temporarily
  reduced to the **elevated poll interval** for a fixed **elevated poll
  duration**.
- **Default elevated poll interval**: 1 second
- **Default elevated poll duration**: 30 seconds
- **Counter reset**: Each subsequent `MOVESIZEEND` event landing on the HDTV
  restarts the elevated poll duration timer. A burst of Win+Shift+Arrow presses
  cycling through the HDTV simply keeps the elevated interval active; no
  false-positive switch can occur without the 5.7 condition also being satisfied.
- **Expiry**: When the elevated duration elapses without a forward switch firing,
  polling silently returns to the normal interval.
- **Scope**: Elevated polling affects the forward-switch dwell check (5.7) in two
  ways: the poll interval is reduced *and* the HDTV dwell threshold is replaced
  by the shorter **elevated HDTV dwell threshold** (`elevatedHdtvDwellSeconds`,
  default 12 seconds). This means a forward switch can fire after only 12 seconds
  of exclusive HDTV activity following a window drag, rather than the full 60-second
  default. The revert polling interval and thresholds are unaffected.

---

## 6. Desktop Primary Monitor Selection

Exactly one desktop monitor is designated as the **preferred primary**. When a
revert occurs, Windows is instructed to make this monitor the primary display.

- Configurable: the user selects which desktop monitor is preferred primary
- Monitors are identified by their stable Windows device path (e.g.
  `\\?\DISPLAY#DEL0DE0#...`), which remains constant across reboots and
  reconnections unlike the volatile `\\.\DISPLAYn` index
- Secondary identification via monitor name/EDID (friendly name as reported by
  Windows) is used to re-resolve the correct monitor if the device path ever
  changes (e.g. after a driver reinstall)

### 6.1 Fallback When Preferred Monitor Is Unavailable

If the preferred desktop primary monitor is not present at the time of a revert
(disconnected, powered off, or unresolvable by EDID):

1. The app attempts to use the **other desktop monitor** (the one not configured
   as preferred primary but also not the HDTV)
2. If neither desktop monitor is present, no display revert is attempted and a
   warning toast is shown
3. The fallback selection mirrors what Windows itself would do if the preferred
   monitor were physically removed: use `QueryDisplayConfig` to enumerate active
   displays and select the next available non-HDTV display in Windows' internal
   priority order (which reflects the adapter and target IDs as Windows ranks
   them)

---

## 7. Configuration Options

All configuration is stored in a local file (e.g. `config.json`) in the
application's data directory.

| Setting | Type | Default | Description |
|---|---|---|---|
| `hdtvDisplayDevicePath` | string | (required) | Stable Windows device path of the HDTV display (e.g. `\\?\DISPLAY#...`) |
| `preferredPrimaryDisplayDevicePath` | string | (required) | Stable Windows device path of the desktop monitor to use as primary after revert |
| `hdtvAudioDeviceId` | string | (auto) | Windows audio endpoint ID for the HDTV's audio device; auto-detected from EDID name match, can be overridden |
| `desktopAudioDeviceId` | string | (auto) | Windows audio endpoint ID for the desktop audio device; auto-detected from preferred primary monitor's EDID name, can be overridden |
| `audioSwitchingEnabled` | bool | true | Whether to switch audio device alongside display reverts |
| `respectManualAudioOverride` | bool | false | When true, skip the audio switch if the current default audio device is not the expected source device (i.e. the user has manually changed audio) |
| `idleTimeoutSeconds` | int | 300 | Seconds of HDTV inactivity before idle revert triggers |
| `desktopDwellSeconds` | int | 120 | Seconds of exclusive desktop activity before early revert triggers |
| `mouseDwellSeconds` | int | 10 | Seconds cursor must remain on a monitor before it is counted as a mouse activity signal |
| `pollIntervalSeconds` | int | 5 | How often activity signals are sampled |
| `hdtvDwellSeconds` | int | 60 | Seconds of exclusive HDTV activity before forward switch triggers (normal polling) |
| `elevatedHdtvDwellSeconds` | int | 12 | Seconds of exclusive HDTV activity before forward switch triggers when elevated polling is active (see §5.8) |
| `elevatedPollIntervalSeconds` | int | 1 | Poll interval (seconds) during the elevated window after a window-move event to the HDTV |
| `elevatedPollDurationSeconds` | int | 30 | How long elevated polling remains active after the last window-move event to the HDTV |
| `tvShowModeEnabled` | bool | false | Suppresses automatic reverts (5.1 and 5.2) when true |
| `hdtvEngagementDetectionEnabled` | bool | true | When true, idle detection (§5.1) uses mouse-cursor position idle (via `GetCursorPos` position tracking) supplemented by DXGI content-frame activity, WASAPI audio peak, and Windows Media Session (SMTC) playback status on the HDTV, rather than foreground window zone attribution. Disable to revert to the original zone-based idle detection. |
| `runOnStartup` | bool | true | Register the application to run on Windows login |

---

## 8. User Interface

The application runs as a **system tray icon** with no main window.

### 8.1 Tray Icon States

| State | Icon Indicator | Meaning |
|---|---|---|
| HDTV is not primary | Neutral | App is idle; desktop monitors are primary |
| HDTV is primary, activity detected | Active (green) | Gaming session in progress |
| HDTV is primary, idle countdown | Warning (yellow) | No HDTV activity; revert pending |
| TV-Show Mode active | Suppressed (orange) | Automatic revert is paused |

### 8.2 Tray Context Menu

- **Status** — current state description (read-only label)
- **Revert now** — immediately switch primary to preferred desktop monitor
- **TV-Show Mode** — toggle suppression on/off (checkmark item)
- **Settings** — open the settings dialog
- **Exit** — quit the application (primary monitor is not changed on exit)

### 8.3 Toast Notifications

A brief toast notification is shown whenever an automatic revert occurs. Toasts
are also shown for warning conditions (e.g., a revert was attempted but a target
display was unavailable).

- **Transience**: Each toast has an `ExpirationTime` of 3 minutes from the
  moment of the switch. After expiry the notification is automatically removed
  from the Action Center notification list.
- **Replacement**: All toasts from this app share a fixed `Tag` value. A new
  notification replaces the previous one in Action Center, so at most one entry
  from this app is ever visible there.
- **No accumulation**: The user will never see a backlog of old switch events in
  Action Center; only the most recent action is visible, and only briefly.
- **Content**: Short summary of what happened and why, e.g.:
  - *"Switched to desktop — no HDTV activity for 5 minutes"*
  - *"Switched to desktop — activity returned to desk monitors"*
  - *"Switched to desktop — session ending"*
  - *"Switched to desktop — workstation locked"*
  - *"Could not switch audio — HDTV audio device not found"*
- **Opt-out**: Toast notifications can be disabled via OS notification settings
  for the app; the app does not provide a separate toggle for this.

### 8.4 Settings Dialog

A minimal settings window allowing the user to:

- Select the HDTV from a dropdown of connected displays
- Select the preferred desktop primary monitor from the remaining displays
- View auto-detected audio endpoints for the HDTV and desktop monitor, with
  the option to override either from a dropdown of all active playback endpoints
- Toggle audio switching on/off
- Adjust all numeric thresholds (sliders or spinners with visible units)
- Toggle TV-Show Mode default
- Toggle run-on-startup

---

## 9. Edge Cases and Handling

| Scenario | Behavior |
|---|---|
| HDTV is disconnected or powered off (Windows deregisters it) | No action needed; Windows auto-reassigns primary. App remains idle. |
| HDTV is primary but the configured display ID is no longer present | App logs a warning; does not attempt a revert to an unknown display |
| Preferred desktop monitor is disconnected at time of revert | App falls back to the other desktop monitor per Section 6.1; shows a warning toast if neither is available |
| Desktop audio device is not present at time of revert | Display revert proceeds; audio switch is skipped and the failure is noted in the toast |
| User manually changes primary monitor via Windows Settings | App detects the change on next poll and updates its internal state accordingly |
| All three monitors show no activity for the idle duration | Idle revert fires normally (covers the case where the user walks away mid-session) |
| Moving a window from a desktop monitor to the TV | Triggers elevated polling (5.8); also attributes activity to the TV for the forward-switch dwell check if mouse/focus follow |
| Win+Shift+Arrow cycling a window through the HDTV | Triggers elevated polling but cannot alone satisfy the 5.7 exclusivity condition; dwell resets if focus returns to desktop |
| System wakes from sleep | App re-evaluates display topology and current primary on wake event before resuming polling |
| Workstation is locked while HDTV is primary | Deferred revert fires on unlock (see §5.3a); lock screen remains on HDTV during the lock |
| System logs out, shuts down, or restarts while HDTV is primary | Session-ending revert fires synchronously before the process exits (see Section 5.3) |
| Session-ending revert call exceeds Windows shutdown time budget | Revert may not complete; Windows force-terminates the app. This is a best-effort operation. |
| Computer loses power while HDTV is primary | No shutdown hook fires; the display configuration is persisted by Windows as-is. On next boot the app will be running but the HDTV may still be primary. As a mitigation, the app checks on startup whether the HDTV is currently the primary monitor and, if so, performs an immediate revert before entering normal polling. |

---

## 10. Non-Functional Requirements

- **Polling overhead**: The activity polling loop must consume negligible CPU. A
  5-second poll interval with lightweight Win32 API calls (`GetCursorPos`,
  `GetForegroundWindow`, `MonitorFromWindow`) is the target approach.
- **No elevation required**: The application must run as a standard user. Setting
  the primary display via `SetDisplayConfig` or `ChangeDisplaySettings` does not
  require administrator privileges.
- **Startup time**: The application should reach its tray-idle state within 2
  seconds of login.
- **Persistence**: Configuration changes are written immediately to disk.

---

## 11. Open Questions

1. Should there be a "gaming session start" detection path (separate spec) that
   also switches the primary monitor and audio device *to* the HDTV, and
   re-applies stored window positions?

---

## 12. Future Features

### 12.1 Lock-Screen Revert via Windows Service

The current §5.3a implementation defers the revert to unlock because
`SetDisplayConfig` returns `ERROR_ACCESS_DENIED` while the secure desktop is
active. To make the lock screen appear on the desk monitor immediately, the
display-switching logic would need to run in a process that has access to the
display hardware regardless of desktop state — specifically a Windows Service
running as `LocalSystem`.

**Architecture**: A `LocalSystem` background service would own the
`SwitchController` and all display calls. The existing tray application would
become a thin client communicating with the service over a named pipe: forwarding
activity samples, sending manual switch commands, and receiving state/notification
callbacks. Audio switching would remain in the user-session process (the
`PolicyConfig` COM interface requires user-session context).

**Scope**: New `DefaultMonitorSwitcher.Service` project, a shared IPC protocol
layer, refactored `AppBootstrapper`, and installer changes to register and manage
the service. Estimated ~400–600 lines net new/changed across two new projects.
