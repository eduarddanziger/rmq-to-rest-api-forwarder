RambbitMQ To REST API Forwarder - Release Notes 
=====================================
~~~
Copyright 2025 by Eduard Danziger
~~~

$version$
--------
~~~
Released on $date$
~~~

## Change
- Configuration extended by attempt number and delay between attempts

## New
- Event delivery attempts via additional RabbitMQ queue
- GitHub Codespaces awaken if not running


3.3.5
--------
~~~
Released on 05.09.2025
~~~

## New
- Separated from SoundWinAgent repository
- Logging to file, configured in appsettings.json
- ApiBaseUrl: Target added to appsettings.json. Possible values: Azure, Local, Codespace
