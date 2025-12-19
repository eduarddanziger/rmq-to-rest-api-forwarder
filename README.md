# RabbitMQ-To-REST-API-Forwarder

A message-forwarding helper service for the Sound Windows Agent; see [SoundWindAgent](https://github.com/eduarddanziger/SoundWindAgent/).

## Motivation

Its purpose is to fetch HTTP request messages from RabbitMQ and forward them to a configured REST API endpoint.

## Fuctionality

- RabbitMQ-To-REST-API-Forwarder runs as a Docker container on the Sound Windows Agent host machine
- Reads from a local RabbitMQ queue and POSTs/PUTs to the configured API base URL
- Logging is handled by NLog (log files configured to be written
  to C:\ProgramData\<processname> by default, see appsettings.json)
- Debouncing of frequent volume-change events.The respective time window
  is configurable via `RabbitMqMessageDeliverySettings:VolumeChangeEventDebouncingWindowInMilliseconds.
- Reliable delivery with delayed retries (via TTL and dead-lettering).
  After the retry max is reached, the message is routed to a failed queue.
  See settings: `RabbitMqMessageDeliverySettings:RetryDelayInSeconds`, `MaxRetryAttempts`.


## Technologies Used

- **.NET 8 Generic Host Template** builds Windows Console App or Windows Service.
- **RabbitMQ.Client** library for interacting with RabbitMQ.
- **NLog** logging library for .NET.

## Usage

1. Install Docker Desktop on the Sound Windows Agent Windows machine:

2. Download and unzip the latest rollout of RabbitMQ-To-REST-API-Forwarder: RmqToRestApiForwarder-x.x.x from the latest repository release assets: [Release](https://github.com/eduarddanziger/rmq-to-rest-api-forwarder/releases/latest)

3. Use docker-compose to bring the RabbitMQ and rmq-to-rest-api-forwarder containers up on the host machine:
   Open a PowerShell prompt in the unzipped folder and run:
  ```powershell
  docker-compose up -d
  ```

## Developer Environment, How to Build and Run:

1. Install Visual Studio 2022 or the .NET 8 SDK
2. Restore packages and build the solution:

```powershell
# Using dotnet CLI
dotnet restore RmqToRestApiForwarder.sln
dotnet build RmqToRestApiForwarder.sln -c Release
```

3. (Optional) Publish a self-contained single-file for Windows x64:

```powershell
# Publish with the included publish profile
dotnet publish "Projects/RmqToRestApiForwarder/RmqToRestApiForwarder.csproj" -c Release -p:PublishProfile=WinX64
```

4. Run: see previous section "Usage", parts 4.

## Changelog

- 2025-12-18: Switched MSBuild inline tasks to RoslynCodeTaskFactory for cross-platform builds (Windows/Linux).
- 2025-12-18: Replaced legacy tasks with inline regex and zip implementations; fixed warnings and improved Docker publish flow.

## License

This project is licensed under the terms of the [MIT License](LICENSE).

## Contact

Eduard Danziger

Email: [edanziger@gmx.de](mailto:edanziger@gmx.de)
