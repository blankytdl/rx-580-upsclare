# Third-party notices

## ReShade DXIL bridge add-on

The source in addons/src/fsr4_gfx803_addon.cpp is derived from the Windows ReShade shader-replacement add-on in:

https://github.com/zgauthier2000/fsr4-shader-performance-overrides

That source is licensed GPL-2.0-or-later. The original author attribution and project link are retained in the source header. The corresponding license is in addons/LICENSE.

The add-on uses ReShade's public add-on API headers from:

https://github.com/crosire/reshade

ReShade API headers are Copyright (C) Patrick Mours and are licensed under BSD-3-Clause. The build workflow checks out the ReShade source tree at the revision recorded in the build log and uses its include/ directory only; ReShade itself is not bundled with this application.

This repository does not bundle AMD's FSR4 DLL or AMD DXIL shaders. The add-on accepts user-supplied, hash-named DXIL override files. Make sure any shader pack you obtain is compatible with your exact FSR4 DLL and is redistributed according to its own license.

Build dependency revision used by CI: ReShade 7bf9de8b33bcc76c3177007e65d73c72dd0f34c0
