import asyncio
import itertools
import socket
from dataclasses import dataclass, field
from typing import Dict, List, Set, Tuple


@dataclass
class Camera:
    ip: str
    port: int
    mode: str  # grid | pallet | camion
    name: str
    writers: Set[asyncio.StreamWriter] = field(default_factory=set)
    counter: int = 0

    def next_message(self) -> str:
        i = self.counter
        self.counter += 1

        if self.mode == "grid":
            shelf = "ESTANTE-A" if "-A" in self.name else "ESTANTE-B" if "-B" in self.name else "ESTANTE-C"
            if i % 2 == 0:
                return f"{shelf}:VACIO"
            if shelf == "ESTANTE-A":
                return f"{shelf}:CAJA-1|CAJA-2|CAJA-3"
            if shelf == "ESTANTE-B":
                return f"{shelf}:CAJA-101|CAJA-102|CAJA-103"
            return f"{shelf}:CAJA-201|CAJA-202|CAJA-203"

        if self.mode == "pallet":
            line = 1 if "LINEA1" in self.name else 2 if "LINEA2" in self.name else 3
            if i % 3 == 0:
                return "PALLET:VACIO"
            if i % 3 == 1:
                return f"PALLET:CAJA-{line}10|CAJA-{line}11|CAJA-{line}12"
            return f"PALLET:CAJA-{line}20|CAJA-{line}21"

        if self.mode == "camion":
            muelle = 1 if "MUELLE1" in self.name else 2 if "MUELLE2" in self.name else 3
            if i % 2 == 0:
                r1 = "CAMION101" if muelle == 1 else "VACIO"
                r2 = "CAMION102" if muelle == 2 else "VACIO"
                r3 = "CAMION103" if muelle == 3 else "VACIO"
            else:
                r1 = r2 = r3 = "VACIO"
            return f"RESERVA1:{r1}|RESERVA2:{r2}|RESERVA3:{r3}"

        return "VACIO"


async def handle_client(camera: Camera, reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
    sock: socket.socket | None = writer.get_extra_info("socket")
    if sock is not None:
        try:
            sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        except OSError:
            pass

    peer = writer.get_extra_info("peername")
    camera.writers.add(writer)
    print(f"[{camera.name}] client connected from {peer}")

    try:
        # Keep the connection open until client disconnects.
        while True:
            data = await reader.read(1024)
            if not data:
                break
            # The real cameras don't require inbound commands; ignore.
    except Exception as ex:
        print(f"[{camera.name}] client error: {ex}")
    finally:
        camera.writers.discard(writer)
        try:
            writer.close()
            await writer.wait_closed()
        except Exception:
            pass
        print(f"[{camera.name}] client disconnected")


async def start_camera_server(camera: Camera) -> asyncio.AbstractServer:
    # Bind to 0.0.0.0 so connections to 127.0.0.X are accepted on Windows.
    server = await asyncio.start_server(
        lambda r, w: handle_client(camera, r, w),
        host="0.0.0.0",
        port=camera.port,
        backlog=50,
    )

    print(f"[{camera.mode}] listening on {camera.ip}:{camera.port} ({camera.name})")
    return server


async def send_loop(cameras: List[Camera], interval_seconds: float = 1.0) -> None:
    rr = itertools.cycle(cameras)

    while True:
        await asyncio.sleep(interval_seconds)

        selected: Camera | None = None
        for _ in range(len(cameras)):
            c = next(rr)
            if c.writers:
                selected = c
                break

        if selected is None:
            continue

        msg = selected.next_message() + "\r\n"
        dead: List[asyncio.StreamWriter] = []

        for w in list(selected.writers):
            try:
                w.write(msg.encode("utf-8"))
                await w.drain()
            except Exception:
                dead.append(w)

        for w in dead:
            selected.writers.discard(w)
            try:
                w.close()
            except Exception:
                pass

        print(f"[{selected.name}] -> {msg.strip()}")


async def main() -> None:
    cameras = [
        # Grid
        Camera(ip="127.0.0.1", port=2321, mode="grid", name="ESTANTERIA-A"),
        Camera(ip="127.0.0.2", port=2322, mode="grid", name="ESTANTERIA-B"),
        Camera(ip="127.0.0.3", port=2323, mode="grid", name="ESTANTERIA-C"),
        # Pallet
        Camera(ip="127.0.0.4", port=2324, mode="pallet", name="PALLET-LINEA1"),
        Camera(ip="127.0.0.5", port=2325, mode="pallet", name="PALLET-LINEA2"),
        Camera(ip="127.0.0.6", port=2326, mode="pallet", name="PALLET-LINEA3"),
        # Camion
        Camera(ip="127.0.0.7", port=2327, mode="camion", name="CAMION-MUELLE1"),
        Camera(ip="127.0.0.8", port=2328, mode="camion", name="CAMION-MUELLE2"),
        Camera(ip="127.0.0.9", port=2329, mode="camion", name="CAMION-MUELLE3"),
    ]

    print("========================================")
    print("Python Camera Simulator")
    print("========================================")
    print("Rate: 1 mensaje/segundo TOTAL (global, round-robin entre cámaras conectadas)")
    print("Press Ctrl+C to stop")
    print("")

    servers = [await start_camera_server(c) for c in cameras]

    try:
        await send_loop(cameras, interval_seconds=1.0)
    finally:
        for s in servers:
            s.close()
        await asyncio.gather(*(s.wait_closed() for s in servers), return_exceptions=True)


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
