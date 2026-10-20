# DEFCON Globe — Linux

A port of the [DEFCON Globe screensaver](../README.md) to Linux. The simulation and every
line of drawing logic are the same files the Windows build uses — `Scene` draws against
`IDrawTarget`, so a platform only has to supply a back end. This one is Skia, with SDL for
the window.

> **Status: spike.** The drawing back end has been exercised by building it for Windows as
> well and rendering the same deterministic frames the Windows build produces, which is how
> the scanline raster and the general look were checked against it. The SDL window, the
> texture upload and the event loop have all been run. What has *not* been tested is any of
> it under a Wayland compositor, or with a font other than Consolas.

## Running it

```bash
tar xzf defcon-saver-linux-x64.tar.gz
cd defcon-saver
chmod +x defcon-saver
./defcon-saver --window          # in a window first
./defcon-saver --fullscreen      # the real thing
```

Any key, a mouse click or a nudge of the mouse ends it.

## Settings

There is no registry and no settings dialog here, so the saver reads
`~/.config/defcon-saver/config` and writes it with every key the first time it runs. Edit and
restart:

```
scanlines  = on      # the CRT raster
bloom      = 2       # 0 off, 1 soft, 2 full
speed      = 1       # simulation rate, 0.25 to 3
```

Scanlines are **on** by default here although the Windows default is off, because they are a
large part of how the thing is meant to look. Turn them off in the file if you disagree.

The build is self-contained: no .NET runtime, no SDL and no Skia packages needed, since all
three are inside the tarball. Fontconfig and freetype are the only system libraries it leans
on, and both are already there on any desktop Arch install.

### If the window never appears

```bash
./defcon-saver --frames out
```

That renders ten stills across a whole scenario to `out/` and exits, touching neither SDL nor
the compositor. If those PNGs look right then the port itself is fine and the problem is
windowing alone, which is a much smaller thing to chase. Send them back either way.

## Making it an actual screensaver

Omarchy runs Hyprland, and Wayland has no screensaver protocol — there is nothing equivalent
to an XScreenSaver hack or a Windows `.scr`. What Hyprland does have is `hypridle`, which can
run any command on idle, so the effect is the same. In `~/.config/hypr/hypridle.conf`:

```
listener {
    timeout = 300
    on-timeout = /opt/defcon-saver/defcon-saver --fullscreen
    on-resume = pkill -f defcon-saver
}
```

It cannot be the `hyprlock` background: hyprlock takes images, not arbitrary renderers.

If `--fullscreen` leaves a tiled or floating window rather than filling the screen, the saver
asks SDL for fullscreen after the window exists, which is the path that works on Wayland - but
a tiling compositor can still have opinions. A window rule settles it:

```
windowrulev2 = fullscreen, title:^(DEFCON)$
```

The first line the saver prints says which video driver SDL picked. `wayland` is native;
`x11` means it went through XWayland, which works but is worth knowing.

## Building from source

Needs the .NET 8 SDK or newer (`pacman -S dotnet-sdk`):

```bash
cd linux
dotnet publish -c Release -r linux-x64 --self-contained true -o publish
```

## Differences from the Windows build

| | Windows | Linux |
|---|---|---|
| Drawing | Direct2D, GDI+ fallback | Skia |
| Window | WinForms | SDL |
| Settings | registry, `/c` dialog | defaults only, for now |
| Blur | fake, by downscale and upscale | a real gaussian, which Skia has |
| Font | Consolas | the first installed of a list of monospace families |

Settings are the obvious gap: the Linux build runs on the defaults because the registry and
the configuration dialog are both Windows-only. A config file is the intended fix.

The font matters more than it sounds. The readout is laid out from font metrics rather than
fixed positions, so a substitute reflows rather than breaking — but it has to be monospaced
or the tally columns stop lining up. `SkiaTarget.FindMono` asks for a list of families by
name and takes the first that is installed; if the readout looks wrong, that list is the
place to start.
