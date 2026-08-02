# Changelog

## 1.4.5

- Restored 8 ms high-priority tracking so borders stay attached during window moves and resizes.
- Removed an extra full-frame allocation and pixel copy from resize rendering.

## 1.4.4

- Added a global Enabled master switch that preserves individual feature choices.
- Added global runtime state to the tray menu and disabled feature commands when it is off.
- Kept the settings-window border above the window while tray popups open and close.

## 1.4.3

- Limited bold text to enabled feature tab headers and the Language/About tab headers.
- Restored regular font weight for all text inside tab content blocks.

## 1.4.2

- Added consistent disabled-card styling for flag, repeat-speed, sound, and motion settings.
- Added feature-aware tab-title emphasis and persistent emphasis for Language and About.
- Added localized build dates and a separate left/right default for web fields and browser apps.

## 1.4.1

- Added graphical flags to the language picker and layout preview.
- Added separate repeat speeds for character and navigation/editing keys.
- Moved key-sound playback off the character-repeat timing path.

## 1.2.13

- Added independent signed border spacing for active and inactive windows.
- Added Windows-style dark-gray tray menu colors and light checkmarks.
- Added browser field detection and stabilized the layout indicator position.
- Added optional side positioning and draggable live offset preview.

## 1.2.12

- Added the layout flag tab with field/caret positioning, content, container, size, opacity, and offsets.
- Added event-driven and 8 ms movement tracking for the layout indicator.
- Added automatic window height and fixed manual resizing.
- Added a dark-blue tray menu and custom checked-item rendering.
- Fixed layout-indicator jitter and high-DPI outward border thickness.
- Made administrator-window colors permanently visible in their own card.

## 1.2.10

- Added independent administrator-window colors.
- Added active/inactive previews and per-side border visibility.
