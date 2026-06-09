# RiftServe64

RiftServe64 is an application server framework and SDK built on C# and .NET for hosting interactive menus, applications, and custom services within the Rift64 network ecosystem.

## Overview

RiftServe64 provides a dual-port server environment (supporting both plain TCP and secure TLS connections) and an extensible SDK. It enables developers to implement complex, interactive user interfaces and business logic on the server while streaming presentation commands back to Commodore 64 clients.

## Key Features

- **Dual-Port Server Architecture:** Supports concurrent unencrypted plain TCP (for local proxies/emulators) and TLS (for secure WAN routing) communication.
- **RiftServe64 SDK:** Comprehensive API for handling client sessions, sending screen data, receiving keyboard/joystick input, and controlling C64 hardware.
- **Interactive Menu System:** Dynamic menu host that processes choices, renders screen templates, and responds to real-time user selections.
- **Session Management:** Robust, thread-safe client connection and state management.

## Developer Examples

The RiftServe64 codebase contains an extensive set of developer examples inside the `Examples` directory. These demonstrate how to utilize the various low-level and high-level APIs of the RIFT64 protocol to build interactive, fast, and rich applications.

### Playable Games

- **Snake Game (`SnakeGameExample.cs`)**
  - **Concept:** A fully playable Snake game showcasing real-time keystroke input polling and highly optimized, incremental screen rendering.
  - **Key Mechanics:** Rather than repainting the entire grid, the host calculates and sends only the cells that change (the new head, the previous head demoted to body, and the erased tail block). This significantly saves serial link bandwidth, achieving smooth, delay-free gameplay.

- **Tetris Game (`TetrisGameExample.cs`)**
  - **Concept:** A playable Tetris game showcasing shadow-buffered differential rendering.
  - **Key Mechanics:** The host maintains a local copy of the game board (screen characters and colors). On each frame, it compares the current state to the previous frame and transmits only the specific rows that have changed using fast memory writes (`StoreMemoryAsync`) straight into screen RAM ($0400) and color RAM ($D800). Filled blocks use reverse-video spaces, and ghosts use a checkerboard pattern.

### Video and Screen Formatting

- **Raster Split (`RasterSplitExample.cs`)**
  - **Concept:** Demonstrates how to change VIC-II video pointer registers mid-frame to show different character sets simultaneously.
  - **Key Mechanics:** Uses the raster-split command (`N`) to arm a hardware interrupt at a chosen screen scanline. When hit, the C64 firmware modifies `$D018` dynamically, rendering the top half of the screen in the uppercase ROM character set and the bottom half in lowercase—all from a single active screen matrix.

- **Color Blocks (`ColorRampExample.cs`)**
  - **Concept:** Demonstrates standard Commodore 64 text-mode Color RAM manipulation.
  - **Key Mechanics:** Decouples foreground colors from character shapes. It paints blocks across all 16 VIC-II hardware colors using color block fill (`Q`) and colored window (`V`) commands to modify the `$D800-$DBE7` color nybble space.

- **Cursor and Scroll (`CursorAndScrollExample.cs`)**
  - **Concept:** Showcases hardware cursor toggles and fast hardware-driven region scrolling.
  - **Key Mechanics:** Toggles the blinking KERNAL hardware cursor (`H` command) and performs fast rectangular scrolls (`G` command) to shift blocks of characters/colors up, down, left, or right. Region scrolling is processed entirely by the C64 client firmware, bypassing the need to resend the scrolled text over the serial line.

- **Metatile Renderer (`MetatileDemoExample.cs`)**
  - **Concept:** Demonstrates client-side map rendering using a compact metatile coordinate structure.
  - **Key Mechanics:** Uploads tile definitions to the `$4000-$68FF` RAM zone and maps tile IDs to 1x1, 2x2, or 3x3 character grids. The host sends simple tile IDs via the metatile command (`D`), which the C64 client expands locally into complete characters and color values, drastically reducing network transmission overhead.

### Graphics and Sprites

- **Sprite Ops (`SpriteDemoExample.cs`)**
  - **Concept:** Explores the VIC-II's built-in 24x21 hardware sprites, showcasing custom asset uploads, multicolor configurations, expansions, and animation.
  - **Key Mechanics:** Uploads custom 64-byte sprite bitmaps to pointer locations in RAM, sets hires vs. multicolor flags, configures X/Y sizing, and utilizes the coordinate batch command (`@`) to update positions of multiple sprites in a single packet to ensure fluid movement.

### Audio and SID Synthesizer

- **SID Music (`AudioPlayerExample.cs`)**
  - **Concept:** Demonstrates remote streaming of page-aligned SID music tracker files and controlling the client-side MiniPlayer2 audio engine.
  - **Key Mechanics:** Streams music modules in 256-byte checksum-verified chunks to the client's safe module RAM ($7000), binds the player, and controls volume, subtunes, play, and stop states using high-level audio commands (`A`).

- **SndBridge Synth (`SoundBridgeDemoExample.cs`)**
  - **Concept:** An interactive, real-time SID synthesizer that turns the client's keyboard into a three-voice instrument.
  - **Key Mechanics:** Uses SoundBridge commands to define instrument ADSR parameters (`AD`), trigger Note On per voice, apply pitch/vibrato/PWM modulation effects (`AE`), and fire pre-uploaded sound effect scripts placed in the `$C000` memory region.

### Hardware Telemetry

- **Telemetry (`TelemetryDemoExample.cs`)**
  - **Concept:** Streams real-time input and collision data from the Commodore 64 client upstream back to the server.
  - **Key Mechanics:** Opens a telemetry stream (`J` command) with a custom frequency divider. The client continuously packages and sends CIA control port bytes (joystick directions and fire button) along with VIC-II sprite-to-sprite and sprite-to-background collision latch bits (`$D01E/$D01F`) back to the host.

## Getting Started

### Prerequisites

- .NET 8.0 SDK or later

### Running the Server

To build and start the RiftServe64 application server:

```bash
dotnet run --project riftserve64.csproj
```
