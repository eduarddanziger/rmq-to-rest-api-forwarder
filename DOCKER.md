# Podman tests, do not work yet

podman run -d --name rabbitmq `
-p 5672:5672 -p 15672:15672 `
-e RABBITMQ_DEFAULT_USER=guest `
-e RABBITMQ_DEFAULT_PASS=guest `
-e RABBITMQ_LOOPBACK_USERS=none `
docker.io/library/rabbitmq:3.13-managementclear

podman run -d --name forwarder `
--env DOTNET_ENVIRONMENT=Docker `
--env RabbitMQ__Service__Port=15672 `
--env RabbitMQ__Service__QueueName=sdr_queue `
--pod new:forwarder-pod01 `
forwarder


# Docker quickstart for RmqToRestApiForwarder

Prerequisites
- Docker Desktop (Windows/macOS) or Docker Engine (Linux) with Compose v2

Build and run
- docker compose build
- docker compose up -d
- RabbitMQ UI: http://localhost:15672, user/pass: guest/guest
- AMQP port for RabbitMQ: 5672

Configuration
- Forwarder reads defaults from `Projects/RmqToRestApiForwarder/appsettings.json`.
- Container overrides in `Projects/RmqToRestApiForwarder/appsettings.Docker.json`.
- Any setting can be overridden via environment variables in `docker-compose.yml`.
- To call a REST API on the host from container:
 - Set `ApiBaseUrl__Target=Local`
 - Set `ApiBaseUrl__Local=http://host.docker.internal:5027/api/AudioDevices`

Stop/cleanup
- docker compose down

