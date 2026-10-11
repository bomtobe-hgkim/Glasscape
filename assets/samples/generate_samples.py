"""Create the project's original, silent desktop motion samples. CC0-1.0.

Requires an FFmpeg executable with lavfi/geq, libx264, PNG and MP4 support.
No downloaded photographs, stock footage, fonts or neural models are used.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import subprocess

WIDTH, HEIGHT, FPS, DURATION = 1280, 720, 30, 8
SOURCE_WIDTH, SOURCE_HEIGHT = 640, 360
ROOT = Path(__file__).resolve().parents[2]
DESTINATION = Path(__file__).resolve().parent
DEFAULT_FFMPEG = ROOT / "third_party/mpv/generation-tools/ffmpeg.exe"


def fields(scene: str) -> tuple[str, str, str]:
    x, y, phase = "(X/W)", "(Y/H)", "(2*PI*T/8)"
    if scene == "aurora-flow":
        ribbon = f"(0.65+0.35*sin({x}*19+0.65*sin({x}*7+{phase})))"
        aurora = f"(exp(-pow(({y}-0.46-0.13*sin({x}*7+{phase}))/0.13,2))*{ribbon})"
        mist = f"exp(-pow(({y}-0.70-0.08*sin({x}*5-{phase}))/0.20,2))"
        return (
            f"12+10*(1-{y})+14*{aurora}+12*{mist}",
            f"22+13*(1-{y})+112*{aurora}+24*{mist}",
            f"39+24*(1-{y})+84*{aurora}+59*{mist}",
        )
    drift = f"(0.5+0.5*sin({x}*5+{y}*7+0.70*sin({x}*3-{phase})+{phase}))"
    crest = f"exp(-pow(({y}-0.57-0.16*sin({x}*4+{phase}))/0.065,2))"
    return (
        f"10+10*{y}+9*{drift}+14*{crest}",
        f"27+41*{y}+26*{drift}+28*{crest}",
        f"45+35*{y}+38*{drift}+35*{crest}",
    )


def periodic_pixel(scene: str, x: float, y: float, time: float) -> tuple[float, ...]:
    phase = 2 * math.pi * time / DURATION
    if scene == "aurora-flow":
        ribbon = 0.65 + 0.35 * math.sin(x * 19 + 0.65 * math.sin(x * 7 + phase))
        aurora = math.exp(-((y - 0.46 - 0.13 * math.sin(x * 7 + phase)) / 0.13) ** 2) * ribbon
        mist = math.exp(-((y - 0.70 - 0.08 * math.sin(x * 5 - phase)) / 0.20) ** 2)
        return (12 + 10 * (1-y) + 14 * aurora + 12 * mist,
                22 + 13 * (1-y) + 112 * aurora + 24 * mist,
                39 + 24 * (1-y) + 84 * aurora + 59 * mist)
    drift = 0.5 + 0.5 * math.sin(x * 5 + y * 7 + 0.70 * math.sin(x * 3 - phase) + phase)
    crest = math.exp(-((y - 0.57 - 0.16 * math.sin(x * 4 + phase)) / 0.065) ** 2)
    return (10 + 10*y + 9*drift + 14*crest,
            27 + 41*y + 26*drift + 28*crest,
            45 + 35*y + 38*drift + 35*crest)


def verify_period(scene: str) -> float:
    maximum = 0.0
    for row in range(19):
        for column in range(33):
            start = periodic_pixel(scene, column / 32, row / 18, 0)
            end = periodic_pixel(scene, column / 32, row / 18, DURATION)
            maximum = max(maximum, *(abs(a-b) for a, b in zip(start, end)))
    if maximum > 1e-10:
        raise RuntimeError(f"The {scene} field is not periodic: {maximum}")
    return maximum


def run(command: list[str]) -> None:
    subprocess.run(command, check=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ffmpeg", type=Path, default=DEFAULT_FFMPEG)
    args = parser.parse_args()
    encoder = args.ffmpeg.resolve(strict=True)
    version = subprocess.check_output([str(encoder), "-version"], text=True, encoding="utf-8").splitlines()[0]
    results = []
    for scene in ("aurora-flow", "ocean-drift"):
        closure_error = verify_period(scene)
        red, green, blue = fields(scene)
        filters = f"format=gbrp,geq=r='{red}':g='{green}':b='{blue}',scale={WIDTH}:{HEIGHT}:flags=lanczos,format=yuv420p"
        output = DESTINATION / f"{scene}.mp4"
        run([str(encoder), "-hide_banner", "-loglevel", "warning", "-y", "-f", "lavfi", "-i",
             f"nullsrc=s={SOURCE_WIDTH}x{SOURCE_HEIGHT}:r={FPS}:d={DURATION}",
             "-vf", filters, "-an", "-c:v", "libx264", "-preset", "medium",
             "-b:v", "1500k", "-minrate", "1500k", "-maxrate", "1500k", "-bufsize", "3000k",
             "-x264-params", "nal-hrd=cbr:force-cfr=1", "-g", str(FPS*DURATION),
             "-keyint_min", str(FPS*DURATION), "-sc_threshold", "0",
             "-pix_fmt", "yuv420p", "-r", str(FPS), "-frames:v", str(FPS*DURATION),
             "-movflags", "+faststart", "-metadata", "title=" + scene,
             "-metadata", "comment=Original procedural Glasscape sample; CC0-1.0", str(output)])
        poster = DESTINATION / f"{scene}.png"
        run([str(encoder), "-hide_banner", "-loglevel", "error", "-y", "-i", str(output),
             "-frames:v", "1", "-update", "1", str(poster)])
        run([str(encoder), "-hide_banner", "-loglevel", "error", "-i", str(output), "-f", "null", "-"])
        results.append({"name": scene, "video": output.name, "poster": poster.name,
                        "width": WIDTH, "height": HEIGHT, "fps": FPS, "duration_seconds": DURATION,
                        "frames": FPS*DURATION, "audio_tracks": 0, "codec": "H.264", "pixel_format": "yuv420p",
                        "periodic_source_max_endpoint_error": closure_error,
                        "bytes": output.stat().st_size, "sha256": hashlib.sha256(output.read_bytes()).hexdigest(),
                        "poster_sha256": hashlib.sha256(poster.read_bytes()).hexdigest()})
        print(f"Created and decoded {output.name}: {output.stat().st_size:,} bytes")
    (DESTINATION / "manifest.json").write_text(json.dumps({"license": "CC0-1.0", "encoder": version,
        "method": "Original periodic mathematical RGB fields, FFmpeg geq, Lanczos upscale, H.264 CBR encode",
        "samples": results}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
