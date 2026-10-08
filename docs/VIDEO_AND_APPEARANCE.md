# Video, audio, and appearance

## Background selection

Open **Einstellungen → Video & Ton** and choose a background:

| Mode | Behavior |
|---|---|
| `Ruhiger Farbverlauf` | Built-in gradient; no video or network connection |
| `YouTube` | Official embedded player selected by URL or video ID |
| `Lokales Video` | Windows: MP4, M4V or WebM over loopback; Mac: native-supported files such as H.264/AAC MP4 read directly |

On Mac, local playback uses AVFoundation below the shared dashboard. The native fullscreen button also works with Control-Command-F. Player view exposes a seek bar for the native player.

Local videos repeat from the beginning after reaching the end. Keep the drive connected and the file accessible. The app does not copy the video into the repository or load the entire file into RAM.

The toolbar provides play/pause, mute, volume, player view, fullscreen, and settings. Player view hides the dashboard and dimming so media controls remain accessible. Return using **Dashboard anzeigen** or **Esc**.

The saved background and audio preferences are restored on startup. Background playback is configured to start automatically. Pause remains available at any time.

## YouTube limitations

The app uses the standard IFrame Player API. Embedding restrictions, advertisements, unavailable videos, and network failures can affect playback. Provider errors are shown in the interface.

Dashboard cards overlay the embedded player. This layout deviates from YouTube's [requirements for overlays and frames](https://developers.google.com/youtube/terms/required-minimum-functionality#overlays-and-frames). Local video provides the same layout without relying on the embedded player.

YouTube videos and local media files are not distributed with this repository.

## Appearance

Open **Einstellungen → Anzeige**:

- **Akzentfarbe** changes highlighted usage values, charts, bars, and icons.
- **Schriftfarbe** changes measurement values, headings, the clock, and ordinary interface text. Secondary labels use a muted variant of the color.
- **Karten-Deckkraft** changes card opacity.
- **Hintergrund abdunkeln**, in the video section, changes background dimming.

These four values save immediately when changed and survive a full restart. Pressing **Speichern** is not required for them. Other form settings use **Speichern**, except controls with their own immediate action, such as video selection and toolbar audio.

High-temperature and high-usage warning colors remain distinct. Appearance does not change quota meaning: the large percentage is used allowance, and remaining allowance is `100 − used`.

The temperature row is deliberately compact so account and storage text can be larger. Temperature bars have labeled endpoints in °C, defaulting to 0–100 °C. **Temperatur-Skala bis (°C)** under **Anzeige** accepts 40–120 °C and saves with **Speichern**. This only rescales the display; actual measurements and controller settings are unchanged.

Pump and fan rings show actual OCTO PWM from 0–100%, while the rotating icons and RPM remain visible. Storage percentages always display two decimal places with a decimal comma. The account credit row uses the provider's credit unit or currency and keeps monthly spending separate from funded balance.
