# DungeonCrawler dedicated server for Render: the exported Linux server build plus the FastAPI front door.
# Build the export first (Project > Export > "Linux Server", or scripts/export_server.sh) into build/server/.
FROM debian:bookworm-slim
RUN apt-get update && apt-get install -y --no-install-recommends python3 python3-pip ca-certificates libfontconfig1 libgl1 libxcursor1 libxinerama1 libxrandr2 libxi6 libasound2 libpulse0 libudev1 \
    && pip3 install --no-cache-dir --break-system-packages "fastapi>=0.110" "uvicorn[standard]>=0.29" "websockets>=12" \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY build/server/ /app/server/
COPY deploy/ /app/deploy/
RUN chmod +x /app/server/DungeonCrawler.x86_64 /app/deploy/start.sh
VOLUME ["/data"]
EXPOSE 10000
ENV PORT=10000 CAMPAIGN=builtin MAX_PLAYERS=4
CMD ["/app/deploy/start.sh"]
