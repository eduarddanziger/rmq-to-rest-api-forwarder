# RambbitMQ To REST API Forwarder

A helper service for a Sound Windows Agent [SoundWindAgent](https://github.com/eduarddanziger/SoundWindAgent/)

## Overview

The Sound Windows Agent registers audio device information on a backend REST API by means of equeing HTTP Requests to RabbitMQ.
The RmqToRestApiForwarder Windows Service dequeues the HTTP Requests from RabbitMQ and forwards them to the backend REST API. 


## Technologies Used

- **RabbitMQ**: Used as a message broker for reliable audio device information delivery.


## Usage

1. Install RabbitMQ (via chocolatey)

2. Download and unzip the latest rollout of RambbitMQ To REST API Forwarder: RmqToRestApiForwarder-x.x.x. from the latest repository release's assets, [Release](https://github.com/eduarddanziger/rmq-to-rest-api-forwarder/releases/latest).

3. Register RmqToRestApiForwarder.exe as a Windows Service and start it:

```powershell
# Register (elevated) and start the RMQ-To-RESTAPI-Forwarder Windows Service
sc create RmqToRestApiForwarder binPath="<your folder>\RmqToRestApiForwarder.exe" start=auto
sc start RmqToRestApiForwarder
```

## Developer Environment, How to Build:

1. Install Visual Studio 2022
2. Download [Nuget.exe](https://dist.nuget.org/win-x86-commandline/latest/nuget.exe) and set a NuGet environment variable to the path of the NuGet executable.
3. Build the solution, e.g. if you use Visual Studio Community Edition:
```powershell
%NuGet% restore SoundWinAgent.sln
"c:\Program Files\Microsoft Visual Studio\2022\Community\Msbuild\Current\Bin\MSBuild.exe" RmqToRestApiForwarder.sln /p:Configuration=Release /target:Rebuild -restore
```

## License

This project is licensed under the terms of the [MIT License](LICENSE).

## Contact

Eduard Danziger

Email: [edanziger@gmx.de](mailto:edanziger@gmx.de)
