QLN SERVER MOD PACK — PLAYER INSTALL
===================================

You only install this once. After that, the pack updates itself from the server.

1. CLOSE 7 DAYS TO DIE.

2. Open your Mods folder:
   Press Win+R, paste this, press Enter:

   %APPDATA%\7DaysToDie\Mods

3. Extract 000_QLNPackBootstrap.zip into that Mods folder.

   Easiest: right-click the zip -> Extract All -> set the destination to
   %APPDATA%\7DaysToDie\Mods -> Extract. Let Windows create the
   000_QLNPackBootstrap folder.

   The result must look like this:

   ...\7DaysToDie\Mods\000_QLNPackBootstrap\ModInfo.xml
   ...\7DaysToDie\Mods\000_QLNPackBootstrap\QLNPackBootstrap.dll
   ...\7DaysToDie\Mods\000_QLNPackBootstrap\QLNPackUpdater.exe
   ...\7DaysToDie\Mods\000_QLNPackBootstrap\packbootstrap.xml

   If you end up with this:

   ...\Mods\000_QLNPackBootstrap\000_QLNPackBootstrap\ModInfo.xml

   then move the inner 000_QLNPackBootstrap folder up one level.

4. START THE GAME WITHOUT EAC.
   The reliable way on Windows is to launch:

   ...\steamapps\common\7 Days To Die\7DaysToDie.exe

   with Steam already running. You can also use your usual no-EAC shortcut.
   If you launch with EAC on, this mod is skipped and no window appears.

5. At the main menu, the "QLN Mod Pack" window appears.
   Review the list, then click "Download & Install".

   If you close the window, click "Check Mod Pack Updates" at the top-right
   of the main menu to open it again.

6. When it says "All mods downloaded", the client restarts automatically.
   It closes, applies the mods, and starts again. Let it finish.

The mods are installed next to the bootstrap folder:

   ...\7DaysToDie\Mods\QuantumLogisticsNetwork
   ...\7DaysToDie\Mods\SomeOtherMod

Do not put them inside 000_QLNPackBootstrap.

If the window does not appear:
- Make sure EAC is off.
- Make sure ModInfo.xml is directly inside 000_QLNPackBootstrap.
- Check this log:

  %APPDATA%\7DaysToDie\Mods\000_QLNPackBootstrap\Data\logs\bootstrap.log

If you need help, send that log file, not your whole Mods folder.

If the client restarts but the game does not start again, start 7 Days to Die
normally. The update has already been applied.
