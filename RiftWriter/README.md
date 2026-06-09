# RiftWriter

RiftWriter is a dedicated networked document viewing and text editing server built on C# and .NET for the Rift64 ecosystem.

## Overview

RiftWriter hosts a text rendering and processing environment specifically tailored to the screen layout and text modes of the Commodore 64. It allows clients to read, view, and manipulate formatted text documents stored on the server through real-time communication.

## Key Features

- **Formatted Text Viewing:** Renders text and layout information optimized for the Commodore 64 screen resolution and text modes.
- **Remote Session Management:** Manages individual user sessions, tracking the current page, editing buffer, and viewer state.
- **Low-Overhead Input Handling:** Receives and processes real-time keyboard inputs and commands streamed from the client terminal.
- **Clean Architectural Design:** Decoupled input/output, rendering, and state management models.

## Getting Started

### Prerequisites

- .NET 8.0 SDK or later

### Running the Server

To build and start the RiftWriter server:

dotnet run --project RiftWriter.csproj
