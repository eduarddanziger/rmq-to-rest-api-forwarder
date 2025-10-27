# Docker quickstart for RmqToRestApiForwarder

Prerequisites
- Docker Desktop (Windows/macOS) or Docker Engine (Linux) with Compose v2

Build and run
- docker compose build
- docker compose up -d
- RabbitMQ UI: http://localhost:15673, user/pass: forwarder/forwarder
- AMQP port for RabbitMQ: 5673
(the RabbitMQ ports set to 5673/15673 instead of default 15672/15672 to avoid conflicts with the possible local RabbitMQ default installation)

Configuration
- Forwarder reads defaults from `Projects/RmqToRestApiForwarder/appsettings.json`.
- Container overrides in `Projects/RmqToRestApiForwarder/appsettings.Docker.json`.
- Any setting can be overridden via environment variables in `docker-compose.yml`.
- To call a REST API on the host from container:
 - Set `ApiBaseUrl__Target=Local`
 - Set `ApiBaseUrl__Local=http://host.docker.internal:5027/api/AudioDevices`

Stop/cleanup
- docker compose down

