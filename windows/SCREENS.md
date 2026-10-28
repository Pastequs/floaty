# Visual reference: screens and settings

Screenshots of the Windows version, taken with a fresh profile (default settings) and demo data, to serve as a reference for the Linux version. The images are in [`../img/`](../img/). The interface is available in English and French (Settings › General › Language); the screenshots are the English version.

Common frame: the open pill has **a single size** (420 × 268 units, scaled by the Size, Width and Height settings). It contains:

- **Header:** time and date on the left, a "Search or command…" bar on the right. Typing in the bar shows the results in place of the current page.
- **Tab row:** one icon per module, all on a single line; the active page is coloured with the accent. The horizontal wheel (or Shift + wheel) moves from one tab to the next.
- **Page content**, which slides and fades in as a cascade when the tab changes.

Default tab order: Home, Weather + Agenda, Stocks, Home automation, Discord, Windows, AI chat, Notes, Resources, Clipboard, Connections, Controls, Tasks, Shelf, Timer + Water, Settings. Modules can be hidden and reordered.

## Closed pill

![Closed pill](../img/closed-pill.png)

Content chosen in Settings › Pastille (pill): mascot, time, current media, timer, water, tasks, battery, weather, pinned note, next appointment, stocks, CPU/RAM. It takes the height of the icon bar when the bar is taller. On hover it opens (spring animation), and it can be attached to any edge of the screen.

## Pages

| Page | Screenshot | Content |
|---|---|---|
| **Home** | ![](../img/pages/home.png) | Large clock (digital, words, analogue…), week number, mascot. When media is playing: cover art, title, artist, progress bar, controls, lyrics. During a Discord call: call controls. |
| **Weather + Agenda** | ![](../img/pages/weather-agenda.png) | Two halves separated by a line. Weather: condition and temperature, feels-like, wind, humidity, five forecasts (days or hours), UV, rain, Hours/Days button. Agenda: upcoming appointments (Home Assistant), Week button. |
| **Stocks** | ![](../img/pages/stocks.png) | Indicative prices with chart and change, "+ Add" button (search by name or symbol), price alerts. |
| **Home automation** | ![](../img/pages/home-automation.png) | Smart home (Home Assistant, IKEA hub): devices by room, routines, "turn everything off" button. |
| **Discord** | ![](../img/pages/discord.png) | Voice call controls: mute, deafen, volumes, servers and channels. |
| **Windows** | ![](../img/pages/windows.png) ![](../img/pages/windows-list.png) | Open windows and pinned applications, as tiles or a list; click: activate or launch, right-click: menu, middle-click: new instance or close. |
| **AI chat** | ![](../img/pages/ai-chat.png) | Streaming conversation (Claude, OpenAI, OpenRouter or compatible), history, model, shortcuts Paste / Summarise / Translate / Explain. |
| **Notes** | ![](../img/pages/notes.png) | Quick notes, pinnable (can be shown in the pill). |
| **Resources** | ![](../img/pages/resources.png) | CPU, memory, GPU, disk (percentage, chart, bar), then throughput, latency and uptime. Option: top processes. |
| **Clipboard** | ![](../img/pages/clipboard.png) | Last ten copies (text, images, files), click to copy again. |
| **Connections** | ![](../img/pages/connections.png) | Windows-style quick settings: Wi-Fi, Bluetooth, airplane mode, battery saver, light/dark theme, hotspot, VPN, power mode, night light, focus, project, accessibility, nearby sharing; volume and brightness below. Second page "Devices": batteries, Bluetooth and Wi-Fi lists. |
| **Tasks** | ![](../img/pages/tasks.png) | List with check boxes, due dates, reminders, remaining-tasks counter. |
| **Shelf** | ![](../img/pages/shelf.png) | File drop zone; screenshot buttons (Screen, Window, Region). |
| **Timer + Water** | ![](../img/pages/timer.png) ![](../img/pages/water.png) | Timer: progress ring, presets (5 min, 15 min, Pomodoro). Water: today's total against the goal, "+ bottle" button, undo, hourly histogram. A selector switches between the two. |
| **Settings** | ![](../img/pages/settings.png) | Scrolling strip of sections, then the options of the selected section (see below). |

## Settings

One section per module, in this order. Every module page also has a cog that leads straight to its section.

| # | Section | Screenshot |
|---|---|---|
| 0 | Appearance | ![](../img/settings/00-appearance.png) |
| 1 | Pill | ![](../img/settings/01-pill.png) |
| 2 | Time | ![](../img/settings/02-time.png) |
| 3 | General | ![](../img/settings/03-general.png) |
| 4 | Media | ![](../img/settings/04-media.png) |
| 5 | Timer | ![](../img/settings/05-timer.png) |
| 6 | Water | ![](../img/settings/06-water.png) |
| 7 | Tasks | ![](../img/settings/07-tasks.png) |
| 8 | Shelf | ![](../img/settings/08-shelf.png) |
| 9 | Sound | ![](../img/settings/09-sound.png) |
| 10 | Devices | ![](../img/settings/10-devices.png) |
| 11 | Search | ![](../img/settings/11-search.png) |
| 12 | Clipboard | ![](../img/settings/12-clipboard.png) |
| 13 | Resources | ![](../img/settings/13-resources.png) |
| 14 | Weather | ![](../img/settings/14-weather.png) |
| 15 | Stocks | ![](../img/settings/15-stocks.png) |
| 16 | AI chat | ![](../img/settings/16-ai-chat.png) |
| 17 | Discord | ![](../img/settings/17-discord.png) |
| 18 | Home automation | ![](../img/settings/18-home-automation.png) |
| 19 | Network | ![](../img/settings/19-network.png) |
| 20 | Agenda | ![](../img/settings/20-agenda.png) |
| 21 | Windows | ![](../img/settings/21-windows.png) |

The screenshots show the top of each page; long pages scroll with the mouse wheel.

## Replacement taskbar and screen edges

![Bar around the pill](../img/bar-around-pill.png) ![Left edge](../img/left-edge.png)

Icon bar on the right, on the left, or split on either side of the pill (or in a column along an edge); resizable, reorderable by drag and drop, with window previews on hover and a right-click menu. The pill attaches to the top, bottom, left or right edge; on a side edge it becomes a vertical tab.

## Themes

![Themes](../img/themes.png)

Dark, Light, System, Glass, Night, Paper, Watermelon, Neon, Ocean, Forest, Sakura, Terminal (each mood has its own background and accent).

## Not photographed

The media that is playing (cover art, controls, lyrics), the overlays (volume, brightness, notifications), the "Devices" page (real device names) and the connected states of Discord, home automation and the agenda could not be captured with demo data.
