# HP Wand

Raspberry Pi Zero 2W replica of the Universal Studios Harry Potter interactive wand experience.

The wand tip is retroreflective. A Pi NoIR camera with an always-on IR LED illuminates and tracks the bright dot. When a recognized gesture is completed, a configurable sequence of GPIO actions fires (LEDs, PWM, relays, servos, sound).

---

## Hardware

| Component | Notes |
|---|---|
| Raspberry Pi Zero 2W | 64-bit ARM Cortex-A53 |
| Pi NoIR Camera Module | Connected via CSI ribbon |
| IR LED (850 nm) | Always-on; wired to 3.3 V via resistor |
| GPIO devices | LEDs, servo, relay — wired per `data/spells.json` |

---

## Raspberry Pi OS Setup

### 1. Flash the OS

Use **Raspberry Pi Imager** to write **Raspberry Pi OS Lite (64-bit)** to a microSD card.

Before writing, open **Advanced Options** (gear icon) and set:
- Hostname (e.g. `hpwand`)
- SSH enabled
- Wi-Fi credentials
- Username / password

### 2. First Boot — SSH In

```bash
ssh pi@hpwand.local
```

Update the system:

```bash
sudo apt update && sudo apt full-upgrade -y
```

### 3. Enable Camera and GPIO

```bash
sudo raspi-config
```

Navigate to:
- **Interface Options → Camera** → Enable
- **Interface Options → I2C** → Enable (optional, for future sensors)

Then reboot:

```bash
sudo reboot
```

### 4. Verify Camera

```bash
# Legacy camera stack (V4L2 kernel driver)
ls /dev/video*
# Should show /dev/video0
```

If `/dev/video0` is missing, add this to `/boot/config.txt` and reboot:

```
start_x=1
gpu_mem=128
```

### 5. Install System Dependencies

Required by Emgu.CV (OpenCV native libraries) and audio playback:

```bash
sudo apt install -y \
    libglib2.0-0 \
    libgtk2.0-0 \
    libavcodec58 \
    libavformat58 \
    libswscale5 \
    libdc1394-25 \
    libgdiplus \
    alsa-utils
```

> `alsa-utils` provides `aplay`, used for sound playback.

---

## Install .NET 8 SDK

The Pi Zero 2W runs 64-bit ARM, so use the official Microsoft install script:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 8.0
```

Add .NET to your PATH (append to `~/.bashrc`):

```bash
echo 'export DOTNET_ROOT=$HOME/.dotnet' >> ~/.bashrc
echo 'export PATH=$PATH:$HOME/.dotnet' >> ~/.bashrc
source ~/.bashrc
```

Verify:

```bash
dotnet --version
# Expected: 8.0.x
```

---

## Deploy the Application

### Clone the Repository

```bash
git clone https://github.com/RMarkusHB/hp-wand.git
cd hp-wand
```

### Build

```bash
cd HP.Wand
dotnet build -c Release
```

A successful build produces output like:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

> The `Emgu.CV.runtime.debian-arm64` NuGet package automatically copies the native OpenCV `.so` files into the build output — no manual library copying needed.

---

## GPIO Wiring

The default `data/spells.json` uses these pins (BCM numbering):

| BCM Pin | Purpose |
|---|---|
| 17 | Digital output (e.g. main LED) |
| 18 | Software PWM (e.g. brightness control) |

Wire your devices between the pin and GND through an appropriate resistor or driver circuit. **Do not connect inductive loads (relays, motors) directly to GPIO pins** — use a transistor or relay driver.

The application must run as a user with GPIO access. Either:

```bash
# Add your user to the gpio group (recommended)
sudo usermod -aG gpio $USER
# Log out and back in for this to take effect
```

Or run with `sudo` (less recommended).

---

## Running the Application

All commands are run from the `HP.Wand/` directory.

### Learn a Gesture

Records your wand movement and saves it as a named template.

```bash
dotnet run -- learn lumos
```

1. A 3-second countdown prints to the terminal.
2. Cast the gesture in front of the camera.
3. The system detects when motion stops and saves the template to `data/gestures/lumos.json`.
4. You are prompted to record additional samples (more samples = better accuracy).

Repeat for every spell gesture you want to use (e.g. `lumos`, `nox`, `wingardium`).

### List Known Gestures and Spells

```bash
dotnet run -- list
```

Output example:

```
=== Gestures ===
  lumos
  nox

=== Spells ===
  Lumos  (gesture: lumos, steps: 7)
  Nox    (gesture: nox, steps: 4)
```

### Test a Spell (No Wand Needed)

Fires the GPIO action sequence immediately, without gesture recognition. Useful for verifying wiring.

```bash
dotnet run -- test lumos
```

### Run (Live Wand Tracking)

```bash
dotnet run -- run
```

Point the IR-tipped wand at the camera and cast a gesture. When a learned gesture is recognized with sufficient confidence, the mapped spell fires.

Press **Ctrl+C** to stop.

---

## Adding New Spells

Edit `data/spells.json` and add an entry:

```json
{
  "name": "Wingardium Leviosa",
  "gesture": "wingardium",
  "steps": [
    { "type": "digital", "pin": 17, "value": 1 },
    { "type": "pwm",     "pin": 18, "value": 128 },
    { "type": "sound",   "file": "data/sounds/wingardium.wav" },
    { "type": "delay",   "ms": 2000 },
    { "type": "digital", "pin": 17, "value": 0 }
  ]
}
```

Then learn the gesture:

```bash
dotnet run -- learn wingardium
```

### Action Step Reference

| Type | Fields | Description |
|---|---|---|
| `digital` | `pin`, `value` (0 or 1) | Set GPIO pin high or low |
| `pwm` | `pin`, `value` (0–255) | Set software PWM duty cycle |
| `delay` | `ms` | Wait for specified milliseconds |
| `sound` | `file` | Play a WAV file via `aplay` |

---

## Auto-Start on Boot (Optional)

Create a systemd service so the wand starts automatically:

```bash
sudo nano /etc/systemd/system/hpwand.service
```

Paste:

```ini
[Unit]
Description=HP Wand
After=network.target

[Service]
ExecStart=/home/pi/.dotnet/dotnet run --project /home/pi/hp-wand/HP.Wand -- run
WorkingDirectory=/home/pi/hp-wand/HP.Wand
User=pi
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

Enable and start:

```bash
sudo systemctl daemon-reload
sudo systemctl enable hpwand
sudo systemctl start hpwand
```

View logs:

```bash
journalctl -u hpwand -f
```

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `/dev/video0` not found | Enable camera in `raspi-config`; check ribbon cable seating |
| `Cannot open /dev/video0` | Ensure no other process holds the camera; check `gpu_mem=128` in `/boot/config.txt` |
| `dotnet: command not found` | Re-run `source ~/.bashrc` or check `DOTNET_ROOT` is set |
| GPIO permission denied | Add user to `gpio` group: `sudo usermod -aG gpio $USER` |
| Gesture not recognized | Record more samples with `learn`; ensure consistent lighting and gesture speed |
| `[SIM]` prefix in output | GPIO simulation mode is active — `/dev/gpiochip0` not found; check Pi OS and GPIO group membership |
| `aplay` not found | `sudo apt install alsa-utils` |
