# Sound Windows Agent

Sound Agent detects and outputs plug-and-play audio endpoint devices under Windows. It handles audio notifications and device changes.

The Sound Agent registers audio device information on a backend server via REST API, optionally using RabbitMQ / RMQ to REST API forwarder (.NET Windows Service).
Fir the backend Audio Device Repository Server (ASP.Net Core) see [audio-device-repo-server](https://github.com/eduarddanziger/audio-device-repo-server/)
with a React / TypeScript frontend [list-audio-react-app](https://github.com/eduarddanziger/list-audio-react-app/) with a [primary Web client](https://eduarddanziger.github.io/list-audio-react-app/).

## Executables Generated

- **SoundWinAgent**: Windows Service collects audio device information and sends it to a remote server.
- **HttpRequestProcessor**: RabbitMQ to REST API forwarder, which is used to forward audio device information from RabbitMQ to the backend server.
- **SoundDefaultUI**: Lightweight WPF UI showing the live volume levels of the default audio devices, output and input device separately.
- **SoundAgentCli** (obsolete): Command-line test CLI.

## Technologies Used

- **C++**: Core logic implementation.
- **Poco and cpprestsdk** packages: Used in order to leverage Windows Server Manager and utilize HTTP REST client code.
- **RabbitMQ**: Optionally used as a message broker for reliable audio device information delivery.
- **C# / WPF**: Lightweight UI for displaying live volume levels of the currently default audio devices.

## Usage

### SoundWinAgent
1. Download and unzip the latest rollout of SoundWinAgent-x.x.x. from the latest repository release's assets, [Release](https://github.com/eduarddanziger/SoundWinAgent/releases/latest)
2. Register / unregister the SoundWinAgent service (elevated):
    - SoundWinAgent.exe /registerService [/startup=auto|manual|disabled]. 
    - SoundWinAgent.exe /unregisterService
3. Start / stop the SoundWinAgent service
    - net start SoundWinAgent
    - net stop SoundWinAgent
4. SoundWinAgent.exe can be started as a Windows CLI, too. Stop it via Ctrl-C
5. SoundWinAgent.exe accepts following optional command line parameters
    - [/url=\<URL\>] can tune the URL of the backend ASP.Net Core REST API Server, example:
    ```
      SoundWinAgent.exe /url=http://localhost:5027
    ```
      - If /url not used, the url is tuned via the configuration file **SoundWinAgent.xml, apiBaseUrl** element.

    - [/transport=None|Direct|RabbitMQ] defines the transport mechanism to use for deliver
      audio device information to the backend server. The default is 'None' (no delivery).
      'Direct' uses an own transient queue and HTTP client; 'RabbitMQ' uses RabbitMQ as a message broker (recommended), example:
    ```
       SoundWinAgent.exe /transport=RabbitMQ
    ```
      - If /transport not used, the transport is tuned via the configuration file SoundWinAgent.xml, apiBaseUrl element

6. SoundWinAgent.exe /help brings a command line help screen with all available options.

### Use RabbitMQ in SoundWinAgent
If you want to use RabbitMQ as a message broker (most reliable solution),
you need to install RabbitMQ (via chocolatey), rabbitmqadmin, and create the necessary exchange and queue.

```powershell
# Create exchange
.\rabbitmqadmin declare exchange --name=sdr_updates --type=direct --durable=true --vhost=/
### Create queue
.\rabbitmqadmin declare queue --name=sdr_metrics --durable=true --vhost=/
# Bind queue to exchange
.\rabbitmqadmin declare binding --source=sdr_updates --destination=sdr_metrics --destination-type=queue --routing-key=metrics-capture --vhost=/
```

Then download and unzip the latest rollout of RabbitMq-To-RESTAPI-Forwarder: HttpRequestProcessor-x.x.x. from the latest repository release's assets, [Release](https://github.com/eduarddanziger/SoundWinAgent/releases/latest) and register HttpRequestProcessor.exe as a Windows Service:

```powershell
# Register (elevated) and start the RMQ-To-RESTAPI-Forwarder Windows Service
sc create HttpRequestProcessor binPath="<your folder>\HttpRequestProcessor.exe" start=auto
sc start HttpRequestProcessor
```

### SoundDefaultUI
1. Download and unzip the latest rollout of SoundDefaultUI-x.x.x. from the latest repository
release's assets, [Release](https://github.com/eduarddanziger/SoundWinAgent/releases/latest)

2. Install certificates and unblock the SoundDefaultUI.exe per PowerShell (start as Administrator):

```powershell
   Import-Certificate -FilePath .\CodeSign.cer -CertStoreLocation Cert:\LocalMachine\Root
   Unblock-File -Path .\SoundDefaultUI.exe
```
3. Run the SoundDefaultUI

    ![SoundDefaultUI screenshot](202509011440SoundDefaultUI.jpg)

## Developer Environment, How to Build:

1. Install Visual Studio 2022
2. Download [Nuget.exe](https://dist.nuget.org/win-x86-commandline/latest/nuget.exe) and set a NuGet environment variable to the path of the NuGet executable.
3. Build the solution, e.g. if you use Visual Studio Community Edition:
```powershell
%NuGet% restore SoundWinAgent.sln
"c:\Program Files\Microsoft Visual Studio\2022\Community\Msbuild\Current\Bin\MSBuild.exe" SoundWinAgent.sln /p:Configuration=Release /target:Rebuild -restore
```

## License

This project is licensed under the terms of the [MIT License](LICENSE).

## Contact

Eduard Danziger

Email: [edanziger@gmx.de](mailto:edanziger@gmx.de)
