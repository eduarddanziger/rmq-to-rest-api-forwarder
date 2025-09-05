# RabbitMQ To REST API Forwarder

A helper message-forwarding service for the Sound Windows Agent, see [SoundWindAgent](https://github.com/eduarddanziger/SoundWindAgent/).

## Overview

The Sound Windows Agent queues HTTP requests to RabbitMQ.
The **RmqToRestApiForwarder** Windows Service consumes those messages and
forwards the JSON payloads to a configured REST API endpoint.

- Runs as a Windows Service on the Sound Windows Agent host machine
- Reads from a local RabbitMQ queue and POSTs/PUTs to the configured API base URL
- Logging is handled by NLog (log files configured to be written
  to C:\ProgramData\<processname> by default, see appsettings.json)

## Technologies Used

- **.NET 8 Worker Service Template** builds Windows Service.
- **RabbitMQ.Client** library for interacting with RabbitMQ.
- **NLog** logging library for .NET.

## Usage

1. Install RabbitMQ locally, e.g., via Chocolatey:
```powershell
choco install rabbitmq -y
```

2. Configure settings in appsettings.json:
   - RabbitMQ: HostName, UserName, Password, QueueName
   - ApiBaseUrl: set the target (AzureUrl or LocalVmUrl, etc.) used by the service

3. Download and unzip the latest rollout of RabbitMQ To REST API Forwarder: RmqToRestApiForwarder-x.x.x from the latest repository release assets: [Release](https://github.com/eduarddanziger/rmq-to-rest-api-forwarder/releases/latest)

4. Register RmqToRestApiForwarder.exe as a Windows Service and start it:

```powershell
sc create RmqToRestApiForwarder binPath="<your folder>\RmqToRestApiForwarder.exe" start=auto
sc start RmqToRestApiForwarder
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

## License

This project is licensed under the terms of the [MIT License](LICENSE).

## Contact

Eduard Danziger

Email: [edanziger@gmx.de](mailto:edanziger@gmx.de)
