# Desk Bounce

Desk Bounce puts a little green ball on your Windows desktop. Double-click `DeskBounce.exe` to put the ball near your cursor. Double-click it again any time to bring the ball back into view. Swipe the cursor through the ball to bat it in the direction you're moving. A faster swipe hits it harder. Mouse clicks pass through the ball to the window beneath it.

The ball bounces around one monitor's usable desktop area at a time. Open a folder or browser in a restored window and aim for its exposed top edge. Moving, closing, or minimizing windows changes the surfaces the ball can land on.

Right-click the green ball icon by the clock to open the controls. Windows may put it in the hidden icons menu.

- **Pause / Resume** freezes the ball or brings it back into play.
- **Reset ball** drops the ball again. You can also double-click the tray icon.
- **Bounce off windows** toggles window ledges.
- **Sound** mutes or enables the bounce sound. Pitch varies between impacts, and harder hits sound louder.
- **Exit** closes Desk Bounce.

The bounce sound is a shortened, pitched-up version of Kenney's `impactSoft_heavy_001.ogg`, released under CC0 1.0. [Impact Sounds](https://kenney.nl/assets/impact-sounds) · [CC0 license](https://creativecommons.org/publicdomain/zero/1.0/) · [WAV conversion notes](https://github.com/receptron/jinglescript/blob/main/samples/sfx/SOURCES.md)

## Build from source

Open PowerShell in the source folder and run the build script. It uses the .NET Framework C# compiler included with Windows 10 and 11 and writes `DeskBounce.exe` beside the source files. Pass `-OutFile` to choose another path.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
```
