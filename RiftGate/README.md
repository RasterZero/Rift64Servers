# RiftGate

RiftGate is an infrastructure routing, management, and directory server built with C# and .NET. It sits at the core of the Rift64 network ecosystem, routing incoming connections from Commodore 64 clients to various downstream applications and services.

## Overview

RiftGate acts as a gateway and traffic controller. It processes client connection requests, displays a centralized application directory, and securely routes client traffic to designated application servers. It also features simulation modules (such as a multi-sprite flocking algorithm) to demonstrate server-driven rendering capabilities over the network.

## Key Features

- **Centralized Application Directory:** Manages a register of downstream apps and servers via simple configuration models (e.g., CSV/JSON).
- **Administration Web Interface:** Built-in lightweight web server for administrators to monitor connections, register apps, and track network metrics.
- **Dynamic Sprite Controls:** Built-in server-driven boids flocking algorithm that generates real-time coordinate updates for the client's hardware sprites.
- **Network Proxying & Routing:** Transparently routes unencrypted local client connections to remote, TLS-secured application endpoints.
- **High Performance:** Fully asynchronous task-based architecture for low latency and high throughput.

## Getting Started

### Prerequisites

- .NET 8.0 SDK or later

### Configuration

The gateway uses `riftgate-admin.json` and `apps.csv` to manage application routing and configuration settings.

### Running the Server

To start the RiftGate server:

dotnet run --project RiftGate.csproj
