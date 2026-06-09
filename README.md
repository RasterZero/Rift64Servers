# Rift64 Servers

Rift64 Servers is the consolidated backend ecosystem for the RIFT64 Commodore 64 network platform. This monorepo contains the shared server-side SDK along with all standard application servers, tools, and routers required to host secure, real-time interactive experiences on vintage hardware.

## Overview

Historically, connecting a 1982 Commodore 64 to modern networks has been bottlenecked by the 1MHz MOS 6502 CPU, which cannot physically process modern SSL/TLS encryption or TCP overhead in real time. 

The RIFT64 platform solves this by offloading all networking, security, routing, database queries, and heavy application loops to a modern C# .NET 10 backend suite, using the physical C64 as a pure hardware-driven rendering and SID synthesis target.

## Directory Structure

This monorepo is structured with a flat, modular design so that all application servers reside as peers alongside the core SDK:

- **riftserve64.sdk/**
  The central C# .NET SDK library containing the core TCP socket server, protocol encoder/decoder codecs, memory helper classes, and hardware control APIs.

- **RiftGate/**
  The local network proxy and router. It runs silently inside your home network, keeping the C64 carrier alive during server reboots, and handling all complex long-haul WAN-side TLS encryption on behalf of the client.

- **RiftServe64/**
  The main dynamic application server hosting the developer example suite (including playable real-time network-streamed Snake and Tetris games, audio synthesizers, and graphics demos).

- **RiftWriter/**
  A specialized networked document viewer and text editor optimized for the Commodore 64 text-screen layouts.

## Prerequisite Environments

- **Runtime:** .NET 10.0 SDK or later
- **C64 Emulator:** VICE (x64sc) with virtual SwiftLink ACIA cartridge emulation enabled at $DE00 (or physical C64 hardware equipped with a SwiftLink clone and serial Wi-Fi modem).

## Getting Started

### 1. Build the Entire Solution

This monorepo includes a Visual Studio Solution (`Rift64Servers.sln`) linking all C# projects together. You can restore and compile the entire suite in one command:

```bash
dotnet build
```

### 2. Launch individual Servers

Each project folder contains a helper batch file (`run_server.bat`) designed to cleanly build and launch that specific application.

- **To run the RiftGate Router (Port 8000):**
  ```bash
  cd RiftGate
  run_server.bat
  ```

- **To run the App Server & Developer Examples (Port 8002 / 64443 TLS):**
  ```bash
  cd RiftServe64
  run_server.bat
  ```

- **To run the RiftWriter Document Editor (Port 8003):**
  ```bash
  cd RiftWriter
  run_server.bat
  ```

## Project Reference Resolution

Because all servers now live in this single monorepo next to `riftserve64.sdk`, compiling any server project automatically resolves, links, and updates the local SDK dependency out-of-the-box. The project file references are mapped as:

```xml
<ProjectReference Include="..\riftserve64.sdk\riftserve64.sdk.csproj" />
```

---

## Authors & Credits

Developed by James Cann (RasterZero) — alumni of Toronto-based 1980s Commodore software group Damage Soft Inc. (DSI). For complete client-side assembly code, documentation, and setup manuals, visit the main RIFT64 client repository.
