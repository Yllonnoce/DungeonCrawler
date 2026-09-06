"""The Render front door: answers the health check, serves a status page, and relays /ws to the Godot server.

Render exposes one HTTPS port. The Godot dedicated server listens on 127.0.0.1:7777 inside the same container;
this app runs on $PORT, answers GET / (health) and GET /status, and forwards every WebSocket on /ws to Godot,
binary frames both ways. Clients connect to wss://<service>.onrender.com/ws.
"""
import asyncio
import json
import os
import time

import websockets
from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from fastapi.responses import HTMLResponse, JSONResponse

GODOT_WS = os.environ.get("GODOT_WS", "ws://127.0.0.1:7777")
STATUS_FILE = os.environ.get("STATUS_FILE", "/data/status.json")
STARTED = time.time()

app = FastAPI(title="DungeonCrawler server")


def read_status() -> dict:
    try:
        with open(STATUS_FILE, encoding="utf-8") as f:
            status = json.load(f)
    except (OSError, ValueError):
        status = {}
    status["frontdoor_uptime"] = int(time.time() - STARTED)
    status["ws"] = "/ws"
    return status


@app.get("/")
async def health() -> JSONResponse:
    return JSONResponse({"ok": True, **read_status()})


@app.get("/status", response_class=HTMLResponse)
async def status_page() -> str:
    s = read_status()
    players = ", ".join(s.get("players", [])) or "nobody"
    return f"""<!doctype html><html><head><meta charset="utf-8"><title>DungeonCrawler server</title>
<style>body{{font-family:system-ui;background:#111;color:#ddd;padding:2rem}} code{{background:#222;padding:.2rem .4rem}}</style></head>
<body><h1>DungeonCrawler server</h1>
<p>Floor <b>{s.get('floor', '?')}</b>, online now: <b>{players}</b> ({s.get('online', 0)}/{s.get('known', 0)} known characters).</p>
<p>Join from the title screen with <code>wss://{os.environ.get('RENDER_EXTERNAL_HOSTNAME', 'this-host')}/ws</code>.</p>
<p>Server tick {s.get('tick', '?')}, up {s.get('uptime', '?')} s, layout hash <code>{s.get('layoutHash', '?')}</code>.</p>
</body></html>"""


@app.websocket("/ws")
async def relay(client: WebSocket) -> None:
    await client.accept(subprotocol=None)
    try:
        async with websockets.connect(GODOT_WS, max_size=16 * 1024 * 1024, ping_interval=None) as upstream:
            async def to_godot() -> None:
                while True:
                    data = await client.receive_bytes()
                    await upstream.send(data)

            async def to_client() -> None:
                async for data in upstream:
                    if isinstance(data, bytes):
                        await client.send_bytes(data)
                    else:
                        await client.send_text(data)

            done, pending = await asyncio.wait([asyncio.create_task(to_godot()), asyncio.create_task(to_client())], return_when=asyncio.FIRST_COMPLETED)
            for task in pending:
                task.cancel()
    except (WebSocketDisconnect, websockets.ConnectionClosed, ConnectionRefusedError, OSError):
        pass
    finally:
        try:
            await client.close()
        except Exception:
            pass
