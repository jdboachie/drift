# Known issues

## Pages freeze when the GPU hangs decoding video

**Seen:** Windows 11, Intel HD Graphics 530 (driver 31.0.101.2111, July 2022), WebView2 runtime 157.0.4341.46.

**What happens:** every page stops drawing and responding together, for 10–20 seconds, then comes back
by itself. A page resized during a freeze stays at its old size with white beside it, and a page may show
Drift's "This page isn't responding" panel (it now clears itself when the page answers again). It came
back every few minutes, from when the PC woke from standby.

**Cause:** the engine's GPU process hangs, and Chromium's watchdog kills and restarts it
(`GpuWatchdogThread::DeliberatelyTerminateToRecoverFromHang` in the crash dumps, under
`%LOCALAPPDATA%\Drift\Profile\EBWebView\Crashpad\reports`). In every dump the last video decoded was
HEVC at a portrait size (Apple Music's moving album art, by the look of it). Zen (Firefox 159) on the same
machine decodes HEVC on the same GPU without trouble: Firefox decodes through Media Foundation, Chromium
through its own D3D11 decoder, and it's likely that path the driver hangs on. Not proven.

**What Drift does:** video decodes on the GPU, except in two cases. Then the engine starts with
`--disable-accelerated-video-decode`: video decodes on the CPU, HEVC isn't offered, and sites send H.264
instead.

- **Known setup** (`Core/VideoDecode.cs`): before the engine starts, Drift reads the display adapters
  Windows lists. Intel 6th-generation graphics (Skylake, device ids `0x19xx`) on driver 31.0.101.2111 or
  older decode on the CPU from the first launch. A newer driver gets the GPU again, untried.
- **Repeated failures** (`Core/GpuFailures.cs`, `MainWindow.GpuProcessFailed`): on any other setup, if the
  GPU process fails twice within 30 minutes, Drift remembers it for that WebView2 runtime and offers to
  restart. A new runtime tries the GPU again. Each GPU failure is noted in `%LOCALAPPDATA%\Drift\crash.log`.

**Not working:** `--disable-features=PlatformHEVCDecoderSupport`, which would turn off only HEVC, has no
effect in runtime 157.

**Workaround:** a newer Intel driver (Drift then tries the GPU again by itself).
